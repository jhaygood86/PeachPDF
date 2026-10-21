using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Runs a PDF content stream and issues the matching calls on a <see cref="Canvas"/>: path construction and painting,
/// clipping, the graphics state (CTM, line style, colour, alpha) and form XObjects. The canvas is expected to be in
/// PDF user space already (see <see cref="PdfPagePainter"/>); every operator maps onto the canvas' own save/restore
/// stack so <c>q</c>/<c>Q</c> and clips unwind in the right order. Operators it does not handle yet (text, images,
/// shadings, patterns, inline images) are recorded in <see cref="Unsupported"/> and skipped.
/// </summary>
internal sealed class PdfContentInterpreter
{
    enum Push { Transform, Clip, Blend, Group }

    /// <summary>One thing pushed onto a canvas by a state, and the canvas it went onto (a soft-mask group redirects drawing to another).</summary>
    readonly record struct Pushed(Push Kind, Canvas Owner, SoftMaskGroup? Group = null);

    /// <summary>Content being drawn into its own tile so a soft mask can be applied to it as a whole when the group ends.</summary>
    sealed record SoftMaskGroup(Canvas Outer, Canvas Content, Image ContentImage, PdfDictionary? Mask, PdfDictionary? Resources, Matrix3x2 Accumulated, ColorMatrix? Transfer = null);

    sealed class State
    {
        public PdfColorSpace FillSpace = PdfColorSpace.Gray, StrokeSpace = PdfColorSpace.Gray;
        public double[] Fill = [0], Stroke = [0];
        public double FillAlpha = 1, StrokeAlpha = 1;
        public double LineWidth = 1, MiterLimit = 10, DashPhase;
        public double[] Dash = [];
        public LineCap Cap = LineCap.Butt;
        public LineJoin Join = LineJoin.Miter;
        public List<Pushed> Pushes = [];
        public PdfImportedFont? TextFont;
        public PdfType3Font? Type3Font;
        public bool ColorLocked;
        public double FontSize, CharSpacing, WordSpacing, HorizontalScale = 1, Leading, Rise;
        public int RenderMode;
        public Matrix3x2 Ctm = Matrix3x2.Identity;
        public PdfDictionary? FillPattern, StrokePattern;
        public PaintColor? FillPatternColor, StrokePatternColor;
        public PaintBlendMode Blend = PaintBlendMode.Normal;

        public State Clone() => new()
        {
            FillSpace = FillSpace, StrokeSpace = StrokeSpace, Fill = Fill, Stroke = Stroke,
            FillAlpha = FillAlpha, StrokeAlpha = StrokeAlpha, LineWidth = LineWidth, MiterLimit = MiterLimit,
            DashPhase = DashPhase, Dash = Dash, Cap = Cap, Join = Join,
            TextFont = TextFont, Type3Font = Type3Font, ColorLocked = ColorLocked, FontSize = FontSize, CharSpacing = CharSpacing, WordSpacing = WordSpacing,
            HorizontalScale = HorizontalScale, Leading = Leading, Rise = Rise, RenderMode = RenderMode,
            Ctm = Ctm, FillPattern = FillPattern, StrokePattern = StrokePattern, FillPatternColor = FillPatternColor, StrokePatternColor = StrokePatternColor, Blend = Blend,
        };
    }

    readonly PdfSourceDocument _doc;
    Canvas _canvas;
    readonly Rect? _region;
    readonly List<State> _stack = [new State()];
    readonly HashSet<string> _unsupported = [];
    GraphicsPath? _path;
    double _curX, _curY, _startX, _startY;
    FillMode? _pendingClip;
    int _formDepth;
    Matrix3x2 _streamBase = Matrix3x2.Identity;
    Matrix3x2 _tm = Matrix3x2.Identity, _tlm = Matrix3x2.Identity;
    readonly Dictionary<PdfDictionary, PdfImportedFont?> _fonts = new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates an interpreter that draws onto <paramref name="canvas"/>.</summary>
    /// <param name="doc">the document the content comes from</param>
    /// <param name="canvas">the canvas to draw on</param>
    /// <param name="groupRegion">
    /// the area of the canvas (in its own units, from the origin) that a soft-masked group is drawn into; null when the caller cannot
    /// say, in which case soft masks are reported instead of applied
    /// </param>
    public PdfContentInterpreter(PdfSourceDocument doc, Canvas canvas, Rect? groupRegion = null)
    {
        _doc = doc;
        _canvas = canvas;
        _region = groupRegion;
    }

    /// <summary>
    /// Whether a <c>/Tx BMC</c> ... <c>EMC</c> span (the text of a form field's appearance) is passed on as variable text, so a reader that
    /// regenerates the field replaces exactly that.
    /// </summary>
    public bool MarkVariableText { get; init; }

    // One entry per open BMC/BDC: whether it is variable text, and the tagging scope (a structure element's content, an artifact) it opened.
    readonly Stack<(bool Variable, IDisposable? Scope)> _markedContent = new();

    /// <summary>
    /// Where to record the structure of the content being drawn, or null to draw it untagged. With it set, a marked-content sequence the source
    /// gave a structure element is opened as that element's content, and everything else that is drawn is marked as an artifact.
    /// </summary>
    public PdfTagBinding? Tags { get; init; }

    /// <summary>The parent-tree key of the stream being interpreted (the page's, or a form's <c>/StructParents</c>), or -1.</summary>
    public int StructParents { get; set; } = -1;

    int _openTagged;
    IDisposable? _implicitArtifact;

    /// <summary>Marks content drawn outside any structure-bound sequence as an artifact, so a tagged document has no untagged content.</summary>
    void BeginUntagged()
    {
        if (Tags is null || _openTagged > 0 || _implicitArtifact is not null) return;
        _implicitArtifact = Tags.Recorder.OpenArtifact(_canvas);
    }

    void EndUntagged()
    {
        _implicitArtifact?.Dispose();
        _implicitArtifact = null;
    }

    IDisposable? OpenTag(string name, List<PdfItem> o, PdfDictionary? res)
    {
        if (Tags is not { } tags) return null;
        EndUntagged();
        IDisposable? scope = null;
        if (name == "/Artifact")
        {
            scope = tags.Recorder.OpenArtifact(_canvas);
        }
        else
        {
            var props = o.Count >= 2 ? _doc.Resolve(o[1]) as PdfDictionary : null;
            if (props is null && o.Count >= 2 && _doc.NameOf(o[1]) is { } propName && res is not null && _doc.DictOf(res, "Properties") is { } table)
                props = _doc.Resolve(table.GetRaw(propName.TrimStart('/'))) as PdfDictionary;
            if (props is not null && props.ContainsKeyRaw("MCID") && tags.Find(StructParents, _doc.IntOf(props.GetRaw("MCID"))) is { } binding)
            {
                var ancestors = tags.Recorder.OpenPhantomAncestors(binding.Ancestors);
                var content = tags.Recorder.OpenContentElement(_canvas, binding.Key, binding.Type, binding.AltText);
                scope = new Scopes(content, ancestors);
            }
        }
        if (scope is not null) _openTagged++;
        return scope;
    }

    sealed class Scopes(IDisposable inner, IDisposable outer) : IDisposable
    {
        public void Dispose()
        {
            inner.Dispose();
            outer.Dispose();
        }
    }

    /// <summary>Draws a form XObject (an appearance stream) under the transform already pushed on the canvas: its matrix, box clip and content.</summary>
    /// <param name="form">the form XObject</param>
    /// <param name="fallbackResources">the resources to use when the form has none of its own</param>
    public void PaintAppearance(PdfDictionary form, PdfDictionary? fallbackResources) => DrawForm(form, fallbackResources);

    /// <summary>The operators and features met that this interpreter does not implement.</summary>
    public IReadOnlyCollection<string> Unsupported => _unsupported;

    State Cur => _stack[^1];

    /// <summary>Interprets <paramref name="content"/> with <paramref name="resources"/> as the resource dictionary in effect.</summary>
    public void Run(byte[] content, PdfDictionary? resources)
    {
        var lx = new PdfLexer(content);
        var operands = new List<PdfItem>();
        int baseDepth = _stack.Count;
        // A pattern's space is the default space of the stream that uses it: here, the current transform at the stream's start.
        var savedBase = _streamBase;
        _streamBase = Cur.Ctm;
        int baseMarks = _markedContent.Count;
        while (true)
        {
            var t = lx.Next();
            if (t.Kind == PdfTokenKind.Eof) break;
            if (t.Kind == PdfTokenKind.Keyword && t.Text is not ("true" or "false" or "null"))
            {
                if (t.Text == "BI") { SkipInlineImage(lx); operands.Clear(); _unsupported.Add("BI"); continue; }
                try { Execute(t.Text, operands, resources); }
                catch (Exception e) when (e is InvalidCastException or ArgumentOutOfRangeException or InvalidOperationException or IndexOutOfRangeException)
                {
                    _unsupported.Add(t.Text + " (malformed)");
                }
                operands.Clear();
            }
            else operands.Add(_doc.ParseItem(lx, t));
        }
        // A stream that leaves q unbalanced must not leak state into whatever follows.
        while (_stack.Count > baseDepth) Restore();
        while (_markedContent.Count > baseMarks) CloseMarked(_markedContent.Pop());
        EndUntagged();
        _streamBase = savedBase;
    }

    static void SkipInlineImage(PdfLexer lx)
    {
        // BI <dict> ID <data> EI: scan for whitespace-delimited EI after ID.
        while (true)
        {
            var t = lx.Next();
            if (t.Kind == PdfTokenKind.Eof) return;
            if (t.Kind == PdfTokenKind.Keyword && t.Text == "ID") break;
        }
        var d = lx.Data;
        for (int i = lx.Position; i + 1 < lx.End; i++)
        {
            if (d[i] == 'E' && d[i + 1] == 'I' && (i == 0 || PdfLexer.IsWhite(d[i - 1])) && (i + 2 >= lx.End || PdfLexer.IsWhite(d[i + 2])))
            {
                lx.Position = i + 2;
                return;
            }
        }
        lx.Position = lx.End;
    }

    double N(List<PdfItem> o, int i) => _doc.NumberOf(o[i]);

    static bool IsColorOperator(string op) => op is "g" or "G" or "rg" or "RG" or "k" or "K" or "cs" or "CS" or "sc" or "SC" or "scn" or "SCN";

    void Execute(string op, List<PdfItem> o, PdfDictionary? res)
    {
        if (Cur.ColorLocked && IsColorOperator(op)) return;
        switch (op)
        {
            // --- graphics state
            case "q": Save(); break;
            case "Q": Restore(); break;
            case "cm":
                Transform(new Matrix3x2((float)N(o, 0), (float)N(o, 1), (float)N(o, 2), (float)N(o, 3), (float)N(o, 4), (float)N(o, 5)));
                break;
            case "w": Cur.LineWidth = N(o, 0); break;
            case "J": Cur.Cap = (int)N(o, 0) switch { 1 => LineCap.Round, 2 => LineCap.Square, _ => LineCap.Butt }; break;
            case "j": Cur.Join = (int)N(o, 0) switch { 1 => LineJoin.Round, 2 => LineJoin.Bevel, _ => LineJoin.Miter }; break;
            case "M": Cur.MiterLimit = N(o, 0); break;
            case "d":
                var arr = (PdfArray)_doc.Resolve(o[0])!;
                var dash = new double[arr.Elements.Count];
                for (int i = 0; i < dash.Length; i++) dash[i] = _doc.NumberOf(arr.Elements[i]);
                Cur.Dash = dash;
                Cur.DashPhase = N(o, 1);
                break;
            case "ri" or "i": break;
            case "gs": ApplyExtGState(o, res); break;

            // --- path construction
            case "m": _path = NewPathIfNone(); _path.Start(_curX = _startX = N(o, 0), _curY = _startY = N(o, 1)); break;
            case "l": Path().LineTo(_curX = N(o, 0), _curY = N(o, 1)); break;
            case "c": Curve(N(o, 0), N(o, 1), N(o, 2), N(o, 3), N(o, 4), N(o, 5)); break;
            case "v": Curve(_curX, _curY, N(o, 0), N(o, 1), N(o, 2), N(o, 3)); break;
            case "y": Curve(N(o, 0), N(o, 1), N(o, 2), N(o, 3), N(o, 2), N(o, 3)); break;
            case "h": Path().CloseFigure(); _curX = _startX; _curY = _startY; break;
            case "re":
                double x = N(o, 0), y = N(o, 1), w = N(o, 2), h = N(o, 3);
                var p = NewPathIfNone();
                p.Start(x, y); p.LineTo(x + w, y); p.LineTo(x + w, y + h); p.LineTo(x, y + h); p.CloseFigure();
                _curX = _startX = x; _curY = _startY = y;
                break;

            // --- path painting
            case "S": Paint(fill: false, stroke: true, FillMode.Nonzero, close: false, res); break;
            case "s": Paint(false, true, FillMode.Nonzero, close: true, res); break;
            case "f" or "F": Paint(true, false, FillMode.Nonzero, false, res); break;
            case "f*": Paint(true, false, FillMode.EvenOdd, false, res); break;
            case "B": Paint(true, true, FillMode.Nonzero, false, res); break;
            case "B*": Paint(true, true, FillMode.EvenOdd, false, res); break;
            case "b": Paint(true, true, FillMode.Nonzero, true, res); break;
            case "b*": Paint(true, true, FillMode.EvenOdd, true, res); break;
            case "n": Paint(false, false, FillMode.Nonzero, false, res); break;
            case "W": _pendingClip = FillMode.Nonzero; break;
            case "W*": _pendingClip = FillMode.EvenOdd; break;

            // --- colour
            case "g": Cur.FillPattern = null; Cur.FillSpace = PdfColorSpace.Gray; Cur.Fill = [N(o, 0)]; break;
            case "G": Cur.StrokePattern = null; Cur.StrokeSpace = PdfColorSpace.Gray; Cur.Stroke = [N(o, 0)]; break;
            case "rg": Cur.FillPattern = null; Cur.FillSpace = PdfColorSpace.Rgb; Cur.Fill = [N(o, 0), N(o, 1), N(o, 2)]; break;
            case "RG": Cur.StrokePattern = null; Cur.StrokeSpace = PdfColorSpace.Rgb; Cur.Stroke = [N(o, 0), N(o, 1), N(o, 2)]; break;
            case "k": Cur.FillPattern = null; Cur.FillSpace = PdfColorSpace.Cmyk; Cur.Fill = [N(o, 0), N(o, 1), N(o, 2), N(o, 3)]; break;
            case "K": Cur.StrokePattern = null; Cur.StrokeSpace = PdfColorSpace.Cmyk; Cur.Stroke = [N(o, 0), N(o, 1), N(o, 2), N(o, 3)]; break;
            case "cs" or "CS":
            {
                var space = PdfColorSpace.Resolve(_doc, o[0], res);
                if (space is null) { _unsupported.Add($"{op} {(o[0] as PdfName)?.Value}"); break; }
                if (op == "cs") { Cur.FillSpace = space; Cur.Fill = space.Initial; Cur.FillPattern = null; }
                else { Cur.StrokeSpace = space; Cur.Stroke = space.Initial; Cur.StrokePattern = null; }
                break;
            }
            case "sc" or "scn" or "SC" or "SCN":
            {
                var comps = new List<double>();
                foreach (var item in o) if (_doc.Resolve(item) is PdfInteger or PdfReal) comps.Add(_doc.NumberOf(item));
                bool fill = op is "sc" or "scn";
                var space = fill ? Cur.FillSpace : Cur.StrokeSpace;
                if (space.IsPattern)
                {
                    // An uncoloured pattern's own colour comes first, in the underlying space: "r g b /P1 scn".
                    PaintColor? patternColor = space.UnderlyingSpace is { } under && comps.Count == under.Components ? under.ToColor(comps.ToArray(), 1) : null;
                    if (fill) Cur.FillPatternColor = patternColor; else Cur.StrokePatternColor = patternColor;
                    var patterns = res is null ? null : _doc.DictOf(res, "Pattern");
                    var named = o.Count > 0 && o[^1] is PdfName pn && patterns is not null ? _doc.Resolve(patterns.GetRaw(pn.Value.TrimStart('/'))) as PdfDictionary : null;
                    if (named is null) _unsupported.Add(op + " (missing pattern)");
                    if (fill) Cur.FillPattern = named; else Cur.StrokePattern = named;
                    break;
                }
                if (comps.Count != space.Components) { _unsupported.Add(op + " (pattern or arity)"); break; }
                if (fill) Cur.Fill = comps.ToArray(); else Cur.Stroke = comps.ToArray();
                break;
            }

            // --- text
            case "BT": _tm = _tlm = Matrix3x2.Identity; break;
            case "ET": EndText(); break;
            case "Tc": Cur.CharSpacing = N(o, 0); break;
            case "Tw": Cur.WordSpacing = N(o, 0); break;
            case "Tz": Cur.HorizontalScale = N(o, 0) / 100.0; break;
            case "TL": Cur.Leading = N(o, 0); break;
            case "Ts": Cur.Rise = N(o, 0); break;
            case "Tr":
                Cur.RenderMode = (int)N(o, 0);
                break;
            case "Tf": SetFont(o, res); break;
            case "Td": NextLine(N(o, 0), N(o, 1)); break;
            case "TD": Cur.Leading = -N(o, 1); NextLine(N(o, 0), N(o, 1)); break;
            case "T*": NextLine(0, -Cur.Leading); break;
            case "Tm":
                _tm = _tlm = new Matrix3x2((float)N(o, 0), (float)N(o, 1), (float)N(o, 2), (float)N(o, 3), (float)N(o, 4), (float)N(o, 5));
                break;
            case "Tj": ShowText([TextOf(o[0])], res); break;
            case "'": NextLine(0, -Cur.Leading); ShowText([TextOf(o[0])], res); break;
            case "\"":
                Cur.WordSpacing = N(o, 0); Cur.CharSpacing = N(o, 1);
                NextLine(0, -Cur.Leading); ShowText([TextOf(o[2])], res);
                break;
            case "TJ":
            {
                var parts = new List<object>();
                foreach (var item in ((PdfArray)_doc.Resolve(o[0])!).Elements)
                    parts.Add(_doc.Resolve(item) is PdfString ps ? ps.Value : _doc.NumberOf(item));
                ShowText(parts, res);
                break;
            }

            // --- structure
            case "Do": DoXObject(o, res); break;
            case "sh": PaintShading(o, res); break;
            case "BMC":
                {
                    bool variable = MarkVariableText && o.Count > 0 && _doc.NameOf(o[0]) == "/Tx";
                    var scope = o.Count > 0 && _doc.NameOf(o[0]) == "/Artifact" ? OpenTag("/Artifact", o, res) : null;
                    _markedContent.Push((variable, scope));
                    if (variable) _canvas.BeginVariableText();
                    break;
                }
            case "BDC":
                _markedContent.Push((false, o.Count > 0 && _doc.NameOf(o[0]) is { } tag ? OpenTag(tag, o, res) : null));
                break;
            case "EMC":
                if (_markedContent.Count > 0) CloseMarked(_markedContent.Pop());
                break;
            case "MP" or "DP" or "BX" or "EX" or "d0": break;
            case "d1": Cur.ColorLocked = true; break;       // a Type 3 glyph that is a shape: it is painted in the current colour

            default:
                _unsupported.Add(op);
                break;
        }
    }

    // ---------------------------------------------------------------- state

    void Save() => _stack.Add(Cur.Clone());

    void Restore()
    {
        if (_stack.Count <= 1) return;
        var top = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        Unwind(top.Pushes);
    }

    /// <summary>Undoes pushes newest first, each on the canvas it went onto; a soft-mask group is composited when its marker is reached.</summary>
    void Unwind(List<Pushed> pushes)
    {
        for (int i = pushes.Count - 1; i >= 0; i--)
        {
            var entry = pushes[i];
            switch (entry.Kind)
            {
                case Push.Clip: entry.Owner.PopClip(); break;
                case Push.Blend: entry.Owner.PopBlendMode(); break;
                case Push.Group: EndSoftMaskGroup(entry.Group!); break;
                default: entry.Owner.PopTransform(); break;
            }
        }

        pushes.Clear();
    }

    void CloseMarked((bool Variable, IDisposable? Scope) span)
    {
        if (span.Variable) _canvas.EndVariableText();
        if (span.Scope is not null)
        {
            span.Scope.Dispose();
            _openTagged--;
        }
    }

    /// <summary>Closes everything the base graphics state still holds (a soft mask set with no <c>q</c> around it); call after the last <see cref="Run"/>.</summary>
    public void Finish()
    {
        while (_stack.Count > 1) Restore();
        Unwind(Cur.Pushes);
    }

    void Transform(Matrix3x2 m)
    {
        _canvas.PushTransform(m);
        Cur.Pushes.Add(new(Push.Transform, _canvas));
        Cur.Ctm = m * Cur.Ctm;
    }

    void ApplyExtGState(List<PdfItem> o, PdfDictionary? res)
    {
        string name = _doc.NameOf(o[0])!.TrimStart('/');
        var gs = res is null ? null : _doc.DictOf(res, "ExtGState") is { } t ? _doc.Resolve(t.GetRaw(name)) as PdfDictionary : null;
        if (gs is null) return;
        if (gs.GetRaw("LW") is { } lw) Cur.LineWidth = _doc.NumberOf(lw);
        if (gs.GetRaw("LC") is { } lc) Cur.Cap = _doc.IntOf(lc) switch { 1 => LineCap.Round, 2 => LineCap.Square, _ => LineCap.Butt };
        if (gs.GetRaw("LJ") is { } lj) Cur.Join = _doc.IntOf(lj) switch { 1 => LineJoin.Round, 2 => LineJoin.Bevel, _ => LineJoin.Miter };
        if (gs.GetRaw("ML") is { } ml) Cur.MiterLimit = _doc.NumberOf(ml);
        if (gs.GetRaw("CA") is { } ca) Cur.StrokeAlpha = _doc.NumberOf(ca, 1);
        if (gs.GetRaw("ca") is { } fa) Cur.FillAlpha = _doc.NumberOf(fa, 1);
        if (_doc.ArrayOf(gs, "D") is { Elements.Count: >= 2 } d && _doc.Resolve(d.Elements[0]) is PdfArray da)
        {
            var dash = new double[da.Elements.Count];
            for (int i = 0; i < dash.Length; i++) dash[i] = _doc.NumberOf(da.Elements[i]);
            Cur.Dash = dash;
            Cur.DashPhase = _doc.NumberOf(d.Elements[1]);
        }
        if (gs.GetRaw("BM") is { } bm) SetBlendMode(bm);
        if (gs.GetRaw("SMask") is { } sm) SetSoftMask(sm, res);
        if (gs.GetRaw("TR2") is { } tr2) SetTransfer(tr2);
        else if (gs.GetRaw("TR") is { } tr) SetTransfer(tr);
        foreach (var key in new[] { "Font", "OP", "op" })
            if (gs.GetRaw(key) is not null)
                _unsupported.Add("gs " + key);
    }

    // ---------------------------------------------------------------- text

    string TextOf(PdfItem item) => _doc.Resolve(item) is PdfString s ? s.Value : "";

    void NextLine(double tx, double ty)
    {
        _tlm = Matrix3x2.CreateTranslation((float)tx, (float)ty) * _tlm;
        _tm = _tlm;
    }

    void SetFont(List<PdfItem> o, PdfDictionary? res)
    {
        Cur.FontSize = N(o, 1);
        string name = _doc.NameOf(o[0])!.TrimStart('/');
        var fonts = res is null ? null : _doc.DictOf(res, "Font");
        if (fonts is null || _doc.Resolve(fonts.GetRaw(name)) is not PdfDictionary fd) { Cur.TextFont = null; _unsupported.Add("Tf (missing font)"); return; }
        if (_doc.NameOf(fd.GetRaw("Subtype")) == "/Type3")
        {
            if (!_type3Fonts.TryGetValue(fd, out var type3))
            {
                type3 = PdfType3Font.Load(_doc, fd, out var type3Why);
                if (type3 is null) _unsupported.Add("font: " + type3Why);
                _type3Fonts[fd] = type3;
            }
            (Cur.TextFont, Cur.Type3Font) = (null, type3);
            return;
        }
        if (!_fonts.TryGetValue(fd, out var font))
        {
            font = PdfImportedFont.Load(_doc, fd, _canvas.RenderContext, out var why);
            if (font is null) _unsupported.Add("font: " + why);
            _fonts[fd] = font;
        }
        (Cur.TextFont, Cur.Type3Font) = (font, null);
    }

    readonly Dictionary<PdfDictionary, PdfType3Font?> _type3Fonts = new(ReferenceEqualityComparer.Instance);

    /// <summary>Shows text in a Type 3 font: each glyph's own content stream is run under the font matrix, font size and text matrix.</summary>
    void ShowType3(IReadOnlyList<object> parts, PdfDictionary? res)
    {
        var st = Cur;
        var font = st.Type3Font!;
        double x = 0;
        foreach (var part in parts)
        {
            if (part is double adjust) { x -= adjust / 1000.0 * st.FontSize; continue; }
            foreach (var g in font.Decode((string)part))
            {
                if (st.RenderMode != 3 && g.Procedure is { Stream: not null } proc && _formDepth < 16)
                {
                    // glyph space -> text space (font matrix) -> sized and placed in the line -> scaled horizontally -> the text matrix
                    var matrix = font.FontMatrix * Matrix3x2.CreateScale((float)st.FontSize) * Matrix3x2.CreateTranslation((float)x, (float)st.Rise)
                        * Matrix3x2.CreateScale((float)st.HorizontalScale, 1f) * _tm;
                    var (savedTm, savedTlm, savedPath) = (_tm, _tlm, _path);
                    Save();
                    _formDepth++;
                    try
                    {
                        Transform(matrix);
                        _path = null;
                        Run(_doc.DecodeStream(proc).Data, font.Resources ?? res);
                    }
                    finally
                    {
                        _formDepth--;
                        Restore();
                        (_tm, _tlm, _path) = (savedTm, savedTlm, savedPath);
                    }
                }
                x += g.Width * st.FontSize + st.CharSpacing + (g.IsWordSpace ? st.WordSpacing : 0);
            }
        }
        _tm = Matrix3x2.CreateTranslation((float)(x * st.HorizontalScale), 0) * _tm;
    }

    /// <summary>Draws the strings and kerning adjustments of one show-text operator as a single glyph run.</summary>
    void ShowText(IReadOnlyList<object> parts, PdfDictionary? res)
    {
        var st = Cur;
        if (st.Type3Font is not null && st.FontSize != 0) { ShowType3(parts, res); return; }
        if (st.TextFont is null || st.FontSize == 0) return;
        var font = st.TextFont.GetFont(Math.Abs(st.FontSize));
        if (font is null) { _unsupported.Add("font size"); return; }
        BeginUntagged();
        var glyphs = new List<GlyphPlacement>();
        double x = 0;
        foreach (var part in parts)
        {
            if (part is double adjust) { x -= adjust / 1000.0 * st.FontSize; continue; }
            foreach (var g in st.TextFont.Decode((string)part))
            {
                glyphs.Add(new GlyphPlacement(g.GlyphIndex, x, -st.Rise, g.Text));
                x += g.Width * st.FontSize + st.CharSpacing + (g.IsWordSpace ? st.WordSpacing : 0);
            }
        }
        if (glyphs.Count > 0)
        {
            // Glyphs are laid out upright in canvas space (y down); the flip puts them upright in PDF user space (y up), and
            // the horizontal scale and text matrix place the run.
            var run = Matrix3x2.CreateScale((float)st.HorizontalScale, st.FontSize < 0 ? 1f : -1f) * _tm;
            // Render modes (PDF 9.3.6): 0 fill, 1 stroke, 2 both, 3 neither, 4-7 the same four and then add the glyphs to the clip.
            int mode = st.RenderMode;
            bool fill = mode is 0 or 2 or 4 or 6;
            bool stroke = mode is 1 or 2 or 5 or 6;
            bool clip = mode >= 4;
            _canvas.PushTransform(run);
            try
            {
                // Text that is not filled is still drawn, invisibly, so the page keeps its text for selection and search.
                var color = fill ? st.FillSpace.ToColor(st.Fill, st.FillAlpha) : PaintColor.Transparent;
                if (mode != 7) _canvas.DrawGlyphs(glyphs, font, color);
            }
            finally { _canvas.PopTransform(); }

            if (stroke || clip) OutlineText(glyphs, run, stroke, clip, res);
        }
        _tm = Matrix3x2.CreateTranslation((float)(x * st.HorizontalScale), 0) * _tm;
    }

    GraphicsPath? _textClip;

    /// <summary>
    /// Strokes the outlines of a run of glyphs and/or adds them to the text clip. The outlines are built in text space, carried into the
    /// current user space with <paramref name="run"/>, and drawn there, so the line width, caps and dashes are in user space as for any
    /// other path.
    /// </summary>
    void OutlineText(List<GlyphPlacement> glyphs, Matrix3x2 run, bool stroke, bool clip, PdfDictionary? res)
    {
        var typeface = Cur.TextFont!.Typeface;
        if (typeface is null || typeface.Metrics.UnitsPerEm <= 0) { _unsupported.Add("Tr (no glyph outlines)"); return; }
        double scale = Math.Abs(Cur.FontSize) / typeface.Metrics.UnitsPerEm;
        var path = _canvas.GetGraphicsPath();
        foreach (var glyph in glyphs)
        {
            if (!typeface.TryGetOutline((ushort)glyph.GlyphIndex, out var outline)) continue;
            foreach (var contour in outline.Contours)
            {
                path.Start(glyph.X + contour.Start.X * scale, glyph.Y - contour.Start.Y * scale);
                foreach (var seg in contour.Segments)
                {
                    if (seg.IsCubic)
                        path.AddBezierTo(glyph.X + seg.Control1.X * scale, glyph.Y - seg.Control1.Y * scale,
                            glyph.X + seg.Control2.X * scale, glyph.Y - seg.Control2.Y * scale,
                            glyph.X + seg.End.X * scale, glyph.Y - seg.End.Y * scale);
                    else path.LineTo(glyph.X + seg.End.X * scale, glyph.Y - seg.End.Y * scale);
                }
                path.CloseFigure();
            }
        }
        path.Transform(run);
        path.FillMode = FillMode.Nonzero;

        if (stroke && MakePen(res) is { } pen) _canvas.DrawPath(pen, path);
        if (clip)
        {
            if (_textClip is null) _textClip = path;
            else _textClip.AddPath(path);
        }
    }

    /// <summary>At the end of a text object, applies the clip that glyphs shown in a clipping render mode built up.</summary>
    void EndText()
    {
        if (_textClip is null) return;
        _canvas.PushClip(_textClip);
        Cur.Pushes.Add(new(Push.Clip, _canvas));
        _textClip = null;
    }

    /// <summary>
    /// Applies a <c>/SMask</c> entry. A mask dictionary starts a group: what is drawn next goes into a tile of its own, and when the
    /// state ends the tile is composited through the mask. <c>/None</c> ends a group begun in this same state when nothing has been
    /// pushed since; a <c>/None</c> deeper than the group it should end leaves that group to the end of its own state.
    /// </summary>
    void SetSoftMask(PdfItem value, PdfDictionary? res)
    {
        var st = Cur;
        if (st.Pushes.Count > 0 && st.Pushes[^1].Kind == Push.Group)
        {
            var open = st.Pushes[^1];
            st.Pushes.RemoveAt(st.Pushes.Count - 1);
            EndSoftMaskGroup(open.Group!);
        }

        var resolved = _doc.Resolve(value);
        if (resolved is not PdfDictionary mask) return;                      // /None (or nothing usable): no mask
        if (_doc.DictOf(mask, "G") is null) { _unsupported.Add("gs SMask (no group)"); return; }
        BeginGroup(mask, res, null, "gs SMask");
    }

    /// <summary>Starts drawing into a tile of its own, to be composited through a soft mask or a colour transfer when the state ends.</summary>
    void BeginGroup(PdfDictionary? mask, PdfDictionary? res, ColorMatrix? transfer, string what)
    {
        if (_region is not { } region) { _unsupported.Add(what); return; }
        if (_canvas.CreateTile(region.Width, region.Height) is not { } tile) { _unsupported.Add(what); return; }

        // The tile is in the canvas' own units from its origin, so it takes on everything pushed so far.
        var accumulated = _canvas.CurrentTransform;
        tile.Graphics.PushTransform(accumulated);
        var group = new SoftMaskGroup(_canvas, tile.Graphics, tile.Image, mask, res, accumulated, transfer);
        Cur.Pushes.Add(new(Push.Group, _canvas, group));
        _canvas = tile.Graphics;
    }

    /// <summary>
    /// Applies a <c>/TR</c> or <c>/TR2</c> entry: a function (or one per colour component) that the colours painted from now on are passed
    /// through. A canvas has no such state, so what follows is drawn into a group that is composited through the equivalent per-channel
    /// colour matrix - which exists only for a function that is linear between 0 and 1 (the clamped scale-and-offset the writer emits).
    /// <c>/Identity</c> and <c>/Default</c> end a transfer begun in this state.
    /// </summary>
    void SetTransfer(PdfItem value)
    {
        var st = Cur;
        if (st.Pushes.Count > 0 && st.Pushes[^1].Kind == Push.Group && st.Pushes[^1].Group!.Transfer is not null)
        {
            var open = st.Pushes[^1];
            st.Pushes.RemoveAt(st.Pushes.Count - 1);
            EndSoftMaskGroup(open.Group!);
        }

        var resolved = _doc.Resolve(value);
        if (resolved is PdfName) return;                                     // /Identity or /Default
        var functions = new List<PdfFunction>();
        if (resolved is PdfArray list)
        {
            foreach (var item in list.Elements)
            {
                if (PdfFunction.Load(_doc, item) is not { } f) { _unsupported.Add("gs TR (function)"); return; }
                functions.Add(f);
            }
        }
        else if (PdfFunction.Load(_doc, value) is { } single) functions.Add(single);
        else { _unsupported.Add("gs TR (function)"); return; }
        if (functions.Count == 0) return;

        var (scale, offset) = (new float[3], new float[3]);
        for (int c = 0; c < 3; c++)
        {
            var f = functions.Count >= 3 ? functions[c] : functions[0];
            if (!FitLinear(f, out double a, out double b)) { _unsupported.Add("gs TR (not a linear function)"); return; }
            (scale[c], offset[c]) = ((float)a, (float)b);
        }
        var matrix = new ColorMatrix(new Matrix4x4(scale[0], 0, 0, 0, 0, scale[1], 0, 0, 0, 0, scale[2], 0, 0, 0, 0, 1), new Vector4(offset[0], offset[1], offset[2], 0));
        BeginGroup(null, null, matrix, "gs TR");
    }

    /// <summary>Fits <c>y = a x + b</c> to a transfer function on 0..1 where its output is not clamped, and checks it also explains the clamped ends.</summary>
    static bool FitLinear(PdfFunction f, out double a, out double b)
    {
        const int n = 20;
        var xs = new double[n + 1];
        var ys = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            xs[i] = i / (double)n;
            ys[i] = f.Evaluate(xs[i])[0];
        }
        // The unclamped samples (strictly inside the range) fix the line; a function that is clamped everywhere is a constant.
        var inside = Enumerable.Range(0, n + 1).Where(i => ys[i] is > 0.001 and < 0.999).ToList();
        if (inside.Count < 2)
        {
            a = 0;
            b = ys[0];
            return ys.All(y => Math.Abs(y - ys[0]) < 0.01);
        }
        int lo = inside[0], hi = inside[^1];
        a = (ys[hi] - ys[lo]) / (xs[hi] - xs[lo]);
        b = ys[lo] - a * xs[lo];
        for (int i = 0; i <= n; i++)
            if (Math.Abs(Math.Clamp(a * xs[i] + b, 0, 1) - ys[i]) > 0.01) return false;
        return true;
    }

    /// <summary>Finishes a soft-mask group: draws the mask into a tile and composites the group's tile through it onto the canvas that began it.</summary>
    void EndSoftMaskGroup(SoftMaskGroup group)
    {
        var region = _region!.Value;
        group.Content.PopTransform();
        group.Content.Dispose();
        _canvas = group.Outer;

        if (group.Transfer is { } transfer)
        {
            if (!Matrix3x2.Invert(_canvas.CurrentTransform, out var undoTransfer)) return;
            _canvas.PushTransform(undoTransfer);
            try { _canvas.DrawImageWithColorMatrix(group.ContentImage, region, transfer); }
            finally { _canvas.PopTransform(); }
            return;
        }

        var maskDict = group.Mask!;
        bool luminosity = _doc.NameOf(maskDict.GetRaw("S")) != "/Alpha";
        var maskTile = _canvas.CreateTile(region.Width, region.Height);
        if (maskTile is null) return;
        var mg = maskTile.Value.Graphics;
        if (luminosity)
        {
            // The mask group is composited over its backdrop colour (black unless /BC says otherwise) across the whole area.
            var backdrop = PaintColor.Black;
            if (_doc.ArrayOf(maskDict, "BC") is { } bc && bc.Elements.Count > 0)
            {
                var comps = new double[bc.Elements.Count];
                for (int i = 0; i < comps.Length; i++) comps[i] = _doc.NumberOf(bc.Elements[i]);
                var space = comps.Length switch { 1 => PdfColorSpace.Gray, 3 => PdfColorSpace.Rgb, 4 => PdfColorSpace.Cmyk, _ => null };
                if (space is not null) backdrop = space.ToColor(comps, 1);
            }

            mg.DrawRectangle(mg.GetSolidBrush(backdrop), 0, 0, region.Width, region.Height);
        }

        mg.PushTransform(group.Accumulated);
        var sub = new PdfContentInterpreter(_doc, mg, region) { _formDepth = _formDepth + 1 };
        if (sub._formDepth < 16 && _doc.DictOf(maskDict, "G") is { } form)
            sub.DrawForm(form, group.Resources);
        else _unsupported.Add("gs SMask (nesting)");
        sub.Finish();
        foreach (var f in sub._unsupported) _unsupported.Add(f);
        mg.PopTransform();
        mg.Dispose();

        // The tiles are in the canvas' units, so they are composited with the transform undone.
        if (!Matrix3x2.Invert(_canvas.CurrentTransform, out var undo)) return;
        _canvas.PushTransform(undo);
        try
        {
            if (luminosity) _canvas.DrawImageMasked(group.ContentImage, maskTile.Value.Image, region);
            else _canvas.DrawImageAlphaMasked(group.ContentImage, maskTile.Value.Image, region);
        }
        finally { _canvas.PopTransform(); }
    }

    /// <summary>Applies a <c>/BM</c> entry (a name, or an array of names of which the first known one is used) to what is painted next.</summary>
    void SetBlendMode(PdfItem value)
    {
        var resolved = _doc.Resolve(value);
        var names = resolved is PdfArray list ? list.Elements.Select(e => _doc.NameOf(e)) : [_doc.NameOf(resolved)];
        PaintBlendMode? mode = null;
        foreach (var name in names)
        {
            mode = name switch
            {
                "/Normal" or "/Compatible" => PaintBlendMode.Normal,
                "/Multiply" => PaintBlendMode.Multiply,
                "/Screen" => PaintBlendMode.Screen,
                "/Overlay" => PaintBlendMode.Overlay,
                "/Darken" => PaintBlendMode.Darken,
                "/Lighten" => PaintBlendMode.Lighten,
                "/ColorDodge" => PaintBlendMode.ColorDodge,
                "/ColorBurn" => PaintBlendMode.ColorBurn,
                "/HardLight" => PaintBlendMode.HardLight,
                "/SoftLight" => PaintBlendMode.SoftLight,
                "/Difference" => PaintBlendMode.Difference,
                "/Exclusion" => PaintBlendMode.Exclusion,
                "/Hue" => PaintBlendMode.Hue,
                "/Saturation" => PaintBlendMode.Saturation,
                "/Color" => PaintBlendMode.Color,
                "/Luminosity" => PaintBlendMode.Luminosity,
                _ => null,
            };
            if (mode is not null) break;
        }
        if (mode is null) { _unsupported.Add("gs BM"); return; }
        if (mode == Cur.Blend) return;
        _canvas.PushBlendMode(mode.Value);
        Cur.Pushes.Add(new(Push.Blend, _canvas));
        Cur.Blend = mode.Value;
    }

    // ---------------------------------------------------------------- paths

    GraphicsPath NewPathIfNone() => _path ??= _canvas.GetGraphicsPath();

    GraphicsPath Path()
    {
        if (_path is null)
        {
            // A segment with no current point starts at the origin of the last point, as readers tolerate.
            _path = _canvas.GetGraphicsPath();
            _path.Start(_curX, _curY);
        }
        return _path;
    }

    void Curve(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        Path().AddBezierTo(x1, y1, x2, y2, x3, y3);
        _curX = x3; _curY = y3;
    }

    /// <summary>The brush a fill or stroke paints with: its solid colour, or the pattern the colour names. Null when it cannot be made.</summary>
    Brush? BrushFor(bool fill, PdfDictionary? res)
    {
        var st = Cur;
        var pattern = fill ? st.FillPattern : st.StrokePattern;
        var space = fill ? st.FillSpace : st.StrokeSpace;
        if (space.IsPattern && pattern is null) return null;
        if (pattern is null) return _canvas.GetSolidBrush(space.ToColor(fill ? st.Fill : st.Stroke, fill ? st.FillAlpha : st.StrokeAlpha));

        int type = _doc.IntOf(pattern.GetRaw("PatternType"));
        if (type is not (1 or 2)) { _unsupported.Add("pattern type " + type); return null; }
        var matrix = Matrix3x2.Identity;
        if (_doc.ArrayOf(pattern, "Matrix") is { Elements.Count: >= 6 } m)
            matrix = new Matrix3x2((float)_doc.NumberOf(m.Elements[0]), (float)_doc.NumberOf(m.Elements[1]), (float)_doc.NumberOf(m.Elements[2]),
                (float)_doc.NumberOf(m.Elements[3]), (float)_doc.NumberOf(m.Elements[4]), (float)_doc.NumberOf(m.Elements[5]));
        if (type == 1) return TilingBrush(pattern, matrix, fill ? st.FillPatternColor : st.StrokePatternColor, res);
        if (_doc.DictOf(pattern, "Shading") is not { } shading) { _unsupported.Add("pattern without shading"); return null; }
        if (PdfMeshShading.IsMesh(_doc.IntOf(shading.GetRaw("ShadingType")))) { if (!fill) _unsupported.Add("stroke with a mesh shading"); return null; }
        return ShadingBrush(shading, matrix, fill ? st.FillAlpha : st.StrokeAlpha, res);
    }

    readonly Dictionary<(PdfDictionary Pattern, int Color, int Scale), Image> _tiles = new(new TileKeyComparer());

    sealed class TileKeyComparer : IEqualityComparer<(PdfDictionary Pattern, int Color, int Scale)>
    {
        public bool Equals((PdfDictionary Pattern, int Color, int Scale) a, (PdfDictionary Pattern, int Color, int Scale) b) =>
            ReferenceEquals(a.Pattern, b.Pattern) && a.Color == b.Color && a.Scale == b.Scale;

        public int GetHashCode((PdfDictionary Pattern, int Color, int Scale) k) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Pattern), k.Color, k.Scale);
    }

    /// <summary>
    /// A tiling pattern (PatternType 1) as a canvas tile brush: one cell (XStep by YStep) of the pattern's content is drawn into a tile - once,
    /// however many cells show it - and the brush repeats it under the transform that carries pattern space onto the current space. A
    /// cell's content is clipped to the cell, so a pattern whose bounding box is larger than its steps does not overlap itself.
    /// </summary>
    Brush? TilingBrush(PdfDictionary pattern, Matrix3x2 patternMatrix, PaintColor? uncolouredColor, PdfDictionary? res)
    {
        if (pattern.Stream is null) { _unsupported.Add("pattern without content"); return null; }
        if (_doc.ArrayOf(pattern, "BBox") is not { Elements.Count: >= 4 } bb) { _unsupported.Add("pattern without a bounding box"); return null; }
        double bx0 = Math.Min(_doc.NumberOf(bb.Elements[0]), _doc.NumberOf(bb.Elements[2])), by0 = Math.Min(_doc.NumberOf(bb.Elements[1]), _doc.NumberOf(bb.Elements[3]));
        double bx1 = Math.Max(_doc.NumberOf(bb.Elements[0]), _doc.NumberOf(bb.Elements[2])), by1 = Math.Max(_doc.NumberOf(bb.Elements[1]), _doc.NumberOf(bb.Elements[3]));
        double cellW = Math.Abs(_doc.NumberOf(pattern.GetRaw("XStep"))), cellH = Math.Abs(_doc.NumberOf(pattern.GetRaw("YStep")));
        if (!(cellW > 0) || !(cellH > 0) || !(bx1 > bx0) || !(by1 > by0) || _formDepth >= 16) { _unsupported.Add("pattern cell"); return null; }
        bool uncoloured = _doc.IntOf(pattern.GetRaw("PaintType")) == 2;
        if (uncoloured && uncolouredColor is null) { _unsupported.Add("uncoloured pattern without a colour"); return null; }
        if (!Matrix3x2.Invert(Cur.Ctm, out var inverse)) return null;

        var toCurrent = patternMatrix * _streamBase * inverse;
        var toCanvas = toCurrent * _canvas.CurrentTransform;
        double scale = Math.Sqrt(Math.Abs(toCanvas.M11 * toCanvas.M22 - toCanvas.M12 * toCanvas.M21));
        // The tile is drawn a little larger than it shows, so a raster canvas has pixels to spare; a PDF canvas keeps it as vector content.
        int oversample = (int)Math.Clamp(Math.Ceiling(scale * 2), 1, 16);
        while (cellW * oversample > 4096 || cellH * oversample > 4096) oversample = Math.Max(1, oversample / 2);

        var key = (pattern, uncoloured ? uncolouredColor!.Value.ToRgb().GetHashCode() ^ uncolouredColor.Value.A : 0, oversample);
        if (!_tiles.TryGetValue(key, out var image))
        {
            int w = (int)Math.Max(1, Math.Ceiling(cellW * oversample)), h = (int)Math.Max(1, Math.Ceiling(cellH * oversample));
            if (_canvas.CreateTile(w, h) is not { } tile) { _unsupported.Add("pattern tile"); return null; }
            var g = tile.Graphics;
            // Pattern space -> the tile: the cell's lower-left bounding-box corner is at the tile's bottom-left; y is flipped, then scaled up.
            g.PushTransform(Matrix3x2.CreateScale((float)(w / cellW), (float)(h / cellH)));
            g.PushTransform(new Matrix3x2(1, 0, 0, -1, (float)-bx0, (float)(by0 + cellH)));
            var clip = g.GetGraphicsPath();
            clip.Start(bx0, by0); clip.LineTo(Math.Min(bx1, bx0 + cellW), by0); clip.LineTo(Math.Min(bx1, bx0 + cellW), Math.Min(by1, by0 + cellH)); clip.LineTo(bx0, Math.Min(by1, by0 + cellH)); clip.CloseFigure();
            g.PushClip(clip);
            var sub = new PdfContentInterpreter(_doc, g, new Rect(0, 0, w, h)) { _formDepth = _formDepth + 1 };
            if (uncoloured) sub.UseFixedColour(uncolouredColor!.Value);
            sub.Run(_doc.DecodeStream(pattern).Data, _doc.DictOf(pattern, "Resources") ?? res);
            sub.Finish();
            foreach (var f in sub._unsupported) _unsupported.Add(f);
            g.PopClip();
            g.PopTransform();
            g.PopTransform();
            g.Dispose();
            image = tile.Image;
            _tiles[key] = image;
        }

        // Brush space has a cell's top-left at the origin with y down; in pattern space that corner is (x0, y0 + YStep) with y up.
        var brushToPattern = new Matrix3x2(1, 0, 0, -1, (float)bx0, (float)(by0 + cellH));
        return new TileBrush(image, cellW, cellH, brushToPattern * toCurrent);
    }

    /// <summary>Makes every colour operator a no-op and paints in <paramref name="colour"/>: the content of an uncoloured pattern (PDF 8.7.3.3).</summary>
    void UseFixedColour(PaintColor colour)
    {
        var (r, g, b) = colour.ToRgb();
        var comps = new[] { r / 255.0, g / 255.0, b / 255.0 };
        Cur.FillSpace = Cur.StrokeSpace = PdfColorSpace.Rgb;
        Cur.Fill = Cur.Stroke = comps;
        Cur.ColorLocked = true;
    }

    /// <summary>A brush for a shading whose space is <paramref name="shadingToBase"/> away from the stream's default space.</summary>
    Brush? ShadingBrush(PdfDictionary shading, Matrix3x2 shadingToBase, double alpha, PdfDictionary? res)
    {
        if (!Matrix3x2.Invert(Cur.Ctm, out var inverse)) return null;
        var toCurrent = shadingToBase * _streamBase * inverse;
        var brush = PdfShading.CreateBrush(_doc, shading, res, toCurrent, alpha, _canvas.RenderContext, out var why);
        if (brush is null) _unsupported.Add(why!);
        return brush;
    }

    /// <summary>Draws a mesh shading as an image of the mesh under the transform that carries shading space onto the canvas.</summary>
    void DrawMesh(PdfDictionary shading, Matrix3x2 shadingToBase, double alpha, PdfDictionary? res)
    {
        // A function-based shading's /Matrix carries its domain onto the space the pattern or sh operator names.
        if (_doc.IntOf(shading.GetRaw("ShadingType")) == 1 && _doc.ArrayOf(shading, "Matrix") is { Elements.Count: >= 6 } fm)
            shadingToBase = new Matrix3x2((float)_doc.NumberOf(fm.Elements[0]), (float)_doc.NumberOf(fm.Elements[1]), (float)_doc.NumberOf(fm.Elements[2]),
                (float)_doc.NumberOf(fm.Elements[3]), (float)_doc.NumberOf(fm.Elements[4]), (float)_doc.NumberOf(fm.Elements[5])) * shadingToBase;
        if (!Matrix3x2.Invert(Cur.Ctm, out var inverse)) return;
        var toCurrent = shadingToBase * _streamBase * inverse;
        var toCanvas = toCurrent * _canvas.CurrentTransform;
        double scale = Math.Sqrt(Math.Abs(toCanvas.M11 * toCanvas.M22 - toCanvas.M12 * toCanvas.M21));
        var bitmap = PdfMeshShading.Render(_doc, shading, res, scale, alpha, out var why);
        if (bitmap is null) { _unsupported.Add(why!); return; }
        Image image;
        try { image = _canvas.RenderContext.ImageFromStream(new MemoryStream(PdfImageXObject.EncodePng(bitmap.Rgba, bitmap.Width, bitmap.Height))); }
        catch (Exception) { _unsupported.Add("mesh shading image"); return; }

        // The unit square (image top at v = 0) maps onto the mesh's box in shading space (y up).
        double w = bitmap.X1 - bitmap.X0, h = bitmap.Y1 - bitmap.Y0;
        _canvas.PushTransform(toCurrent);
        _canvas.PushTransform(new Matrix3x2((float)w, 0, 0, (float)-h, (float)bitmap.X0, (float)bitmap.Y1));
        try { _canvas.DrawImage(image, new Rect(0, 0, 1, 1)); }
        finally { _canvas.PopTransform(); _canvas.PopTransform(); }
    }

    /// <summary>The <c>sh</c> operator: paints a shading over the whole current clip.</summary>
    void PaintShading(List<PdfItem> o, PdfDictionary? res)
    {
        BeginUntagged();
        string name = _doc.NameOf(o[0])!.TrimStart('/');
        var shadings = res is null ? null : _doc.DictOf(res, "Shading");
        if (shadings is null || _doc.Resolve(shadings.GetRaw(name)) is not PdfDictionary shading) { _unsupported.Add("sh (missing shading)"); return; }
        if (PdfMeshShading.IsMesh(_doc.IntOf(shading.GetRaw("ShadingType")))) { DrawMesh(shading, Matrix3x2.Identity, Cur.FillAlpha, res); return; }
        var brush = ShadingBrush(shading, Matrix3x2.Identity, Cur.FillAlpha, res);
        if (brush is not null) _canvas.DrawRectangle(brush, -1e5, -1e5, 2e5, 2e5);
    }

    /// <summary>The pen the current stroke colour, line width, caps, joins and dash describe; null when the colour cannot be made.</summary>
    Pen? MakePen(PdfDictionary? res)
    {
        var strokeBrush = BrushFor(fill: false, res);
        if (strokeBrush is null) return null;
        var pen = _canvas.GetPen(strokeBrush);
        // A zero-width line is the thinnest line the device can draw; a hairline stands in for it.
        pen.Width = Cur.LineWidth <= 0 ? 0.1 : Cur.LineWidth;
        pen.MiterLimit = Cur.MiterLimit;
        pen.LineCap = Cur.Cap;
        pen.LineJoin = Cur.Join;
        if (Cur.Dash.Length > 0 && Cur.Dash.Any(d => d > 0)) pen.SetDashPattern(Cur.Dash.Length % 2 == 1 ? [.. Cur.Dash, .. Cur.Dash] : Cur.Dash, Cur.DashPhase);
        return pen;
    }

    void Paint(bool fill, bool stroke, FillMode mode, bool close, PdfDictionary? res)
    {
        var path = _path;
        _path = null;
        if (path is null) { _pendingClip = null; return; }
        if (fill || stroke) BeginUntagged();
        if (close) path.CloseFigure();
        path.FillMode = mode;
        if (fill)
        {
            if (Cur.FillPattern is { } fillPattern && _doc.IntOf(fillPattern.GetRaw("PatternType")) == 2 && _doc.DictOf(fillPattern, "Shading") is { } meshShading
                && PdfMeshShading.IsMesh(_doc.IntOf(meshShading.GetRaw("ShadingType"))))
            {
                // A mesh is drawn as an image, so the path becomes a clip for it.
                var matrix = Matrix3x2.Identity;
                if (_doc.ArrayOf(fillPattern, "Matrix") is { Elements.Count: >= 6 } m)
                    matrix = new Matrix3x2((float)_doc.NumberOf(m.Elements[0]), (float)_doc.NumberOf(m.Elements[1]), (float)_doc.NumberOf(m.Elements[2]),
                        (float)_doc.NumberOf(m.Elements[3]), (float)_doc.NumberOf(m.Elements[4]), (float)_doc.NumberOf(m.Elements[5]));
                _canvas.PushClip(path);
                try { DrawMesh(meshShading, matrix, Cur.FillAlpha, res); }
                finally { _canvas.PopClip(); }
            }
            else
            {
                var brush = BrushFor(fill: true, res);
                if (brush is not null) _canvas.DrawPath(brush, path);
            }
        }
        if (stroke)
        {
            if (MakePen(res) is { } pen) _canvas.DrawPath(pen, path);
        }
        if (_pendingClip is { } clipMode)
        {
            path.FillMode = clipMode;
            _canvas.PushClip(path);
            Cur.Pushes.Add(new(Push.Clip, _canvas));
        }
        _pendingClip = null;
    }

    // ---------------------------------------------------------------- XObjects

    readonly Dictionary<PdfDictionary, Image?> _images = new(ReferenceEqualityComparer.Instance);

    /// <summary>Draws an image XObject into the unit square of the current space (PDF 8.9.4).</summary>
    void DrawImage(PdfDictionary x, PdfDictionary? res)
    {
        bool stencil = PdfImageXObject.IsStencil(_doc, x);
        Image? image;
        if (stencil || !_images.TryGetValue(x, out image))
        {
            var fill = Cur.FillSpace.ToColor(Cur.Fill, Cur.FillAlpha);
            image = PdfImageXObject.Decode(_doc, x, res, fill, _canvas.RenderContext, out var why);
            if (image is null) _unsupported.Add("image: " + why);
            if (!stencil) _images[x] = image;
        }
        if (image is null) return;
        // An image is drawn with its pixels showing unless it asks to be smoothed (/Interpolate, PDF 8.9.5.3).
        image.Interpolate = _doc.Resolve(x.GetRaw("Interpolate") ?? x.GetRaw("I")) is PdfBoolean { Value: true };

        // The unit square has the image's top edge at y = 1; the canvas draws an image upright in y-down space, so flip into it.
        _canvas.PushTransform(new Matrix3x2(1, 0, 0, -1, 0, 1));
        try
        {
            var unit = new Rect(0, 0, 1, 1);
            if (!stencil && Cur.FillAlpha < 1) _canvas.DrawImageWithOpacity(image, unit, Cur.FillAlpha);
            else _canvas.DrawImage(image, unit);
        }
        finally { _canvas.PopTransform(); }
    }

    void DoXObject(List<PdfItem> o, PdfDictionary? res)
    {
        string name = _doc.NameOf(o[0])!.TrimStart('/');
        var xobjects = res is null ? null : _doc.DictOf(res, "XObject");
        if (xobjects is null || _doc.Resolve(xobjects.GetRaw(name)) is not PdfDictionary x) { _unsupported.Add("Do (missing XObject)"); return; }
        string? subtype = _doc.NameOf(x.GetRaw("Subtype"));
        if (subtype == "/Image") { BeginUntagged(); DrawImage(x, res); return; }
        if (subtype != "/Form") { _unsupported.Add("Do " + subtype); return; }
        if (_formDepth >= 16) { _unsupported.Add("Do (form nesting)"); return; }
        DrawForm(x, res);
    }

    /// <summary>Draws a form XObject: its matrix, its bounding-box clip, then its content with its own resources (or the caller's).</summary>
    void DrawForm(PdfDictionary x, PdfDictionary? res)
    {
        Save();
        _formDepth++;
        var savedStructParents = StructParents;
        if (x.ContainsKeyRaw("StructParents")) StructParents = _doc.IntOf(x.GetRaw("StructParents"));
        try
        {
            if (_doc.ArrayOf(x, "Matrix") is { Elements.Count: >= 6 } m)
                Transform(new Matrix3x2((float)_doc.NumberOf(m.Elements[0]), (float)_doc.NumberOf(m.Elements[1]), (float)_doc.NumberOf(m.Elements[2]),
                    (float)_doc.NumberOf(m.Elements[3]), (float)_doc.NumberOf(m.Elements[4]), (float)_doc.NumberOf(m.Elements[5])));
            if (_doc.ArrayOf(x, "BBox") is { Elements.Count: >= 4 } b)
            {
                double x0 = _doc.NumberOf(b.Elements[0]), y0 = _doc.NumberOf(b.Elements[1]), x1 = _doc.NumberOf(b.Elements[2]), y1 = _doc.NumberOf(b.Elements[3]);
                var clip = _canvas.GetGraphicsPath();
                clip.Start(Math.Min(x0, x1), Math.Min(y0, y1));
                clip.LineTo(Math.Max(x0, x1), Math.Min(y0, y1));
                clip.LineTo(Math.Max(x0, x1), Math.Max(y0, y1));
                clip.LineTo(Math.Min(x0, x1), Math.Max(y0, y1));
                clip.CloseFigure();
                _canvas.PushClip(clip);
                Cur.Pushes.Add(new(Push.Clip, _canvas));
            }
            var formRes = _doc.DictOf(x, "Resources") ?? res;
            var savedPath = _path;
            _path = null;
            Run(_doc.DecodeStream(x).Data, formRes);
            _path = savedPath;
        }
        finally
        {
            StructParents = savedStructParents;
            _formDepth--;
            Restore();
        }
    }
}

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
    enum Push { Transform, Clip }

    sealed class State
    {
        public PdfColorSpace FillSpace = PdfColorSpace.Gray, StrokeSpace = PdfColorSpace.Gray;
        public double[] Fill = [0], Stroke = [0];
        public double FillAlpha = 1, StrokeAlpha = 1;
        public double LineWidth = 1, MiterLimit = 10, DashPhase;
        public double[] Dash = [];
        public LineCap Cap = LineCap.Butt;
        public LineJoin Join = LineJoin.Miter;
        public List<Push> Pushes = [];
        public PdfImportedFont? TextFont;
        public double FontSize, CharSpacing, WordSpacing, HorizontalScale = 1, Leading, Rise;
        public int RenderMode;

        public State Clone() => new()
        {
            FillSpace = FillSpace, StrokeSpace = StrokeSpace, Fill = Fill, Stroke = Stroke,
            FillAlpha = FillAlpha, StrokeAlpha = StrokeAlpha, LineWidth = LineWidth, MiterLimit = MiterLimit,
            DashPhase = DashPhase, Dash = Dash, Cap = Cap, Join = Join,
            TextFont = TextFont, FontSize = FontSize, CharSpacing = CharSpacing, WordSpacing = WordSpacing,
            HorizontalScale = HorizontalScale, Leading = Leading, Rise = Rise, RenderMode = RenderMode,
        };
    }

    readonly PdfSourceDocument _doc;
    readonly Canvas _canvas;
    readonly List<State> _stack = [new State()];
    readonly HashSet<string> _unsupported = [];
    GraphicsPath? _path;
    double _curX, _curY, _startX, _startY;
    FillMode? _pendingClip;
    int _formDepth;
    Matrix3x2 _tm = Matrix3x2.Identity, _tlm = Matrix3x2.Identity;
    readonly Dictionary<PdfDictionary, PdfImportedFont?> _fonts = new(ReferenceEqualityComparer.Instance);

    /// <summary>Creates an interpreter that draws onto <paramref name="canvas"/>.</summary>
    public PdfContentInterpreter(PdfSourceDocument doc, Canvas canvas)
    {
        _doc = doc;
        _canvas = canvas;
    }

    /// <summary>The operators and features met that this interpreter does not implement.</summary>
    public IReadOnlyCollection<string> Unsupported => _unsupported;

    State Cur => _stack[^1];

    /// <summary>Interprets <paramref name="content"/> with <paramref name="resources"/> as the resource dictionary in effect.</summary>
    public void Run(byte[] content, PdfDictionary? resources)
    {
        var lx = new PdfLexer(content);
        var operands = new List<PdfItem>();
        int baseDepth = _stack.Count;
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

    void Execute(string op, List<PdfItem> o, PdfDictionary? res)
    {
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
            case "S": Paint(fill: false, stroke: true, FillMode.Nonzero, close: false); break;
            case "s": Paint(false, true, FillMode.Nonzero, close: true); break;
            case "f" or "F": Paint(true, false, FillMode.Nonzero, false); break;
            case "f*": Paint(true, false, FillMode.EvenOdd, false); break;
            case "B": Paint(true, true, FillMode.Nonzero, false); break;
            case "B*": Paint(true, true, FillMode.EvenOdd, false); break;
            case "b": Paint(true, true, FillMode.Nonzero, true); break;
            case "b*": Paint(true, true, FillMode.EvenOdd, true); break;
            case "n": Paint(false, false, FillMode.Nonzero, false); break;
            case "W": _pendingClip = FillMode.Nonzero; break;
            case "W*": _pendingClip = FillMode.EvenOdd; break;

            // --- colour
            case "g": Cur.FillSpace = PdfColorSpace.Gray; Cur.Fill = [N(o, 0)]; break;
            case "G": Cur.StrokeSpace = PdfColorSpace.Gray; Cur.Stroke = [N(o, 0)]; break;
            case "rg": Cur.FillSpace = PdfColorSpace.Rgb; Cur.Fill = [N(o, 0), N(o, 1), N(o, 2)]; break;
            case "RG": Cur.StrokeSpace = PdfColorSpace.Rgb; Cur.Stroke = [N(o, 0), N(o, 1), N(o, 2)]; break;
            case "k": Cur.FillSpace = PdfColorSpace.Cmyk; Cur.Fill = [N(o, 0), N(o, 1), N(o, 2), N(o, 3)]; break;
            case "K": Cur.StrokeSpace = PdfColorSpace.Cmyk; Cur.Stroke = [N(o, 0), N(o, 1), N(o, 2), N(o, 3)]; break;
            case "cs" or "CS":
            {
                var space = PdfColorSpace.Resolve(_doc, o[0], res);
                if (space is null) { _unsupported.Add($"{op} {(o[0] as PdfName)?.Value}"); break; }
                if (op == "cs") { Cur.FillSpace = space; Cur.Fill = space.Initial; }
                else { Cur.StrokeSpace = space; Cur.Stroke = space.Initial; }
                break;
            }
            case "sc" or "scn" or "SC" or "SCN":
            {
                var comps = new List<double>();
                foreach (var item in o) if (_doc.Resolve(item) is PdfInteger or PdfReal) comps.Add(_doc.NumberOf(item));
                bool fill = op is "sc" or "scn";
                var space = fill ? Cur.FillSpace : Cur.StrokeSpace;
                if (comps.Count != space.Components) { _unsupported.Add(op + " (pattern or arity)"); break; }
                if (fill) Cur.Fill = comps.ToArray(); else Cur.Stroke = comps.ToArray();
                break;
            }

            // --- text
            case "BT": _tm = _tlm = Matrix3x2.Identity; break;
            case "ET": break;
            case "Tc": Cur.CharSpacing = N(o, 0); break;
            case "Tw": Cur.WordSpacing = N(o, 0); break;
            case "Tz": Cur.HorizontalScale = N(o, 0) / 100.0; break;
            case "TL": Cur.Leading = N(o, 0); break;
            case "Ts": Cur.Rise = N(o, 0); break;
            case "Tr":
                Cur.RenderMode = (int)N(o, 0);
                if (Cur.RenderMode is 1 or 2 or >= 4) _unsupported.Add("Tr " + Cur.RenderMode);
                break;
            case "Tf": SetFont(o, res); break;
            case "Td": NextLine(N(o, 0), N(o, 1)); break;
            case "TD": Cur.Leading = -N(o, 1); NextLine(N(o, 0), N(o, 1)); break;
            case "T*": NextLine(0, -Cur.Leading); break;
            case "Tm":
                _tm = _tlm = new Matrix3x2((float)N(o, 0), (float)N(o, 1), (float)N(o, 2), (float)N(o, 3), (float)N(o, 4), (float)N(o, 5));
                break;
            case "Tj": ShowText([TextOf(o[0])]); break;
            case "'": NextLine(0, -Cur.Leading); ShowText([TextOf(o[0])]); break;
            case "\"":
                Cur.WordSpacing = N(o, 0); Cur.CharSpacing = N(o, 1);
                NextLine(0, -Cur.Leading); ShowText([TextOf(o[2])]);
                break;
            case "TJ":
            {
                var parts = new List<object>();
                foreach (var item in ((PdfArray)_doc.Resolve(o[0])!).Elements)
                    parts.Add(_doc.Resolve(item) is PdfString ps ? ps.Value : _doc.NumberOf(item));
                ShowText(parts);
                break;
            }

            // --- structure
            case "Do": DoXObject(o, res); break;
            case "BMC" or "BDC" or "EMC" or "MP" or "DP" or "BX" or "EX" or "d0" or "d1": break;

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
        for (int i = top.Pushes.Count - 1; i >= 0; i--)
        {
            if (top.Pushes[i] == Push.Clip) _canvas.PopClip(); else _canvas.PopTransform();
        }
    }

    void Transform(Matrix3x2 m)
    {
        _canvas.PushTransform(m);
        Cur.Pushes.Add(Push.Transform);
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
        foreach (var key in new[] { "BM", "SMask", "Font", "TR", "TR2", "OP", "op" })
            if (gs.GetRaw(key) is { } v && !(key == "BM" && _doc.NameOf(v) is "/Normal" or "/Compatible") && !(key == "SMask" && _doc.NameOf(v) == "/None"))
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
        if (!_fonts.TryGetValue(fd, out var font))
        {
            font = PdfImportedFont.Load(_doc, fd, _canvas.RenderContext, out var why);
            if (font is null) _unsupported.Add("font: " + why);
            _fonts[fd] = font;
        }
        Cur.TextFont = font;
    }

    /// <summary>Draws the strings and kerning adjustments of one show-text operator as a single glyph run.</summary>
    void ShowText(IReadOnlyList<object> parts)
    {
        var st = Cur;
        if (st.TextFont is null || st.FontSize == 0) return;
        var font = st.TextFont.GetFont(Math.Abs(st.FontSize));
        if (font is null) { _unsupported.Add("font size"); return; }
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
            _canvas.PushTransform(run);
            try
            {
                var color = st.RenderMode == 3
                    ? PaintColor.Transparent
                    : st.FillSpace.ToColor(st.Fill, st.FillAlpha);
                _canvas.DrawGlyphs(glyphs, font, color);
            }
            finally { _canvas.PopTransform(); }
        }
        _tm = Matrix3x2.CreateTranslation((float)(x * st.HorizontalScale), 0) * _tm;
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

    void Paint(bool fill, bool stroke, FillMode mode, bool close)
    {
        var path = _path;
        _path = null;
        if (path is null) { _pendingClip = null; return; }
        if (close) path.CloseFigure();
        path.FillMode = mode;
        if (fill)
        {
            using var brush = _canvas.GetSolidBrush(Cur.FillSpace.ToColor(Cur.Fill, Cur.FillAlpha));
            _canvas.DrawPath(brush, path);
        }
        if (stroke)
        {
            var pen = _canvas.GetPen(_canvas.GetSolidBrush(Cur.StrokeSpace.ToColor(Cur.Stroke, Cur.StrokeAlpha)));
            // A zero-width line is the thinnest line the device can draw; a hairline stands in for it.
            pen.Width = Cur.LineWidth <= 0 ? 0.1 : Cur.LineWidth;
            pen.MiterLimit = Cur.MiterLimit;
            pen.LineCap = Cur.Cap;
            pen.LineJoin = Cur.Join;
            if (Cur.Dash.Length > 0 && Cur.Dash.Any(d => d > 0)) pen.SetDashPattern(Cur.Dash.Length % 2 == 1 ? [.. Cur.Dash, .. Cur.Dash] : Cur.Dash, Cur.DashPhase);
            _canvas.DrawPath(pen, path);
        }
        if (_pendingClip is { } clipMode)
        {
            path.FillMode = clipMode;
            _canvas.PushClip(path);
            Cur.Pushes.Add(Push.Clip);
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
        if (subtype == "/Image") { DrawImage(x, res); return; }
        if (subtype != "/Form") { _unsupported.Add("Do " + subtype); return; }
        if (_formDepth >= 16) { _unsupported.Add("Do (form nesting)"); return; }

        Save();
        _formDepth++;
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
                Cur.Pushes.Add(Push.Clip);
            }
            var formRes = _doc.DictOf(x, "Resources") ?? res;
            var savedPath = _path;
            _path = null;
            Run(_doc.DecodeStream(x).Data, formRes);
            _path = savedPath;
        }
        finally
        {
            _formDepth--;
            Restore();
        }
    }
}

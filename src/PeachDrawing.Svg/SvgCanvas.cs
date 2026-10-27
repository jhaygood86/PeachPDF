using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace PeachDrawing.Svg;

/// <summary>
/// A <see cref="Canvas"/> that writes what is drawn on it as SVG. Paths keep their curves, brushes become gradients and patterns, clips
/// become clip paths, transforms become group transforms, blend modes and layers become CSS compositing, images are embedded as they
/// came, and text is written as exact glyph outlines (or as selectable text, see <see cref="SvgTextMode"/>). Only what SVG cannot
/// express - a bitmap a caller asks for through <see cref="BeginRasterSurface"/> - is embedded as pixels.
/// </summary>
/// <remarks>
/// Get one from <see cref="SvgComposer.Canvas"/>. Push and pop calls (clips, transforms, blend modes, layers) must nest, as they do on
/// the PDF canvas, because each becomes a group in the document.
/// </remarks>
public sealed partial class SvgCanvas : Canvas
{
    private enum OpenKind { Clip, Transform, Blend, Layer }

    /// <summary>What was in force before a push, and whether the push wrote a group, so the matching pop can put it back.</summary>
    private readonly record struct Open(OpenKind Kind, bool Emitted, Matrix3x2 Ctm, PaintBlendMode Blend, string? Markup, string? ClipShape = null, int BodyStart = 0, int BodyEnd = 0);

    private readonly SvgDefs _defs;
    private readonly StringBuilder _body = new();
    private readonly List<Open> _open = [];
    private readonly double _ppp;
    private readonly SvgComposerOptions _options;
    private readonly bool _isTile;
    private Matrix3x2 _ctm = Matrix3x2.Identity;
    private PaintBlendMode _blend = PaintBlendMode.Normal;
    private int _depth;
    private string? _completed;

    internal SvgCanvas(RenderContext renderContext, SvgDefs defs, Rect initialClip, double pixelsPerPoint, SvgComposerOptions options, bool isTile)
        : base(renderContext, initialClip)
    {
        _defs = defs;
        _ppp = pixelsPerPoint > 0 ? pixelsPerPoint : 1.0;
        _options = options;
        _isTile = isTile;
    }

    /// <inheritdoc/>
    public override double PixelsPerPoint => _ppp;

    /// <inheritdoc/>
    public override object? TileCacheOwner => _defs;

    /// <inheritdoc/>
    public override bool IsOffscreenTile => _isTile;

    /// <inheritdoc/>
    public override Matrix3x2 CurrentTransform => _ctm;

    /// <inheritdoc/>
    public override (double X, double Y) TransformScale
    {
        get
        {
            var x = Math.Sqrt((double)_ctm.M11 * _ctm.M11 + (double)_ctm.M12 * _ctm.M12);
            var y = Math.Sqrt((double)_ctm.M21 * _ctm.M21 + (double)_ctm.M22 * _ctm.M22);
            return (x > 1e-6 ? x : 1.0, y > 1e-6 ? y : 1.0);
        }
    }

    // ---- output plumbing ---------------------------------------------------------------------------------

    private string N(double value) => _defs.Num(value);

    private void Line(string text)
    {
        _body.Append(' ', Math.Min(_depth, 12)).Append(text).Append('\n');
    }

    /// <summary>
    /// Closes whatever is still open and returns the canvas's content. Idempotent: a tile is completed when it is first used, the document when
    /// it is saved, and drawing after that is not written.
    /// </summary>
    internal string CompleteContent()
    {
        if (_completed is not null)
            return _completed;

        while (_open.Count > 0)
            Pop(_open[^1].Kind);

        AppendLinks();
        _completed = _body.ToString();
        return _completed;
    }

    private void Push(OpenKind kind, string? groupOpen, string? clipShape = null)
    {
        var start = _body.Length;
        if (groupOpen is not null)
        {
            Line(groupOpen);
            _depth++;
        }

        _open.Add(new Open(kind, groupOpen is not null, _ctm, _blend, groupOpen, clipShape, start, _body.Length));
    }

    /// <summary>Writes the end of an open group - or, when nothing was written inside it, takes the group back out, so no empty group is left.</summary>
    private void CloseGroup(Open entry)
    {
        _depth--;
        if (_body.Length == entry.BodyEnd)
            _body.Length = entry.BodyStart;
        else
            Line("</g>");
    }

    /// <summary>Opens <paramref name="entry"/>'s group again where the output now is.</summary>
    private Open Reopen(Open entry)
    {
        var start = _body.Length;
        Line(entry.Markup!);
        _depth++;
        return entry with { BodyStart = start, BodyEnd = _body.Length };
    }

    /// <summary>
    /// Pops the innermost push. Like the PDF canvas (one saved graphics state per push) it counts pushes rather than matching their kinds:
    /// a fixed-position box suspends the clips and resumes them around its painting, which pops and pushes clips while a transform or a
    /// layer is open, so what is innermost is not always what the caller names.
    /// </summary>
    private void Pop(OpenKind expected)
    {
        if (_open.Count == 0)
            throw new InvalidOperationException($"There is no {expected} pushed on this canvas to pop.");

        var entry = _open[^1];
        _open.RemoveAt(_open.Count - 1);
        _ctm = entry.Ctm;
        _blend = entry.Blend;
        if (entry.Emitted)
            CloseGroup(entry);
    }

    /// <summary>The <c>style</c> attribute an element carries: the current blend mode plus <paramref name="extra"/> declarations.</summary>
    private string Style(string? extra = null, PaintBlendMode? blend = null)
    {
        var mode = blend ?? _blend;
        var blendText = mode == PaintBlendMode.Normal ? null : "mix-blend-mode:" + BlendName(mode);
        var clip = FlatClipAttribute();
        if (blendText is null && string.IsNullOrEmpty(extra))
            return clip;

        return clip + " style=\"" + (extra is { Length: > 0 } ? blendText is null ? extra : extra + ";" + blendText : blendText) + "\"";
    }

    // ---- clips that must not be groups ---------------------------------------------------------------------
    //
    // A group with a clip-path is an isolated group in CSS compositing, so a blend mode inside one blends with the group's own content and
    // not with the page behind it. Clips in force while something is blended are therefore not written as groups at all: each element
    // carries the clip itself (an element that is blended and clipped is blended after it is clipped, which is the PDF reading), as a
    // clip path that intersects the clips in force, expressed in the coordinates the element is drawn in.

    /// <summary>Stops writing the clips in force above the innermost layer as groups: the groups are closed, and the elements from here on carry the clips.</summary>
    private void FlattenClips()
    {
        var floor = _open.FindLastIndex(o => o.Kind == OpenKind.Layer) + 1;
        var first = _open.FindIndex(floor, o => o.Kind == OpenKind.Clip && o.Emitted);
        if (first < 0)
            return;

        var tail = _open.GetRange(first, _open.Count - first);
        _open.RemoveRange(first, tail.Count);
        for (var i = tail.Count - 1; i >= 0; i--)
        {
            if (tail[i].Emitted)
                CloseGroup(tail[i]);
        }

        foreach (var entry in tail)
        {
            if (entry.Kind == OpenKind.Clip)
                _open.Add(entry with { Emitted = false, Markup = null });
            else
                _open.Add(entry.Emitted ? Reopen(entry) : entry);
        }
    }

    /// <summary>The <c>clip-path</c> attribute that applies the flat clips above the innermost layer, or nothing when there are none.</summary>
    private string FlatClipAttribute()
    {
        var floor = _open.FindLastIndex(o => o.Kind == OpenKind.Layer) + 1;
        string? id = null;
        for (var i = floor; i < _open.Count; i++)
        {
            var clip = _open[i];
            if (clip.Kind != OpenKind.Clip || clip.Emitted || clip.ClipShape is not { } shape || !_ctm.TryInvert(out var inverse))
                continue;

            // The clip was pushed in the coordinates the canvas had then; the element is drawn in the ones it has now.
            var relative = Matrix3x2Value.From(clip.Ctm * inverse);
            if (!relative.IsIdentity)
                shape = shape.Insert(shape.Length - 2, $" transform=\"{_defs.Matrix(relative)}\"");

            id = _defs.Define("c", $"<clipPath id=\"{{id}}\"{(id is null ? string.Empty : $" clip-path=\"url(#{id})\"")}>{shape}</clipPath>");
        }

        return id is null ? string.Empty : $" clip-path=\"url(#{id})\"";
    }

    /// <summary>Pushes a clip: a group around what follows, or - while something is blended - a flat clip each element carries.</summary>
    private void PushClipShape(string shape)
    {
        if (_blend != PaintBlendMode.Normal)
        {
            Push(OpenKind.Clip, null, shape);
            return;
        }

        var id = _defs.Define("c", $"<clipPath id=\"{{id}}\">{shape}</clipPath>");
        Push(OpenKind.Clip, $"<g clip-path=\"url(#{id})\">", shape);
    }

    private static string BlendName(PaintBlendMode mode) => mode switch
    {
        PaintBlendMode.Multiply => "multiply",
        PaintBlendMode.Screen => "screen",
        PaintBlendMode.Overlay => "overlay",
        PaintBlendMode.Darken => "darken",
        PaintBlendMode.Lighten => "lighten",
        PaintBlendMode.ColorDodge => "color-dodge",
        PaintBlendMode.ColorBurn => "color-burn",
        PaintBlendMode.HardLight => "hard-light",
        PaintBlendMode.SoftLight => "soft-light",
        PaintBlendMode.Difference => "difference",
        PaintBlendMode.Exclusion => "exclusion",
        PaintBlendMode.Hue => "hue",
        PaintBlendMode.Saturation => "saturation",
        PaintBlendMode.Color => "color",
        PaintBlendMode.Luminosity => "luminosity",
        PaintBlendMode.Plus => "plus-lighter",
        _ => "normal",
    };

    // ---- clip, transform, blend ---------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void PushClip(Rect rect)
    {
        _clipStack.Push(rect);
        PushClipShape($"<rect x=\"{N(rect.X)}\" y=\"{N(rect.Y)}\" width=\"{N(rect.Width)}\" height=\"{N(rect.Height)}\"/>");
    }

    /// <inheritdoc/>
    public override void PushClip(GraphicsPath path)
    {
        // No simple bounding rectangle for an arbitrary path, so the tracked clip bound stays as it was (it is only used for culling).
        _clipStack.Push(_clipStack.Peek());
        var rule = path.FillMode == FillMode.EvenOdd ? " clip-rule=\"evenodd\"" : string.Empty;
        PushClipShape($"<path d=\"{PathData(path)}\"{rule}/>");
    }

    /// <inheritdoc/>
    public override void PushClipExclude(Rect rect)
    {
        // Everything except the rectangle: an even-odd path of a huge rectangle with the excluded one cut out of it.
        _clipStack.Push(_clipStack.Peek());
        const double big = 1e7;
        var d = $"M{N(-big)} {N(-big)}H{N(big)}V{N(big)}H{N(-big)}ZM{N(rect.X)} {N(rect.Y)}h{N(rect.Width)}v{N(rect.Height)}h{N(-rect.Width)}Z";
        PushClipShape($"<path d=\"{d}\" clip-rule=\"evenodd\"/>");
    }

    /// <inheritdoc/>
    public override void PopClip()
    {
        Pop(OpenKind.Clip);
        if (_clipStack.Count > 1)
            _clipStack.Pop();
    }

    /// <inheritdoc/>
    public override void PushTransform(Matrix3x2 matrix)
    {
        var value = Matrix3x2Value.From(matrix);
        Push(OpenKind.Transform, value.IsIdentity ? null : $"<g transform=\"{_defs.Matrix(value)}\">");
        _ctm = matrix.Then(_ctm);
    }

    /// <inheritdoc/>
    public override void PopTransform() => Pop(OpenKind.Transform);

    /// <inheritdoc/>
    public override void PushBlendMode(PaintBlendMode mode)
    {
        if (mode != PaintBlendMode.Normal)
            FlattenClips();

        Push(OpenKind.Blend, null);
        _blend = mode;
    }

    /// <inheritdoc/>
    public override void PopBlendMode() => Pop(OpenKind.Blend);

    /// <inheritdoc/>
    public override object SetAntiAliasSmoothingMode() => new object();

    /// <inheritdoc/>
    public override void ReturnPreviousSmoothingMode(object? prevMode)
    {
    }

    /// <inheritdoc/>
    public override GraphicsPath GetGraphicsPath() => new SvgGraphicsPath();

    // ---- tagging ------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void BeginMarkedContent(string structureType, int mcid)
    {
    }

    /// <inheritdoc/>
    public override void EndMarkedContent()
    {
    }

    /// <inheritdoc/>
    public override void BeginArtifact()
    {
    }

    /// <inheritdoc/>
    public override void BeginVariableText()
    {
    }

    /// <inheritdoc/>
    public override void EndVariableText()
    {
    }

    // ---- tiles and layers ---------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override (Canvas Graphics, Image Image)? CreateTile(double width, double height)
    {
        if (!(width > 0) || !(height > 0) || double.IsInfinity(width) || double.IsInfinity(height))
            return null;

        var tile = new SvgCanvas(_renderContext, _defs, new Rect(0, 0, width, height), _ppp, _options, isTile: true)
        {
            // Bookkeeping only (see Canvas.CreateTile): lets a reader inside the tile relate its space to the one outside.
            _ctm = _ctm,
        };
        return (tile, new SvgTileImage(width, height, tile));
    }

    /// <inheritdoc/>
    public override CanvasLayer? BeginLayer(LayerOptions options)
    {
        var region = options.Bounds ?? GetClip();
        if (options.BlendMode != PaintBlendMode.Normal)
            FlattenClips();

        var attributes = new StringBuilder("<g");

        if (options.Opacity < 1)
            attributes.Append(" opacity=\"").Append(N(Math.Clamp(options.Opacity, 0, 1))).Append('"');

        var filter = LayerFilter(options, region);
        if (filter is not null)
            attributes.Append(" filter=\"url(#").Append(filter).Append(")\"");

        // A layer with stated bounds is exactly that big, as the tile the default layer is made from would be.
        if (options.Bounds is { } bounds)
        {
            var clip = _defs.Define("c", $"<clipPath id=\"{{id}}\"><rect x=\"{N(bounds.X)}\" y=\"{N(bounds.Y)}\" width=\"{N(bounds.Width)}\" height=\"{N(bounds.Height)}\"/></clipPath>");
            attributes.Append(" clip-path=\"url(#").Append(clip).Append(")\"");
        }

        // A layer is an isolated group: what is drawn inside it blends with itself, and the finished group blends with what is below it.
        attributes.Append(Style("isolation:isolate", options.BlendMode == PaintBlendMode.Normal ? _blend : options.BlendMode)).Append('>');

        Push(OpenKind.Layer, attributes.ToString());
        _blend = PaintBlendMode.Normal;
        var index = _open.Count - 1;
        return new CanvasLayer(this, () => CloseLayer(index));
    }

    private void CloseLayer(int index)
    {
        if (_completed is not null)
            return;

        while (_open.Count > index + 1)
            Pop(_open[^1].Kind);

        if (_open.Count == index + 1 && _open[index].Kind == OpenKind.Layer)
            Pop(OpenKind.Layer);
    }

    /// <summary>The filter that applies the layer's colour matrix and effects, or null when it has none.</summary>
    private string? LayerFilter(LayerOptions options, Rect region)
    {
        if (options.ColorMatrix is null && options.Effects is not { Count: > 0 })
            return null;

        var primitives = new StringBuilder();
        var margin = 0d;
        if (options.ColorMatrix is { } matrix)
            primitives.Append(ColorMatrixPrimitive(matrix));

        if (options.Effects is { } effects)
        {
            foreach (var effect in effects)
            {
                switch (effect)
                {
                    case BlurEffect blur:
                        primitives.Append($"<feGaussianBlur stdDeviation=\"{N(blur.SigmaX)} {N(blur.SigmaY)}\"/>");
                        margin += 3 * Math.Max(blur.SigmaX, blur.SigmaY);
                        break;
                    case DropShadowEffect shadow:
                        primitives.Append($"<feDropShadow dx=\"{N(shadow.OffsetX)}\" dy=\"{N(shadow.OffsetY)}\" stdDeviation=\"{N(shadow.SigmaX)} {N(shadow.SigmaY)}\" flood-color=\"{SvgDefs.Hex(shadow.Color)}\" flood-opacity=\"{N(SvgDefs.Alpha(shadow.Color))}\"/>");
                        margin += 3 * Math.Max(shadow.SigmaX, shadow.SigmaY) + Math.Max(Math.Abs(shadow.OffsetX), Math.Abs(shadow.OffsetY));
                        break;
                    case ColorMatrixEffect colorMatrix:
                        primitives.Append(ColorMatrixPrimitive(colorMatrix.Matrix));
                        break;
                }
            }
        }

        // A colour matrix alone never draws outside the content, so with no stated bounds the region only has to be everything: the tracked
        // clip is not usable (it is in the coordinates of wherever it was pushed, which a transform may have moved this layer out of), and
        // the default region (the fill's bounding box plus 10%) cuts off a wide stroke and removes a lone horizontal line altogether.
        if (options.Bounds is null && margin == 0)
        {
            const double everything = 1e6;
            return _defs.Define("f",
                $"<filter id=\"{{id}}\" filterUnits=\"userSpaceOnUse\" x=\"-{everything:0}\" y=\"-{everything:0}\" width=\"{2 * everything:0}\" height=\"{2 * everything:0}\" color-interpolation-filters=\"sRGB\">{primitives}</filter>");
        }

        var area = new Rect(region.X - margin, region.Y - margin, region.Width + 2 * margin, region.Height + 2 * margin);
        return _defs.Define("f",
            $"<filter id=\"{{id}}\" filterUnits=\"userSpaceOnUse\" x=\"{N(area.X)}\" y=\"{N(area.Y)}\" width=\"{N(area.Width)}\" height=\"{N(area.Height)}\" color-interpolation-filters=\"sRGB\">{primitives}</filter>");
    }

    private string ColorMatrixPrimitive(ColorMatrix matrix)
    {
        // Canvas colours are row vectors multiplied by the matrix, so output channel j reads column j.
        var m = matrix.Linear;
        var o = matrix.Offset;
        var values = string.Join(' ',
            N(m.M11), N(m.M21), N(m.M31), N(m.M41), N(o.X),
            N(m.M12), N(m.M22), N(m.M32), N(m.M42), N(o.Y),
            N(m.M13), N(m.M23), N(m.M33), N(m.M43), N(o.Z),
            N(m.M14), N(m.M24), N(m.M34), N(m.M44), N(o.W));
        return $"<feColorMatrix type=\"matrix\" values=\"{values}\"/>";
    }

    // ---- the raster seam ----------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override RasterRegion? BeginRasterSurface(Rect layoutBounds, double? dpiOverride = null) =>
        RasterSurfaceFactory.Create(_renderContext, _ppp, layoutBounds, dpiOverride ?? _renderContext.RasterizationDpi, _renderContext.MaxRasterPixels, TransformScale, _ctm);

    /// <inheritdoc/>
    public override void DrawRaster(RasterSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var rect = surface.LayoutRect;
        var png = RasterSurfaceEncoding.EncodePng(surface, RasterSurfaceEncoding.IsOpaque(surface));
        Line($"<image x=\"{N(rect.X)}\" y=\"{N(rect.Y)}\" width=\"{N(rect.Width)}\" height=\"{N(rect.Height)}\" preserveAspectRatio=\"none\" href=\"data:image/png;base64,{Convert.ToBase64String(png)}\"{Style()}/>");
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        // The document canvas belongs to its composer, and a tile is completed on first use (or when saved); nothing is released here.
        if (_isTile)
            CompleteContent();
    }

    // ---- shapes -------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override void DrawLine(Pen pen, double x1, double y1, double x2, double y2) =>
        Stroke(pen, $"<line x1=\"{N(x1)}\" y1=\"{N(y1)}\" x2=\"{N(x2)}\" y2=\"{N(y2)}\"");

    /// <inheritdoc/>
    public override void DrawRectangle(Pen pen, double x, double y, double width, double height) =>
        Stroke(pen, $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"{N(height)}\"");

    /// <inheritdoc/>
    public override void DrawRectangle(Brush brush, double x, double y, double width, double height) =>
        Fill(brush, $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(width)}\" height=\"{N(height)}\"");

    /// <inheritdoc/>
    public override void DrawPath(Pen pen, GraphicsPath path) => Stroke(pen, $"<path d=\"{PathData(path)}\"");

    /// <inheritdoc/>
    public override void DrawPath(Brush brush, GraphicsPath path) =>
        Fill(brush, $"<path d=\"{PathData(path)}\"" + (path.FillMode == FillMode.EvenOdd ? " fill-rule=\"evenodd\"" : string.Empty));

    /// <inheritdoc/>
    public override void DrawPolygon(Brush brush, PaintPoint[] points)
    {
        if (points.Length < 3)
            return;

        var sb = new StringBuilder("<polygon points=\"");
        for (var i = 0; i < points.Length; i++)
        {
            if (i > 0)
                sb.Append(' ');
            _defs.AppendNum(sb, points[i].X).Append(',');
            _defs.AppendNum(sb, points[i].Y);
        }

        Fill(brush, sb.Append('"').ToString());
    }

    /// <summary>
    /// The path's <c>d</c> data, written from its curves (not flattened): move, line and cubic commands, with <c>Z</c> for a closed figure.
    /// A path's coordinates are in points, unlike every other coordinate a canvas is given (the PDF and raster canvases read them the same
    /// way: the ambient transform's translation is divided by <see cref="PixelsPerPoint"/> but a path is not), so they are scaled to the
    /// document's layout units here.
    /// </summary>
    internal string PathData(GraphicsPath path)
    {
        var sb = new StringBuilder();
        foreach (var contour in path.GetCurveContours())
        {
            _defs.AppendNum(sb.Append('M'), contour.Start.X * _ppp).Append(' ');
            _defs.AppendNum(sb, contour.Start.Y * _ppp);
            foreach (var command in contour.Commands)
            {
                if (command.Kind == PathCommandKind.Line)
                {
                    _defs.AppendNum(sb.Append('L'), command.End.X * _ppp).Append(' ');
                    _defs.AppendNum(sb, command.End.Y * _ppp);
                }
                else
                {
                    _defs.AppendNum(sb.Append('C'), command.Control1.X * _ppp).Append(' ');
                    _defs.AppendNum(sb, command.Control1.Y * _ppp).Append(' ');
                    _defs.AppendNum(sb, command.Control2.X * _ppp).Append(' ');
                    _defs.AppendNum(sb, command.Control2.Y * _ppp).Append(' ');
                    _defs.AppendNum(sb, command.End.X * _ppp).Append(' ');
                    _defs.AppendNum(sb, command.End.Y * _ppp);
                }
            }

            if (contour.Closed)
                sb.Append('Z');
        }

        return sb.ToString();
    }

    /// <summary>Writes <paramref name="shape"/> (an opening tag with its geometry, no closing) filled with <paramref name="brush"/>.</summary>
    private void Fill(Brush brush, string shape)
    {
        if (brush is HatchBrush hatch)
        {
            if (hatch.ToTileBrush(this) is not { } hatchTile)
                return;

            brush = hatchTile;
        }

        if (brush is ConicGradientBrush conic)
        {
            FillConic(conic, shape);
            return;
        }

        if (PaintValue(brush, "fill") is not { } paint)
            return;

        Line($"{shape}{paint}{Style()}/>");
    }

    private void Stroke(Pen pen, string shape)
    {
        if (pen.Paint is not { } brush)
            return;

        if (brush is HatchBrush hatch)
        {
            if (hatch.ToTileBrush(this) is not { } hatchTile)
                return;

            brush = hatchTile;
        }

        // A conic gradient has no stroke form in SVG: the stroke is drawn in white into a mask, and the wedges are painted through it.
        var conic = brush as ConicGradientBrush;
        if (conic is not null)
        {
            if (conic.Stops.Count == 0)
                return;

            brush = new SolidBrush(PaintColor.White);
        }

        if (PaintValue(brush, "stroke") is not { } paint)
            return;

        // A pen's width and dash lengths are in points, like a path's coordinates (see PathData).
        var sb = new StringBuilder(shape).Append(" fill=\"none\"").Append(paint);
        var width = pen.Width * _ppp;
        if (width > 0)
            sb.Append(" stroke-width=\"").Append(N(width)).Append('"');
        else
            sb.Append(" stroke-width=\"1\" vector-effect=\"non-scaling-stroke\""); // a zero width draws the thinnest line the device can

        switch (pen.LineCap)
        {
            case LineCap.Round: sb.Append(" stroke-linecap=\"round\""); break;
            case LineCap.Square: sb.Append(" stroke-linecap=\"square\""); break;
        }

        switch (pen.LineJoin)
        {
            case LineJoin.Round: sb.Append(" stroke-linejoin=\"round\""); break;
            case LineJoin.Bevel: sb.Append(" stroke-linejoin=\"bevel\""); break;
        }

        // An unset limit (0) is 10 on the PDF and raster canvases; SVG's own default is 4, which would bevel the sharp joins they miter.
        var miterLimit = pen.MiterLimit >= 1 ? pen.MiterLimit : 10;
        if (miterLimit != 4)
            sb.Append(" stroke-miterlimit=\"").Append(N(miterLimit)).Append('"');

        if (ResolveDashes(pen) is { } dashes)
        {
            sb.Append(" stroke-dasharray=\"").Append(string.Join(' ', Array.ConvertAll(dashes, d => N(d * _ppp)))).Append('"');
            if (pen.DashStyle == DashStyle.Custom && pen.DashOffset != 0)
                sb.Append(" stroke-dashoffset=\"").Append(N(pen.DashOffset * _ppp)).Append('"');
        }

        if (conic is not null)
        {
            var reach = conic.OuterRadius * 1.1;
            var mask = _defs.Define("m",
                $"<mask id=\"{{id}}\" maskUnits=\"userSpaceOnUse\" x=\"{N(conic.Center.X - reach)}\" y=\"{N(conic.Center.Y - reach)}\" width=\"{N(2 * reach)}\" height=\"{N(2 * reach)}\">{sb}/></mask>");
            Line($"<g mask=\"url(#{mask})\"{Style()}>");
            _depth++;
            EmitConic(conic);
            _depth--;
            Line("</g>");
            return;
        }

        Line(sb.Append(Style()).Append("/>").ToString());
    }

    /// <summary>The dash lengths a pen draws with, in user units: presets are multiples of the pen's width, as on the PDF canvas.</summary>
    private static double[]? ResolveDashes(Pen pen)
    {
        var w = pen.Width;
        if (!(w > 0))
            return null;

        switch (pen.DashStyle)
        {
            case DashStyle.Dash:
                // A thin dashed line reads better with longer dashes than the standard 3:1 ratio.
                return w < 2 ? [4 * w, 4 * w] : [3 * w, w];
            case DashStyle.Dot: return [w];
            case DashStyle.DashDot: return [3 * w, w, w, w];
            case DashStyle.DashDotDot: return [3 * w, w, w, w, w, w];
            case DashStyle.Custom:
                if (pen.DashPattern is not { Length: > 0 } pattern)
                    return null;

                var list = new List<double>(pattern);

                // An odd count is padded the way GDI+ (and the PDF writer) does.
                if (list.Count % 2 == 1)
                    list.Add(0.2 * w);

                return [.. list];
            default:
                return null;
        }
    }

    // ---- brushes ------------------------------------------------------------------------------------------

    /// <summary>
    /// The paint attributes (<paramref name="attribute"/> is <c>fill</c> or <c>stroke</c>) that paint with <paramref name="brush"/>, or null
    /// when it paints nothing (a fully transparent colour, a gradient without stops).
    /// </summary>
    private string? PaintValue(Brush brush, string attribute)
    {
        switch (brush)
        {
            case SolidBrush solid:
                if (solid.PaintColor.A == 0)
                    return null;

                var alpha = SvgDefs.Alpha(solid.PaintColor);
                var color = $" {attribute}=\"{SvgDefs.Hex(solid.PaintColor)}\"";
                return alpha < 1 ? color + $" {attribute}-opacity=\"{N(alpha)}\"" : color;

            case LinearGradientBrush linear:
                if (linear.Stops.Count == 0)
                    return null;

                return Url(attribute, _defs.Define("g",
                    $"<linearGradient id=\"{{id}}\" gradientUnits=\"userSpaceOnUse\" x1=\"{N(linear.Start.X)}\" y1=\"{N(linear.Start.Y)}\" x2=\"{N(linear.End.X)}\" y2=\"{N(linear.End.Y)}\"{Spread(linear.Spread)}>{Stops(linear.Stops)}</linearGradient>"));

            case RadialGradientBrush radial:
                if (radial.Stops.Count == 0)
                    return null;

                return Url(attribute, _defs.Define("g", RadialGradient(radial)));

            case TileBrush tile:
                return Url(attribute, TilePattern(tile));

            case HatchBrush hatch:
                return hatch.ToTileBrush(this) is { } converted ? Url(attribute, TilePattern(converted)) : null;

            default:
                throw new NotSupportedException($"Unknown brush type {brush.GetType()}");
        }
    }

    private static string Url(string attribute, string id) => $" {attribute}=\"url(#{id})\"";

    private static string Spread(GradientSpread spread) => spread == GradientSpread.Repeat ? " spreadMethod=\"repeat\"" : string.Empty;

    private string Stops(IReadOnlyList<GradientStop> stops)
    {
        var sb = new StringBuilder();
        foreach (var stop in stops)
        {
            sb.Append("<stop offset=\"").Append(N(Math.Clamp(stop.Position, 0, 1))).Append("\" stop-color=\"").Append(SvgDefs.Hex(stop.PaintColor)).Append('"');
            if (stop.PaintColor.A < 255)
                sb.Append(" stop-opacity=\"").Append(N(SvgDefs.Alpha(stop.PaintColor))).Append('"');
            sb.Append("/>");
        }

        return sb.ToString();
    }

    private string RadialGradient(RadialGradientBrush radial)
    {
        // SVG's radial gradient is a circle; an ellipse is that circle squashed by a transform about its centre. The focus is expressed in
        // the circle's own space, so it is stretched the opposite way.
        var rx = radial.RadiusX > 0 ? radial.RadiusX : 1e-9;
        var ry = radial.RadiusY > 0 ? radial.RadiusY : 1e-9;
        var ratio = ry / rx;
        var fy = radial.Center.Y + (radial.Focus.Y - radial.Center.Y) / ratio;

        var transform = new StringBuilder();
        if (radial.Transform is { } brushTransform)
            transform.Append(_defs.Matrix(Matrix3x2Value.From(brushTransform)));
        if (Math.Abs(ratio - 1) > 1e-9)
        {
            if (transform.Length > 0)
                transform.Append(' ');
            transform.Append($"translate({N(radial.Center.X)} {N(radial.Center.Y)}) scale(1 {SvgDefs.Factor(ratio)}) translate({N(-radial.Center.X)} {N(-radial.Center.Y)})");
        }

        var attributes = $"gradientUnits=\"userSpaceOnUse\" cx=\"{N(radial.Center.X)}\" cy=\"{N(radial.Center.Y)}\" r=\"{N(rx)}\" fx=\"{N(radial.Focus.X)}\" fy=\"{N(fy)}\"";
        if (radial.FocusRadius > 0)
            attributes += $" fr=\"{N(radial.FocusRadius)}\"";
        if (transform.Length > 0)
            attributes += $" gradientTransform=\"{transform}\"";

        return $"<radialGradient id=\"{{id}}\" {attributes}{Spread(radial.Spread)}>{Stops(radial.Stops)}</radialGradient>";
    }

    /// <summary>A repeating tile as an SVG pattern: the brush's cells in brush space, carried into user space by its transform.</summary>
    private string TilePattern(TileBrush tile)
    {
        var imageId = ImageId(tile.Tile);
        var sx = tile.CellWidth / Math.Max(tile.Tile.Width, 1e-9);
        var sy = tile.CellHeight / Math.Max(tile.Tile.Height, 1e-9);
        var rendering = RenderingStyle(tile.Tile, tile.Sampling);
        var transform = Matrix3x2Value.From(tile.Transform);
        var transformAttribute = transform.IsIdentity ? string.Empty : $" patternTransform=\"{_defs.Matrix(transform)}\"";

        return _defs.Define("p",
            $"<pattern id=\"{{id}}\" patternUnits=\"userSpaceOnUse\" width=\"{N(tile.CellWidth)}\" height=\"{N(tile.CellHeight)}\"{transformAttribute}><use href=\"#{imageId}\" transform=\"scale({SvgDefs.Factor(sx)} {SvgDefs.Factor(sy)})\"{rendering}/></pattern>");
    }

    // ---- conic gradients ----------------------------------------------------------------------------------

    private const int ConicWedges = 360;

    /// <summary>
    /// SVG has no conic gradient, so it is written as a fan of thin wedges, each in the colour the gradient has at its middle, clipped to the
    /// shape. The wedges overlap a little so no seam shows between them.
    /// </summary>
    private void FillConic(ConicGradientBrush conic, string shape)
    {
        if (conic.Stops.Count == 0)
            return;

        var clip = _defs.Define("c", $"<clipPath id=\"{{id}}\">{shape.Replace(" fill-rule=", " clip-rule=", StringComparison.Ordinal)}/></clipPath>");
        Line($"<g clip-path=\"url(#{clip})\"{Style()}>");
        _depth++;
        EmitConic(conic);
        _depth--;
        Line("</g>");
    }

    /// <summary>
    /// The picture of a conic gradient: the wedges, or - when <see cref="SvgComposerOptions.ConicGradients"/> asks for it - a CSS
    /// <c>conic-gradient()</c> in a <c>&lt;foreignObject&gt;</c> inside a <c>&lt;switch&gt;</c> whose fallback is the wedges, so a viewer without
    /// XHTML support (anything that is not a browser) still gets a picture.
    /// </summary>
    private void EmitConic(ConicGradientBrush conic)
    {
        if (_options.ConicGradients != SvgConicGradientMode.Css)
        {
            EmitConicWedges(conic);
            return;
        }

        Line("<switch>");
        _depth++;
        var reach = conic.OuterRadius;
        var first = conic.Stops[0].Position;
        var stops = new StringBuilder();
        foreach (var stop in conic.Stops)
        {
            var c = stop.PaintColor.ToRgb();
            stops.Append(", rgba(").Append(c.R).Append(',').Append(c.G).Append(',').Append(c.B).Append(',').Append(N(SvgDefs.Alpha(stop.PaintColor))).Append(") ")
                .Append(SvgDefs.Factor((stop.Position - first) * 180 / Math.PI)).Append("deg");
        }

        Line($"<foreignObject requiredExtensions=\"http://www.w3.org/1999/xhtml\" x=\"{N(conic.Center.X - reach)}\" y=\"{N(conic.Center.Y - reach)}\" width=\"{N(2 * reach)}\" height=\"{N(2 * reach)}\">");
        _depth++;
        Line($"<div xmlns=\"http://www.w3.org/1999/xhtml\" style=\"width:100%;height:100%;background:conic-gradient(from {SvgDefs.Factor(first * 180 / Math.PI)}deg at 50% 50%{stops})\"></div>");
        _depth--;
        Line("</foreignObject>");
        Line("<g>");
        _depth++;
        EmitConicWedges(conic);
        _depth--;
        Line("</g>");
        _depth--;
        Line("</switch>");
    }

    private void EmitConicWedges(ConicGradientBrush conic)
    {
        var start = conic.Stops[0].Position;
        var step = 2 * Math.PI / ConicWedges;
        var radius = conic.OuterRadius / Math.Cos(step);
        for (var i = 0; i < ConicWedges; i++)
        {
            var a0 = start + i * step;
            var a1 = a0 + step;
            var color = ConicColorAt(conic, (a0 + a1) / 2);
            if (color.A == 0)
                continue;

            var p0 = Polar(conic.Center, radius, a0 - step * 0.05);
            var p1 = Polar(conic.Center, radius, a1 + step * 0.05);
            var alpha = color.A < 255 ? $" fill-opacity=\"{N(SvgDefs.Alpha(color))}\"" : string.Empty;
            Line($"<path d=\"M{N(conic.Center.X)} {N(conic.Center.Y)}L{N(p0.X)} {N(p0.Y)}L{N(p1.X)} {N(p1.Y)}Z\" fill=\"{SvgDefs.Hex(color)}\"{alpha}/>");
        }
    }

    /// <summary>The point at <paramref name="angle"/> (0 at 12 o'clock, growing clockwise) and <paramref name="radius"/> from <paramref name="center"/>.</summary>
    private static PaintPoint Polar(PaintPoint center, double radius, double angle) =>
        new(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

    private static PaintColor ConicColorAt(ConicGradientBrush conic, double angle)
    {
        var stops = conic.Stops;
        if (stops.Count == 0)
            return PaintColor.Transparent;

        var turn = 2 * Math.PI;
        var start = stops[0].Position;
        angle = start + (angle - start - turn * Math.Floor((angle - start) / turn));

        if (angle <= stops[0].Position)
            return stops[0].PaintColor;

        for (var i = 1; i < stops.Count; i++)
        {
            if (angle > stops[i].Position)
                continue;

            var a = stops[i - 1];
            var b = stops[i];
            var span = b.Position - a.Position;
            var t = span <= 0 ? 1 : (angle - a.Position) / span;
            var (ar, ag, ab) = a.PaintColor.ToRgb();
            var (br, bg, bb) = b.PaintColor.ToRgb();
            return PaintColor.FromArgb(Lerp(a.PaintColor.A, b.PaintColor.A, t), Lerp(ar, br, t), Lerp(ag, bg, t), Lerp(ab, bb, t));
        }

        return stops[^1].PaintColor;
    }

    private static int Lerp(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);
}

using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text;
using PeachDrawing.Core;
using PeachDrawing.Core.ColorGlyphs;
using System;
using System.Collections.Generic;

namespace PeachDrawing;

public sealed partial class RasterCanvas
{
    // Measurement is the layout engine's own routine, so paint can never disagree with layout.
    /// <inheritdoc/>
    public override Size MeasureString(string str, Font font, ShapeSettings? features = null)
    {
        var size = TextMeasurement.Measure(str, font.Typeface!, font.Size, font.SyntheticStyle, features ?? ShapeSettings.Default);
        return new Size(size.Width * _pixelsPerPoint, size.Height * _pixelsPerPoint);
    }

    // Real production fonts always resolve a typeface (Font.Typeface never returns null for any concrete
    // Font this codebase constructs); only a test double's font stub can, and this raster path never sees
    // one of those.
    /// <inheritdoc/>
    public override int CountShapedGlyphs(string str, Font font, ShapeSettings? features = null) =>
        Shaper.Shape(font.Typeface!, str, features ?? ShapeSettings.Default).Glyphs.Count;

    /// <inheritdoc/>
    public override void MeasureString(string str, Font font, double maxWidth, out int charFit, out double charFitWidth) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public override GraphicsPath? GetTextOutline(string str, Font font, PaintPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null) =>
        TextOutlineBuilder.Build(GetGraphicsPath(), font, _pixelsPerPoint, str, baselineOrigin, letterSpacing,
            features ?? ShapeSettings.Default);

    /// <inheritdoc/>
    public override IReadOnlyList<InkSpan>? GetInkCrossings(string str, Font font, PaintPoint origin, double bandTop, double bandBottom,
        double letterSpacing = 0, ShapeSettings? features = null)
    {
        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (unitsPerEm == 0 || bandBottom <= bandTop)
            return null;

        // The baseline DrawString paints this run on, and the size of a design unit, both in the canvas's own units.
        var baselineY = origin.Y + font.Size * typeface.Metrics.CellAscent / unitsPerEm * _pixelsPerPoint;
        var scale = font.Size * _pixelsPerPoint / unitsPerEm;

        if (InkCrossings.Measure(typeface, str, scale, bandTop - baselineY, bandBottom - baselineY, letterSpacing,
                features ?? ShapeSettings.Default) is not { } relative)
            return null;

        var spans = new InkSpan[relative.Count];
        for (var i = 0; i < spans.Length; i++)
            spans[i] = new InkSpan(relative[i].Start + origin.X, relative[i].End + origin.X);

        return spans;
    }

    /// <inheritdoc/>
    public override void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing = 0,
        FontPalette? fontPalette = null, ShapeSettings? features = null)
    {
        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (InvisibleText || unitsPerEm == 0 || str.Length == 0)
            return;

        // Where the PDF renderer puts the baseline: the run's top-left plus the cell ascent, in points.
        var lineSpace = typeface.Metrics.LineSpacing * font.Size / unitsPerEm;
        var baselineY = point.Y / _pixelsPerPoint + lineSpace * typeface.Metrics.CellAscent / typeface.Metrics.LineSpacing;
        var originX = point.X / _pixelsPerPoint;
        var spacing = letterSpacing / _pixelsPerPoint;
        var resolved = features ?? ShapeSettings.Default;

        var glyphs = Shaper.Shape(typeface, str, resolved).Glyphs;
        var scale = font.Size / unitsPerEm;
        var skew = ItalicSkew(font);
        var contours = new FlatPath();
        var toDevice = UserToDevice;
        var tolerance = 0.1 / Math.Max(toDevice.MaxScale, 1e-9);
        var hinting = HintingRequest(font, toDevice);
        var svgOverrides = typeface.HasSvgGlyphs ? ToGlyphOverrides(fontPalette) : null;

        var penX = originX;
        foreach (var glyph in glyphs)
        {
            if (typeface.HasBitmapGlyphs && typeface.TryGetBitmap((ushort)glyph.GlyphIndex, font.Size, out var bitmap))
            {
                DrawBitmapGlyph(typeface, glyph.GlyphIndex, bitmap, font.Size, penX + glyph.XOffset * scale, baselineY - glyph.YOffset * scale);
            }
            else if (typeface.HasSvgGlyphs && TryDrawSvgGlyph(typeface, glyph.GlyphIndex, font.Size, color, fontPalette?.BasePaletteIndex ?? 0,
                         svgOverrides, penX + glyph.XOffset * scale, baselineY - glyph.YOffset * scale))
            {
                // drawn from the glyph's own SVG document
            }
            else if (typeface.HasColorGlyphs && TryDrawColorGlyph(typeface, glyph.GlyphIndex, font.Size, color, fontPalette?.BasePaletteIndex ?? 0,
                         svgOverrides ?? ToGlyphOverrides(fontPalette), penX + glyph.XOffset * scale, baselineY - glyph.YOffset * scale))
            {
                // drawn from the glyph's COLR/CPAL color artwork
            }
            else if (hinting is { } request && typeface.TryGetOutline((ushort)glyph.GlyphIndex, request, out var fitted))
            {
                AddPixelGlyph(contours, fitted, toDevice, penX + glyph.XOffset * scale, baselineY - glyph.YOffset * scale, skew, tolerance,
                    request.GridFitting == GridFitting.Monochrome);
            }
            else if (typeface.TryGetOutline((ushort)glyph.GlyphIndex, out var outline))
            {
                AddGlyph(contours, outline, penX + glyph.XOffset * scale, baselineY - glyph.YOffset * scale, scale, skew, tolerance);
            }

            penX += (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale + spacing;
        }

        var paint = PaintSource.FromColor(color);
        FillGlyphs(contours, paint, font, toDevice);
    }

    /// <summary>Draws a bitmap colour glyph (CBDT/sbix) at a glyph origin given in points, the way the PDF backend places it.</summary>
    private void DrawBitmapGlyph(Typeface typeface, int glyphId, EmbeddedBitmap bitmap, double fontSize, double originX, double baselineY)
    {
        var image = BitmapGlyphImages.Get(typeface, glyphId, bitmap);
        if (image is null)
            return;

        var scale = fontSize / bitmap.Ppem;
        var width = bitmap.Width * scale;
        var height = bitmap.Height * scale;
        var left = originX + bitmap.BearingX * scale;
        var top = baselineY - bitmap.BearingTop * scale;

        // The picture is a layout-unit rectangle to DrawImage, like every other image.
        DrawImage(image, new Rect(left * _pixelsPerPoint, top * _pixelsPerPoint, width * _pixelsPerPoint, height * _pixelsPerPoint));
    }

    /// <summary>
    /// Draws a glyph's COLR/CPAL color artwork (a v1 paint graph, else v0 layers) at a glyph origin given in points, through the shared
    /// <see cref="ColorGlyphPainter"/>, so this canvas paints color fonts exactly as every other backend does. False when the glyph has
    /// no color artwork (it is an ordinary outline glyph, which the caller draws).
    /// </summary>
    private bool TryDrawColorGlyph(Typeface typeface, int glyphId, double fontSize, PaintColor color, int paletteIndex,
        IReadOnlyDictionary<int, PaintColor>? overrides, double originX, double baselineY)
    {
        if (typeface.GetColorPaint((ushort)glyphId) is null && !typeface.TryGetColorLayers((ushort)glyphId, out _))
            return false;

        // Path coordinates are in this canvas's user units (points * pixelsPerPoint); the glyph's design units scale to those.
        var painter = new ColorGlyphPainter(typeface, fontSize * _pixelsPerPoint, color, yDown: true, paletteIndex, overrides);
        painter.Paint((ushort)glyphId, painter.Placement(originX * _pixelsPerPoint, baselineY * _pixelsPerPoint), new CanvasColorGlyphTarget(this) { SupportsAdditiveComposite = true, SupportsPeriodicConeGradients = true });
        return true;
    }

    /// <summary>
    /// Draws a glyph from its SVG document (OpenType SVG) at a glyph origin given in points, through the SVG renderer on this surface, so
    /// what a filter, a shadow or a flattened region shows is the glyph a PDF page would show, in the font's palette colours. False when the
    /// glyph has no document (or has a COLR paint, which comes first), or the document cannot be used: the caller draws the outline.
    /// </summary>
    private bool TryDrawSvgGlyph(Typeface typeface, int glyphId, double fontSize, PaintColor color, int paletteIndex,
        IReadOnlyDictionary<int, PaintColor>? overrides, double originX, double baselineY)
    {
        if (typeface.GetColorPaint((ushort)glyphId) is not null || typeface.TryGetColorLayers((ushort)glyphId, out _) ||
            !typeface.TryGetSvgGlyph((ushort)glyphId, out var svg))
            return false;

        // Null when this canvas's RenderContext has no SVG engine to render one with (e.g. a standalone
        // canvas, which has no SVG rendering capability at all) - the caller draws the outline instead.
        _svgGlyphs ??= _adapter.CreateSvgGlyphPainter(this);
        if (_svgGlyphs is null)
            return false;

        return _svgGlyphs.TryPaint(typeface, (ushort)glyphId, svg, fontSize, originX, baselineY, color, paletteIndex, overrides);
    }

    private ISvgGlyphPainter? _svgGlyphs;

    private static Dictionary<int, PaintColor>? ToGlyphOverrides(FontPalette? palette)
    {
        if (palette is not { Overrides.Count: > 0 })
            return null;

        var overrides = new Dictionary<int, PaintColor>(palette.Overrides.Count);
        foreach (var (entry, colour) in palette.Overrides)
            overrides[entry] = colour;

        return overrides;
    }

    /// <inheritdoc/>
    public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, Font font, PaintColor color)
    {
        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (InvisibleText || unitsPerEm == 0)
            return;

        var scale = font.Size / unitsPerEm;
        var toDevice = UserToDevice;
        var tolerance = 0.1 / Math.Max(toDevice.MaxScale, 1e-9);
        var contours = new FlatPath();
        var hinting = HintingRequest(font, toDevice);

        foreach (var placement in glyphs)
        {
            var glyphX = placement.X / _pixelsPerPoint;
            var glyphY = placement.Y / _pixelsPerPoint;
            if (typeface.HasBitmapGlyphs && typeface.TryGetBitmap((ushort)placement.GlyphIndex, font.Size, out var bitmap))
                DrawBitmapGlyph(typeface, placement.GlyphIndex, bitmap, font.Size, glyphX, glyphY);
            else if (typeface.HasSvgGlyphs && TryDrawSvgGlyph(typeface, placement.GlyphIndex, font.Size, color, 0, null, glyphX, glyphY))
            {
                // drawn from the glyph's own SVG document
            }
            else if (typeface.HasColorGlyphs && TryDrawColorGlyph(typeface, placement.GlyphIndex, font.Size, color, 0, null, glyphX, glyphY))
            {
                // drawn from the glyph's COLR/CPAL color artwork
            }
            else if (hinting is { } request && typeface.TryGetOutline((ushort)placement.GlyphIndex, request, out var fitted))
                AddPixelGlyph(contours, fitted, toDevice, placement.X / _pixelsPerPoint, placement.Y / _pixelsPerPoint, 0, tolerance,
                    request.GridFitting == GridFitting.Monochrome);
            else if (typeface.TryGetOutline((ushort)placement.GlyphIndex, out var outline))
                AddGlyph(contours, outline, placement.X / _pixelsPerPoint, placement.Y / _pixelsPerPoint, scale, 0, tolerance);
        }

        FillGlyphs(contours, PaintSource.FromColor(color), font, toDevice);
    }

    /// <summary>
    /// What to ask the typeface for when text is to be hinted: the size in device pixels per em, one for each axis, and the kind of
    /// hinting. Null when hinting is off or means nothing here: hinting fits outlines to a pixel grid, so it needs the text to reach the
    /// pixels unrotated and unskewed, though not necessarily at the same scale in both directions (a font's hinting instructions fit a
    /// stretched grid as well as a square one; a rotation or a skew would turn the fitted grid into something else no font expects).
    /// </summary>
    private OutlineRequest? HintingRequest(Font font, in Affine toDevice)
    {
        var mode = _adapter.TextHinting;
        if (mode == TextHinting.None)
            return null;

        // a pure scale and translation, no rotation or skew, and no mirroring; the two axes may scale differently (a non-square output
        // DPI, or a non-uniform CTM), which is exactly the real-world trigger for non-square-pixel hinting
        var scaleX = toDevice.M11;
        var scaleY = toDevice.M22;
        if (toDevice.M12 != 0 || toDevice.M21 != 0 || !(scaleX > 0) || !(scaleY > 0))
            return null;

        return new OutlineRequest
        {
            PixelsPerEmX = font.Size * scaleX,
            PixelsPerEmY = font.Size * scaleY,
            GridFitting = mode == TextHinting.Monochrome ? GridFitting.Monochrome : GridFitting.Standard,
            StemDarkening = _adapter.TextStemDarkening,
        };
    }

    /// <summary>
    /// Appends an outline in device pixels (a grid-fitted one, or a scaled one) to <paramref name="target"/>, in user space, with its origin
    /// on the baseline at (<paramref name="x"/>, <paramref name="y"/>) in user space. A grid-fitted glyph has its origin moved to a
    /// whole device pixel vertically, so the baseline the hinting was done for lies on a pixel edge; horizontally it stays where layout put
    /// it, unless the outline was fitted in both directions (<paramref name="fitX"/>, monochrome hinting), when the origin is on a pixel
    /// edge too, as the stems were fitted relative to it.
    /// </summary>
    private static void AddPixelGlyph(FlatPath target, GlyphOutline outline, in Affine toDevice, double x, double y, double skew, double tolerance,
        bool fitX)
    {
        var (originX, originY) = toDevice.Apply(x, y);
        if (outline.IsGridFitted)
        {
            originY = Math.Round(originY);
            if (fitX)
                originX = Math.Round(originX);
        }

        if (toDevice.Invert() is not { } toUser)
            return;

        // The outline is in device pixels with y up; the device has y down. Mapped back to user space so the caller's transform to the
        // device puts every point exactly where it was fitted.
        double X(OutlinePoint p) => originX + p.X + skew * p.Y;
        double Y(OutlinePoint p) => originY - p.Y;

        (double, double) ToUser(OutlinePoint p) => toUser.Apply(X(p), Y(p));

        for (var ci = 0; ci < outline.Contours.Count; ci++)
        {
            var contour = outline.Contours[ci];
            var (cx, cy) = ToUser(contour.Start);
            target.MoveTo(cx, cy);

            for (var si = 0; si < contour.Segments.Count; si++)
            {
                var segment = contour.Segments[si];
                var (ex, ey) = ToUser(segment.End);
                if (segment.IsCubic)
                {
                    var (c1x, c1y) = ToUser(segment.Control1);
                    var (c2x, c2y) = ToUser(segment.Control2);
                    target.CubicTo(cx, cy, c1x, c1y, c2x, c2y, ex, ey, tolerance);
                }
                else
                {
                    target.LineTo(ex, ey);
                }

                cx = ex;
                cy = ey;
            }

            target.Close();
        }
    }

    private static double ItalicSkew(Font font)
    {
        var simulated = (font.SyntheticStyle & SyntheticStyle.Italic) != 0;
        return simulated ? font.ObliqueSkewSinus ?? TextMeasurement.ItalicSkewAngleSinus : 0;
    }

    /// <summary>
    /// Appends <paramref name="outline"/> (font design units, y-up) to <paramref name="target"/> in user space
    /// with its origin on the baseline at (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    private static void AddGlyph(FlatPath target, GlyphOutline outline, double x, double y, double scale, double skew, double tolerance)
    {
        // Faux italic shears about the baseline: points above it move right.
        double X(OutlinePoint p) => x + p.X * scale + skew * p.Y * scale;
        double Y(OutlinePoint p) => y - p.Y * scale;

        for (var ci1 = 0; ci1 < outline.Contours.Count; ci1++)
        {
            OutlineContour contour = outline.Contours[ci1];
            var cx = X(contour.Start);
            var cy = Y(contour.Start);
            target.MoveTo(cx, cy);

            for (var si2 = 0; si2 < contour.Segments.Count; si2++)
            {
                OutlineSegment segment = contour.Segments[si2];
                var ex = X(segment.End);
                var ey = Y(segment.End);
                if (segment.IsCubic)
                    target.CubicTo(cx, cy, X(segment.Control1), Y(segment.Control1), X(segment.Control2), Y(segment.Control2), ex, ey, tolerance);
                else
                    target.LineTo(ex, ey);

                cx = ex;
                cy = ey;
            }

            target.Close();
        }
    }

    private void FillGlyphs(FlatPath contours, PaintSource paint, Font font, in Affine toDevice)
    {
        if (contours.ContourCount == 0)
            return;

        var polygons = new PolygonSet();
        polygons.AddTransformed(contours.Contours, toDevice);
        FillPolygons(polygons, evenOdd: false, paint);

        // Faux bold: the PDF renderer strokes the outline at 2% of the em (text render mode 2).
        var boldSimulated = (font.SyntheticStyle & SyntheticStyle.Bold) != 0;
        if (boldSimulated)
        {
            var style = new StrokeStyle(font.Size * TextMeasurement.BoldEmphasis, StrokeCap.Butt, StrokeJoin.Miter, 10, null, 0);
            var stroked = new PolygonSet();
            Stroker.Stroke(contours, style, toDevice, stroked);
            stroked.NormalizeWinding();
            FillPolygons(stroked, evenOdd: false, paint);
        }
    }
}

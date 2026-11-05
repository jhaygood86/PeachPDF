using CoreGraphics;
using PeachDrawing.Core;
using PeachDrawing.Core.ColorGlyphs;
using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;

namespace PeachDrawing.CoreGraphics;

public sealed partial class CoreGraphicsCanvas
{
    // ---- measuring ----------------------------------------------------------------------------------------

    // With the PeachDrawing engine measurement is the layout engine's own routine, so what is drawn can never disagree with layout. A native
    // font measures with CoreText, and draws with it.
    /// <inheritdoc />
    public override Size MeasureString(string str, Font font, ShapeSettings? features = null)
    {
        Guard();
        if (font is CoreGraphicsNativeFont native)
            return native.Measure(str);

        var size = TextMeasurement.Measure(str, font.Typeface!, font.Size, font.SyntheticStyle, features ?? ShapeSettings.Default);
        var width = size.Width * _ppp;
        if (TrackingPerGlyph(font) is var tracking and not 0)
            width += Shaper.Shape(font.Typeface!, str, features ?? ShapeSettings.Default).Glyphs.Count * tracking;

        return new Size(width, size.Height * _ppp);
    }

    /// <summary>
    /// How many positions a pixel is divided into horizontally for glyphs of <paramref name="font"/>, as Apple places text: the least whole number n such that n times the size in device pixels
    /// reaches 100/3, at most 5 on macOS and 3 on iOS (so a glyph is a pixel high at 34 pixels and up, half a pixel at 17, a third at 12, a quarter at 9, a fifth below). Zero when the matrix is not a plain
    /// scale and translation, where no grid is meant.
    /// </summary>
    private int PositionsPerPixel(Font font)
    {
        var m = _cg.GetCTM();
        if (m.B != 0 || m.C != 0 || !((double)m.A > 0))
            return 0;

        return GridFor(font.Size * _ppp * (double)m.A);
    }

    /// <summary>
    /// The most positions a pixel is divided into: five on macOS and Mac Catalyst, three on iOS and tvOS (measured against CoreGraphics on each: the size thresholds are the same, but iOS stops at thirds).
    /// </summary>
    internal static int MaxPositionsPerPixel { get; } = OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? 5 : 3;

    /// <summary>The number of horizontal positions per pixel for text <paramref name="sizeInPixels"/> high: see <see cref="CoreGraphicsGlyphPlacement.Platform"/>.</summary>
    internal static int GridFor(double sizeInPixels) => GridFor(sizeInPixels, MaxPositionsPerPixel);

    /// <summary>The number of horizontal positions per pixel for text <paramref name="sizeInPixels"/> high, at most <paramref name="maxPositions"/>.</summary>
    internal static int GridFor(double sizeInPixels, int maxPositions) =>
        sizeInPixels > 0 ? Math.Clamp((int)Math.Ceiling(100.0 / 3 / sizeInPixels - 1e-9), 1, maxPositions) : maxPositions;

    /// <summary>The layout x whose device column is <paramref name="x"/>'s rounded down to a 1/<paramref name="positionsPerPixel"/> pixel (<paramref name="x"/> unchanged for 0).</summary>
    private double QuantizeX(double x, int positionsPerPixel)
    {
        if (positionsPerPixel <= 0)
            return x;

        var m = _cg.GetCTM();
        var deviceX = (double)m.A * x + (double)m.Tx;
        return (Math.Floor(deviceX * positionsPerPixel + 1e-9) / positionsPerPixel - (double)m.Tx) / (double)m.A;
    }

    /// <summary>
    /// The layout y whose device row is the whole pixel nearest to where <paramref name="y"/> lands, for a context whose matrix only scales and translates (otherwise <paramref name="y"/> unchanged).
    /// Apple snaps the baseline of text to a whole device pixel (while placing it horizontally at a fraction of one), which is why its text has crisp baselines however the line was positioned.
    /// </summary>
    internal static double SnapToPixelRow(CGContext context, double x, double y)
    {
        var m = context.GetCTM();
        if (m.B != 0 || m.C != 0 || (double)m.D == 0)
            return y;

        var deviceY = (double)m.D * y + (double)m.Ty;
        return (Math.Round(deviceY) - (double)m.Ty) / (double)m.D;
    }

    /// <summary>
    /// What the font's tracking table adds to each glyph's advance at its size, in layout units; zero unless
    /// <see cref="CoreGraphicsRenderContext.ApplyFontTracking"/> is on and the font has one. Added to every advance (the last glyph's too, as CoreText does), so what is measured is what is drawn.
    /// </summary>
    private double TrackingPerGlyph(Font font)
    {
        if (!_context.ApplyFontTracking || font.Typeface is not { } typeface)
            return 0;

        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        return unitsPerEm == 0 ? 0 : typeface.GetTracking(font.Size) / unitsPerEm * font.Size * _ppp;
    }

    /// <inheritdoc />
    public override int CountShapedGlyphs(string str, Font font, ShapeSettings? features = null)
    {
        Guard();
        return font is CoreGraphicsNativeFont native ? native.CountGlyphs(str) : Shaper.Shape(font.Typeface!, str, features ?? ShapeSettings.Default).Glyphs.Count;
    }

    /// <inheritdoc />
    public override void MeasureString(string str, Font font, double maxWidth, out int charFit, out double charFitWidth)
    {
        // The longest prefix, cut between whole characters, that fits.
        charFit = 0;
        charFitWidth = 0;
        for (var length = 1; length <= str.Length; length++)
        {
            // A prefix that ends between the halves of a surrogate pair is not a prefix of characters.
            if (char.IsHighSurrogate(str[length - 1]) && length < str.Length)
                continue;

            var width = MeasureString(str[..length], font).Width;
            if (width > maxWidth)
                break;

            charFit = length;
            charFitWidth = width;
        }
    }

    /// <inheritdoc />
    public override GraphicsPath? GetTextOutline(string str, Font font, PaintPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null)
    {
        Guard();

        // A native font hands back no outlines a path could be built from, so it reports none and the caller falls back to drawing the
        // string, as Canvas.GetTextOutline allows for a font without outlines.
        return font is CoreGraphicsNativeFont
            ? null
            : TextOutlineBuilder.Build(GetGraphicsPath(), font, _ppp, str, baselineOrigin, letterSpacing + TrackingPerGlyph(font), features ?? ShapeSettings.Default);
    }

    /// <inheritdoc />
    public override IReadOnlyList<InkSpan>? GetInkCrossings(string str, Font font, PaintPoint origin, double bandTop, double bandBottom,
        double letterSpacing = 0, ShapeSettings? features = null)
    {
        Guard();
        if (font.Typeface is not { } typeface)
            return null;

        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (unitsPerEm == 0 || bandBottom <= bandTop)
            return null;

        // The baseline DrawString paints this run on, and the size of a design unit, both in the canvas's own units.
        var baselineY = origin.Y + font.Size * typeface.Metrics.CellAscent / unitsPerEm * _ppp;
        var scale = font.Size * _ppp / unitsPerEm;

        if (InkCrossings.Measure(typeface, str, scale, bandTop - baselineY, bandBottom - baselineY, letterSpacing + TrackingPerGlyph(font), features ?? ShapeSettings.Default) is not { } relative)
            return null;

        var spans = new InkSpan[relative.Count];
        for (var i = 0; i < spans.Length; i++)
            spans[i] = new InkSpan(relative[i].Start + origin.X, relative[i].End + origin.X);

        return spans;
    }

    // ---- drawing ------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public override void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing = 0,
        FontPalette? fontPalette = null, ShapeSettings? features = null)
    {
        Guard();
        if (str.Length == 0 || InvisibleText)
            return;

        if (font is CoreGraphicsNativeFont native)
        {
            if (color.A != 0)
                native.Draw(_cg, color, str, point, letterSpacing, _context.GlyphPlacement);

            return;
        }

        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (unitsPerEm == 0)
            return;

        // Where the PDF renderer puts the baseline: the run's top-left plus the cell ascent (here in layout units).
        var lineSpace = typeface.Metrics.LineSpacing * font.Size / unitsPerEm * _ppp;
        var baselineY = point.Y + lineSpace * typeface.Metrics.CellAscent / typeface.Metrics.LineSpacing;
        if (_context.GlyphPlacement == CoreGraphicsGlyphPlacement.Platform)
            baselineY = SnapToPixelRow(_cg, point.X, baselineY);

        DrawRun(str, font, color, point.X, baselineY, letterSpacing + TrackingPerGlyph(font), fontPalette, features ?? ShapeSettings.Default);
    }

    /// <inheritdoc />
    public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, Font font, PaintColor color)
    {
        Guard();

        // Glyphs addressed by index need the font file's outlines, which only a PeachDrawing font has.
        var typeface = font.Typeface;
        if (InvisibleText || typeface is null)
            return;

        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (unitsPerEm == 0)
            return;

        var scale = font.Size / unitsPerEm * _ppp;
        var hinting = HintingRequest(font);
        using var run = new GlyphRun(this);
        foreach (var placement in glyphs)
        {
            if (TryDrawSpecialGlyph(typeface, placement.GlyphIndex, font, color, null, placement.X, placement.Y))
                continue;

            if (hinting is { } request && typeface.TryGetOutline((ushort)placement.GlyphIndex, request, out var fitted))
                run.AddFitted(fitted, placement.X, placement.Y, 0, request.GridFitting == GridFitting.Monochrome);
            else if (typeface.TryGetOutline((ushort)placement.GlyphIndex, out var outline))
                run.Add(outline, placement.X, placement.Y, scale, 0);
        }

        run.Fill(font, color);
    }

    private void DrawRun(string str, Font font, PaintColor color, double originX, double baselineY, double letterSpacing,
        FontPalette? palette, ShapeSettings features)
    {
        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        var glyphs = Shaper.Shape(typeface, str, features).Glyphs;
        var scale = font.Size / unitsPerEm * _ppp;
        var skew = ItalicSkew(font);
        var hinting = HintingRequest(font);
        var grid = _context.GlyphPlacement == CoreGraphicsGlyphPlacement.Platform ? PositionsPerPixel(font) : 0;

        using var run = new GlyphRun(this);
        var penX = originX;
        foreach (var glyph in glyphs)
        {
            var gx = QuantizeX(penX + glyph.XOffset * scale, grid);
            var gy = baselineY - glyph.YOffset * scale;
            if (!TryDrawSpecialGlyph(typeface, glyph.GlyphIndex, font, color, palette, gx, gy))
            {
                if (hinting is { } request && typeface.TryGetOutline((ushort)glyph.GlyphIndex, request, out var fitted))
                    run.AddFitted(fitted, gx, gy, skew, request.GridFitting == GridFitting.Monochrome);
                else if (typeface.TryGetOutline((ushort)glyph.GlyphIndex, out var outline))
                    run.Add(outline, gx, gy, scale, skew);
            }

            penX += (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale + letterSpacing;
        }

        run.Fill(font, color);
    }

    /// <summary>
    /// What to ask the typeface for when text is to be hinted, or <see langword="null"/> when it is not. Hinting fits outlines to the device's pixel grid, so it needs
    /// the text to reach the pixels unrotated and unskewed (the two axes may scale differently). The mode is the context's <c>TextHinting</c>; when that is
    /// <see cref="TextHinting.None"/> and <see cref="CoreGraphicsRenderContext.HintTextOnLowDensityDisplays"/> is on, light (vertical-only) hinting is used on a
    /// device that has fewer than two pixels to a point.
    /// </summary>
    private OutlineRequest? HintingRequest(Font font)
    {
        var m = _cg.GetCTM();
        if (m.B != 0 || m.C != 0 || !((double)m.A > 0) || (double)m.D == 0)
            return null;

        var pixelsPerLayoutUnitX = (double)m.A;
        var pixelsPerLayoutUnitY = Math.Abs((double)m.D);
        var mode = EffectiveHinting(_context.TextHinting, _context.HintTextOnLowDensityDisplays, Math.Min(pixelsPerLayoutUnitX, pixelsPerLayoutUnitY) * _ppp);
        if (mode == TextHinting.None)
            return null;

        return new OutlineRequest
        {
            PixelsPerEmX = font.Size * _ppp * pixelsPerLayoutUnitX,
            PixelsPerEmY = font.Size * _ppp * pixelsPerLayoutUnitY,
            GridFitting = mode == TextHinting.Monochrome ? GridFitting.Monochrome : GridFitting.Standard,
            StemDarkening = _context.TextStemDarkening,
        };
    }

    /// <summary>The hinting mode in force: the one asked for, or light hinting for a low-density display when that is switched on and none was asked for.</summary>
    /// <param name="requested">the context's <c>TextHinting</c></param>
    /// <param name="hintOnLowDensity">whether light hinting is wanted on a low-density display</param>
    /// <param name="pixelsPerPoint">the device pixels to one point</param>
    internal static TextHinting EffectiveHinting(TextHinting requested, bool hintOnLowDensity, double pixelsPerPoint) =>
        requested != TextHinting.None ? requested : hintOnLowDensity && pixelsPerPoint < 1.999 ? TextHinting.Standard : TextHinting.None;

    private static double ItalicSkew(Font font) =>
        (font.SyntheticStyle & SyntheticStyle.Italic) != 0 ? font.ObliqueSkewSinus ?? TextMeasurement.ItalicSkewAngleSinus : 0;

    /// <summary>The outlines of a run of glyphs gathered into one path, so the run is a single fill, released with the run.</summary>
    private sealed class GlyphRun(CoreGraphicsCanvas canvas) : IDisposable
    {
        /// <summary>
        /// The stroke width, in ems, that reproduces macOS font smoothing: the width that best fitted CoreText's own smoothed text for one font at 13, 16 and 24 px
        /// at one and two pixels to a point (0.0077 to 0.0125, clustered at 0.0105).
        /// </summary>
        public const double SmoothingStrokeEm = 0.0105;

        private CGPath? _path;
        private int _figures;

        public void Add(GlyphOutline outline, double x, double y, double scale, double skew)
        {
            _path ??= new CGPath();

            // Faux italic shears about the baseline: points above it move right.
            CGPoint P(OutlinePoint p) => new(x + p.X * scale + skew * p.Y * scale, y - p.Y * scale);

            foreach (var contour in outline.Contours)
            {
                var start = P(contour.Start);
                _path.MoveToPoint(start.X, start.Y);
                foreach (var segment in contour.Segments)
                {
                    var end = P(segment.End);
                    if (segment.IsCubic)
                    {
                        var c1 = P(segment.Control1);
                        var c2 = P(segment.Control2);
                        _path.AddCurveToPoint(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                    }
                    else
                    {
                        _path.AddLineToPoint(end.X, end.Y);
                    }
                }

                _path.CloseSubpath();
                _figures++;
            }
        }

        /// <summary>
        /// Adds a grid-fitted outline, which is in device pixels with y up and has its origin on the baseline. The baseline is moved to a whole device pixel vertically
        /// (and horizontally for <paramref name="fitX"/>), the points are placed there, and the path is mapped back to layout units so the context's matrix puts each exactly
        /// where it was fitted.
        /// </summary>
        public void AddFitted(GlyphOutline outline, double x, double y, double skew, bool fitX)
        {
            var ctm = canvas._cg.GetCTM();
            var inverse = ctm.Invert();
            _path ??= new CGPath();

            var device = ctm.TransformPoint(new CGPoint(x, y));
            var originX = (double)device.X;
            var originY = (double)device.Y;
            if (outline.IsGridFitted)
            {
                originY = Math.Round(originY);
                if (fitX)
                    originX = Math.Round(originX);
            }

            // The canvas is y-down in layout units, so a glyph's up is device-y-up exactly when the matrix mirrors y.
            var up = (double)ctm.D < 0 ? 1.0 : -1.0;
            CGPoint P(OutlinePoint p) => inverse.TransformPoint(new CGPoint(originX + p.X + skew * p.Y, originY + up * p.Y));

            foreach (var contour in outline.Contours)
            {
                var start = P(contour.Start);
                _path.MoveToPoint(start.X, start.Y);
                foreach (var segment in contour.Segments)
                {
                    var end = P(segment.End);
                    if (segment.IsCubic)
                    {
                        var c1 = P(segment.Control1);
                        var c2 = P(segment.Control2);
                        _path.AddCurveToPoint(c1.X, c1.Y, c2.X, c2.Y, end.X, end.Y);
                    }
                    else
                    {
                        _path.AddLineToPoint(end.X, end.Y);
                    }
                }

                _path.CloseSubpath();
                _figures++;
            }
        }

        public void Fill(Font font, PaintColor color)
        {
            if (_path is null || _figures == 0 || color.A == 0)
                return;

            var cg = canvas._cg;
            var depth = canvas._depth;
            canvas.Save();
            try
            {
                using var paint = ToColor(color);
                cg.SetFillColor(paint);
                cg.BeginPath();
                cg.AddPath(_path);
                cg.DrawPath(CGPathDrawingMode.Fill);

                // One hairline stroke thickens the glyphs: synthetic bold's width, plus font smoothing's extra weight (see MatchPlatformTextWeight) when that is on.
                var strokeEm = ((font.SyntheticStyle & SyntheticStyle.Bold) != 0 ? TextMeasurement.BoldEmphasis : 0)
                               + (canvas._context.MatchPlatformTextWeight ? SmoothingStrokeEm : 0);
                if (strokeEm > 0)
                {
                    cg.SetStrokeColor(paint);
                    cg.SetLineWidth((nfloat)(font.Size * canvas._ppp * strokeEm));
                    cg.SetLineJoin((font.SyntheticStyle & SyntheticStyle.Bold) != 0 ? CGLineJoin.Miter : CGLineJoin.Round);
                    cg.BeginPath();
                    cg.AddPath(_path);
                    cg.StrokePath();
                }
            }
            finally
            {
                canvas.RestoreTo(depth);
            }
        }

        public void Dispose() => _path?.Dispose();
    }

    // ---- glyphs that are not a single-colour outline --------------------------------------------------------

    private bool TryDrawSpecialGlyph(Typeface typeface, int glyphIndex, Font font, PaintColor color, FontPalette? palette, double originX, double baselineY)
    {
        if (typeface.HasBitmapGlyphs && typeface.TryGetBitmap((ushort)glyphIndex, font.Size, out var bitmap))
            return DrawBitmapGlyph(typeface, glyphIndex, bitmap, font.Size, originX, baselineY);

        var paletteIndex = palette?.BasePaletteIndex ?? 0;
        if (typeface.HasSvgGlyphs && TryDrawSvgGlyph(typeface, glyphIndex, font.Size, color, paletteIndex, ToGlyphOverrides(palette), originX, baselineY))
            return true;

        if (typeface.HasColorGlyphs && TryDrawColorGlyph(typeface, glyphIndex, font.Size, color, paletteIndex, ToGlyphOverrides(palette), originX, baselineY))
            return true;

        return false;
    }

    // The decoded bitmaps of an embedded-bitmap font, by glyph and size, released with the canvas.
    private Dictionary<(Typeface, int, int), CoreGraphicsImage?>? _bitmapGlyphs;

    private bool DrawBitmapGlyph(Typeface typeface, int glyphId, EmbeddedBitmap bitmap, double fontSize, double originX, double baselineY)
    {
        _bitmapGlyphs ??= [];
        var key = (typeface, glyphId, bitmap.Ppem);
        if (!_bitmapGlyphs.TryGetValue(key, out var image))
        {
            image = _context.DecodeImage(bitmap.Data);
            _bitmapGlyphs[key] = image;
        }

        if (image is null)
            return false;

        var scale = fontSize / bitmap.Ppem * _ppp;
        DrawImage(image, new Rect(originX + bitmap.BearingX * scale, baselineY - bitmap.BearingTop * scale, bitmap.Width * scale, bitmap.Height * scale));
        return true;
    }

    private void ReleaseBitmapGlyphs()
    {
        if (_bitmapGlyphs is null)
            return;

        foreach (var image in _bitmapGlyphs.Values)
            image?.Dispose();

        _bitmapGlyphs = null;
    }

    private bool TryDrawColorGlyph(Typeface typeface, int glyphId, double fontSize, PaintColor color, int paletteIndex,
        IReadOnlyDictionary<int, PaintColor>? overrides, double originX, double baselineY)
    {
        if (typeface.GetColorPaint((ushort)glyphId) is null && !typeface.TryGetColorLayers((ushort)glyphId, out _))
            return false;

        var painter = new ColorGlyphPainter(typeface, fontSize * _ppp, color, yDown: true, paletteIndex, overrides);
        painter.Paint((ushort)glyphId, painter.Placement(originX, baselineY),
            new CanvasColorGlyphTarget(this) { SupportsAdditiveComposite = true, SupportsPeriodicConeGradients = true });
        return true;
    }

    private bool TryDrawSvgGlyph(Typeface typeface, int glyphId, double fontSize, PaintColor color, int paletteIndex,
        IReadOnlyDictionary<int, PaintColor>? overrides, double originX, double baselineY)
    {
        if (typeface.GetColorPaint((ushort)glyphId) is not null || typeface.TryGetColorLayers((ushort)glyphId, out _) ||
            !typeface.TryGetSvgGlyph((ushort)glyphId, out var svg))
            return false;

        _svgGlyphs ??= _renderContext.CreateSvgGlyphPainter(this);
        return _svgGlyphs?.TryPaint(typeface, (ushort)glyphId, svg, fontSize, originX / _ppp, baselineY / _ppp, color, paletteIndex, overrides) ?? false;
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
}

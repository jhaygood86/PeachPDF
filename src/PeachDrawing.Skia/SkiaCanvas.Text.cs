using PeachDrawing.Core;
using PeachDrawing.Core.ColorGlyphs;
using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Shaping;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Skia;

public sealed partial class SkiaCanvas
{
    // ---- measuring ----------------------------------------------------------------------------------------

    // With the PeachDrawing engine measurement is the layout engine's own routine, so what is drawn can never disagree with layout. A native
    // font measures with Skia and the context's shaper, and draws with them.
    /// <inheritdoc />
    public override Size MeasureString(string str, Font font, ShapeSettings? features = null)
    {
        Guard();
        if (font is NativeSkiaFont native)
            return native.Measure(str);

        var size = TextMeasurement.Measure(str, font.Typeface!, font.Size, font.SyntheticStyle, features ?? ShapeSettings.Default);
        return new Size(size.Width * _ppp, size.Height * _ppp);
    }

    /// <inheritdoc />
    public override int CountShapedGlyphs(string str, Font font, ShapeSettings? features = null)
    {
        Guard();
        return font is NativeSkiaFont native ? native.CountClusters(str) : Shaper.Shape(font.Typeface!, str, features ?? ShapeSettings.Default).Glyphs.Count;
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
        return font is NativeSkiaFont
            ? null
            : TextOutlineBuilder.Build(GetGraphicsPath(), font, _ppp, str, baselineOrigin, letterSpacing, features ?? ShapeSettings.Default);
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

        if (InkCrossings.Measure(typeface, str, scale, bandTop - baselineY, bandBottom - baselineY, letterSpacing, features ?? ShapeSettings.Default) is not { } relative)
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

        if (font is NativeSkiaFont native)
        {
            using var paint = SolidPaint(color);
            if (paint is not null)
                native.Draw(_canvas, paint, str, point, letterSpacing);

            return;
        }

        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        if (unitsPerEm == 0)
            return;

        // Where the PDF renderer puts the baseline: the run's top-left plus the cell ascent (here in layout units).
        var lineSpace = typeface.Metrics.LineSpacing * font.Size / unitsPerEm * _ppp;
        var baselineY = point.Y + lineSpace * typeface.Metrics.CellAscent / typeface.Metrics.LineSpacing;
        DrawRun(str, font, color, point.X, baselineY, letterSpacing, fontPalette, features ?? ShapeSettings.Default);
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
        using var run = new GlyphRun();
        foreach (var placement in glyphs)
        {
            if (!TryDrawSpecialGlyph(typeface, placement.GlyphIndex, font, color, null, placement.X, placement.Y)
                && typeface.TryGetOutline((ushort)placement.GlyphIndex, out var outline))
            {
                run.Add(outline, placement.X, placement.Y, scale, 0);
            }
        }

        run.Fill(this, font, color);
    }

    private void DrawRun(string str, Font font, PaintColor color, double originX, double baselineY, double letterSpacing,
        FontPalette? palette, ShapeSettings features)
    {
        var typeface = font.Typeface!;
        var unitsPerEm = typeface.Metrics.UnitsPerEm;
        var glyphs = Shaper.Shape(typeface, str, features).Glyphs;
        var scale = font.Size / unitsPerEm * _ppp;
        var skew = ItalicSkew(font);

        using var run = new GlyphRun();
        var penX = originX;
        foreach (var glyph in glyphs)
        {
            var gx = penX + glyph.XOffset * scale;
            var gy = baselineY - glyph.YOffset * scale;
            if (!TryDrawSpecialGlyph(typeface, glyph.GlyphIndex, font, color, palette, gx, gy)
                && typeface.TryGetOutline((ushort)glyph.GlyphIndex, out var outline))
            {
                run.Add(outline, gx, gy, scale, skew);
            }

            penX += (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale + letterSpacing;
        }

        run.Fill(this, font, color);
    }

    private static double ItalicSkew(Font font) =>
        (font.SyntheticStyle & SyntheticStyle.Italic) != 0 ? font.ObliqueSkewSinus ?? TextMeasurement.ItalicSkewAngleSinus : 0;

    /// <summary>The outlines of a run of glyphs gathered into one path, so the run is a single fill, released with the run.</summary>
    private sealed class GlyphRun : IDisposable
    {
        private SKPath? _path;
        private int _figures;

        public void Add(GlyphOutline outline, double x, double y, double scale, double skew)
        {
            _path ??= new SKPath { FillType = SKPathFillType.Winding };

            // Faux italic shears about the baseline: points above it move right.
            SKPoint P(OutlinePoint p) => new((float)(x + p.X * scale + skew * p.Y * scale), (float)(y - p.Y * scale));

            foreach (var contour in outline.Contours)
            {
                _path.MoveTo(P(contour.Start));
                foreach (var segment in contour.Segments)
                {
                    if (segment.IsCubic)
                        _path.CubicTo(P(segment.Control1), P(segment.Control2), P(segment.End));
                    else
                        _path.LineTo(P(segment.End));
                }

                _path.Close();
                _figures++;
            }
        }

        public void Fill(SkiaCanvas canvas, Font font, PaintColor color)
        {
            if (_path is null || _figures == 0 || color.A == 0)
                return;

            using var paint = canvas.SolidPaint(color);
            if (paint is null)
                return;

            canvas._canvas.DrawPath(_path, paint);

            // Synthetic bold thickens the outline by stroking it in the same colour.
            if ((font.SyntheticStyle & SyntheticStyle.Bold) != 0)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = (float)(font.Size * canvas._ppp * TextMeasurement.BoldEmphasis);
                canvas._canvas.DrawPath(_path, paint);
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
    private Dictionary<(Typeface, int, int), SkiaImage?>? _bitmapGlyphs;

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

using PeachPDF.Fonts;
using PeachPDF.Adapters;
using PeachPDF.Fonts.OpenType;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Text;
using PeachPDF.Utilities;
using System;
using System.Collections.Generic;

namespace PeachPDF.Raster;

internal sealed partial class RasterGraphics
{
    // Measurement is the layout engine's own routine, so paint can never disagree with layout.
    public override RSize MeasureString(string str, RFont font, TextShapingFeatures? features = null)
    {
        var realFont = ((FontAdapter)font).Font;
        var size = FontHelper.MeasureString(str, realFont, XStringFormats.Default, features ?? TextShapingFeatures.Default);
        return Utils.Convert(size, _pixelsPerPoint);
    }

    public override int CountShapedGlyphs(string str, RFont font, TextShapingFeatures? features = null) =>
        ((FontAdapter)font).Font.Descriptor.Shape(str, features ?? TextShapingFeatures.Default).Count;

    public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth) =>
        throw new NotSupportedException();

    public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, TextShapingFeatures? features = null) =>
        TextOutlineBuilder.Build(GetGraphicsPath(), ((FontAdapter)font).Font, _pixelsPerPoint, str, baselineOrigin, letterSpacing,
            features ?? TextShapingFeatures.Default);

    public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing = 0,
        RFontPalette? fontPalette = null, TextShapingFeatures? features = null)
    {
        var xFont = ((FontAdapter)font).Font;
        var descriptor = xFont.Descriptor;
        if (InvisibleText || descriptor is null || descriptor.UnitsPerEm == 0 || str.Length == 0)
            return;

        // Where the PDF renderer puts the baseline: the run's top-left plus the cell ascent, in points.
        var lineSpace = xFont.GetHeight();
        var baselineY = point.Y / _pixelsPerPoint + lineSpace * xFont.CellAscent / xFont.CellSpace;
        var originX = point.X / _pixelsPerPoint;
        var spacing = letterSpacing / _pixelsPerPoint;
        var resolved = features ?? TextShapingFeatures.Default;

        var glyphs = descriptor.Shape(str, resolved);
        var scale = xFont.Size / descriptor.UnitsPerEm;
        var skew = ItalicSkew(xFont);
        var contours = new FlatPath();
        var toDevice = UserToDevice;
        var tolerance = 0.1 / Math.Max(toDevice.MaxScale, 1e-9);

        var penX = originX;
        foreach (var glyph in glyphs)
        {
            if (descriptor.HasBitmapGlyphs && descriptor.TryGetBitmapGlyph(glyph.GlyphIndex, xFont.Size, out var bitmap))
            {
                DrawBitmapGlyph(descriptor, glyph.GlyphIndex, bitmap, xFont.Size, penX + glyph.XOffset * scale, baselineY - glyph.YOffset * scale);
            }
            else if (descriptor.TryGetGlyphOutline(glyph.GlyphIndex, out var outline))
            {
                AddGlyph(contours, outline, penX + glyph.XOffset * scale, baselineY - glyph.YOffset * scale, scale, skew, tolerance);
            }

            penX += (descriptor.GlyphIndexToWidth(glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale + spacing;
        }

        var paint = PaintSource.FromColor(Utils.Convert(color));
        FillGlyphs(contours, paint, xFont, toDevice);
    }

    /// <summary>Draws a bitmap colour glyph (CBDT/sbix) at a glyph origin given in points, the way the PDF backend places it.</summary>
    private void DrawBitmapGlyph(OpenTypeDescriptor descriptor, int glyphId, BitmapGlyph bitmap, double fontSize, double originX, double baselineY)
    {
        var scale = fontSize / bitmap.Ppem;
        var width = bitmap.Width * scale;
        var height = bitmap.Height * scale;
        var left = originX + bitmap.BearingX * scale;
        var top = baselineY - bitmap.BearingTop * scale;

        // The picture is a layout-unit rectangle to DrawImage, like every other image.
        var image = new ImageAdapter(BitmapGlyphImages.Get(descriptor, glyphId, bitmap));
        DrawImage(image, new RRect(left * _pixelsPerPoint, top * _pixelsPerPoint, width * _pixelsPerPoint, height * _pixelsPerPoint));
    }

    public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, RFont font, RColor color)
    {
        var xFont = ((FontAdapter)font).Font;
        var descriptor = xFont.Descriptor;
        if (InvisibleText || descriptor is null || descriptor.UnitsPerEm == 0)
            return;

        var scale = xFont.Size / descriptor.UnitsPerEm;
        var toDevice = UserToDevice;
        var tolerance = 0.1 / Math.Max(toDevice.MaxScale, 1e-9);
        var contours = new FlatPath();

        foreach (var placement in glyphs)
        {
            if (descriptor.TryGetGlyphOutline(placement.GlyphIndex, out var outline))
                AddGlyph(contours, outline, placement.X / _pixelsPerPoint, placement.Y / _pixelsPerPoint, scale, 0, tolerance);
        }

        FillGlyphs(contours, PaintSource.FromColor(Utils.Convert(color)), xFont, toDevice);
    }

    private static double ItalicSkew(XFont font)
    {
        var simulated = (font.GlyphTypeface.StyleSimulations & SyntheticStyle.Italic) != 0;
        return simulated ? font.ObliqueSkewSinus ?? Const.ItalicSkewAngleSinus : 0;
    }

    /// <summary>
    /// Appends <paramref name="outline"/> (font design units, y-up) to <paramref name="target"/> in user space
    /// with its origin on the baseline at (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    private static void AddGlyph(FlatPath target, GlyphOutline outline, double x, double y, double scale, double skew, double tolerance)
    {
        // Faux italic shears about the baseline: points above it move right.
        double X(GlyphOutlinePoint p) => x + p.X * scale + skew * p.Y * scale;
        double Y(GlyphOutlinePoint p) => y - p.Y * scale;

        foreach (var contour in outline.Contours)
        {
            var cx = X(contour.Start);
            var cy = Y(contour.Start);
            target.MoveTo(cx, cy);

            foreach (var segment in contour.Segments)
            {
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

    private void FillGlyphs(FlatPath contours, PaintSource paint, XFont font, in Affine toDevice)
    {
        if (contours.ContourCount == 0)
            return;

        var polygons = new PolygonSet();
        polygons.AddTransformed(contours.Contours, toDevice);
        FillPolygons(polygons, evenOdd: false, paint);

        // Faux bold: the PDF renderer strokes the outline at 2% of the em (text render mode 2).
        var boldSimulated = (font.GlyphTypeface.StyleSimulations & SyntheticStyle.Bold) != 0;
        if (boldSimulated)
        {
            var style = new StrokeStyle(font.Size * Const.BoldEmphasis, StrokeCap.Butt, StrokeJoin.Miter, 10, null, 0);
            var stroked = new PolygonSet();
            Stroker.Stroke(contours, style, toDevice, stroked);
            stroked.NormalizeWinding();
            FillPolygons(stroked, evenOdd: false, paint);
        }
    }
}

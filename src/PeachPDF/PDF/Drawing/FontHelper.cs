// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// Bunch of functions that do not have a better place.
/// </summary>
internal static class FontHelper
{
    /// <summary>
    /// Measure string directly from font data. The algorithm itself is
    /// <see cref="PeachDrawing.Core.TextMeasurement.Measure"/> - shared with every other
    /// backend that measures text (the raster canvas calls it directly, having no <see cref="TypefaceFont"/>
    /// of its own to pass here) - this is just the <see cref="Size"/> wrapping this method's own
    /// callers still expect.
    /// </summary>
    public static Size MeasureString(string text, TypefaceFont font, PdfStringFormat stringFormat, ShapeSettings features)
    {
        var size = PeachDrawing.Core.TextMeasurement.Measure(text, font.Typeface, font.Size, font.SyntheticStyle, features);
        return new Size(size.Width, size.Height);
    }

    public static PaintFontStyle CreateStyle(bool isBold, bool isItalic)
    {
        return (isBold ? PaintFontStyle.Bold : 0) | (isItalic ? PaintFontStyle.Italic : 0);
    }
}

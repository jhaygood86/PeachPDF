// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Numerics;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Core;
using PeachPDF.PDF.Rendering;
using PeachPDF.PDF;
using PeachPDF.PDF.Advanced;
using System;

#pragma warning disable 1587
// ReSharper disable UseNullPropagation
// ReSharper disable RedundantNameQualifier
// ReSharper disable UseNameofExpression

namespace PeachPDF.PDF.Drawing;

internal sealed partial class PdfGraphics : IDisposable
{
    public void DrawString(string s, TypefaceFont font, Brush brush, PaintPoint point)
    {
        DrawString(s, font, brush, new Rect(point.X, point.Y, 0, 0), PdfStringFormats.SharedDefault);
    }

    /// <summary>
    /// Draws the specified text string.
    /// </summary>
    public void DrawString(string s, TypefaceFont font, Brush brush, PaintPoint point, PdfStringFormat format)
    {
        DrawString(s, font, brush, new Rect(point.X, point.Y, 0, 0), format);
    }

    /// <summary>
    /// Draws the specified text string.
    /// </summary>
    public void DrawString(string s, TypefaceFont font, Brush brush, double x, double y)
    {
        DrawString(s, font, brush, new Rect(x, y, 0, 0), PdfStringFormats.SharedDefault);
    }

    /// <summary>
    /// Draws the specified text string.
    /// </summary>
    public void DrawString(string s, TypefaceFont font, Brush brush, double x, double y, PdfStringFormat format, double letterSpacing = 0, FontPalette? fontPalette = null, ShapeSettings? features = null, string? logicalText = null)
    {
        DrawString(s, font, brush, new Rect(x, y, 0, 0), format, letterSpacing, fontPalette, features, logicalText);
    }

    /// <summary>
    /// Draws the specified text string.
    /// </summary>
    public void DrawString(string s, TypefaceFont font, Brush brush, Rect layoutRectangle)
    {
        DrawString(s, font, brush, layoutRectangle, PdfStringFormats.SharedDefault);
    }

    /// <summary>
    /// Draws the specified text string. <paramref name="logicalText"/> is the true logical-order
    /// (pre-bidi-mirroring) source <paramref name="text"/> was derived from, when the two differ -
    /// see <see cref="PeachDrawing.Core.Canvas"/>'s own equivalent overload for why this
    /// exists (ToUnicode CMap fidelity for a reversed/mirrored RTL word).
    /// </summary>
    public void DrawString(string text, TypefaceFont font, Brush brush, Rect layoutRectangle, PdfStringFormat format, double letterSpacing = 0, FontPalette? fontPalette = null, ShapeSettings? features = null, string? logicalText = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(brush);

        if (format is not null && format.LineAlignment == PdfLineAlignment.BaseLine && layoutRectangle.Height != 0)
            throw new InvalidOperationException("DrawString: With PdfLineAlignment.BaseLine the height of the layout rectangle must be 0.");

        if (text.Length == 0)
            return;

        if (format is null)
            format = PdfStringFormats.SharedDefault;

        if (_renderer is not null)
            _renderer.DrawString(text, font, brush, layoutRectangle, format, letterSpacing, fontPalette, features ?? ShapeSettings.Default, logicalText);
    }

    /// <summary>
    /// Draws each glyph in <paramref name="glyphs"/> at its own explicit position, addressed
    /// directly by font glyph index - see <see cref="PeachDrawing.Core.Canvas.DrawGlyphs"/>'s
    /// own remarks for why this exists alongside the ordinary character-based <c>DrawString</c>.
    /// </summary>
    public void DrawGlyphsAtPositions(System.Collections.Generic.IReadOnlyList<(int GlyphIndex, double X, double Y)> glyphs, TypefaceFont font, Brush brush)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(brush);

        if (_renderer is not null)
            _renderer.DrawGlyphsAtPositions(glyphs, font, brush);
    }

    // ----- MeasureString ------------------------------------------------------------------------

    /// <summary>
    /// Measures the specified string when drawn with the specified font.
    /// </summary>
    public Size MeasureString(string text, TypefaceFont font, PdfStringFormat stringFormat, ShapeSettings? features = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(stringFormat);

        Size size = FontHelper.MeasureString(text, font, PdfStringFormats.SharedDefault, features ?? ShapeSettings.Default);
        return size;
    }

    /// <summary>
    /// Measures the specified string when drawn with the specified font.
    /// </summary>
    public Size MeasureString(string text, TypefaceFont font, ShapeSettings? features = null)
    {
        return MeasureString(text, font, PdfStringFormats.SharedDefault, features);
    }
}

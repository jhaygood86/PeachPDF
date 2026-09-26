using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using System.Collections.Generic;

namespace PeachPDF.PdfSharpCore.Drawing.Pdf
{
    /// <summary>
    /// Draws the SVG document a font gives for a glyph (OpenType SVG) into the graphics it is set on. The PDF text renderer knows how
    /// to place a glyph but not how to render SVG, which the layers above it do; this is the seam between the two.
    /// </summary>
    internal interface ISvgGlyphPainter
    {
        /// <summary>Draws the glyph with its origin at (<paramref name="originX"/>, <paramref name="baselineY"/>), in the units of the graphics.</summary>
        /// <returns><see langword="false"/> when the document cannot be drawn, so that the caller draws the glyph's outline.</returns>
        bool TryPaint(Typeface typeface, ushort glyph, SvgGlyph svg, double fontSize, double originX, double baselineY,
            XColor foreground, int paletteIndex, IReadOnlyDictionary<int, XColor>? overrides);
    }
}

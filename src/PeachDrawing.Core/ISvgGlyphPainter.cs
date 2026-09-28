using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using System.Collections.Generic;

namespace PeachDrawing.Core
{
    /// <summary>
    /// Draws the SVG document a font gives for a glyph (OpenType SVG) into the graphics it is set on. A
    /// text-painting backend knows how to place a glyph but not how to render SVG, which needs a real SVG
    /// engine (e.g. PeachPDF's own <c>SvgRenderer</c>) it may not have - this is the seam between the two,
    /// obtained through <see cref="RenderContext.CreateSvgGlyphPainter"/> so a backend with no SVG engine
    /// (a standalone raster canvas) can supply none.
    /// </summary>
    public interface ISvgGlyphPainter
    {
        /// <summary>Draws the glyph with its origin at (<paramref name="originX"/>, <paramref name="baselineY"/>), in the units of the graphics.</summary>
        /// <returns><see langword="false"/> when the document cannot be drawn, so that the caller draws the glyph's outline.</returns>
        bool TryPaint(Typeface typeface, ushort glyph, SvgGlyph svg, double fontSize, double originX, double baselineY,
            PaintColor foreground, int paletteIndex, IReadOnlyDictionary<int, PaintColor>? overrides);
    }
}

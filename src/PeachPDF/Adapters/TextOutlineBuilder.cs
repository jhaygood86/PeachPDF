using PeachPDF.Fonts.OpenType;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Text;

namespace PeachPDF.Adapters
{
    /// <summary>
    /// Turns a shaped glyph run into a vector <see cref="RGraphicsPath"/>. Shared by every backend's
    /// <see cref="RGraphics.GetTextOutline"/> so they all place glyphs identically.
    /// </summary>
    internal static class TextOutlineBuilder
    {
        /// <summary>
        /// Builds the outline of <paramref name="str"/> into <paramref name="path"/> (returned, or disposed and
        /// null when the font produced no outlines - the caller's cue to fall back to drawing the string).
        /// </summary>
        /// <param name="path">an empty path from the calling graphics' own <see cref="RGraphics.GetGraphicsPath"/></param>
        /// <param name="realFont">the font to outline with</param>
        /// <param name="pixelsPerPoint">layout units per point (path coordinates are in layout units)</param>
        /// <param name="str">the run to outline</param>
        /// <param name="baselineOrigin">the pen origin on the baseline (layout units)</param>
        /// <param name="letterSpacing">extra advance between glyphs (layout units)</param>
        /// <param name="features">which GSUB features to apply when shaping</param>
        public static RGraphicsPath? Build(RGraphicsPath path, XFont realFont, double pixelsPerPoint, string str,
            RPoint baselineOrigin, double letterSpacing, TextShapingFeatures features)
        {
            var descriptor = realFont.Descriptor;
            if (descriptor is null || descriptor.UnitsPerEm == 0)
            {
                path.Dispose();
                return null;
            }

            // Design units -> SVG user space. font.Size is in points (XFont.Size = css/svg size / PixelsPerPoint),
            // while these path coordinates reach the backend un-scaled by PixelsPerPoint (see GraphicsPathAdapter.
            // Transform), the same space shape paths are built in - so multiply back by PixelsPerPoint. The em-square
            // is y-up; user space is y-down, so glyph Y is subtracted from the baseline.
            double scale = realFont.Size * pixelsPerPoint / descriptor.UnitsPerEm;

            path.FillMode = RFillMode.Nonzero;

            double penX = baselineOrigin.X;
            double baseY = baselineOrigin.Y;
            bool anyGeometry = false;

            foreach (ShapedGlyph glyph in descriptor.Shape(str, features))
            {
                int glyphId = glyph.GlyphIndex;

                // TryGetGlyphOutline returns false for an empty glyph (e.g. space) or a font with no
                // usable outline source at all (a CID-keyed CFF or bitmap font) - either way there's
                // nothing to add for this glyph.
                if (descriptor.TryGetGlyphOutline(glyphId, out GlyphOutline outline))
                {
                    // GPOS positioning (kerning's XOffset, mark attachment's XOffset/YOffset) shifts
                    // where this glyph paints without changing its own outline shape - see
                    // GposPositioner. Y is subtracted (em-square is y-up, user space is y-down), same
                    // as the outline's own Y coordinates just below.
                    double glyphX = penX + glyph.XOffset * scale;
                    double glyphY = baseY - glyph.YOffset * scale;

                    foreach (GlyphContour contour in outline.Contours)
                    {
                        path.AddMove(glyphX + contour.Start.X * scale, glyphY - contour.Start.Y * scale);

                        foreach (GlyphSegment segment in contour.Segments)
                        {
                            if (segment.IsCubic)
                            {
                                path.AddBezierTo(
                                    glyphX + segment.Control1.X * scale, glyphY - segment.Control1.Y * scale,
                                    glyphX + segment.Control2.X * scale, glyphY - segment.Control2.Y * scale,
                                    glyphX + segment.End.X * scale, glyphY - segment.End.Y * scale);
                            }
                            else
                            {
                                path.LineTo(glyphX + segment.End.X * scale, glyphY - segment.End.Y * scale);
                            }
                        }

                        path.CloseFigure();
                        anyGeometry = true;
                    }
                }

                penX += (descriptor.GlyphIndexToWidth(glyphId) + glyph.XAdvanceDelta) * scale + letterSpacing;
            }

            // No geometry at all means the font produced no outlines (a CID-keyed CFF or bitmap font) -
            // signal the caller to fall back to DrawString.
            if (!anyGeometry)
            {
                path.Dispose();
                return null;
            }

            return path;
        }
    }
}

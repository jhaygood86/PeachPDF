using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Core
{
    /// <summary>
    /// Finds where a run of text puts ink inside a horizontal band, so a line drawn through that band (an underline, say) can be
    /// broken around the letters. This is what <see cref="Canvas.GetInkCrossings"/> answers for a canvas that has the glyph outlines.
    /// </summary>
    public static class InkCrossings
    {
        /// <summary>
        /// Measures the horizontal stretches of <paramref name="text"/> that have ink between two heights.
        /// </summary>
        /// <param name="typeface">the typeface the text is set in</param>
        /// <param name="text">the text</param>
        /// <param name="scale">the size of one font design unit in the canvas's user units: the font size, in those units, divided by the typeface's units per em</param>
        /// <param name="bandTop">the band's upper edge, measured down from the baseline</param>
        /// <param name="bandBottom">the band's lower edge, measured down from the baseline</param>
        /// <param name="letterSpacing">extra advance after every glyph, in user units</param>
        /// <param name="features">how the text is shaped</param>
        /// <returns>
        /// the stretches, left to right and merged where they touch, measured from the start of the run. Each glyph contributes one
        /// stretch covering everything it puts in the band, so the counter of an <c>o</c> or the gap between a <c>g</c>'s loops does not
        /// leave a stray fragment of line. Null when no glyph has an outline to measure (a bitmap font, or a run of spaces): unknown
        /// is not the same as "crosses nothing", which is an empty list.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="typeface"/> or <paramref name="text"/> is <see langword="null"/></exception>
        public static IReadOnlyList<InkSpan>? Measure(Typeface typeface, string text, double scale, double bandTop, double bandBottom,
            double letterSpacing, ShapeSettings features)
        {
            ArgumentNullException.ThrowIfNull(typeface);
            ArgumentNullException.ThrowIfNull(text);
            if (scale <= 0 || bandBottom <= bandTop || typeface.Metrics.UnitsPerEm == 0)
                return null;

            List<InkSpan> spans = [];
            var sawOutline = false;
            double penX = 0;

            foreach (var glyph in Shaper.Shape(typeface, text, features).Glyphs)
            {
                var glyphId = glyph.GlyphIndex;

                if (typeface.TryGetOutline((ushort)glyphId, out var outline))
                {
                    sawOutline = true;

                    // Shaper positioning shifts where a glyph paints without changing its outline, so ink is measured where it is
                    // drawn. A mark attached with a negative offset lands left of the base it follows, which is why the spans are
                    // sorted and merged at the end rather than assumed to be in order.
                    var glyphX = penX + glyph.XOffset * scale;
                    var glyphY = -glyph.YOffset * scale;

                    // The em square is y-up and user space is y-down, so the band's top edge is the high design y.
                    var crossings = outline.Crossings((glyphY - bandBottom) / scale, (glyphY - bandTop) / scale);

                    // One span per glyph, hulling everything it puts in the band, rather than one per ink run: per-run spans leave a
                    // stub of line stranded inside the bowl of a g or the counter of an o (CSS Text Decoration 4 §2.10.5 leaves the
                    // shape to the UA, and browsers hull per glyph). Crossings is sorted and disjoint, so its first start and last end
                    // are the extremes.
                    if (crossings.Count > 0)
                        spans.Add(new InkSpan(glyphX + crossings[0].Start * scale, glyphX + crossings[^1].End * scale));
                }

                penX += (typeface.GetAdvance((ushort)glyphId) + glyph.XAdvanceDelta) * scale + letterSpacing;
            }

            return sawOutline ? MergeSpans(spans) : null;
        }

        /// <summary>The spans sorted left to right and unioned, so the result is ordered and disjoint whatever order glyphs produced them in.</summary>
        private static List<InkSpan> MergeSpans(List<InkSpan> spans)
        {
            if (spans.Count <= 1)
                return spans;

            spans.Sort(static (a, b) => a.Start.CompareTo(b.Start));

            List<InkSpan> merged = [spans[0]];
            for (var i = 1; i < spans.Count; i++)
            {
                var last = merged[^1];
                var next = spans[i];

                if (next.Start <= last.End)
                    merged[^1] = new InkSpan(last.Start, Math.Max(last.End, next.End));
                else
                    merged.Add(next);
            }

            return merged;
        }
    }
}

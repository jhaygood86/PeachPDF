#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Answers one question about a decoded glyph outline: across a horizontal band,
// where does the glyph's ink lie? This is what `text-decoration-skip-ink`
// (CSS Text Decoration 4 §2.5) needs in order to interrupt an underline or
// overline where it would otherwise run through a descender.
//
// Pure geometry over GlyphOutline, in font design units (y-up) - no font, PDF
// or CSS dependency, so it is unit-testable without producing a document.
//
#endregion

using PeachDrawing.Text.Outlines;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// Finds the horizontal ranges in which a glyph outline's ink crosses a horizontal band.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The outline is flattened to polylines and sampled with a handful of scanlines across the band,
    /// each resolved by the same <b>nonzero winding</b> rule the glyph itself is filled with, so the
    /// result is where the glyph is genuinely painted: the counter of an <c>o</c> and the bowl of a
    /// <c>g</c> come back as gaps between runs, not as ink.
    /// </para>
    /// <para>
    /// That is a statement about the glyph, not about where a decoration line should break. Deciding
    /// the skip <i>shape</i> — in particular whether to hull a glyph's runs into one interval — belongs
    /// to the caller (<c>GraphicsAdapter.MeasureInkCrossings</c>), which is what CSS Text Decoration 4
    /// §2.10.5 leaves to the UA. Keeping the two apart is deliberate: this class stays honest geometry,
    /// unit-testable against a fixture whose ink is known exactly.
    /// </para>
    /// <para>
    /// Sampling, rather than solving each segment against the band analytically, is deliberate. A
    /// decoration band is thin (a line's thickness plus its clearance), the consumer dilates every
    /// result by that clearance anyway, and a missed sliver costs a slightly-too-short gap rather than a
    /// wrong-looking one — while the analytic version would need per-segment root finding for every
    /// cubic in every glyph of every decorated word.
    /// </para>
    /// </remarks>
    internal static class GlyphInkScanner
    {
        /// <summary>
        /// Scanlines taken across the band, including both its edges. A glyph stroke entering the band
        /// diagonally is widest at one edge, so sampling the edges is what makes the result cover it;
        /// the interior samples catch a stroke that starts and ends inside the band without reaching
        /// either edge (the crossbar of an <c>f</c> against an overline, say).
        /// </summary>
        private const int ScanlineCount = 5;

        /// <summary>
        /// Straight pieces each cubic is flattened into. Glyph outlines are small and smooth; at this
        /// count the error over an em is well under a design unit at any text size this is used at.
        /// </summary>
        private const int CubicFlattenSteps = 8;

        /// <summary>
        /// How far inside the band's upper edge the topmost scanline is taken, as a fraction of the
        /// band's height. <see cref="CollectCrossings"/>'s crossing test is half-open, so an edge that
        /// <i>ends</i> exactly on the scanline does not count — and a sample taken at
        /// <c>bandHigh</c> itself would therefore find nothing at all in a glyph whose ink stops right
        /// there, reporting an unskipped line straight through it. Small enough to be well below one
        /// design unit for any real band, large enough to survive double rounding.
        /// </summary>
        private const double TopSampleInset = 1e-4;

        /// <summary>
        /// The x-ranges of <paramref name="outline"/>'s ink between <paramref name="bandLow"/> and
        /// <paramref name="bandHigh"/>, in font design units (y-up), left to right and already merged.
        /// </summary>
        /// <param name="outline">the decoded glyph outline</param>
        /// <param name="bandLow">the band's lower y edge (design units, y-up)</param>
        /// <param name="bandHigh">the band's upper y edge; a band with <c>bandHigh &lt;= bandLow</c> yields nothing</param>
        internal static List<(double Start, double End)> Crossings(
            GlyphOutline outline, double bandLow, double bandHigh)
        {
            List<(double Start, double End)> spans = [];

            if (outline.IsEmpty || bandHigh <= bandLow) return spans;

            var edges = Flatten(outline);
            if (edges.Count == 0) return spans;

            List<(double X, int Direction)> crossings = [];
            var height = bandHigh - bandLow;

            for (var i = 0; i < ScanlineCount; i++)
            {
                var y = bandLow + height * i / (ScanlineCount - 1.0);

                if (i == ScanlineCount - 1) y -= height * TopSampleInset;

                crossings.Clear();
                CollectCrossings(edges, y, crossings);
                AddFilledRuns(crossings, spans);
            }

            return Merge(spans);
        }

        /// <summary>
        /// Every contour of <paramref name="outline"/> as closed polylines - each cubic subdivided, each
        /// contour's last point joined back to its first.
        /// </summary>
        private static List<(OutlinePoint From, OutlinePoint To)> Flatten(GlyphOutline outline)
        {
            List<(OutlinePoint, OutlinePoint)> edges = [];

            foreach (var contour in outline.Contours)
            {
                var start = contour.Start;
                var current = start;

                foreach (var segment in contour.Segments)
                {
                    if (segment.IsCubic)
                    {
                        for (var step = 1; step <= CubicFlattenSteps; step++)
                        {
                            var next = CubicAt(current, segment.Control1, segment.Control2, segment.End,
                                (double)step / CubicFlattenSteps);
                            edges.Add((current, next));
                            current = next;
                        }
                    }
                    else
                    {
                        edges.Add((current, segment.End));
                        current = segment.End;
                    }
                }

                // A `glyf` contour is closed by definition; the decoder does not emit the closing segment.
                if (current.X != start.X || current.Y != start.Y)
                {
                    edges.Add((current, start));
                }
            }

            return edges;
        }

        private static OutlinePoint CubicAt(
            OutlinePoint p0, OutlinePoint p1, OutlinePoint p2, OutlinePoint p3, double t)
        {
            var u = 1 - t;
            var a = u * u * u;
            var b = 3 * u * u * t;
            var c = 3 * u * t * t;
            var d = t * t * t;

            return new OutlinePoint(
                a * p0.X + b * p1.X + c * p2.X + d * p3.X,
                a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
        }

        /// <summary>
        /// Where the polyline crosses the horizontal line <paramref name="y"/>, each with the sign of the
        /// crossing's direction — the winding contribution the nonzero rule sums.
        /// </summary>
        /// <remarks>
        /// The half-open test (<c>From.Y &lt;= y &lt; To.Y</c>, or its mirror) is what keeps a vertex
        /// lying exactly on the scanline from being counted twice, which would cancel a real crossing to
        /// a winding of zero and punch a hole in the middle of a stem.
        /// </remarks>
        private static void CollectCrossings(
            List<(OutlinePoint From, OutlinePoint To)> edges, double y,
            List<(double X, int Direction)> crossings)
        {
            foreach (var (from, to) in edges)
            {
                int direction;

                if (from.Y <= y && to.Y > y) direction = 1;
                else if (to.Y <= y && from.Y > y) direction = -1;
                else continue;

                var t = (y - from.Y) / (to.Y - from.Y);
                crossings.Add((from.X + t * (to.X - from.X), direction));
            }
        }

        /// <summary>
        /// Turns one scanline's crossings into the filled runs the nonzero rule paints, appending each
        /// to <paramref name="spans"/>.
        /// </summary>
        private static void AddFilledRuns(
            List<(double X, int Direction)> crossings, List<(double Start, double End)> spans)
        {
            if (crossings.Count < 2) return;

            crossings.Sort(static (a, b) => a.X.CompareTo(b.X));

            var winding = 0;
            double runStart = 0;

            foreach (var (x, direction) in crossings)
            {
                var before = winding;
                winding += direction;

                if (before == 0 && winding != 0)
                {
                    runStart = x;
                }
                else if (before != 0 && winding == 0 && x > runStart)
                {
                    spans.Add((runStart, x));
                }
            }
        }

        /// <summary>
        /// <paramref name="spans"/> sorted and unioned, so overlapping runs from different scanlines
        /// become one range. Always a new list, never <paramref name="spans"/> itself, so the result's
        /// ownership does not depend on how many runs happened to be found.
        /// </summary>
        private static List<(double Start, double End)> Merge(List<(double Start, double End)> spans)
        {
            if (spans.Count <= 1) return [.. spans];

            spans.Sort(static (a, b) => a.Start.CompareTo(b.Start));

            List<(double Start, double End)> merged = [new(spans[0].Start, spans[0].End)];

            for (var i = 1; i < spans.Count; i++)
            {
                var last = merged[^1];
                var next = spans[i];

                if (next.Start <= last.End)
                {
                    merged[^1] = (last.Start, Math.Max(last.End, next.End));
                }
                else
                {
                    merged.Add(next);
                }
            }

            return merged;
        }
    }
}

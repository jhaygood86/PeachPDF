using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// One half-open interval along a text decoration's inline axis — an x-range, since the decoration
    /// painter is horizontal-only (a vertical writing mode wants a vertical band and is deliberately out
    /// of scope, see <see cref="DecorationSegments"/>).
    /// </summary>
    /// <param name="Start">the interval's low edge</param>
    /// <param name="End">the interval's high edge; an interval with <c>End &lt;= Start</c> is empty</param>
    internal readonly record struct DecorationInterval(double Start, double End)
    {
        internal double Length => End - Start;

        /// <summary>
        /// This interval grown by <paramref name="by"/> at each end — how a skipped region is given the
        /// clearance css-text-decor-4 §2.5 asks for around the ink it skips.
        /// </summary>
        internal DecorationInterval Dilated(double by) => new(Start - by, End + by);
    }

    /// <summary>
    /// Turns one text decoration line into the segments actually drawn, by subtracting the regions the
    /// line must not cross.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two separate rules need exactly this shape, which is why they share one implementation rather than
    /// each growing a subtraction of its own:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see href="https://www.w3.org/TR/css-text-decor-3/#line-decoration">css-text-decor-3 §2.4</see> —
    /// "Atomic inlines, such as images and inline blocks, are not decorated", so the line breaks around
    /// an atomic inline's margin box.
    /// </description></item>
    /// <item><description>
    /// <see href="https://www.w3.org/TR/css-text-decor-4/#text-decoration-skip-ink-property">css-text-decor-4
    /// §2.5</see>'s <c>text-decoration-skip-ink</c>, where the line breaks around the glyph ink it would
    /// otherwise cross.
    /// </description></item>
    /// </list>
    /// <para>
    /// Both produce interval exclusions in the same coordinate space as the span, so the only thing this
    /// class knows about either is that they are ranges to remove.
    /// </para>
    /// </remarks>
    internal static class DecorationSegments
    {
        /// <summary>
        /// Segments shorter than this are dropped rather than drawn. A subtraction that leaves a sliver a
        /// fraction of a point wide produces a visually meaningless tick — and, at the dash-pattern
        /// styles, a lone dot at a place the dash phase never intended — so the sliver is discarded
        /// instead. Chosen well below one point (nothing legible is this narrow) yet far above float
        /// noise, so an exactly-abutting exclusion pair cannot leave a stray mark between them.
        /// </summary>
        internal const double MinimumSegmentLength = 0.25;

        /// <summary>
        /// <paramref name="span"/> with every interval in <paramref name="exclusions"/> removed, in
        /// ascending order, dropping any remainder shorter than <see cref="MinimumSegmentLength"/>.
        /// </summary>
        /// <remarks>
        /// <paramref name="exclusions"/> may arrive in any order and may overlap — glyph ink intervals
        /// routinely do, since the clearance each is dilated by makes neighbouring letters' exclusions
        /// touch — so they are sorted and merged before being subtracted. The list is <b>not</b> mutated:
        /// a caller's exclusion list is per line box and is reused across the several decoration lines a
        /// single <c>text-decoration-line</c> value can name.
        /// </remarks>
        /// <param name="span">the whole decoration line, before anything is removed</param>
        /// <param name="exclusions">the regions the line must not cross</param>
        /// <returns>
        /// the segments to draw, left to right. Exactly <paramref name="span"/> when nothing overlaps it,
        /// and empty when the exclusions cover it entirely.
        /// </returns>
        internal static List<DecorationInterval> Subtract(
            DecorationInterval span, IReadOnlyList<DecorationInterval> exclusions)
        {
            List<DecorationInterval> segments = [];

            if (exclusions.Count == 0)
            {
                // Through AddIfDrawable like every other path, so a span too short to be worth drawing is
                // dropped whether or not anything was subtracted from it.
                AddIfDrawable(segments, span.Start, span.End);
                return segments;
            }

            var ordered = new List<DecorationInterval>(exclusions.Count);

            foreach (var exclusion in exclusions)
            {
                // An exclusion that misses the span entirely (an atomic inline past the end of the line's
                // text, ink outside the band) would only cost a comparison in the walk below.
                if (exclusion.Length > 0 && exclusion.End > span.Start && exclusion.Start < span.End)
                    ordered.Add(exclusion);
            }

            ordered.Sort(static (a, b) => a.Start.CompareTo(b.Start));

            var cursor = span.Start;

            foreach (var exclusion in ordered)
            {
                // Merging is implicit: an exclusion contained in one already passed leaves the cursor
                // where it was, so no zero-or-negative-length segment is ever emitted.
                if (exclusion.Start > cursor)
                {
                    AddIfDrawable(segments, cursor, exclusion.Start);
                }

                cursor = Math.Max(cursor, exclusion.End);

                if (cursor >= span.End) return segments;
            }

            AddIfDrawable(segments, cursor, span.End);

            return segments;
        }

        private static void AddIfDrawable(List<DecorationInterval> segments, double start, double end)
        {
            if (end - start >= MinimumSegmentLength)
            {
                segments.Add(new DecorationInterval(start, end));
            }
        }
    }
}

using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using System;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS 2.1 <see href="https://www.w3.org/TR/CSS21/tables.html#border-conflict-resolution">§17.6.2</see>'s
    /// border-conflict resolution, transcribed clause by clause. Pure and stateless - every input is a
    /// <see cref="CollapsedBorderCandidate"/> value, so this has no dependency on <see cref="CssBox"/>,
    /// layout, or paint and is unit-testable on its own.
    /// </summary>
    internal static class CollapsedBorderResolver
    {
        /// <summary>
        /// §17.6.2's own style priority order - "hidden, then double, solid, dashed, dotted, ridge,
        /// outset, groove, and the lowest: inset" (none never reaches here, see <see cref="Resolve"/>).
        /// This is deliberately NOT <see cref="LineStyle"/>'s declaration order
        /// (<c>None, Hidden, Dotted, Dashed, Solid, Double, Groove, Ridge, Inset, Outset</c>), which is
        /// unrelated - never cast the enum to <c>int</c> for this comparison.
        /// </summary>
        internal static int StylePriority(LineStyle style) => style switch
        {
            LineStyle.Hidden => 9,
            LineStyle.Double => 8,
            LineStyle.Solid => 7,
            LineStyle.Dashed => 6,
            LineStyle.Dotted => 5,
            LineStyle.Ridge => 4,
            LineStyle.Outset => 3,
            LineStyle.Groove => 2,
            LineStyle.Inset => 1,
            _ => 0, // None
        };

        /// <summary>
        /// Resolves the single winning border for one grid-line segment from every candidate that
        /// touches it.
        /// </summary>
        /// <param name="candidates">
        /// Every border declaration competing for this segment - order does not matter, every tiebreak
        /// below is explicit.
        /// </param>
        /// <param name="leftToRight">
        /// The table's own <c>direction</c> (not the cell's) - used only for the final position tiebreak,
        /// clause 6.
        /// </param>
        internal static CollapsedBorder Resolve(ReadOnlySpan<CollapsedBorderCandidate> candidates, bool leftToRight)
        {
            CollapsedBorderCandidate? best = null;

            foreach (var candidate in candidates)
            {
                // Clause 1: "hidden" suppresses the border unconditionally, beating every other
                // candidate regardless of width - including a wider one from a more specific origin.
                if (candidate.Style == LineStyle.Hidden)
                    return new CollapsedBorder(LineStyle.Hidden, 0, RColor.Empty);

                // Clause 2: "none" never participates in the comparison at all.
                if (candidate.Style == LineStyle.None)
                    continue;

                if (best is null || Beats(candidate, best.Value, leftToRight))
                    best = candidate;
            }

            return best is null
                ? CollapsedBorder.None
                : new CollapsedBorder(best.Value.Style, best.Value.Width, best.Value.Color);
        }

        /// <summary>Whether <paramref name="a"/> wins over the current best, <paramref name="b"/>.</summary>
        private static bool Beats(CollapsedBorderCandidate a, CollapsedBorderCandidate b, bool leftToRight)
        {
            // Clause 3: widest wins, regardless of style or origin.
            if (a.Width != b.Width)
                return a.Width > b.Width;

            // Clause 4: equal width, style priority decides.
            var stylePriorityA = StylePriority(a.Style);
            var stylePriorityB = StylePriority(b.Style);
            if (stylePriorityA != stylePriorityB)
                return stylePriorityA > stylePriorityB;

            // Clause 5: equal width and style, origin priority decides (cell > row > row-group >
            // column > column-group > table - lower enum value wins, see CollapsedBorderOrigin).
            if (a.Origin != b.Origin)
                return a.Origin < b.Origin;

            // Clause 6: same origin (e.g. two adjacent cells' own borders tie exactly) - the one
            // further to the left (or right, under rtl) wins; failing that, the one further to the top.
            if (a.Column != b.Column)
                return leftToRight ? a.Column < b.Column : a.Column > b.Column;

            return a.Row < b.Row;
        }
    }
}

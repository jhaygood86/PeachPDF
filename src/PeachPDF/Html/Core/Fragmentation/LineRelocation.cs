using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// How far a flex line or grid row has to move to stop being cut by a fragmentainer boundary, per
    /// <see href="https://www.w3.org/TR/css-break-3/#break-between">CSS Fragmentation Level 3 §3.1</see>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A flex container's break points are between its <b>lines</b> and a grid container's between its
    /// <b>rows</b>, not between the items sharing one — so the two engines ask exactly the same question
    /// of exactly the same inputs, and asking it in one place is what stops them drifting apart.
    /// </para>
    /// <para>
    /// The caller accumulates what this returns and moves each line by the <i>running total</i>, not by
    /// its own answer. That is the whole of why the displacement is returned rather than applied here: a
    /// line below one that moved to the next fragmentainer follows it there, and a line needing no
    /// relocation of its own still has to keep its place under the one that did. Applying each line's own
    /// delta instead leaves the lines after the first relocation sitting on top of it.
    /// </para>
    /// </remarks>
    internal static class LineRelocation
    {
        /// <summary>
        /// Whether a forced page break falls at the break point between two adjacent lines — the one
        /// ending with <paramref name="above"/> and the one beginning with <paramref name="below"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// §3.1 states this of the break <i>point</i>, not of either box: a forced break occurs there if
        /// the earlier sibling's <c>break-after</c> <b>or</b> the later sibling's <c>break-before</c> has
        /// a forced value, so one side asking is sufficient and neither side can veto the other. Reading
        /// only the later side is what made <c>break-after: page</c> on a flex or grid item inert while
        /// the same intent spelt as <c>break-before</c> on the following line took effect.
        /// </para>
        /// <para>
        /// Both sides are read one box deep rather than through the chains they begin and end
        /// (<see cref="BreakPropagation"/>): a break travelling out of an item would name a position the
        /// engine is about to overwrite, which is why propagation stops before a box whose children an
        /// engine places for itself.
        /// </para>
        /// </remarks>
        /// <param name="above">
        /// the items of the line immediately above the break point, or null where there is none — the
        /// first line of a container has nothing before it for a <c>break-after</c> to be declared on
        /// </param>
        /// <param name="below">the items of the line immediately below it</param>
        internal static bool ForcedBreakBetween(IEnumerable<CssBox>? above, IEnumerable<CssBox> below) =>
            below.Any(box => BreakValues.IsForcedPageBreak(box.BreakBefore))
            || (above is not null && above.Any(box => BreakValues.IsForcedPageBreak(box.BreakAfter)));

        /// <summary>
        /// How far the line spanning <paramref name="top"/>..<paramref name="bottom"/> must move down,
        /// or 0 to leave it where it is.
        /// </summary>
        /// <param name="container">the layout container, for the page grid</param>
        /// <param name="top">the line's top, already displaced by everything above it that moved</param>
        /// <param name="bottom">the line's bottom, displaced the same way</param>
        /// <param name="takesAForcedBreak">
        /// a forced break falls at the break point before it — see <see cref="ForcedBreakBetween"/>
        /// </param>
        /// <param name="mayNotBeCut">
        /// something in it asks not to be broken, or §2 says no user agent may break it. A line that
        /// neither asks nor forbids is left where it is and the boundary cuts it — the same answer
        /// ordinary block content gets when it has no line to break at.
        /// </param>
        internal static double DeltaFor(
            HtmlContainerInt container, double top, double bottom, bool takesAForcedBreak, bool mayNotBeCut)
        {
            var slot = container.SlotStartingAt(top);

            var straddles = mayNotBeCut
                            && bottom - HtmlContainerInt.PageBoundaryEpsilon > container.PageBottomOf(slot);

            if (!takesAForcedBreak && !straddles) return 0;

            // A line taller than a whole band has nowhere better to be, and moving it would ask the same
            // question again on the next fragmentainer, forever.
            if (!takesAForcedBreak && bottom - top > container.PageBandHeightOf(slot + 1)) return 0;

            var delta = container.PageTopOf(slot + 1) - top;

            return delta > 0 ? delta : 0;
        }
    }
}

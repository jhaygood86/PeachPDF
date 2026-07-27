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
        /// How far the line spanning <paramref name="top"/>..<paramref name="bottom"/> must move down,
        /// or 0 to leave it where it is.
        /// </summary>
        /// <param name="container">the layout container, for the page grid</param>
        /// <param name="top">the line's top, already displaced by everything above it that moved</param>
        /// <param name="bottom">the line's bottom, displaced the same way</param>
        /// <param name="takesAForcedBreak">a <c>break-before</c> on any of its items forces a break here</param>
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

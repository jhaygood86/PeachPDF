using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core.Fragmentation
{
    /// <summary>
    /// The fragmentainer-relative space a §4.3 mover asks its "does this fit" / "does this straddle"
    /// question against: which fragmentainer is in question, and how far a box's own border-box top
    /// already sits below that fragmentainer's content edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A read-only view over the same page-grid arithmetic (<see cref="HtmlContainerInt.PageTopOf"/>,
    /// <see cref="HtmlContainerInt.PageBandHeightOf"/>) the movers already called directly, factored into
    /// one type so "does this fit the band it's about to occupy" is asked the same way everywhere it is
    /// asked. <see cref="Fragmentainer"/> is constructed fresh at the slot in question rather than read
    /// off the ambient <see cref="HtmlContainerInt.CurrentFragmentainer"/> — these movers ask about a
    /// box's own already-settled position, or a specific candidate slot, never about which fragmentainer
    /// the live pass's cursor is currently naming — so this stays exactly as behaviour-neutral as the raw
    /// calls it replaces (see <c>.claude/recent-fixes/</c> on why conflating "the box's own slot" with
    /// "the pass's cursor" is not, in general, a safe substitution).
    /// </para>
    /// <para>
    /// <see cref="Fragmentainer"/> is null exactly where there is no page grid to ask at all
    /// (<see cref="HtmlContainerInt.HasRealPageGrid"/> false — a measurement/unpaginated pass): no
    /// fragmentation question may be asked there, per css-break-3 §400(c)'s own requirement.
    /// </para>
    /// </remarks>
    /// <param name="Fragmentainer">the fragmentainer being asked about, or null where there is none</param>
    /// <param name="BlockOffset">the box's own border-box top, below <see cref="Fragmentainer"/>'s content edge</param>
    internal readonly record struct BlockConstraint(FragmentainerContext? Fragmentainer, double BlockOffset)
    {
        /// <summary>No page grid at all — a measurement pass, where no fragmentation question may be asked.</summary>
        internal static readonly BlockConstraint Measurement = default;

        /// <summary>How tall <see cref="Fragmentainer"/>'s own band is, or unbounded where there is none.</summary>
        internal double NextBandHeight => Fragmentainer?.BandHeight ?? double.MaxValue;

        /// <summary>How much of <see cref="Fragmentainer"/>'s band remains below <see cref="BlockOffset"/>.</summary>
        internal double RemainingBlockSize => NextBandHeight - BlockOffset;

        /// <summary>
        /// Whether content <paramref name="blockExtent"/> tall, starting at <see cref="BlockOffset"/>,
        /// crosses out of this band. Always false during a measurement pass — there is nothing to
        /// straddle out of.
        /// </summary>
        internal bool Straddles(double blockExtent) => Fragmentainer is not null && blockExtent > RemainingBlockSize;

        /// <summary>
        /// Where <see cref="Fragmentainer"/>'s band ends, in document space — equally, the next
        /// fragmentainer's own content top.
        /// </summary>
        internal double AbsoluteBandBottom => Fragmentainer?.BandBottom ?? double.MaxValue;

        /// <summary>
        /// The constraint a box already placed at its own <c>Location.Y</c> asks its straddle questions
        /// against — the page-grid slot its own top falls in, at that slot's own offset.
        /// </summary>
        internal static BlockConstraint For(CssBox box)
        {
            var container = box.HtmlContainer;
            if (container is null || !container.HasRealPageGrid) return Measurement;

            var slot = container.PageIndexOf(box.Location.Y);
            var fragmentainer = new FragmentainerContext(container, box, slot);

            return new BlockConstraint(fragmentainer, box.Location.Y - fragmentainer.BandTop);
        }

        /// <summary>
        /// The constraint a box would face if placed <paramref name="blockOffset"/> below fragmentainer
        /// <paramref name="slot"/>'s own content edge.
        /// </summary>
        internal static BlockConstraint AtSlot(HtmlContainerInt container, CssBox contextRoot, int slot, double blockOffset = 0) =>
            new(new FragmentainerContext(container, contextRoot, slot), blockOffset);

        /// <summary>The same box's constraint one fragmentainer later, at that band's own content top.</summary>
        internal BlockConstraint AtNextSlot() =>
            Fragmentainer is null
                ? this
                : new BlockConstraint(
                    new FragmentainerContext(Fragmentainer.Container, Fragmentainer.ContextRoot, Fragmentainer.SlotIndex + 1),
                    0);
    }
}

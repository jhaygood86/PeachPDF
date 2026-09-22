using PeachPDF.Html.Core.Dom;
using System.Collections.Generic;

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
    /// <see cref="Fragmentainer"/> is null wherever no fragmentation question may be asked at all, per
    /// css-break-3 §400(c)'s own requirement — either there is no page grid to ask
    /// (<see cref="HtmlContainerInt.HasRealPageGrid"/> false), or breaking is not live right now
    /// (<see cref="HtmlContainerInt.CurrentFragmentainer"/> null: a flex/grid item's own measurement
    /// pass, <see cref="HtmlContainerInt.DetachFragmentainer"/>). The second case is content laid out
    /// at a throwaway, provisional position purely to be measured — asking either question against it
    /// would translate that content to a real page-break target before the engine measuring it has
    /// even decided where it will really go.
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

        /// <summary>
        /// How far above <see cref="Fragmentainer"/>'s own band bottom content has to stop because
        /// something has reserved that room at this slot's foot - a repeating table <c>&lt;tfoot&gt;</c>
        /// (<see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">css-tables-3 §6.2</see>) or a
        /// page's footnote area (<see href="https://www.w3.org/TR/css-gcpm-3/#footnotes">css-gcpm-3's
        /// <c>float: footnote</c></see>) are the two reservations <see cref="FragmentainerContext.ReserveBandEnd"/>
        /// exists for today. Deliberately read from the <b>live</b> pass's own context
        /// (<see cref="HtmlContainerInt.CurrentFragmentainer"/>), not <see cref="Fragmentainer"/> itself -
        /// <see cref="For"/>/<see cref="AtSlot"/>/<see cref="EndingAt"/> each construct a fresh
        /// <see cref="FragmentainerContext"/> for the slot in question (see this type's own remarks on why),
        /// so it never itself carries a reservation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The slot the live pass is actually filling</b> asks <see cref="FragmentainerContext.BandEndInsetOf"/>
        /// directly, which already correctly handles a repeating <c>&lt;tfoot&gt;</c>'s reservation (a
        /// genuine constant across every slot the table spans, composed forward from its own
        /// <c>fromSlot</c>) as well as the live slot's own footnote-area amount, whichever is active.
        /// </para>
        /// <para>
        /// <b>A different slot</b> answers from <see cref="HtmlContainerInt.FootnoteAreaHeightsBySlot"/>
        /// directly instead - that dictionary is seeded fresh, per slot, with a genuinely different amount
        /// each time <see cref="HtmlContainerInt.LayoutDocument"/> enters a new one, so (unlike asking the
        /// live context's own <c>BandEndInsetOf</c> about a slot it never itself reserved for) this can
        /// never misattribute one page's footnote reservation to another's - measured, before this, as a
        /// `break-inside: avoid` box being told a footnote-free next page had less room than it really
        /// did. A repeating <c>&lt;tfoot&gt;</c>'s own reservation is not visible this way for a
        /// non-live slot (it isn't tracked per-slot the way footnotes are, and the live context's
        /// <c>_bandEndReservation</c> doesn't say which kind is active) - narrower than a full fix would
        /// be, but a strict improvement over reading zero unconditionally, and never a wrong answer - see
        /// <c>.claude/accepted-gaps/footnote-reservation-not-honored-by-every-4-3-mover.md</c>.
        /// </para>
        /// <para>
        /// Zero during a measurement pass, where there is no live fragmentainer to ask at all.
        /// </para>
        /// </remarks>
        internal double BandEndInset =>
            Fragmentainer is null
                ? 0
                : Fragmentainer.Container.CurrentFragmentainer is { } live && live.SlotIndex == Fragmentainer.SlotIndex
                    ? live.BandEndInsetOf(Fragmentainer.SlotIndex)
                    : Fragmentainer.Container.FootnoteAreaHeightsBySlot.GetValueOrDefault(Fragmentainer.SlotIndex, 0);

        /// <summary>How much of <see cref="Fragmentainer"/>'s band remains below <see cref="BlockOffset"/>, after <see cref="BandEndInset"/>.</summary>
        internal double RemainingBlockSize => NextBandHeight - BlockOffset - BandEndInset;

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
        /// Where <see cref="Fragmentainer"/>'s band begins, in document space — the coordinate content
        /// flush at its content edge is placed at. Zero during a measurement pass, where there is no band
        /// and nothing may be placed against one.
        /// </summary>
        internal double AbsoluteBandTop => Fragmentainer?.BandTop ?? 0;

        /// <summary>
        /// Whether a bottom edge at <paramref name="blockEnd"/> has crossed out of this band, under
        /// <see cref="HtmlContainerInt.SlotEndingAt"/>'s bottom-edge convention: an edge landing flush on
        /// the boundary has <i>not</i> crossed it, because it is the first thing in the next band.
        /// </summary>
        /// <remarks>
        /// Deliberately not <see cref="Straddles"/>. That one asks whether content of a given extent
        /// starting at <see cref="BlockOffset"/> reaches past the band, and compares with a bare
        /// <c>&gt;</c>; this one asks the same question of an absolute coordinate and carries
        /// <see cref="HtmlContainerInt.PageBoundaryEpsilon"/>, which is what keeps a box landing exactly on
        /// a boundary out of the band it merely touches. One membership question, one tolerance — the two
        /// are not interchangeable, and substituting either for the other moves boxes a page.
        /// </remarks>
        internal bool FallsPast(double blockEnd) =>
            Fragmentainer is not null && HtmlContainerInt.FallsPast(blockEnd, Fragmentainer.Band);

        /// <summary>
        /// The constraint a box already placed at its own <c>Location.Y</c> asks its straddle questions
        /// against — the page-grid slot its own top falls in, at that slot's own offset.
        /// </summary>
        internal static BlockConstraint For(CssBox box)
        {
            var container = box.HtmlContainer;

            // HasRealPageGrid alone answers "is there a page grid at all", not "may this pass ask it
            // a fragmentation question" - a flex/grid item's own measurement pass (MeasureItem's
            // PerformLayoutBlockified) keeps a real page grid but detaches the fragmentainer
            // (HtmlContainerInt.DetachFragmentainer) precisely so content laid out at its provisional
            // position cannot answer one. Without this, a nested box whose throwaway coordinate
            // happens to straddle a page boundary during that measurement gets translated by the
            // avoid/monolithic or widows mover below to a position it is about to be measured away
            // from anyway, corrupting the very size the engine is in the middle of computing.
            if (container is null || !container.HasRealPageGrid || container.CurrentFragmentainer is null)
                return Measurement;

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

        /// <summary>
        /// The constraint over the fragmentainer a <i>bottom edge</i> at <paramref name="blockEnd"/> ends
        /// in — <see cref="HtmlContainerInt.SlotEndingAt"/>'s convention, so an edge flush on a boundary
        /// belongs to the band above it rather than to the one it merely touches.
        /// </summary>
        /// <remarks>
        /// The counterpart of <see cref="For"/>, which asks about a box's own <i>top</i> and so uses the
        /// top-edge convention (<see cref="HtmlContainerInt.PageIndexOf"/>). §5.2's margin-crossing test is
        /// asked about a predecessor's bottom edge and pairs with <see cref="FallsPast"/>, which carries the
        /// same tolerance; the two conventions are not interchangeable.
        /// </remarks>
        internal static BlockConstraint EndingAt(HtmlContainerInt container, CssBox contextRoot, double blockEnd)
        {
            // Same detached-pass guard as For's own remark above.
            if (!container.HasRealPageGrid || container.CurrentFragmentainer is null) return Measurement;

            var fragmentainer = new FragmentainerContext(container, contextRoot, container.SlotEndingAt(blockEnd));

            return new BlockConstraint(fragmentainer, blockEnd - fragmentainer.BandTop);
        }

        /// <summary>The same box's constraint one fragmentainer later, at that band's own content top.</summary>
        internal BlockConstraint AtNextSlot() =>
            Fragmentainer is null
                ? this
                : new BlockConstraint(
                    new FragmentainerContext(Fragmentainer.Container, Fragmentainer.ContextRoot, Fragmentainer.SlotIndex + 1),
                    0);
    }
}

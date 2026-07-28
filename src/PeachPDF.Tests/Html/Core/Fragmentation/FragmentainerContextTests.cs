using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;

namespace PeachPDF.Tests.Html.Core.Fragmentation
{
    /// <summary>
    /// The fragmentainer a layout pass targets. These pin the two things the rest of the fragmentation
    /// work stands on: that the context is a plain cursor over the existing page grid (so no second,
    /// drifting notion of where a page starts is introduced), and that
    /// <see cref="FragmentainerContext.ResumeContentTop"/> is the band top itself.
    /// </summary>
    public class FragmentainerContextTests
    {
        private const double BandHeight = 800;
        private const double MarginTop = 70;

        private static HtmlContainerInt CreateContainer(double bandHeight = BandHeight) =>
            new(new PdfSharpAdapter())
            {
                PageSize = new RSize(500, bandHeight),
                MarginTop = MarginTop,
            };

        private static FragmentainerContext CreateContext(HtmlContainerInt container, int slot = 0) =>
            new(container, new CssBox(null, null), slot);

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        public void BandGeometry_IsTheExistingPageGrid_NotASecondSourceOfTruth(int slot)
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot);

            Assert.Equal(container.PageTopOf(slot), context.BandTop, 9);
            Assert.Equal(container.PageBottomOf(slot), context.BandBottom, 9);
            Assert.Equal(container.PageBandHeightOf(slot), context.BandHeight, 9);
        }

        [Fact]
        public void ResumeContentTop_IsTheBandTopItself_NotOnePastIt()
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot: 3);

            // The retired CssRect.BreakPage relocated to NextPageTopOf(Top) + 1, leaving every
            // continuation line one unit below its fragmentainer's content edge. css-break-3 §2 puts a
            // fragment at the content edge, so the nudge is gone.
            Assert.Equal(container.PageTopOf(3), context.ResumeContentTop, 9);
        }

        [Fact]
        public void ResumeContentTop_LandsExactlyOnABoundary_AndTheGridStillAttributesItForward()
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot: 2);

            // Dropping the +1 puts resumed content exactly on a band top, which is the case the nudge
            // used to hide: the grid must attribute that value to the band it starts, not the one before.
            Assert.Equal(2, container.PageIndexOf(context.ResumeContentTop));
            Assert.Equal(2, container.PageIndexOf(context.ResumeContentTop + HtmlContainerInt.PageBoundaryEpsilon));
        }

        [Fact]
        public void StepOverTo_MovesTheWholeBandOntoTheSlotThePassSteppedTo()
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot: 0);

            // A forced break is realized by placement: the box is put at the content top of the slot the
            // break names and the pass carries on from there, with no resumption record in between. From
            // that point the fragmentainer being filled is slot 2, so every band question has to answer
            // about slot 2 - which is what "the pass has left slot 0 behind" means concretely.
            context.StepOverTo(2);

            Assert.Equal(2, context.SlotIndex);
            Assert.Equal(container.PageTopOf(2), context.BandTop, 9);
            Assert.Equal(container.PageBottomOf(2), context.BandBottom, 9);
            Assert.Equal(new PageBand(container.PageTopOf(2), container.PageBottomOf(2)), context.Band);
            Assert.Equal(container.PageTopOf(2), context.ResumeContentTop, 9);
        }

        [Fact]
        public void StepOverTo_IsMonotonic_SoASlotThePassHasAlreadyLeftIsIgnored()
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot: 3);

            // A pass fills fragmentainers in document order and never goes back to an earlier one. A pass
            // that has to reconsider one is re-entered from the driver with a context of its own, so a
            // cursor that could move backwards would only ever be describing a stale placement - and one
            // slot too early is a whole page of content read against the wrong band.
            context.StepOverTo(2);
            Assert.Equal(3, context.SlotIndex);

            context.StepOverTo(3);
            Assert.Equal(3, context.SlotIndex);

            context.StepOverTo(4);
            Assert.Equal(4, context.SlotIndex);
        }

        /// <summary>
        /// A repeated table header claims room at its fragmentainer's content edge, and a flow resuming
        /// there starts below it — css-tables-3 §6.2's "leave room", which the row cursor cannot state for
        /// a cell whose content position is the flow's question rather than the cursor's.
        /// </summary>
        [Fact]
        public void ReserveResumeContent_StartsAResumedFlowBelowTheRoomTaken()
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot: 2);

            context.ReserveResumeContent(30);

            Assert.Equal(30, context.ResumeContentInset, 9);
            Assert.Equal(container.PageTopOf(2) + 30, context.ResumeContentTop, 9);

            // The band itself is untouched: the reservation says where content begins, not where the
            // fragmentainer does, so nothing that measures against the band moves.
            Assert.Equal(container.PageTopOf(2), context.BandTop, 9);
            Assert.Equal(container.PageBandHeightOf(2), context.BandHeight, 9);
        }

        /// <summary>
        /// A reservation belongs to the fragmentainer it was made in and stops applying once the pass has
        /// stepped past it.
        /// </summary>
        /// <remarks>
        /// A forced break is realized by placement, so a pass steps over a fragmentainer without ending —
        /// and the page such a break opens gets no repeated header, because the table's per-row header
        /// block only runs at a break between two rows. Room held there is room held for a header that is
        /// not drawn: measured as a 13pt blank strip at the top of that page before the slot became part
        /// of the reservation.
        /// </remarks>
        [Fact]
        public void AReservation_StopsApplyingOnceThePassHasSteppedPastItsOwnFragmentainer()
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot: 2);

            context.ReserveResumeContent(30);

            context.StepOverTo(3);

            Assert.Equal(0, context.ResumeContentInset, 9);
            Assert.Equal(container.PageTopOf(3), context.ResumeContentTop, 9);
        }

        /// <summary>
        /// Reservations compose within one fragmentainer and are restored in the order they were taken, so
        /// a subtree that reserves room owes it only for as long as it is being laid out.
        /// </summary>
        [Fact]
        public void ReserveResumeContent_ComposesWithinAFragmentainer_AndRestoresWhatItReplaced()
        {
            var container = CreateContainer();
            var context = CreateContext(container, slot: 1);

            var outer = context.ReserveResumeContent(20);
            var inner = context.ReserveResumeContent(12);

            Assert.Equal(32, context.ResumeContentInset, 9);

            context.RestoreResumeContent(inner);
            Assert.Equal(20, context.ResumeContentInset, 9);

            context.RestoreResumeContent(outer);
            Assert.Equal(0, context.ResumeContentInset, 9);
            Assert.Equal(container.PageTopOf(1), context.ResumeContentTop, 9);
        }

        [Fact]
        public void StepOverTo_IsANoOpForANestedFragmentainer()
        {
            var container = CreateContainer();
            var column = new FragmentainerContext(container, new CssBox(null, null), slotIndex: 1,
                ownBand: (200, 400));

            // A multi-column column's band is not a slot of the page grid at all - SlotIndex there names
            // only the page the column sits on. A forced *page* break inside a column is not the column's
            // to satisfy: it escapes to the page driver (CssBox.PlaceBlockChild's escaping-break arm), so
            // stepping this cursor would re-band the column onto a page it never reached.
            column.StepOverTo(5);

            Assert.Equal(1, column.SlotIndex);
            Assert.Equal(new PageBand(200, 400), column.Band);
        }

        [Fact]
        public void IsFragmenting_IsFalse_WhenThePageHeightIsTheMeasurementSentinel()
        {
            var container = CreateContainer(bandHeight: double.MaxValue);

            Assert.False(CreateContext(container).IsFragmenting);
        }

        [Fact]
        public void IsFragmenting_IsFalse_ForAZeroHeightPage()
        {
            var container = CreateContainer(bandHeight: 0);

            Assert.False(CreateContext(container).IsFragmenting);
        }

        // "May a break be recorded here?" and "is the legacy per-word relocation suppressed?" are different
        // questions, and this property used to answer both at once by reading the flag directly. That made
        // it impossible to enable breaking for a placed flex or grid item without also re-enabling the word
        // relocation. The engines' measurement re-layouts now enter a monolithic scope of their own
        // alongside setting the flag, so the two are independent here.
        [Fact]
        public void IsFragmenting_IsIndependentOfSuppressWordPageBreaks()
        {
            var container = CreateContainer();
            var context = CreateContext(container);

            Assert.True(context.IsFragmenting);

            container.SuppressWordPageBreaks = true;
            Assert.True(context.IsFragmenting);

            container.SuppressWordPageBreaks = false;
            Assert.True(context.IsFragmenting);
        }

        [Fact]
        public void DetachFragmentainer_LeavesNoFragmentainerToAskAtAll_AndComposes()
        {
            var container = CreateContainer();
            container.EnterNestedFragmentainer(CreateContext(container));

            Assert.True(container.IsFragmenting);
            Assert.NotNull(container.CurrentFragmentainer);

            var outer = container.DetachFragmentainer();

            // Not "a fragmentainer that will not break" — no fragmentainer. Everything that asks *which*
            // fragmentainer this is, and not merely whether it may break, gets the same answer.
            Assert.Null(container.CurrentFragmentainer);
            Assert.False(container.IsFragmenting);

            // Nested measurement scopes compose: the inner restore puts back "still detached".
            var inner = container.DetachFragmentainer();
            Assert.Null(container.CurrentFragmentainer);
            container.RestoreFragmentainer(inner);
            Assert.Null(container.CurrentFragmentainer);

            container.RestoreFragmentainer(outer);
            Assert.True(container.IsFragmenting);
        }

        [Fact]
        public void ASuppressedContext_ExistsButDoesNotFragment()
        {
            var container = CreateContainer();
            var root = new CssBox(null, null);

            // The driver's last-resort relaxation: a real pass, with a real slot the emitter reads, that
            // must not break again.
            var context = new FragmentainerContext(container, root, slotIndex: 2, suppressed: true);

            Assert.False(context.IsFragmenting);
            Assert.Equal(2, context.SlotIndex);
        }

        [Fact]
        public void RecordBreak_IsHowAPassHandsItsResumptionRecordToTheDriver()
        {
            var container = CreateContainer();
            var root = new CssBox(null, null);
            var context = new FragmentainerContext(container, root, slotIndex: 0);

            Assert.Null(context.OutgoingToken);

            var token = new BlockBreakToken(root, ResumeSlotIndex: 1, ResumeChildIndex: 2, ChildToken: null, IsBreakBefore: true, ResumeTopOverride: null);
            context.RecordBreak(token);
            Assert.Same(token, context.OutgoingToken);
        }

        [Fact]
        public void ContainerIsFragmenting_IsFalse_OutsideALayoutPass()
        {
            var container = CreateContainer();

            // No LayoutDocument pass is running, so there is no fragmentainer to break against.
            Assert.Null(container.CurrentFragmentainer);
            Assert.False(container.IsFragmenting);
        }
    }
}

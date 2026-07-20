using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Pure-math tests for the shifted-grid helper family on <see cref="HtmlContainerInt"/> —
    /// <see cref="HtmlContainerInt.PageIndexOf"/>/<see cref="HtmlContainerInt.PageTopOf"/> and the
    /// derived <see cref="HtmlContainerInt.PageBandHeightOf"/>/<see cref="HtmlContainerInt.PageBottomOf"/>/
    /// <see cref="HtmlContainerInt.NextPageTopOf"/> every page-boundary decision now routes through.
    /// On the uniform grid these identities pin the arithmetic the raw-math call sites were migrated
    /// away from (modulo in CssBox/CssRect.BreakPage, hand-rolled pageIndex/pageTop in the table and
    /// multicol engines).
    /// </summary>
    public class HtmlContainerIntPageGridTests
    {
        private const double BandHeight = 800;
        private const double MarginTop = 70;

        private static HtmlContainerInt CreateContainer()
        {
            var container = new HtmlContainerInt(new PdfSharpAdapter())
            {
                PageSize = new RSize(500, BandHeight),
                MarginTop = MarginTop,
            };
            return container;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        public void PageBottomOf_EqualsNextSlotsTop(int pageIndex)
        {
            var container = CreateContainer();

            Assert.Equal(container.PageTopOf(pageIndex + 1), container.PageBottomOf(pageIndex));
        }

        [Fact]
        public void PageBandHeightOf_UniformGrid_IsPageSizeHeight()
        {
            var container = CreateContainer();

            Assert.Equal(BandHeight, container.PageBandHeightOf(0));
            Assert.Equal(BandHeight, container.PageBandHeightOf(7));
        }

        [Theory]
        [InlineData(MarginTop, 0)]                       // flush at slot 0's content top
        [InlineData(MarginTop + BandHeight - 0.01, 0)]   // just above the first boundary
        [InlineData(MarginTop + BandHeight, 1)]          // flush ON the boundary -> next slot
        [InlineData(MarginTop + 2.5 * BandHeight, 2)]
        public void PageIndexOf_ShiftedGridAttribution(double y, int expectedSlot)
        {
            var container = CreateContainer();

            Assert.Equal(expectedSlot, container.PageIndexOf(y));
        }

        [Theory]
        [InlineData(MarginTop + 1)]
        [InlineData(MarginTop + BandHeight + 1)]
        [InlineData(MarginTop + 4 * BandHeight + 123.5)]
        public void NextPageTopOf_IsTopOfSlotAfterTheOneContainingY(double y)
        {
            var container = CreateContainer();

            Assert.Equal(container.PageTopOf(container.PageIndexOf(y) + 1), container.NextPageTopOf(y));
        }

        [Fact]
        public void PageTopOf_RoundTripsThroughPageIndexOf()
        {
            var container = CreateContainer();

            for (var k = 0; k < 5; k++)
                Assert.Equal(k, container.PageIndexOf(container.PageTopOf(k)));
        }

        // ── CssBox.BreakPage (the block-level relocation used by table spacing boxes) ──

        [Fact]
        public void CssBoxBreakPage_StraddlingBox_RelocatesToNextSlotTop()
        {
            var container = CreateContainer();
            var box = PeachPDF.Html.Core.Dom.CssBox.CreateBlock();
            box.HtmlContainer = container;
            box.Location = new RPoint(0, MarginTop + BandHeight - 50);
            box.ActualBottom = MarginTop + BandHeight + 50;

            Assert.True(box.BreakPage());
            // The historical "+1" nudge past the slot top is deliberately preserved.
            Assert.Equal(container.PageTopOf(1) + 1, box.Location.Y);
        }

        [Fact]
        public void CssBoxBreakPage_FlushFitAtBoundary_DoesNotRelocate()
        {
            // A box ending exactly ON a slot boundary is wholly inside the earlier slot - the
            // flush-fit epsilon makes it a non-break (the historical modulo formulation spuriously
            // relocated it a full page).
            var container = CreateContainer();
            var box = PeachPDF.Html.Core.Dom.CssBox.CreateBlock();
            box.HtmlContainer = container;
            box.Location = new RPoint(0, MarginTop + BandHeight - 100);
            box.ActualBottom = MarginTop + BandHeight;

            Assert.False(box.BreakPage());
            Assert.Equal(MarginTop + BandHeight - 100, box.Location.Y);
        }

        [Fact]
        public void CssBoxBreakPage_TallerThanBand_NeverRelocates()
        {
            var container = CreateContainer();
            var box = PeachPDF.Html.Core.Dom.CssBox.CreateBlock();
            box.HtmlContainer = container;
            box.Location = new RPoint(0, MarginTop + 10);
            box.ActualBottom = MarginTop + 10 + BandHeight + 5;

            Assert.False(box.BreakPage());
        }
    }
}

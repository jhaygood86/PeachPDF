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
            // The slot's own content edge, per css-break-3 §2. This used to land one unit below it,
            // to keep the relocated box off the exact boundary value - a question PageIndexOf's own
            // epsilon already answers, as the flush-fit case below shows.
            Assert.Equal(container.PageTopOf(1), box.Location.Y);
            Assert.Equal(1, container.PageIndexOf(box.Location.Y));
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
        [Fact]
        public void SlotStartingAt_ATopEdgeFlushOnABoundary_BeginsTheLaterSlot()
        {
            var container = CreateContainer();

            Assert.Equal(1, container.SlotStartingAt(MarginTop + BandHeight));
            Assert.Equal(0, container.SlotStartingAt(MarginTop + BandHeight - 1));
        }

        [Fact]
        public void SlotEndingAt_ABottomEdgeFlushOnABoundary_EndsTheEarlierSlot()
        {
            var container = CreateContainer();

            Assert.Equal(0, container.SlotEndingAt(MarginTop + BandHeight));
            Assert.Equal(1, container.SlotEndingAt(MarginTop + BandHeight + 1));
        }

        [Fact]
        public void TheTwoConventions_DisagreeOnlyWithinTheEpsilonOfABoundary()
        {
            var container = CreateContainer();
            var boundary = MarginTop + BandHeight;

            // The whole point of having two: on a boundary they answer differently, and everywhere
            // else they agree — so a call site that picks the wrong one is wrong only where it matters.
            Assert.NotEqual(container.SlotStartingAt(boundary), container.SlotEndingAt(boundary));

            foreach (var y in new[] { boundary - 100, boundary + 100, MarginTop + 5 })
            {
                Assert.Equal(container.SlotStartingAt(y), container.SlotEndingAt(y));
            }
        }
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        public void BandOfSlot_IsContiguousWithTheNext(int slot)
        {
            var container = CreateContainer();

            // The identity the band form of every boundary question rests on: "the next slot's top" and
            // "this band's bottom" are the same coordinate.
            Assert.Equal(container.BandOfSlot(slot + 1).Top, container.BandOfSlot(slot).Bottom);
        }

        [Fact]
        public void FallsPast_ABottomEdgeFlushOnTheBandBottom_HasNotLeftIt()
        {
            var container = CreateContainer();
            var band = container.BandOfSlot(0);

            Assert.False(HtmlContainerInt.FallsPast(band.Bottom, band));
            Assert.True(HtmlContainerInt.FallsPast(band.Bottom + HtmlContainerInt.PageBoundaryEpsilon, band));
        }

        [Theory]
        [InlineData(0, 10)]
        [InlineData(0, BandHeight)]
        [InlineData(0, BandHeight + 1)]
        [InlineData(10, BandHeight * 2)]
        [InlineData(BandHeight, BandHeight + 5)]
        [InlineData(BandHeight - 1, BandHeight)]
        public void FallsPast_AgreesWithTheSlotComparisonItReplaces(double topOffset, double bottomOffset)
        {
            var container = CreateContainer();
            var top = MarginTop + topOffset;
            var bottom = MarginTop + bottomOffset;

            // The rewrite's whole claim: asking the band is the same question as comparing the slot the
            // top starts in against the slot the bottom ends in.
            Assert.Equal(
                container.SlotStartingAt(top) < container.SlotEndingAt(bottom),
                HtmlContainerInt.FallsPast(bottom, container.BandStartingAt(top)));
        }
    }
}

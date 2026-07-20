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
    }
}

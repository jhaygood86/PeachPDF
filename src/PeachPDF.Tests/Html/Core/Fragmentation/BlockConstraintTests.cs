using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;

namespace PeachPDF.Tests.Html.Core.Fragmentation
{
    /// <summary>
    /// The fragmentainer-relative space a §4.3 mover asks its "does this fit" question against.
    /// <see cref="BlockConstraint"/> is a read-only view over the same page-grid arithmetic the movers
    /// called directly before it existed - these pin its arithmetic in isolation, against the plain
    /// <see cref="HtmlContainerInt.PageTopOf"/>/<see cref="HtmlContainerInt.PageBandHeightOf"/> calls it
    /// replaces, so a future change to either cannot drift them apart unnoticed.
    /// </summary>
    public class BlockConstraintTests
    {
        private const double BandHeight = 800;
        private const double MarginTop = 70;

        private static HtmlContainerInt CreateContainer(double bandHeight = BandHeight) =>
            new(new PdfSharpAdapter())
            {
                PageSize = new RSize(500, bandHeight),
                MarginTop = MarginTop,
            };

        private static CssBox CreateBox(HtmlContainerInt container, double locationY)
        {
            var box = new CssBox(null, null) { HtmlContainer = container };
            box.Location = new RPoint(0, locationY);
            return box;
        }

        [Fact]
        public void For_PlacesTheBandAtTheSlotTheBoxsOwnTopFallsIn()
        {
            var container = CreateContainer();
            // Slot 2 starts at MarginTop + 2 * BandHeight; land the box 30pt into it.
            var box = CreateBox(container, MarginTop + 2 * BandHeight + 30);

            var constraint = BlockConstraint.For(box);

            Assert.NotNull(constraint.Fragmentainer);
            Assert.Equal(container.PageTopOf(2), constraint.Fragmentainer!.BandTop, 9);
            Assert.Equal(30, constraint.BlockOffset, 9);
        }

        [Fact]
        public void For_RemainingBlockSize_IsBandHeightMinusTheBoxsOwnOffset()
        {
            var container = CreateContainer();
            var box = CreateBox(container, MarginTop + 30);

            var constraint = BlockConstraint.For(box);

            Assert.Equal(BandHeight - 30, constraint.RemainingBlockSize, 9);
        }

        [Fact]
        public void For_ReturnsMeasurement_WhenThereIsNoRealPageGrid()
        {
            // The unpaginated sentinel HtmlContainerInt.HasRealPageGrid itself checks for.
            var container = CreateContainer(double.MaxValue);
            var box = CreateBox(container, 500);

            var constraint = BlockConstraint.For(box);

            Assert.Null(constraint.Fragmentainer);
            Assert.Equal(BlockConstraint.Measurement, constraint);
        }

        [Fact]
        public void Measurement_NeverStraddles()
        {
            // A measurement pass may not ask a fragmentation question at all (css-break-3, #400(c)) -
            // Straddles has to refuse rather than answer against an arbitrary sentinel band.
            Assert.False(BlockConstraint.Measurement.Straddles(double.MaxValue / 2));
            Assert.Equal(double.MaxValue, BlockConstraint.Measurement.NextBandHeight);
            Assert.Equal(double.MaxValue, BlockConstraint.Measurement.AbsoluteBandBottom);
        }

        [Theory]
        [InlineData(BandHeight - 30 - 0.01, false)] // fits with room to spare
        [InlineData(BandHeight - 30, false)] // exact fit is not a straddle
        [InlineData(BandHeight - 30 + 0.01, true)] // crosses out of the band
        public void Straddles_ComparesAgainstWhatRemainsBelowTheBoxsOwnOffset(double extent, bool expected)
        {
            var container = CreateContainer();
            var box = CreateBox(container, MarginTop + 30);
            var constraint = BlockConstraint.For(box);

            Assert.Equal(expected, constraint.Straddles(extent));
        }

        [Fact]
        public void AtSlot_ConstructsTheGivenSlotAtTheGivenOffset()
        {
            var container = CreateContainer();
            var box = new CssBox(null, null) { HtmlContainer = container };

            var constraint = BlockConstraint.AtSlot(container, box, slot: 4, blockOffset: 12);

            Assert.Equal(container.PageTopOf(4), constraint.Fragmentainer!.BandTop, 9);
            Assert.Equal(container.PageBandHeightOf(4), constraint.NextBandHeight, 9);
            Assert.Equal(12, constraint.BlockOffset, 9);
        }

        [Fact]
        public void AtSlot_DefaultsToZeroOffset()
        {
            var container = CreateContainer();
            var box = new CssBox(null, null) { HtmlContainer = container };

            var constraint = BlockConstraint.AtSlot(container, box, slot: 1);

            Assert.Equal(0, constraint.BlockOffset);
        }

        [Fact]
        public void AtNextSlot_MovesOneFragmentainerOnAtItsOwnContentTop()
        {
            var container = CreateContainer();
            var box = CreateBox(container, MarginTop + 2 * BandHeight + 30);
            var constraint = BlockConstraint.For(box);

            var next = constraint.AtNextSlot();

            Assert.Equal(0, next.BlockOffset);
            Assert.Equal(container.PageTopOf(3), next.Fragmentainer!.BandTop, 9);
            Assert.Equal(container.PageBandHeightOf(3), next.NextBandHeight, 9);
        }

        [Fact]
        public void AtNextSlot_OnMeasurement_StaysAMeasurement()
        {
            var next = BlockConstraint.Measurement.AtNextSlot();

            Assert.Equal(BlockConstraint.Measurement, next);
        }

        [Fact]
        public void AbsoluteBandBottom_IsWhereTheNextFragmentainerBegins()
        {
            var container = CreateContainer();
            var box = CreateBox(container, MarginTop + 30);
            var constraint = BlockConstraint.For(box);

            Assert.Equal(container.PageTopOf(1), constraint.AbsoluteBandBottom, 9);
            Assert.Equal(container.PageBottomOf(0), constraint.AbsoluteBandBottom, 9);
        }
    }
}

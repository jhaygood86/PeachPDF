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

        private static HtmlContainerInt CreateContainer(double bandHeight = BandHeight)
        {
            var container = new HtmlContainerInt(new PdfSharpAdapter())
            {
                PageSize = new RSize(500, bandHeight),
                MarginTop = MarginTop,
            };

            // BlockConstraint.For/EndingAt answer "no fragmentainer" whenever breaking isn't live
            // (HtmlContainerInt.CurrentFragmentainer is null), the same detached state a flex/grid
            // item's own measurement pass runs in - not just when there is no real page grid at all.
            // These tests are about the "real, live pass" arithmetic, so they need a live fragmentainer
            // in place, exactly as HtmlContainerInt.LayoutDocument's own per-slot loop sets one up.
            var root = new CssBox(null, null) { HtmlContainer = container };
            container.EnterNestedFragmentainer(new FragmentainerContext(container, root, 0));

            return container;
        }

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

        // A flex/grid item's own measurement pass (CssLayoutEngineFlex.MeasureItem's
        // PerformLayoutBlockified) keeps a real page grid but detaches the fragmentainer
        // (HtmlContainerInt.DetachFragmentainer) so content laid out at its provisional position cannot
        // answer a fragmentation question. Before this guard, a nested box whose throwaway coordinate
        // happened to straddle a page boundary during that measurement had the §4.3 widows/orphans or
        // avoid/monolithic mover translate it to the next page anyway — corrupting the very size the
        // engine was in the middle of computing (a nested flex-in-flex footer's own measured height
        // could come out roughly double, observed in the "invoice" showcase). HasRealPageGrid alone
        // does not tell the two states apart, since it stays true throughout a detached pass.
        [Fact]
        public void For_ReturnsMeasurement_WhenTheFragmentainerIsDetached()
        {
            var container = CreateContainer();
            var box = CreateBox(container, MarginTop + 30);
            var live = container.DetachFragmentainer();

            var constraint = BlockConstraint.For(box);

            container.RestoreFragmentainer(live);

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

        [Fact]
        public void AbsoluteBandTop_IsWhereThisFragmentainerBegins()
        {
            var container = CreateContainer();
            var box = new CssBox(null, null) { HtmlContainer = container };

            Assert.Equal(container.PageTopOf(3), BlockConstraint.AtSlot(container, box, 3).AbsoluteBandTop, 9);
            // A measurement pass has no band to begin at, and must not answer with a sentinel that reads
            // as a coordinate content could be placed at.
            Assert.Equal(0, BlockConstraint.Measurement.AbsoluteBandTop);
        }

        // EndingAt carries HtmlContainerInt.SlotEndingAt's bottom-edge convention: an edge flush on a
        // boundary belongs to the band above it, not the one it merely touches. For's top-edge convention
        // answers the other way for the very same coordinate, which is why they are separate factories.
        [Theory]
        [InlineData(MarginTop + BandHeight, 0)] // flush on slot 1's top: still slot 0's bottom edge
        [InlineData(MarginTop + BandHeight + 0.4, 0)] // inside the tolerance, still slot 0
        [InlineData(MarginTop + BandHeight + 1, 1)] // genuinely into slot 1
        [InlineData(MarginTop + BandHeight - 1, 0)]
        public void EndingAt_PlacesTheBandABottomEdgeEndsIn(double blockEnd, int expectedSlot)
        {
            var container = CreateContainer();
            var box = new CssBox(null, null) { HtmlContainer = container };

            var constraint = BlockConstraint.EndingAt(container, box, blockEnd);

            Assert.Equal(expectedSlot, constraint.Fragmentainer!.SlotIndex);
            Assert.Equal(container.PageTopOf(expectedSlot), constraint.AbsoluteBandTop, 9);
            Assert.Equal(blockEnd - container.PageTopOf(expectedSlot), constraint.BlockOffset, 9);
        }

        [Fact]
        public void EndingAt_ReturnsMeasurement_WhenThereIsNoRealPageGrid()
        {
            var container = CreateContainer(double.MaxValue);
            var box = new CssBox(null, null) { HtmlContainer = container };

            Assert.Equal(BlockConstraint.Measurement, BlockConstraint.EndingAt(container, box, 500));
        }

        // Same detached-pass guard as For's own test above - the §5.2 margin-truncation mover
        // (CssBox.ResolveBlockChildOffset) that calls EndingAt must not truncate a margin, or move a
        // child to a page boundary, against a position a flex/grid item's own measurement pass will
        // discard anyway.
        [Fact]
        public void EndingAt_ReturnsMeasurement_WhenTheFragmentainerIsDetached()
        {
            var container = CreateContainer();
            var box = new CssBox(null, null) { HtmlContainer = container };
            var live = container.DetachFragmentainer();

            var constraint = BlockConstraint.EndingAt(container, box, MarginTop + BandHeight + 1);

            container.RestoreFragmentainer(live);

            Assert.Equal(BlockConstraint.Measurement, constraint);
        }

        // FallsPast is the same bottom-edge convention asked of an absolute coordinate, tolerance included -
        // deliberately NOT Straddles, whose bare `>` would call a box landing exactly on the boundary a
        // crossing and truncate a margin that never spanned anything.
        [Theory]
        [InlineData(MarginTop + BandHeight - 1, false)]
        [InlineData(MarginTop + BandHeight, false)] // flush on the boundary has not crossed it
        [InlineData(MarginTop + BandHeight + 0.4, false)] // inside the tolerance
        [InlineData(MarginTop + BandHeight + 1, true)]
        public void FallsPast_UsesTheBoundaryToleranceThatStraddlesDoesNot(double blockEnd, bool expected)
        {
            var container = CreateContainer();
            var box = new CssBox(null, null) { HtmlContainer = container };
            var constraint = BlockConstraint.AtSlot(container, box, 0);

            Assert.Equal(expected, constraint.FallsPast(blockEnd));

            // The same coordinate, asked as an extent from the band's own top: Straddles calls the flush
            // case a crossing. The two answers differ exactly where the tolerance is, which is the whole
            // reason both members exist.
            Assert.Equal(blockEnd > container.PageBottomOf(0),
                constraint.Straddles(blockEnd - container.PageTopOf(0)));
        }

        [Fact]
        public void FallsPast_IsFalseDuringAMeasurementPass()
        {
            // No band means nothing to cross out of - the same refusal Straddles makes.
            Assert.False(BlockConstraint.Measurement.FallsPast(double.MaxValue / 2));
        }
    }
}

using PeachBrackets.Html.Fragmentation;

namespace PeachBrackets.Engine.Tests.Html.Core.Fragmentation
{
    /// <summary>
    /// The run frame a row sliced across bands is laid out in: its bands are the strips the slicer will
    /// cut, so a table nested in the row breaks where the strips end.
    /// </summary>
    public class RunBandGridTests
    {
        /// <summary>Pages of <c>height</c> starting at <c>origin</c>, back to back.</summary>
        private sealed class UniformGrid(double origin, double height) : IPageGrid
        {
            public double PageTopOf(int slot) => origin + slot * height;

            public double PageBottomOf(int slot) => PageTopOf(slot + 1);

            public double PageBandHeightOf(int slot) => height;

            public int SlotStartingAt(double y) => (int)Math.Floor((y + 0.5 - origin) / height);

            public int SlotEndingAt(double y) => (int)Math.Floor((y - 0.5 - origin) / height);

            public PageBand BandOfSlot(int slot) => new(PageTopOf(slot), PageBottomOf(slot));
        }

        [Fact]
        public void Strips_GiveEveryBandAfterTheFirstTheHeaderRoomAndShiftByTheGapsSoFar()
        {
            var strips = RunBandGrid.Strips(new UniformGrid(20, 60), 31.25, headerRoom: 11.25, footerHeight: 0).Take(3).ToList();

            Assert.Equal(new RunStrip(0, 31.25, 48.75, 0), strips[0]);
            Assert.Equal(new RunStrip(1, 91.25, 48.75, 11.25), strips[1]);
            Assert.Equal(new RunStrip(2, 151.25, 48.75, 22.5), strips[2]);
        }

        [Fact]
        public void Strips_TakeTheFooterOffTheBottomOfEveryBand()
        {
            var strips = RunBandGrid.Strips(new UniformGrid(0, 100), 10, headerRoom: 0, footerHeight: 20).Take(2).ToList();

            Assert.Equal(new RunStrip(0, 10, 70, 0), strips[0]);
            Assert.Equal(new RunStrip(1, 100, 80, 20), strips[1]);
        }

        [Fact]
        public void Strips_GiveABandTheFooterWouldEmptyItsWholeHeight()
        {
            var strips = RunBandGrid.Strips(new UniformGrid(0, 100), 90, headerRoom: 0, footerHeight: 20).Take(1).ToList();

            Assert.Equal(10, strips[0].Room);
        }

        [Fact]
        public void Bands_AreTheStripsInTheRunFrame()
        {
            var grid = new RunBandGrid(new UniformGrid(20, 60), 31.25, headerRoom: 11.25, footerHeight: 0);

            // Real band 1 is 80..140 and shows run positions 80..128.75 below the 11.25 header.
            Assert.Equal(80, grid.PageBottomOf(0));
            Assert.Equal(80, grid.PageTopOf(1));
            Assert.Equal(128.75, grid.PageBottomOf(1));
            Assert.Equal(48.75, grid.PageBandHeightOf(1));
            Assert.Equal(177.5, grid.PageBottomOf(2));
        }

        [Fact]
        public void TheFirstBand_OpensAtTheRealPageTopNotTheRow()
        {
            var grid = new RunBandGrid(new UniformGrid(20, 60), 31.25, headerRoom: 11.25, footerHeight: 0);

            Assert.Equal(20, grid.PageTopOf(0));
        }

        [Fact]
        public void SlotLookups_FindTheStripACoordinateFallsIn()
        {
            var grid = new RunBandGrid(new UniformGrid(20, 60), 31.25, headerRoom: 11.25, footerHeight: 0);

            Assert.Equal(0, grid.SlotStartingAt(79));
            Assert.Equal(1, grid.SlotStartingAt(80));
            Assert.Equal(1, grid.SlotEndingAt(128.75));
            Assert.Equal(2, grid.SlotStartingAt(128.75));
            Assert.Equal(2, grid.SlotStartingAt(150));
        }

        [Fact]
        public void BandOfSlot_PairsTheTopAndBottom()
        {
            var grid = new RunBandGrid(new UniformGrid(20, 60), 31.25, headerRoom: 11.25, footerHeight: 0);

            Assert.Equal(new PageBand(80, 128.75), grid.BandOfSlot(1));
        }

        [Fact]
        public void ABandBeforeTheRun_IsAnsweredByTheParentGrid()
        {
            var grid = new RunBandGrid(new UniformGrid(20, 60), 91.25, headerRoom: 11.25, footerHeight: 0);

            Assert.Equal(80, grid.PageBottomOf(0));
            Assert.Equal(0, grid.SlotStartingAt(30));
            Assert.Equal(1, grid.SlotStartingAt(91.25));
        }

        [Fact]
        public void ARunBeginningLaterInThePage_StartsItsFirstStripAtTheRowTop()
        {
            var grid = new RunBandGrid(new UniformGrid(20, 60), 60, headerRoom: 10, footerHeight: 0);

            Assert.Equal(80, grid.PageBottomOf(0));
            Assert.Equal(130, grid.PageBottomOf(1));
        }
    }
}

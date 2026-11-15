using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>Item sizing against the grid area and through subgrids (css-grid-1 section 6.6, css-grid-2 section 9.5).</summary>
    public class GridSubgridSizingTests
    {
        private static async Task<CssBox> LayOut(string body, string id)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0}</style></head><body>" + body + "</body></html>";
            var rc = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var c = new HtmlContainerInt(rc);
            await c.SetHtml(html, null);
            var size = new Size(595, 842);
            c.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            c.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var g = PdfCanvas.CreateMeasuring(rc, size);
            await c.PerformLayout(g);

            static CssBox? Find(CssBox b, string want)
            {
                if (b.HtmlTag?.TryGetAttribute("id", "") == want) return b;
                foreach (var x in b.Boxes) if (Find(x, want) is { } f) return f;
                return null;
            }

            return Find(c.Root!, id)!;
        }

        [Fact]
        public async Task APercentageHeightItem_IsHeldToTheRowsFixedGrowthLimit()
        {
            var item = await LayOut("<div style='display:grid;grid-template-rows:minmax(auto,100pt);grid-template-columns:100pt'>" +
                                    "<div id='i' style='height:100%'><div style='height:200pt'></div></div></div>", "i");

            Assert.Equal(100, item.ActualBottom - item.Location.Y, 0.5);
        }

        [Fact]
        public async Task AStretchedItemSpanningTwoRows_CoversTheGapBetweenThem()
        {
            var item = await LayOut("<div style='display:grid;gap:20pt;grid-template-rows:repeat(3,auto);grid-template-columns:50pt 50pt'>" +
                                    "<div style='height:64pt'></div><div id='i' style='grid-row:2/4'></div>" +
                                    "<div style='grid-row:2/4;grid-column:2;height:128pt'></div></div>", "i");

            Assert.Equal(128, item.ActualBottom - item.Location.Y, 0.5);
        }

        [Fact]
        public async Task AMinmaxRowWithALengthMinimum_StartsAtThatMinimum()
        {
            var grid = await LayOut("<div id='g' style='display:grid;gap:10pt;grid-template-rows:repeat(2,minmax(100pt,auto))'>" +
                                    "<div style='height:20pt'></div><div style='height:20pt'></div></div>", "g");

            Assert.Equal(210, grid.ActualBottom - grid.Location.Y, 0.5);
        }

        [Fact]
        public async Task ASubgridOfASubgrid_TakesItsMarginFromTheGapBetweenTheRealLines()
        {
            // Both subgrids ask for a 100pt gap over the grid's 20pt: the inner one's item gives up 40pt below it, not 0.
            var item = await LayOut("<div style='display:grid;grid-template-rows:100pt auto;gap:20pt;grid-template-columns:100pt'>" +
                                    "<div style='display:grid;grid:subgrid/subgrid;gap:100pt;grid-row:span 2'>" +
                                    "<div style='display:grid;grid:subgrid/subgrid;gap:100pt;grid-row:span 2'><div id='i'></div><div></div></div></div></div>", "i");

            Assert.Equal(60, item.ActualBottom - item.Location.Y, 0.5);
        }

        [Theory]
        [InlineData("grid", "min-content")]
        [InlineData("inline-grid", "min-content")]
        [InlineData("inline-grid;grid-template-rows:auto auto", "max-content")]
        public async Task AMinContentHeightItem_IsNotStretchedToItsArea(string display, string height)
        {
            var item = await LayOut($"<div style='display:{display};grid-template-columns:50pt 50pt;font:10pt/1 monospace'>" +
                                    $"<div id='i' style='grid-row:1/3;height:{height}'>x</div>" +
                                    "<div style='grid-row:1/3;grid-column:2;height:100pt'></div></div>", "i");

            Assert.Equal(10, item.ActualBottom - item.Location.Y, 0.5);
        }

        [Fact]
        public async Task ItemsLockedToARow_PlacePastTheOnesAlreadyThereAndGrowTheImplicitGrid()
        {
            var b = await LayOut("<div style='display:grid;width:300pt;grid-template-rows:repeat(3,20pt)'>" +
                                 "<div style='grid-row:2;width:50pt'></div><div id='i' style='grid-row:2/4;width:50pt'></div></div>", "i");

            Assert.True(b.Location.X >= 50, $"x={b.Location.X}");
        }

        [Fact]
        public async Task ALastLineLockedItem_SharesTheOnlyColumn()
        {
            var b = await LayOut("<div style='display:grid;width:200pt;grid-template-rows:repeat(8,50pt)'>" +
                                 "<div style='grid-row:1/span 3;width:100pt'></div><div id='i' style='grid-row:-1;width:100pt'></div></div>", "i");

            Assert.True(b.Location.X < 50, $"x={b.Location.X}");
        }

        [Fact]
        public async Task ARowSubgridInARowLane_IsAsWideAsItsRatioChildNeedsInTheFixedLane()
        {
            var item = await LayOut("<div style='display:grid-lanes;grid-template-rows:50pt 100pt;fit-tolerance:0'>" +
                                    "<div style='grid-row:1;width:100pt;height:50pt'></div>" +
                                    "<div id='i' style='display:grid;grid:subgrid/auto;grid-row:auto/span 1'><div style='aspect-ratio:1;height:100%'></div></div></div>", "i");

            Assert.Equal(100, item.ActualRight - item.Location.X, 0.5);
        }

        [Fact]
        public async Task APercentageHeight_ResolvesAgainstTheGridArea()
        {
            var item = await LayOut("<div style='display:grid;grid-template-rows:80pt;grid-template-columns:100pt'><div id='i' style='height:50%'></div></div>", "i");

            Assert.Equal(40, item.ActualBottom - item.Location.Y, 0.5);
        }

        [Fact]
        public async Task AMinContentGrid_IsAsWideAsTheItemsOfItsColumnSubgrid()
        {
            var grid = await LayOut("<div id='g' style='display:grid;width:min-content;font:10pt/1 monospace'>" +
                                    "<div style='display:grid;grid-template-columns:subgrid'><div style='width:70pt'>a</div></div></div>", "g");

            Assert.Equal(70, grid.ActualRight - grid.Location.X, 0.5);
        }

        [Fact]
        public async Task ARowSubgridItem_TransfersItsPercentageHeightThroughItsRatioIntoTheColumn()
        {
            var grid = await LayOut("<div id='g' style='display:inline-grid;grid-template-rows:100pt;grid-template-columns:auto'>" +
                                    "<div style='display:grid;grid-template-rows:subgrid'><div style='aspect-ratio:1;height:100%'></div></div></div>", "g");

            Assert.Equal(100, grid.ActualRight - grid.Location.X, 0.5);
        }
    }
}

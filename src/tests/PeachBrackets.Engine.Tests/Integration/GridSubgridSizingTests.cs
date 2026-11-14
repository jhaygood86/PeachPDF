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

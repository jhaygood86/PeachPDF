using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>The static position of an absolutely positioned child of a grid container (css-grid-1 section 9, css-align-3 appendix A).</summary>
    public class GridAbsposStaticPositionTests
    {
        private static async Task<CssBox> LayOut(string body, string id)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0}" +
                ".g{display:grid;width:100pt;height:200pt;padding:10pt 20pt;border:5pt solid}" +
                ".a{position:absolute;width:20pt;height:40pt}</style></head><body>" + body + "</body></html>";
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
        public async Task AutoLines_AlignInTheContentBox_NotThePaddingBox_WhenTheGridIsNotTheContainingBlock()
        {
            var box = await LayOut("<div class='g'><div class='a' id='a' style='align-self:center;justify-self:end'></div></div>", "a");

            // content box: x 25..125 (border 5 + padding 20), y 15..215.
            Assert.Equal(25 + 100 - 20, box.Location.X, 0.5);
            Assert.Equal(15 + (200 - 40) / 2.0, box.Location.Y, 0.5);
        }

        [Fact]
        public async Task NormalAndStretch_AreStart_NotTheContainersItems()
        {
            var box = await LayOut("<div class='g' style='align-items:center'><div class='a' id='a' style='align-self:normal'></div></div>", "a");

            Assert.Equal(15, box.Location.Y, 0.5);
        }

        [Fact]
        public async Task ANamedArea_IsWhereAFloatedUnpositionedGridAlignsItsChild()
        {
            var html = "<div class='g2' id='g'><div id='a' style='position:absolute;grid-area:2/2/3/3;height:6px;width:8px;align-self:center'></div></div>";
            var box = await LayOut(html.Replace("<div class='g2'", "<div style='display:grid;float:left;padding:1px 2px;border:1px solid black;background:yellow;grid:2px 20px 2px / 3px 14px 3px;height:30px;width:22px;align-items:center'"), "a");
            var grid = box.ParentBox!;

            // Row two starts 2pt below the content top and is 20pt tall; the 6pt box is centered in it.
            Assert.Equal(grid.ClientTop + 1.5 + (15 - 4.5) / 2.0, box.Location.Y, 0.5);
            Assert.Equal(grid.ClientLeft + 2.25, box.Location.X, 0.5);
        }

        [Fact]
        public async Task APercentageRowGap_OfAnAutoHeightGrid_IsOfTheHeightTheRowsGive()
        {
            var body = "<div style='display:grid;width:200px;gap:10%;grid-template-columns:90px 90px;grid-template-rows:90px 90px' id='g'><div id='a'></div><div></div><div id='c'></div><div></div></div>";
            var grid = await LayOut(body, "g");
            var c = await LayOut(body, "c");

            // 10% of the 180px the rows make, not of the 200px width; the gap overflows rather than adding to the height.
            Assert.Equal(135, grid.ActualBoxSizingHeight, 0.5);
            Assert.Equal(grid.ClientTop + 67.5 + 13.5, c.Location.Y, 0.5);
        }

        [Fact]
        public async Task AFloatedGridShrinkWrapsItsItemsOnceCountingTheirPaddingAndBorder()
        {
            const string item = "<div style='border:2px solid;padding:6px 3px 7px 8px;margin:10px 6px 4px 12px'>Hello</div>";
            var grid = await LayOut("<div id='g' style='float:left;display:grid;grid-auto-flow:column'>" + item + item + "</div>", "g");
            var block = await LayOut("<div id='b' style='float:left'>" + item + "</div>", "b");

            // Two items side by side are twice what one float holds (its own width plus margins).
            Assert.Equal(block.ActualBoxSizingWidth * 2, grid.ActualBoxSizingWidth, 1.0);
        }

        [Fact]
        public async Task Rtl_StartIsTheRightEdge_AndLeftRightStayPhysical()
        {
            var start = await LayOut("<div class='g' style='direction:rtl'><div class='a' id='a'></div></div>", "a");
            var left = await LayOut("<div class='g' style='direction:rtl'><div class='a' id='a' style='justify-self:left'></div></div>", "a");

            Assert.Equal(25 + 100 - 20, start.Location.X, 0.5);
            Assert.Equal(25, left.Location.X, 0.5);
        }

        [Fact]
        public async Task FlexAndSafeKeywords_AreAcceptedOnJustifySelf()
        {
            var box = await LayOut("<div class='g'><div class='a' id='a' style='justify-self:flex-end'></div></div>", "a");

            Assert.Equal("flex-end", box.JustifySelf.ToString());
            Assert.Equal(25 + 100 - 20, box.Location.X, 0.5);
        }
    }
}

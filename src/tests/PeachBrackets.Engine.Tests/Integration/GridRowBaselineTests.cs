using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>Row-axis baseline alignment of grid items (css-align-3 section 9): first and last baseline groups, margins, orthogonal items.</summary>
    public class GridRowBaselineTests
    {
        private static async Task<CssBox> LayOut(string gridStyle, string items)
        {
            var html = "<!DOCTYPE html><html><head><style>body{margin:0}.g{display:grid;grid-auto-flow:column;width:400pt}" +
                       ".i{margin:3pt 0 5pt;padding:2pt 0 4pt;line-height:1}</style></head><body><div class='g' id='g' style='" + gridStyle + "'>" + items + "</div></body></html>";
            var rc = new HtmlRenderContext { PixelsPerPoint = 1.0 };
            var c = new HtmlContainerInt(rc);
            await c.SetHtml(html, null);
            var size = new Size(595, 842);
            c.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            c.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var g = PdfCanvas.CreateMeasuring(rc, size);
            await c.PerformLayout(g);

            static CssBox? Find(CssBox b)
            {
                if (b.HtmlTag?.TryGetAttribute("id", "") == "g") return b;
                foreach (var x in b.Boxes) if (Find(x) is { } f) return f;
                return null;
            }

            return Find(c.Root!)!;
        }

        private static double Baseline(CssBox item) => item.Location.Y + BaselineAlignment.GetItemBaselineOffset(item)!.Value;

        [Fact]
        public async Task FirstBaseline_SharesTheTextBaseline_WhateverTheMargins()
        {
            var grid = await LayOut("align-items:first baseline", "<div class='i' style='font-size:10pt'>a</div><div class='i' style='font-size:30pt;margin-top:9pt'>b</div>");
            var items = grid.Boxes.Where(b => b.HtmlTag is not null).ToList();

            Assert.Equal(Baseline(items[0]), Baseline(items[1]), 0.5);
        }

        [Fact]
        public async Task LastBaseline_EndAlignsTheGroupOnTheLastLine()
        {
            var grid = await LayOut("align-items:last baseline;height:100pt", "<div class='i' style='font-size:10pt'>a</div><div class='i' style='font-size:30pt'>b</div>");
            var items = grid.Boxes.Where(b => b.HtmlTag is not null).ToList();

            Assert.Equal(Baseline(items[0]), Baseline(items[1]), 0.5);
            // the bigger item ends at the row's end, the smaller one is not pushed to its own end
            Assert.True(items[0].ActualBottom <= items[1].ActualBottom + 0.5);
        }

        [Fact]
        public async Task AnOrthogonalItem_RestsItsBottomBorderEdgeOnTheSharedBaseline()
        {
            var grid = await LayOut("align-items:baseline", "<div class='i' style='font-size:20pt'>a</div><div class='i' style='writing-mode:vertical-lr;font-size:20pt;margin-bottom:0'>b</div>");
            var items = grid.Boxes.Where(b => b.HtmlTag is not null).ToList();

            Assert.Equal(Baseline(items[0]), items[1].ActualBottom, 0.5);
        }

        [Fact]
        public async Task AnOrthogonalItem_IsAsWideAsItsLinesStackedAcrossItsAreasHeight()
        {
            var grid = await LayOut("grid-auto-flow:row;grid-auto-rows:50pt;justify-content:start;height:100pt",
                "<div style='writing-mode:vertical-lr;font:10pt/1 monospace;justify-self:start'>aaaa<br>bbbb<br>cccc</div>");
            var item = grid.Boxes.First(b => b.HtmlTag is not null);

            // Three lines of 10pt stack across, whatever the height.
            Assert.Equal(30, item.ActualBoxSizingWidth, 1.5);
        }

        [Fact]
        public async Task ColumnBaseline_ShiftsEachItemByTheDifferenceOfItsCentralBaselines()
        {
            var grid = await LayOut("grid-auto-flow:row;grid-auto-rows:50pt;justify-items:baseline;justify-content:start;height:100pt",
                "<div style='writing-mode:vertical-lr;font:10pt/1 monospace'>a</div><div style='writing-mode:vertical-lr;font:20pt/1 monospace'>b</div>");
            var items = grid.Boxes.Where(b => b.HtmlTag is not null).ToList();

            // central baselines: half a line (5pt and 10pt) from the left edge, so the first item sits 5pt in
            Assert.Equal(items[1].Location.X + 10, items[0].Location.X + 5, 0.75);
        }
    }
}

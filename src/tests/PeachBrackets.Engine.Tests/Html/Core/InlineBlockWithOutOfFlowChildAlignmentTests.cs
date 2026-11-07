using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// webkit.org's navigation: right-aligned <c>display: inline-block</c> items, some holding an absolutely positioned sub-menu. The absolute child
    /// makes the item lay out as an atomic box with line boxes of its own, and those lines were aligned against the container's width (an auto-width
    /// inline-block was taken to fill it) and then shifted again with the outer line, which put the text far to the right on top of its neighbours.
    /// </summary>
    public class InlineBlockWithOutOfFlowChildAlignmentTests
    {
        private static async Task<CssBox> LayOutAsync(string body)
        {
            var html = $"<!DOCTYPE html><html><body style='margin:0;font:14px Arial'>{body}</body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);
            var size = new Size(900, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);
            return container.Root!;
        }

        private static IEnumerable<CssRect> Words(CssBox box)
        {
            foreach (var w in box.Words) yield return w;
            foreach (var c in box.Boxes)
            {
                foreach (var w in Words(c)) yield return w;
            }
        }

        [Theory]
        [InlineData("<i style='position:absolute;top:0;left:0'></i>")]
        [InlineData("")]
        public async Task RightAlignedInlineBlocks_DoNotOverlap_WhateverOutOfFlowContentTheyHold(string extra)
        {
            var root = await LayOutAsync(
                "<div style='width:600px;text-align:right'><span style='display:inline-block;padding-left:15px'>Downloads</span> " +
                $"<span style='display:inline-block;padding-left:15px'>Status{extra}</span> <span style='display:inline-block'>Docs</span></div>");

            var words = Words(root).Where(w => w.Text is "Downloads" or "Status" or "Docs").OrderBy(w => w.Left).ToList();

            Assert.Equal(["Downloads", "Status", "Docs"], words.Select(w => w.Text));
            Assert.True(words[0].Right <= words[1].Left, "Downloads overlaps Status");
            Assert.True(words[1].Right <= words[2].Left, "Status overlaps Docs");
            Assert.Equal(600 * 0.75, words[2].Right, 0.5);
        }
    }
}

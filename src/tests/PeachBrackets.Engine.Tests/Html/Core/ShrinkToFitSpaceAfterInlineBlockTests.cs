using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// The markup's space between an inline-block and what follows it (<c>&lt;i class="icon"&gt;&lt;/i&gt; &lt;span&gt;Software&lt;/span&gt;</c>) is one space on the
    /// line, which layout advances over; the max-content measure that sizes a shrink-to-fit box left it out, so the box was one space narrower than its own
    /// content and the last word hung out past its edge (CSS Text 3 section 4.1.1, CSS Sizing 3 section 5.2). slashdot.org's navigation items were narrower
    /// than their text, which let the whole bar wrap. Chromium sizes the float below 16 + 3.9 + 55.2 = 75.1px at 14px Arial.
    /// </summary>
    public class ShrinkToFitSpaceAfterInlineBlockTests
    {
        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id") == id) return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, id) is { } found) return found;
            }

            return null;
        }

        private static CssRect? LastWord(CssBox box)
        {
            CssRect? last = null;
            foreach (var word in box.Words)
            {
                if (!word.IsSpaces && !word.IsLineBreak) last = word;
            }

            foreach (var child in box.Boxes)
            {
                if (LastWord(child) is { } found) last = found;
            }

            return last;
        }

        private static async Task<CssBox> LayOutAsync(string body)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0; font: 14px Arial, sans-serif }} .sp {{ display: inline-block; width: 16px; height: 12px }}</style></head><body>{body}</body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(600, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);
            return container.Root!;
        }

        [Theory]
        [InlineData("<div id='b' style='float:left'><i class='sp'></i> <span>Software</span></div>")]
        [InlineData("<div id='b' style='float:left'><a href='#' style='display:block;padding:0 15px'><i class='sp'></i> <span>Software</span></a></div>")]
        [InlineData("<div id='b' style='float:left'><i class='sp'></i>\n  <b>Software</b></div>")]
        public async Task AShrinkToFitBox_IsAsWideAsItsContent_SpaceAfterTheInlineBlockIncluded(string body)
        {
            var root = await LayOutAsync(body);
            var box = Find(root, "b")!;
            var last = LastWord(box)!;

            // The box reaches at least to the end of its last word (plus the right padding the second case has).
            Assert.True(box.ActualRight >= last.Right - 0.01,
                $"the box ends at {box.ActualRight / 0.75:0.#}px, short of its last word's end at {last.Right / 0.75:0.#}px");
        }

        [Fact]
        public async Task TheSpace_IsNotCounted_WhenTheInlineBlockEndsTheLine()
        {
            // Nothing follows the inline-block on its line, so there is no space to count: the box is as wide as the inline-block alone.
            var root = await LayOutAsync("<div id='b' style='float:left'><i class='sp'></i> </div>");

            Assert.Equal(12, Find(root, "b")!.ActualRight - Find(root, "b")!.Location.X, 0.5);
        }
    }
}

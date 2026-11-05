using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// An atomic inline (an inline-block, inline-flex or inline-grid box) is placed on its line by its margin box: the line is as tall as that box needs and the box sits
    /// below its own top margin (CSS 2.1 section 10.8.1, section 9.4.2).
    /// </summary>
    public class AtomicInlineMarginTests
    {
        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id") == id)
                return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, id) is { } found)
                    return found;
            }

            return null;
        }

        private static IEnumerable<CssBox> Descend(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                yield return child;
                foreach (var deeper in Descend(child))
                    yield return deeper;
            }
        }

        private static async Task<(double LineHeight, double AtomicOffset)> LayOutAsync(string display, string margin)
        {
            var html = $@"<!DOCTYPE html><html><head><style>body {{ margin: 0; font-size: 16px; line-height: 24px }}
#box {{ display: {display}; height: 24px; margin: {margin}; vertical-align: baseline }}</style></head>
<body><div id='line'><button id='box' type='button'>x</button></div></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(600, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var line = Find(container.Root!, "line")!;
            var box = Find(container.Root!, "box")!;
            const double k = 4.0 / 3.0;
            return ((line.ActualBottom - line.Location.Y) * k, (Descend(box).SelectMany(b => b.Words).Concat(box.Words).Min(w => w.Top) - line.Location.Y) * k);
        }

        [Theory]
        [InlineData("inline-flex")]
        [InlineData("inline-block")]
        [InlineData("inline-grid")]
        public async Task ATopMargin_IsPartOfTheLineAndOffsetsTheBox(string display)
        {
            var (height, offset) = await LayOutAsync(display, "20px 0 0");

            Assert.True(height >= 43.5, $"{display}: the line is {height:0.#}px tall, less than the box's 44px margin box");
            Assert.True(offset >= 19.5, $"{display}: its text starts {offset:0.#}px below the top of its line, not below the box's 20px margin");
        }

        [Theory]
        [InlineData("inline-flex")]
        [InlineData("inline-block")]
        public async Task ABottomMargin_IsPartOfTheLine(string display)
        {
            var (height, _) = await LayOutAsync(display, "0 0 20px");

            Assert.True(height >= 43.5, $"{display}: the line is {height:0.#}px tall, less than the box's 44px margin box");
        }
    }
}

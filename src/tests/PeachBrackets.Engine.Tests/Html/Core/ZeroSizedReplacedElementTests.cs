using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// <c>width: 0; height: 0</c> on a replaced element is a size of nothing, not an absent size. webkit.org hides the SVG holding its filter definitions this way
    /// (<c>svg { display: block; width: 0; height: 0 }</c>); read as "no size", the svg took its default 300x150 and pushed the whole page down 150px.
    /// </summary>
    public class ZeroSizedReplacedElementTests
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

        private static async Task<CssBox> LayOutAsync(string html)
        {
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

        [Theory]
        [InlineData("svg { display: block; width: 0; height: 0 }")]
        [InlineData("svg { display: block; width: 0px; height: 0px }")]
        public async Task ABlockSvgSizedZero_TakesNoRoom(string rule)
        {
            var root = await LayOutAsync($"<!DOCTYPE html><html><head><style>body {{ margin: 0 }} {rule}</style></head><body><svg xmlns='http://www.w3.org/2000/svg'><filter id='f'/></svg><div id='d' style='height:20px'></div></body></html>");

            Assert.Equal(0, Find(root, "d")!.Location.Y, 0.01);
        }

        [Fact]
        public async Task AnSvgWithNoSize_StillTakesItsDefault()
        {
            var root = await LayOutAsync("<!DOCTYPE html><html><head><style>body { margin: 0 } svg { display: block }</style></head><body><svg xmlns='http://www.w3.org/2000/svg'><filter id='f'/></svg><div id='d' style='height:20px'></div></body></html>");

            Assert.Equal(150 * 0.75, Find(root, "d")!.Location.Y, 0.01);
        }
    }
}

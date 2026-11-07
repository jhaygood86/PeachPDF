using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// <c>height: 100%</c> on an image resolves against a containing block whose height is definite (CSS 2.1 section 10.5), and an explicit height is not replaced
    /// by one derived from the width and the aspect ratio when the width is a percentage too (section 10.6.2). mad4mod.space's gallery photos are
    /// <c>width: 100%; height: 100%; object-fit: cover</c> in a figure the flex algorithm sized: they were drawn at their natural ratio, leaving the figure's
    /// background showing below them.
    /// </summary>
    public class PercentageHeightReplacedElementTests
    {
        // A 2x1 px PNG.
        private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEklEQVR4nGNkYGD4z8DAwMAAAAcAAQ0Ym6QAAAAASUVORK5CYII=";

        private static CssBox? Find(CssBox box, string id)
        {
            if (box.HtmlTag?.TryGetAttribute("id") == id) return box;
            foreach (var child in box.Boxes)
            {
                if (Find(child, id) is { } found) return found;
            }

            return null;
        }

        private static async Task<CssBox> LayOutAsync(string body)
        {
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml($"<!DOCTYPE html><html><body style='margin:0'>{body}</body></html>", null);
            var size = new Size(900, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);
            return container.Root!;
        }

        [Fact]
        public async Task AnImageWithPercentageSizes_FillsAFlexFigureTheContainerSized()
        {
            // The figure grows to the 200px left by the column container (180px less a 20px caption); the photo's own ratio would make it 100px tall.
            var root = await LayOutAsync(
                "<div style='display:flex;flex-direction:column;width:200px;height:220px'>" +
                "<div id='f' style='flex:1 1 auto;min-height:0;overflow:hidden'>" +
                $"<img id='i' src='{Png}' style='width:100%;height:100%'></div>" +
                "<div style='flex:none;height:20px'>caption</div></div>");

            var image = Find(root, "i")!;

            Assert.Equal(200 * 0.75, image.Words[0].Width, 0.5);
            Assert.Equal(200 * 0.75, image.Words[0].Height, 0.5);
        }

        [Fact]
        public async Task AnImageWithAPercentageWidthAndAPixelHeight_KeepsItsHeight()
        {
            var root = await LayOutAsync($"<div style='width:200px'><img id='i' src='{Png}' style='width:100%;height:50px'></div>");

            var image = Find(root, "i")!;

            Assert.Equal(200 * 0.75, image.Words[0].Width, 0.5);
            Assert.Equal(50 * 0.75, image.Words[0].Height, 0.5);
        }

        [Fact]
        public async Task AnImageWithAPercentageHeightInAnIndefiniteContainer_StaysAuto()
        {
            // The container has no height of its own, so the percentage computes to auto: the same size as with no height at all.
            var withPercentage = Find(await LayOutAsync($"<div style='width:200px'><img id='i' src='{Png}' style='width:100%;height:100%'></div>"), "i")!;
            var withNone = Find(await LayOutAsync($"<div style='width:200px'><img id='i' src='{Png}' style='width:100%'></div>"), "i")!;

            Assert.Equal(withNone.Words[0].Height, withPercentage.Words[0].Height, 0.5);
        }
    }
}

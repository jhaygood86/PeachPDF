using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// An image that cannot be loaded and has alternative text is rendered as that text (HTML "rendering", "images"): its box is the text's size unless the
    /// author gave the image a size, and an empty or missing <c>alt</c> leaves the box as before.
    /// </summary>
    public class BrokenImageAltTextTests
    {
        private static async Task<(double Width, double Height, string? Alt)> ImageAsync(string attributes)
        {
            var html = $"<!DOCTYPE html><html><body style='margin:0;font:12pt Arial'><p style='margin:0'><img id='i' src='no-such-file.png' {attributes}></p></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var image = (CssBoxImage)Find(container.Root!)!;
            var word = image.Words.First();
            return (word.Width, word.Height, image.BrokenImageAlt);
        }

        private static CssBox? Find(CssBox box)
        {
            if (box is CssBoxImage) return box;
            foreach (var child in box.Boxes)
            {
                if (Find(child) is { } found) return found;
            }

            return null;
        }

        [Fact]
        public async Task AMissingImageWithAlt_TakesTheTextsSize()
        {
            var without = await ImageAsync("");
            var with = await ImageAsync("alt='Company logo of the Acme Corporation'");

            Assert.Equal("Company logo of the Acme Corporation", with.Alt);
            Assert.Null(without.Alt);
            Assert.True(with.Width > 100, $"the box is {with.Width:0.#}pt wide, not as wide as its text");
            Assert.True(with.Width > without.Width * 3);
            Assert.InRange(with.Height, 10, 30);
        }

        [Fact]
        public async Task AnAuthorsSize_StillWinsOverTheTexts()
        {
            var sized = await ImageAsync("alt='Company logo of the Acme Corporation' width='100' height='50'");

            Assert.Equal(75, sized.Width, 0.5);
            Assert.Equal(37.5, sized.Height, 0.5);
        }

        [Theory]
        [InlineData("alt=''")]
        [InlineData("alt='   '")]
        public async Task AnEmptyAlt_LeavesTheBoxAsItWas(string attributes)
        {
            var without = await ImageAsync("");
            var empty = await ImageAsync(attributes);

            Assert.Null(empty.Alt);
            Assert.Equal(without.Width, empty.Width, 0.01);
            Assert.Equal(without.Height, empty.Height, 0.01);
        }
    }
}

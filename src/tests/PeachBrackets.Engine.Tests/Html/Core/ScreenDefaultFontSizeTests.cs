using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// A document laid out for a screen starts from the type size every browser does: <c>medium</c> is 16 px (12 pt) and the absolute-size keywords
    /// follow the browsers' scale (CSS Fonts 4, "font-size-prop"), so <c>1rem</c> and a page that sets nothing agree with them. A paged document keeps
    /// the paper default (11 pt), because a PDF is not styled by a browser's defaults.
    /// </summary>
    public class ScreenDefaultFontSizeTests
    {
        private static async Task<double> FontSizePtAsync(string css, MediaType media)
        {
            var html = $"<!DOCTYPE html><html><head><style>{css}</style></head><body><p id='p'>Hello</p></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = media };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            return Find(container.Root!, "p")!.ActualFont.Size;
        }

        private static CssBox? Find(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
                return box;

            foreach (var child in box.Boxes)
            {
                if (Find(child, tag) is { } found)
                    return found;
            }

            return null;
        }

        [Fact]
        public async Task OnScreen_NothingSet_IsSixteenPixels() =>
            Assert.Equal(12, await FontSizePtAsync("", MediaType.Screen), 0.01);

        [Fact]
        public async Task OnPaper_NothingSet_KeepsThePaperDefault() =>
            Assert.Equal(11, await FontSizePtAsync("", MediaType.Print), 0.01);

        [Theory]
        [InlineData("xx-small", 9)]
        [InlineData("x-small", 10)]
        [InlineData("small", 13)]
        [InlineData("medium", 16)]
        [InlineData("large", 18)]
        [InlineData("x-large", 24)]
        [InlineData("xx-large", 32)]
        public async Task OnScreen_AbsoluteSizeKeywords_FollowTheBrowserScale(string keyword, double px) =>
            Assert.Equal(px * 0.75, await FontSizePtAsync($"p {{ font-size: {keyword} }}", MediaType.Screen), 0.01);

        [Theory]
        [InlineData("xx-small", 7)]
        [InlineData("small", 9)]
        [InlineData("large", 13)]
        [InlineData("xx-large", 15)]
        public async Task OnPaper_AbsoluteSizeKeywords_KeepTheirSteps(string keyword, double pt) =>
            Assert.Equal(pt, await FontSizePtAsync($"p {{ font-size: {keyword} }}", MediaType.Print), 0.01);

        [Fact]
        public async Task OnScreen_RemIsSixteenPixels() =>
            Assert.Equal(24, await FontSizePtAsync("p { font-size: 2rem }", MediaType.Screen), 0.01);
    }
}

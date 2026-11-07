using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// An inline <c>&lt;svg&gt;</c> with a <c>viewBox</c> and no <c>width</c> or <c>height</c> has an aspect ratio and no size. Like a block-level replaced
    /// element in that case (CSS 2.1 section 10.3.2) it takes the width its containing block has available and its height follows the ratio; in a box that
    /// is sized to its content it has no width to give. The expected sizes are what Chromium reports for the same markup (the social icons of slashdot.org
    /// once drew 1792 px wide, in a 20px link, because the viewBox's own extent stood in for a size). 1px is 0.75pt.
    /// </summary>
    public class ViewBoxOnlySvgSizeTests
    {
        private const string Wide = "<svg viewBox='0 260 1792 1260'><path d='M0 260h1792v1260H0z'/></svg>";

        private static CssBox? Descendant(CssBox box, string tag)
        {
            foreach (var child in box.Boxes)
            {
                if (child.HtmlTag?.Name == tag) return child;
                if (Descendant(child, tag) is { } found) return found;
            }

            return null;
        }

        private static async Task<(double Width, double Height)> SvgSizeAsync(string body, string style = "")
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0 }} {style}</style></head><body>{body}</body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(600, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var word = Descendant(container.Root!, "svg")!.Words.First();
            return (word.Width, word.Height);
        }

        [Fact]
        public async Task InABlock_TakesTheBlocksWidth_AndTheRatioGivesTheHeight()
        {
            // Chromium: 200px -> 200 x 140.625.
            var (width, height) = await SvgSizeAsync($"<div style='width:200px'>{Wide}</div>");

            Assert.Equal(150, width, 0.01);
            Assert.Equal(105.46875, height, 0.01);
        }

        [Fact]
        public async Task InAnInlineWrapperInsideANarrowBlockLink_IsAsWideAsTheLink()
        {
            // The slashdot.org social links: <a style="display:block; width:1.25rem"><i><svg viewBox></svg></i></a>. Chromium: 20 x 14.0625.
            var (width, height) = await SvgSizeAsync($"<a href='#' style='display:block;width:20px'><i>{Wide}</i></a>");

            Assert.Equal(15, width, 0.01);
            Assert.Equal(10.546875, height, 0.01);
        }

        [Fact]
        public async Task WiderThanTheViewBox_IsStillAsWideAsTheBlock()
        {
            // The viewBox is no size: a 1000px block gives 1000 x 703.125 in Chromium, not the 1792 x 1260 of the viewBox's extent.
            var (width, height) = await SvgSizeAsync($"<div style='width:1000px'>{Wide}</div>");

            Assert.Equal(750, width, 0.01);
            Assert.Equal(527.34375, height, 0.01);
        }

        [Fact]
        public async Task AMaxWidth_LimitsTheWidth_AndTheHeightFollows()
        {
            // Chromium: max-width:50px on a 2:1 viewBox in a 400px block -> 50 x 25.
            var (width, height) = await SvgSizeAsync("<div style='width:400px'><svg style='max-width:50px' viewBox='0 0 100 50'></svg></div>");

            Assert.Equal(37.5, width, 0.01);
            Assert.Equal(18.75, height, 0.01);
        }

        [Fact]
        public async Task AHeight_GivesTheWidthByTheRatio()
        {
            // Chromium: height:50px on a 2:1 viewBox -> 100 x 50.
            var (width, height) = await SvgSizeAsync("<div style='width:400px'><svg style='height:50px' viewBox='0 0 100 50'></svg></div>");

            Assert.Equal(75, width, 0.01);
            Assert.Equal(37.5, height, 0.01);
        }

        [Theory]
        [InlineData("<div style='display:inline-block'>{0}</div>")]
        [InlineData("<div style='float:left'>{0}</div>")]
        [InlineData("<div style='position:absolute'>{0}</div>")]
        [InlineData("<div style='display:flex;width:400px'><div>{0}</div></div>")]
        public async Task InABoxSizedToItsContent_HasNoWidthToContribute(string wrapper)
        {
            // Chromium reports 0 x 0 for each of these.
            var (width, height) = await SvgSizeAsync(string.Format(wrapper, Wide));

            Assert.Equal(0, width, 0.01);
            Assert.Equal(0, height, 0.01);
        }

        [Fact]
        public async Task AWidthAttribute_GivesTheHeightByTheViewBoxRatio()
        {
            // Chromium: width="80" with a 2:1 viewBox -> 80 x 40 (the viewBox's own height of 50 is no size).
            var (width, height) = await SvgSizeAsync("<div style='width:400px'><svg width='80' viewBox='0 0 100 50'></svg></div>");

            Assert.Equal(60, width, 0.01);
            Assert.Equal(30, height, 0.01);
        }

        [Fact]
        public async Task NoViewBoxAndNoSize_IsTheDefaultObjectSize()
        {
            // Chromium: 300 x 150.
            var (width, height) = await SvgSizeAsync("<div style='width:400px'><svg></svg></div>");

            Assert.Equal(225, width, 0.01);
            Assert.Equal(112.5, height, 0.01);
        }
    }
}

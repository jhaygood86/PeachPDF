using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Html.Core
{
    /// <summary>
    /// A replaced element with an intrinsic ratio whose width and height are both <c>auto</c> but constrained by min/max values is sized by the
    /// constraint violation table of CSS 2.1 section 10.4, which resolves the two dimensions together: they never break the ratio unless a
    /// constraint forces them to, and one that scales a dimension never carries it past the other axis' limit. A Tailwind card image is exactly this
    /// (<c>img { max-width: 100%; height: auto }</c> with <c>min-w-full min-h-full</c>, absolutely positioned, a 16:9 picture in a 4:3 card).
    /// </summary>
    public class ReplacedElementConstraintTableTests
    {
        // A 160 x 90 CSS pixel picture (16:9), drawn by a data URI so no image file is needed.
        private const string Picture = "data:image/svg+xml;utf8,%3Csvg xmlns='http://www.w3.org/2000/svg' width='160' height='90'%3E%3Crect width='160' height='90' fill='red'/%3E%3C/svg%3E";

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
                return box;

            foreach (var child in box.Boxes)
            {
                if (FindByTag(child, tag) is { } found)
                    return found;
            }

            return null;
        }

        // The size of the <img> in points once a 300pt x 225pt (4:3) card holds it with the given rules.
        private static async Task<(double Width, double Height)> ImageSizeAsync(string imageStyle)
        {
            var html = $@"<!DOCTYPE html>
<html><head><style>
body {{ margin: 0 }}
.card {{ position: relative; width: 300pt; height: 225pt; overflow: hidden }}
img {{ {imageStyle} }}
</style></head>
<body><div class='card'><img src=""{Picture}""></div></body></html>";

            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext);
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var image = FindByTag(container.Root!, "img")!;
            var word = image.Words.First();
            return (word.Width, word.Height);
        }

        [Fact]
        public async Task ALargerRatioPictureThatMustFillTheCard_IsTheCardsSize_NotWiderThanItsMaxWidth()
        {
            // w=120pt, h=67.5pt; both are below the minimums (300 x 225): min-width/w = 2.5 <= min-height/h = 3.33, so the table gives
            // (min(max-width, min-height * w/h), min-height) = (min(300, 400), 225) = (300, 225).
            var (width, height) = await ImageSizeAsync("position: absolute; top: 0; left: 0; min-width: 100%; min-height: 100%; max-width: 100%; height: auto");

            Assert.Equal(300, width, 0.5);
            Assert.Equal(225, height, 0.5);
        }

        [Fact]
        public async Task ASmallerMinimumHeight_ScalesBothDimensionsBySameRatio()
        {
            // Only min-height binds (h=67.5 < 150): the width follows the ratio, 150 * 16/9 = 266.7, within max-width.
            var (width, height) = await ImageSizeAsync("position: absolute; top: 0; left: 0; min-height: 150pt; max-width: 100%; height: auto");

            Assert.Equal(266.67, width, 0.5);
            Assert.Equal(150, height, 0.5);
        }

        [Fact]
        public async Task APercentageMaximumHeight_IsOfTheCardsHeight_ForAnAbsolutePicture()
        {
            // 20% of the 225pt card is 45pt, below the picture's 67.5pt: the picture is scaled to 45pt tall and 80pt wide, as a length would do.
            var (width, height) = await ImageSizeAsync("position: absolute; top: 0; left: 0; max-height: 20%");

            Assert.Equal(80, width, 0.5);
            Assert.Equal(45, height, 0.5);
        }

        [Fact]
        public async Task AMinimumWidth_ScalesTheHeightByTheRatio()
        {
            // Only min-width binds (w=120 < 240): 240 x 135.
            var (width, height) = await ImageSizeAsync("position: absolute; top: 0; left: 0; min-width: 240pt; height: auto");

            Assert.Equal(240, width, 0.5);
            Assert.Equal(135, height, 0.5);
        }

        [Fact]
        public async Task AMaximumWidth_ScalesTheHeightByTheRatio()
        {
            // w=120 > max 60: 60 x 33.75.
            var (width, height) = await ImageSizeAsync("position: absolute; top: 0; left: 0; max-width: 60pt; height: auto");

            Assert.Equal(60, width, 0.5);
            Assert.Equal(33.75, height, 0.5);
        }

        [Theory]
        [InlineData("position: absolute; top: 0; left: 0; max-width: 100%; height: auto")]
        [InlineData("position: relative; max-width: 100%; height: auto")]
        [InlineData("position: static; max-width: 100%; height: auto")]
        public async Task APercentageMaximumWidth_IsOfTheCardsWidth_WhateverThePositioning(string style)
        {
            // 100% of the 300pt card is far more than the picture's 120pt, so the picture keeps its size; resolved against a card that has no width
            // yet (0), it would be crushed to nothing.
            var (width, height) = await ImageSizeAsync(style);

            Assert.Equal(120, width, 0.5);
            Assert.Equal(67.5, height, 0.5);
        }

        // The size of an inline <svg> (80 x 80 units, no width or height of its own) set by the given rules, in points.
        private static async Task<(double Width, double Height)> SvgSizeAsync(string svgStyle, string bodyStyle = "")
        {
            var html = $@"<!DOCTYPE html>
<html><head><style>
body {{ margin: 0; {bodyStyle} }}
svg {{ {svgStyle} }}
</style></head>
<body><p><svg id='s' viewBox='0 0 80 80'><rect width='80' height='80'/></svg></p></body></html>";

            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { MediaType = MediaType.Screen };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var word = FindByTag(container.Root!, "svg")!.Words.First();
            return (word.Width, word.Height);
        }

        [Theory]
        [InlineData("width: 1rem; height: 1rem", "", 12, 12)]
        [InlineData("width: 2em; height: 2em", "font-size: 20px", 30, 30)]
        [InlineData("width: 3rem; height: auto", "", 36, 36)]
        [InlineData("width: 10vw; height: 10vw", "", 59.5, 59.5)]
        [InlineData("min-width: 2rem; max-width: 1rem; height: auto", "", 24, 24)]
        public async Task ALengthInRelativeUnits_SizesAReplacedElementAsAnAbsoluteOneDoes(string style, string bodyStyle, double width, double height)
        {
            // `1rem` is 16 CSS pixels (12pt) at the default size; these are the units a utility-class style sheet writes icon sizes in (h-4 w-4).
            var (actualWidth, actualHeight) = await SvgSizeAsync(style, bodyStyle);

            Assert.Equal(width, actualWidth, 0.6);
            Assert.Equal(height, actualHeight, 0.6);
        }

        [Fact]
        public async Task NoConstraints_KeepsTheIntrinsicSize()
        {
            var (width, height) = await ImageSizeAsync("position: absolute; top: 0; left: 0");

            Assert.Equal(120, width, 0.5);
            Assert.Equal(67.5, height, 0.5);
        }
    }
}

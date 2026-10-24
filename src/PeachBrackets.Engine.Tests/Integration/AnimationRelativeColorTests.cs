using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Drawing;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A relative colour in a keyframe is read, and mixed in Oklab as every colour that is not a legacy sRGB format is (CSS Color 4 §13.2): each row compares it
    /// to the same animation written with the colour the relative one resolves to.
    /// </summary>
    public class AnimationRelativeColorTests
    {
        [Theory]
        [InlineData("rgb(from red r g b)", "color(srgb 1 0 0)", "#0000ff")]
        [InlineData("hsl(from red calc(h + 240) s l)", "color(srgb 0 0 1)", "#00ff00")]
        [InlineData("oklch(from red l c h / 0.5)", "oklch(0.628 0.2577 29.23 / 0.5)", "#00ff00")]
        [InlineData("rgb(from #336699 r g b / alpha)", "color(srgb 0.2 0.4 0.6)", "#ffffff")]
        public async Task RelativeColour_IsMixedInOklab(string relative, string resolved, string other)
        {
            foreach (var progress in new[] { 0.25, 0.5, 0.75 })
            {
                var expected = await ColourAt(resolved, other, progress);
                var actual = await ColourAt(relative, other, progress);

                Assert.True(
                    Math.Abs(expected.R - actual.R) <= 3 && Math.Abs(expected.G - actual.G) <= 3 && Math.Abs(expected.B - actual.B) <= 3 && Math.Abs(expected.A - actual.A) <= 3,
                    $"{relative} to {other} at {progress}: expected rgba({expected.R}, {expected.G}, {expected.B}, {expected.A}), got rgba({actual.R}, {actual.G}, {actual.B}, {actual.A})");
            }
        }

        [Fact]
        public async Task RelativeColour_IsNotTheSrgbMix()
        {
            var relative = await ColourAt("rgb(from red r g b)", "#00f", 0.5);

            // an sRGB mix of red and blue is (128, 0, 128); the Oklab one is not
            Assert.True(Math.Abs(relative.R - 128) + Math.Abs(relative.G) + Math.Abs(relative.B - 128) > 20);
        }

        private static async Task<PaintColor> ColourAt(string from, string to, double progress)
        {
            var html = $@"<!DOCTYPE html><html><head><style>
                html, body {{ margin: 0 }}
                @keyframes k {{ from {{ background-color: {from} }} to {{ background-color: {to} }} }}
                #a {{ width: 20px; height: 20px; animation: k 1s linear }}</style></head><body><div id=""a""></div></body></html>";

            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationProgress = progress };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return Find(container.Root!)?.ActualBackgroundColor ?? throw new InvalidOperationException("No #a");
        }

        private static CssBox? Find(CssBox box)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true && string.Equals(boxId, "a", StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = Find(child);
                if (found is not null) return found;
            }

            return null;
        }
    }
}

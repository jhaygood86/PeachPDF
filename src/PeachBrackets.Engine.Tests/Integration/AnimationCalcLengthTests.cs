using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A <c>calc()</c> against a length (or a <c>calc()</c> of another shape) is mixed as a <c>calc()</c> of the two terms (CSS Values 4 §10.1) instead of flipping at
    /// the midpoint. The widths are what Chrome computes for the same paused animation, in px; the box holds points.
    /// </summary>
    public class AnimationCalcLengthTests
    {
        private const double PointsPerPx = 0.75;

        private static async Task<CssBox> BoxAt(string keyframes, double progress, string timing = "linear")
        {
            var html = $"<!DOCTYPE html><html><head><style>@keyframes k {{ {keyframes} }} p {{ margin: 0; font-size: 20px; animation: k 1s {timing} }}</style></head><body><p>Ab</p></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationProgress = progress };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            return DomUtils.GetBoxByTagName(container.Root!, "p")!;
        }

        [Theory]
        [InlineData("from { width: 40px } to { width: calc(100px + 2em) }", 0.25, 65)]
        [InlineData("from { width: 40px } to { width: calc(100px + 2em) }", 0.5, 90)]
        [InlineData("from { width: 40px } to { width: calc(100px + 2em) }", 0.75, 115)]
        [InlineData("from { width: calc(100px + 2em) } to { width: 40px }", 0.25, 115)]
        [InlineData("from { width: calc(100px + 2em) } to { width: 40px }", 0.75, 65)]
        [InlineData("from { width: calc(10px + 1em) } to { width: calc(100px + 2em) }", 0.5, 85)]
        [InlineData("from { width: calc(10px * 2 + 1em) } to { width: calc(100px + 2em) }", 0.5, 90)]
        public async Task ACalcAgainstALength_Blends(string keyframes, double progress, double pixels)
        {
            Assert.Equal(pixels * PointsPerPx, (await BoxAt(keyframes, progress)).Size.Width, 2);
        }

        [Theory]
        [InlineData("from { margin-left: calc(10px + 1em) } to { margin-left: 0 }", 0.5, 15)]
        [InlineData("from { margin-left: 0 } to { margin-left: calc(10px + 1em) }", 0.5, 15)]
        [InlineData("from { margin-left: calc(10px + 1em) } to { margin-left: 0px }", 0.25, 22.5)]
        public async Task ACalcAgainstAZero_Blends(string keyframes, double progress, double pixels)
        {
            // A bare 0 is a zero length beside a calc() of lengths, so this is a mix and not a flip to the other end.
            Assert.Equal(pixels * PointsPerPx, (await BoxAt(keyframes, progress)).ActualMarginLeft, 2);
        }

        [Fact]
        public async Task ACalcAgainstANumber_IsNotMixedAsALength()
        {
            // calc(1 + 3) against 0 is a pair of numbers: z-index takes the whole-number mix of it, not a length mix.
            var box = await BoxAt("from { z-index: calc(1 + 3) } to { z-index: 0 }", 0.5);

            Assert.Equal("2", box.ZIndex.ToString());
        }

        [Fact]
        public async Task AnOvershootingEasing_DoesNotTakeANonNegativePropertyBelowZero()
        {
            // cubic-bezier(.5, 2, .5, 2) is about 1.6 half way, so the mix of calc(100px + 2em) and 10px is negative and padding cannot be.
            var box = await BoxAt("from { padding-left: calc(100px + 2em) } to { padding-left: 10px }", 0.5, "cubic-bezier(.5, 2, .5, 2)");

            Assert.Equal(0, box.ActualPaddingLeft, 2);
        }
    }
}

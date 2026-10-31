using PeachBrackets.Html;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <c>letter-spacing</c> and <c>word-spacing</c> animated on an HTML box, sampled at <c>AnimationProgress</c>: <c>normal</c> is the zero length, so it blends
    /// with a length (CSS Text 3 §8.1 and §8.2), and a <c>calc()</c> blends with a length as a calc() of the two. Values are what Chrome computes for the same
    /// paused animation (px); the box holds them in points, so 20px is 15pt.
    /// </summary>
    public class AnimationSpacingTests
    {
        private const double PointsPerPx = 0.75;

        private static async Task<double> SpacingAt(string property, string keyframes, double progress)
        {
            var html = $"<!DOCTYPE html><html><head><style>@keyframes k {{ {keyframes} }} p {{ margin: 0; font-size: 20px; animation: k 1s linear }}</style></head><body><p>Ab cd</p></body></html>";
            var renderContext = new HtmlRenderContext();
            var container = new HtmlContainerInt(renderContext) { AnimationProgress = progress };
            await container.SetHtml(html, null);

            var size = new Size(595, 842);
            container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
            container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);

            using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
            await container.PerformLayout(graphics);

            var box = DomUtils.GetBoxByTagName(container.Root!, "p")!;
            return property == "letter-spacing" ? box.ActualLetterSpacing : box.ActualWordSpacing;
        }

        // A box's word spacing is the width of its space glyph plus the property, so the property is what it has over the same box with `normal`.
        private static async Task<double> AddedSpacingAt(string property, string keyframes, double progress) =>
            await SpacingAt(property, keyframes, progress) - (property == "word-spacing" ? await SpacingAt(property, $"from {{ {property}: normal }} to {{ {property}: normal }}", progress) : 0);

        [Theory]
        [InlineData("letter-spacing", 0.25, 5)]
        [InlineData("letter-spacing", 0.5, 10)]
        [InlineData("letter-spacing", 0.75, 15)]
        [InlineData("word-spacing", 0.25, 5)]
        [InlineData("word-spacing", 0.5, 10)]
        [InlineData("word-spacing", 0.75, 15)]
        public async Task NormalToALength_Blends(string property, double progress, double pixels)
        {
            var spacing = await AddedSpacingAt(property, $"from {{ {property}: normal }} to {{ {property}: 20px }}", progress);

            Assert.Equal(pixels * PointsPerPx, spacing, 2);
        }

        [Theory]
        [InlineData("letter-spacing", 0.25, 15)]
        [InlineData("letter-spacing", 0.5, 10)]
        [InlineData("word-spacing", 0.5, 10)]
        public async Task ALengthToNormal_Blends(string property, double progress, double pixels)
        {
            var spacing = await AddedSpacingAt(property, $"from {{ {property}: 20px }} to {{ {property}: normal }}", progress);

            Assert.Equal(pixels * PointsPerPx, spacing, 2);
        }

        [Theory]
        [InlineData("letter-spacing", 0.25, 5)]
        [InlineData("letter-spacing", 0.5, 10)]
        [InlineData("letter-spacing", 0.75, 15)]
        [InlineData("word-spacing", 0.5, 10)]
        public async Task NormalToACalc_Blends(string property, double progress, double pixels)
        {
            // calc(10px + 0.5em) is 20px at this box's font-size
            var spacing = await AddedSpacingAt(property, $"from {{ {property}: normal }} to {{ {property}: calc(10px + 0.5em) }}", progress);

            Assert.Equal(pixels * PointsPerPx, spacing, 2);
        }
    }
}

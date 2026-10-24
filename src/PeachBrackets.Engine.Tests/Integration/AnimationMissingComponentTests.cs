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
    /// A component written as <c>none</c> is missing (CSS Color 4 §12.2): in a mix it takes the other colour's value, where the colour used on its own has it as 0.
    /// Each row compares an animation to the same animation written with the other colour's value in place of <c>none</c>, which is what the specification says the mix is.
    /// </summary>
    public class AnimationMissingComponentTests
    {
        [Theory]
        [InlineData("oklab(none 0.1 0.05)", "oklab(0.8 0.1 0.05)", "oklab(0.8 -0.1 0.02)")]
        [InlineData("oklab(0.3 none 0.05)", "oklab(0.3 -0.1 0.05)", "oklab(0.8 -0.1 0.02)")]
        [InlineData("oklab(0.3 0.1 none)", "oklab(0.3 0.1 0.02)", "oklab(0.8 -0.1 0.02)")]
        [InlineData("oklch(none 0.1 30)", "oklch(0.7 0.1 30)", "oklch(0.7 0.2 200)")]
        [InlineData("oklab(0.5 0.1 0.05 / none)", "oklab(0.5 0.1 0.05 / 0.5)", "oklab(0.7 -0.1 0.1 / 0.5)")]
        public async Task LabLikeNone_TakesTheOtherColoursValue(string withNone, string filled, string other)
        {
            foreach (var progress in new[] { 0.25, 0.5, 0.75 })
            {
                var missing = await ColourAt(withNone, other, progress);
                var explicitly = await ColourAt(filled, other, progress);
                AssertClose(explicitly, missing, $"{withNone} to {other} at {progress}");
            }
        }

        [Fact]
        public async Task NoneIsNotZero()
        {
            var missing = await ColourAt("oklab(none 0.1 0.05)", "oklab(0.8 -0.1 0.02)", 0.5);
            var zero = await ColourAt("oklab(0 0.1 0.05)", "oklab(0.8 -0.1 0.02)", 0.5);

            Assert.True(Math.Abs(missing.R - zero.R) + Math.Abs(missing.G - zero.G) + Math.Abs(missing.B - zero.B) > 40);
        }

        [Fact]
        public async Task HslNone_MixesInOklab_NotInSrgb()
        {
            // A legacy colour with a missing component is mixed in Oklab, as Chrome does (it was an sRGB mix before).
            var withNone = await ColourAt("hsl(none 100% 50%)", "lab(60 40 -40)", 0.5);

            Assert.Equal(255, withNone.A);
            // red to lab(60 40 -40) in Oklab is not the sRGB mix of the two, as it would have been
            var inSrgb = await ColourAt("hsl(0 100% 50%)", "hsl(300 70% 60%)", 0.5);
            Assert.True(Math.Abs(withNone.G - inSrgb.G) + Math.Abs(withNone.B - inSrgb.B) > 10);
        }

        [Fact]
        public async Task ColorFunction_MixesInOklab()
        {
            // Chrome: #f00 to color(display-p3 0 0 1) is oklab(0.547 0.096 -0.098) half way
            var mixed = await ColourAt("#f00", "color(display-p3 0 0 1)", 0.5);
            var expected = await ColourAt("oklab(0.547 0.096 -0.098)", "oklab(0.547 0.096 -0.098)", 0.5);

            AssertClose(expected, mixed, "#f00 to color(display-p3 0 0 1)");
        }

        [Fact]
        public async Task MissingAlpha_TakesTheOtherColoursAlpha()
        {
            var colour = await ColourAt("oklab(0.5 0.1 0.05 / none)", "oklab(0.5 0.1 0.05 / 0.4)", 0.5);

            Assert.InRange((int)colour.A, 101, 103);
        }

        private static void AssertClose(PaintColor expected, PaintColor actual, string label) =>
            Assert.True(
                Math.Abs(expected.R - actual.R) <= 2 && Math.Abs(expected.G - actual.G) <= 2 && Math.Abs(expected.B - actual.B) <= 2 && Math.Abs(expected.A - actual.A) <= 2,
                $"{label}: expected rgba({expected.R}, {expected.G}, {expected.B}, {expected.A}), got rgba({actual.R}, {actual.G}, {actual.B}, {actual.A})");

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

using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachBrackets.Html.Parse;
using Xunit;

namespace PeachBrackets.Engine.Tests.CSS
{
    /// <summary>
    /// <c>none</c> components and the <c>color()</c> function (CSS Color 4): read by the colour parser as the colour a browser draws, and carried into <c>color-mix()</c> as missing components.
    /// </summary>
    public class ColorNoneAndColorFunctionTests
    {
        private static bool TryParse(string text, out PaintColor color) =>
            new CssValueParser(new HtmlRenderContext()).TryGetColor(text, 0, text.Length, out color);

        private static PaintColor Parse(string text)
        {
            Assert.True(TryParse(text, out var color), text);
            return color;
        }

        [Theory]
        [InlineData("rgb(none 0 0)", 0, 0, 0, 255)]
        [InlineData("rgb(none 128 255 / 50%)", 0, 128, 255, 128)]
        [InlineData("rgb(255 none none / none)", 255, 0, 0, 0)]
        [InlineData("lab(50 40 20 / none)", 185, 88, 88, 0)]
        [InlineData("hsl(none 100% 50%)", 255, 0, 0, 255)]
        [InlineData("hwb(none 0% 0% / none)", 255, 0, 0, 0)]
        public void NoneIsZero_AndAMissingAlphaIsTransparent(string text, int r, int g, int b, int a)
        {
            var color = Parse(text);

            Assert.InRange((int)color.R, r - 3, r + 3);
            Assert.InRange((int)color.G, g - 3, g + 3);
            Assert.InRange((int)color.B, b - 3, b + 3);
            Assert.InRange((int)color.A, a - 1, a + 1);
        }

        [Theory]
        [InlineData("rgb(none, 0, 0)")]
        [InlineData("hsl(none, 100%, 50%)")]
        [InlineData("rgba(0, 0, 0, none)")]
        public void NoneIsNotPartOfTheLegacyCommaSyntax(string text) => Assert.False(TryParse(text, out _));

        [Theory]
        [InlineData("color(srgb 1 0 0)", 255, 0, 0, 255)]
        [InlineData("color(srgb 100% 0% 50% / 50%)", 255, 0, 128, 128)]
        [InlineData("color(srgb none 0 0)", 0, 0, 0, 255)]
        [InlineData("COLOR(SRGB 0 1 0 / 0.5)", 0, 255, 0, 128)]
        [InlineData("color(srgb-linear 0.5 0.5 0.5)", 188, 188, 188, 255)]
        [InlineData("color(display-p3 0 1 0)", 0, 255, 0, 255)]
        public void ColorFunction_ReadsThePredefinedSpaces(string text, int r, int g, int b, int a)
        {
            var color = Parse(text);

            Assert.InRange((int)color.R, r - 2, r + 2);
            Assert.InRange((int)color.G, g - 2, g + 2);
            Assert.InRange((int)color.B, b - 2, b + 2);
            Assert.InRange((int)color.A, a - 1, a + 1);
        }

        [Theory]
        [InlineData("color(srgb 1 1 1)")]
        [InlineData("color(srgb-linear 1 1 1)")]
        [InlineData("color(display-p3 1 1 1)")]
        [InlineData("color(a98-rgb 1 1 1)")]
        [InlineData("color(prophoto-rgb 1 1 1)")]
        [InlineData("color(rec2020 1 1 1)")]
        [InlineData("color(xyz 0.95047 1 1.08883)")]
        [InlineData("color(xyz-d65 0.95047 1 1.08883)")]
        [InlineData("color(xyz-d50 0.96422 1 0.82521)")]
        public void ColorFunction_WhiteIsWhiteInEverySpace(string text)
        {
            var color = Parse(text);

            Assert.InRange((int)color.R, 253, 255);
            Assert.InRange((int)color.G, 253, 255);
            Assert.InRange((int)color.B, 253, 255);
        }

        [Theory]
        [InlineData("color(a98-rgb 0.5 0.5 0.5)")]
        [InlineData("color(prophoto-rgb 0.5 0.5 0.5)")]
        [InlineData("color(rec2020 0.5 0.5 0.5)")]
        [InlineData("color(display-p3 0.5 0.5 0.5)")]
        public void ColorFunction_AGrayStaysGray(string text)
        {
            var color = Parse(text);

            Assert.InRange((int)color.R - color.G, -2, 2);
            Assert.InRange((int)color.B - color.G, -2, 2);
            Assert.InRange((int)color.G, 100, 150);
        }

        [Theory]
        [InlineData("color(srgb 1 0 0 0.5)")]
        [InlineData("color(srgb 1 0)")]
        [InlineData("color(nonsense 1 0 0)")]
        [InlineData("color(1 0 0)")]
        [InlineData("color(srgb red 0 0)")]
        public void ColorFunction_RejectsWhatItDoesNotRead(string text) => Assert.False(TryParse(text, out _));

        [Fact]
        public void ColorMix_AMissingComponentTakesTheOtherOperandsValue()
        {
            var missing = Parse("color-mix(in oklab, oklab(none 0.1 0.05), oklab(0.8 -0.1 0.02))");
            var filled = Parse("color-mix(in oklab, oklab(0.8 0.1 0.05), oklab(0.8 -0.1 0.02))");
            var zero = Parse("color-mix(in oklab, oklab(0 0.1 0.05), oklab(0.8 -0.1 0.02))");

            Assert.Equal(filled, missing);
            Assert.NotEqual(zero, missing);
        }

        [Fact]
        public void ColorMix_InLab_AMissingLightnessTakesTheOtherOperandsValue()
        {
            var missing = Parse("color-mix(in lab, lab(none 40 20), lab(80 -20 10))");
            var filled = Parse("color-mix(in lab, lab(80 40 20), lab(80 -20 10))");

            Assert.Equal(filled, missing);
        }

        [Fact]
        public void ColorMix_AMissingHslComponentLandsOnTheAnalogousComponent()
        {
            // hsl lightness is analogous to oklab lightness, so a missing one is missing in an oklab mix.
            var missing = Parse("color-mix(in oklab, hsl(120 100% none), lab(80 -20 10))");
            var zero = Parse("color-mix(in oklab, hsl(120 100% 0%), lab(80 -20 10))");

            Assert.NotEqual(zero, missing);
        }

        [Fact]
        public void ColorMix_AMissingAlphaTakesTheOtherOperandsAlpha()
        {
            var mixed = Parse("color-mix(in srgb, rgb(255 0 0 / none), rgb(0 0 255 / 0.5))");

            Assert.InRange((int)mixed.A, 126, 129);
            Assert.InRange((int)mixed.R, 126, 129);
            Assert.InRange((int)mixed.B, 126, 129);
        }

        [Fact]
        public void ColorMix_BothMissing_MixesAsZero()
        {
            var mixed = Parse("color-mix(in srgb, rgb(255 none 0), rgb(0 none 255))");

            Assert.Equal(0, mixed.G);
        }

        [Fact]
        public void ColorMix_ColorFunctionOperandKeepsItsOutOfGamutCoordinates()
        {
            var mixed = Parse("color-mix(in oklab, color(display-p3 0 1 0), color(display-p3 0 1 0))");

            Assert.Equal(0, mixed.R);
            Assert.True(mixed.G >= 254);
        }

        [Fact]
        public void ColorMix_InTheWrittenSpace_UsesTheWrittenValues()
        {
            var mixed = Parse("color-mix(in srgb, rgb(none 0 0), rgb(200 0 0))");

            Assert.InRange((int)mixed.R, 198, 202);
        }

        [Fact]
        public void ColorMix_HwbAndHslMissingComponents()
        {
            var hsl = Parse("color-mix(in hsl, hsl(none 100% 50%), hsl(120 100% 50%))");
            var hwb = Parse("color-mix(in hwb, hwb(none 0% 0%), hwb(120 0% 0%))");

            Assert.True(hsl.G > 200 && hsl.R < 60);
            Assert.True(hwb.G > 200 && hwb.R < 60);
        }
    }
}

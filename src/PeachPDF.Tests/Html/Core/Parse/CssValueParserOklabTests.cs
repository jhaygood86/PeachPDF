using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using PeachPDF.PdfBackend;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// <see cref="CssValueParser.TryGetOklab"/>: the Oklab coordinates of a <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c> or <c>color-mix()</c>
    /// colour as written, kept out of the sRGB clip <c>TryGetColor</c> applies, which is what an animation mixes two colours from (CSS Color 4 §13).
    /// </summary>
    public class CssValueParserOklabTests
    {
        private static bool TryGet(string text, out double[] oklab) => new CssValueParser(new PdfRenderContext()).TryGetOklab(text, out oklab);

        [Fact]
        public void Oklab_IsReadAsWritten_EvenOutsideTheGamut()
        {
            Assert.True(TryGet("oklab(0.8 0.2 0.1)", out var v));
            Assert.Equal(0.8, v[0], 6);
            Assert.Equal(0.2, v[1], 6);
            Assert.Equal(0.1, v[2], 6);
        }

        [Fact]
        public void Oklch_IsReadAsRectangularCoordinates_WithItsFullChroma()
        {
            // oklch(0.7 0.3 150) is far outside sRGB: the clipped colour has a smaller chroma than the 0.3 it was written with.
            Assert.True(TryGet("oklch(0.7 0.3 150)", out var v));
            Assert.Equal(0.7, v[0], 6);
            Assert.Equal(0.3 * System.Math.Cos(150 * System.Math.PI / 180), v[1], 6);
            Assert.Equal(0.3 * System.Math.Sin(150 * System.Math.PI / 180), v[2], 6);
            Assert.Equal(0.3, System.Math.Sqrt(v[1] * v[1] + v[2] * v[2]), 6);
        }

        [Theory]
        [InlineData("lab(50 0 0)")]
        [InlineData("lch(50 0 0)")]
        public void LabAndLch_AreConvertedThroughXyz_ANeutralGreyStaysNeutral(string text)
        {
            // L* 50 is Y = 0.1842, and the Oklab lightness of a neutral is the cube root of Y.
            Assert.True(TryGet(text, out var v));
            Assert.Equal(System.Math.Cbrt(0.184187), v[0], 3);
            Assert.Equal(0, v[1], 3);
            Assert.Equal(0, v[2], 3);
        }

        [Fact]
        public void Lab_OutsideTheGamut_KeepsMoreChromaThanItsClippedColour()
        {
            Assert.True(TryGet("lab(60 90 60)", out var wide));
            Assert.True(new CssValueParser(new PdfRenderContext()).TryGetColor("lab(60 90 60)", 0, 13, out var clipped));

            var clippedOklab = ColorSpaceMath.ToSpace(clipped.R / 255d, clipped.G / 255d, clipped.B / 255d, ColorSpaceMath.Space.Oklab);
            Assert.True(System.Math.Sqrt(wide[1] * wide[1] + wide[2] * wide[2]) > System.Math.Sqrt(clippedOklab[1] * clippedOklab[1] + clippedOklab[2] * clippedOklab[2]) + 0.02);
        }

        [Fact]
        public void ColorMix_InLch_IsMixedBeforeTheClip()
        {
            // red is lch(54.3 106.8 40.9), blue lch(29.6 131.2 301.4): half way through the shorter arc is far outside sRGB, and the mix is read
            // as its own coordinates, not as the clipped colour a plain color-mix() resolves to.
            Assert.True(TryGet("color-mix(in lch, red, blue)", out var v));
            Assert.True(System.Math.Sqrt(v[1] * v[1] + v[2] * v[2]) > 0.27);
        }

        [Fact]
        public void ColorMix_InOklch_MixesChromaAndLightness()
        {
            // red oklch(0.628 0.2577 29.2), blue oklch(0.452 0.3132 264.05): the midpoint has the mean lightness and chroma.
            Assert.True(TryGet("color-mix(in oklch, red, blue)", out var v));
            Assert.Equal(0.540, v[0], 2);
            Assert.Equal(0.2855, System.Math.Sqrt(v[1] * v[1] + v[2] * v[2]), 2);
        }

        [Fact]
        public void ColorMix_InSrgb_IsConvertedThroughSrgb()
        {
            Assert.True(TryGet("color-mix(in srgb, red, blue)", out var v));
            var expected = ColorSpaceMath.ToSpace(0.5, 0, 0.5, ColorSpaceMath.Space.Oklab);
            Assert.Equal(expected[0], v[0], 2);
            Assert.Equal(expected[1], v[1], 2);
            Assert.Equal(expected[2], v[2], 2);
        }

        [Theory]
        [InlineData("")]
        [InlineData("red")]
        [InlineData("#ff0000")]
        [InlineData("rgb(255 0 0)")]
        [InlineData("hsl(120 100% 50%)")]
        [InlineData("lab(")]
        [InlineData("lab(50 40)")]
        [InlineData("oklch(0.5 0.2)")]
        [InlineData("lch(50 60 purple)")]
        [InlineData("color-mix(in srgb, red)")]
        [InlineData("color-mix(in srgb, device-cmyk(0 1 1 0), device-cmyk(1 0 0 0))")]
        [InlineData("color-mix(in srgb, device-cmyk(0 1 1 0), red)")]
        public void AnythingElse_IsNotAWideColour(string text)
        {
            Assert.False(TryGet(text, out var v));
            Assert.Empty(v);
        }

        [Theory]
        [InlineData("lab(150 0 0)", 1, 0, 0)]
        [InlineData("lab(-30 0 0)", 0, 0, 0)]
        [InlineData("oklab(1.4 0 0)", 1, 0, 0)]
        [InlineData("oklch(0.5 -0.2 100)", 0.5, 0, 0)]
        [InlineData("oklch(1.5 0.3 150)", 1, 0.3 * -0.8660254, 0.3 * 0.5)]
        public void AnOutOfRangeLightnessOrChroma_IsClampedWhenTheColourIsRead(string text, double l, double a, double b)
        {
            Assert.True(TryGet(text, out var v));
            Assert.Equal(l, v[0], 3);
            Assert.Equal(a, v[1], 3);
            Assert.Equal(b, v[2], 3);
        }

        [Fact]
        public async System.Threading.Tasks.Task ANestedColorMix_IsReadOnceAtEachLevel()
        {
            // Every level used to be read twice (once for the colour, once for its Oklab), so the work doubled with each level: 26 levels is a
            // few milliseconds read once and minutes read twice per level, so the bound separates them on any machine.
            var text = "blue";
            for (var i = 0; i < 26; i++) text = $"color-mix(in oklab, {text}, red)";

            var parser = new CssValueParser(new PdfRenderContext());
            var read = System.Threading.Tasks.Task.Run(() =>
            {
                Assert.True(parser.TryGetColor(text, 0, text.Length, out _));
                Assert.True(parser.TryGetOklab(text, out _));
            }, TestContext.Current.CancellationToken);
            await read.WaitAsync(System.TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        }

        [Theory]
        [InlineData("lab(50% 20% 10%)")]
        [InlineData("lch(60% 20% 120)")]
        [InlineData("oklab(60% 25% -10%)")]
        [InlineData("oklch(60% 25% 250)")]
        [InlineData("lab(none 0 0)")]
        [InlineData("oklch(0.6 none 30)")]
        [InlineData("lch(60 30 0.5turn)")]
        [InlineData("oklab(0.6 0.1 -0.04 / 50%)")]
        public void InGamutColours_AgreeWithTheirSrgbValue_WhateverTheirSyntax(string text)
        {
            // Percentages, none, angle units and alpha go through the same component readers as the colour parse, so the two agree to a byte's rounding.
            var parser = new CssValueParser(new PdfRenderContext());
            Assert.True(parser.TryGetOklab(text, out var wide));
            Assert.True(parser.TryGetColor(text, 0, text.Length, out var color));

            var viaSrgb = ColorSpaceMath.ToSpace(color.R / 255d, color.G / 255d, color.B / 255d, ColorSpaceMath.Space.Oklab);
            for (var i = 0; i < 3; i++) Assert.InRange(wide[i], viaSrgb[i] - 0.01, viaSrgb[i] + 0.01);
        }

        [Theory]
        [InlineData("color-mix(in srgb, oklch(0.7 0.3 150) 90%, red)", 0, 183, 0)]
        [InlineData("color-mix(in lch, oklch(0.7 0.3 150) 80%, red)", 32, 186, 0)]
        [InlineData("color-mix(in oklab, oklch(0.7 0.3 150) 80%, red)", 69, 183, 0)]
        [InlineData("color-mix(in oklch, red, lab(60 90 60) 90%)", 255, 0, 43)]
        [InlineData("color-mix(in srgb, red, blue)", 128, 0, 128)]
        [InlineData("color-mix(in lab, lab(50 40 20), lab(70 -30 30))", 165, 141, 101)]
        [InlineData("color-mix(in display-p3, oklch(0.7 0.3 150), white)", 0, 231, 146)]
        [InlineData("color-mix(in oklab, color-mix(in xyz, oklch(0.7 0.3 150), blue), white)", 130, 203, 222)]
        // A lightness out of its range is clamped, and a negative chroma is 0, when the colour is read, before any mix (CSS Color 4 section 9).
        [InlineData("color-mix(in oklab, lab(150 0 0), black)", 99, 99, 99)]
        [InlineData("color-mix(in srgb, lab(150 0 0), black)", 127, 128, 128)]
        [InlineData("color-mix(in oklab, lab(-30 0 0), white)", 99, 99, 99)]
        [InlineData("color-mix(in oklab, oklab(1.4 0 0), black)", 99, 99, 99)]
        [InlineData("color-mix(in oklab, oklch(0.5 -0.2 100), white)", 174, 174, 174)]
        [InlineData("color-mix(in lab, lab(120 20 20), lab(20 -20 -20))", 145, 145, 145)]
        [InlineData("color-mix(in oklab, oklch(1.5 0.3 150), red)", 219, 194, 75)]
        // A gray has no hue of its own, so the other operand's stands in for it in lch.
        [InlineData("color-mix(in lch, oklch(0.5 0 120), oklch(0.6 0.2 240))", 68, 117, 164)]
        [InlineData("color-mix(in lch, lab(50 0 0), lch(60 40 240))", 97, 138, 161)]
        // Chrome calls a hue missing at a chroma of 0.02 or less, so a near-gray (the grays of a Bootstrap palette) takes the other operand's hue too.
        [InlineData("color-mix(in oklch, #f8f9fa, red)", 255, 158, 142)]
        [InlineData("color-mix(in oklch, #6c757d, red)", 194, 89, 76)]
        [InlineData("color-mix(in oklch, #212529, red)", 143, 46, 36)]
        [InlineData("color-mix(in oklch, #808081, red)", 198, 99, 85)]
        [InlineData("color-mix(in oklch, lch(55 2 300), #f00)", 201, 100, 86)]
        [InlineData("color-mix(in lch, #f8f9fa, #0a7)", 136, 211, 218)]
        [InlineData("color-mix(in lch, oklab(0.6 0 0), red)", 200, 97, 71)]
        // Two grays: both hues are missing, and Chrome mixes them at hue 0.
        [InlineData("color-mix(in oklch, #6c757d, #6c757d)", 125, 112, 115)]
        [InlineData("color-mix(in oklch, #6c757d, #adb5bd)", 156, 143, 147)]
        [InlineData("color-mix(in oklch, #6c757d, #f8f9fa)", 185, 178, 180)]
        // `longer hue` between two equal hues goes half way round (CSS Color 4: -180 < difference <= 0 adds a turn), which a gray's substituted hue always is.
        [InlineData("color-mix(in oklch longer hue, #888, red)", 0, 156, 178)]
        [InlineData("color-mix(in oklch longer hue, red, red)", 0, 169, 219)]
        [InlineData("color-mix(in lch longer hue, #888, #00f)", 57, 115, 0)]
        public void AColorMix_WithAnOperandOutsideTheGamut_IsMixedBeforeTheClip(string text, int r, int g, int b)
        {
            // Chrome's values (canvas readback). The operand that is outside sRGB is mixed from the coordinates it was written with, not clipped first.
            Assert.True(new CssValueParser(new PdfRenderContext()).TryGetColor(text, 0, text.Length, out var color));
            Assert.InRange(color.R, r - 2, r + 2);
            Assert.InRange(color.G, g - 2, g + 2);
            Assert.InRange(color.B, b - 2, b + 2);
        }

        [Theory]
        [InlineData("Srgb")]
        [InlineData("SrgbLinear")]
        [InlineData("DisplayP3")]
        [InlineData("Lab")]
        [InlineData("Oklab")]
        [InlineData("XyzD65")]
        [InlineData("XyzD50")]
        [InlineData("Lch")]
        [InlineData("Oklch")]
        [InlineData("Hsl")]
        [InlineData("Hwb")]
        public void ToSpaceFromOklab_IsTheInverseOfToSpace_ForAnInGamutColour(string spaceName)
        {
            var space = System.Enum.Parse<ColorSpaceMath.Space>(spaceName);
            var oklab = ColorSpaceMath.ToSpace(0.8, 0.4, 0.2, ColorSpaceMath.Space.Oklab);
            var expected = ColorSpaceMath.ToSpace(0.8, 0.4, 0.2, space);
            var actual = ColorSpaceMath.ToSpaceFromOklab(oklab, space);
            for (var i = 0; i < 3; i++) Assert.Equal(expected[i], actual[i], 3);

            // and ToOklab undoes it
            var back = ColorSpaceMath.ToOklab(expected, space);
            for (var i = 0; i < 3; i++) Assert.Equal(oklab[i], back[i], 3);
        }

        [Fact]
        public void OnlyLchAndOklch_HaveAHueThatCanBePowerless()
        {
            Assert.False(ColorSpaceMath.IsHuePowerless([50, 0, 0], ColorSpaceMath.Space.Hsl));
            Assert.False(ColorSpaceMath.IsHuePowerless([0.5, 0, 0], ColorSpaceMath.Space.Oklab));

            // Chrome's own limit, found by bisection: 0.02 is missing, 0.021 is not.
            Assert.True(ColorSpaceMath.IsHuePowerless([0.5, 0, 0], ColorSpaceMath.Space.Oklch));
            Assert.True(ColorSpaceMath.IsHuePowerless([0.5, 0.02, 0], ColorSpaceMath.Space.Oklch));
            Assert.False(ColorSpaceMath.IsHuePowerless([0.5, 0.021, 0], ColorSpaceMath.Space.Oklch));
            Assert.True(ColorSpaceMath.IsHuePowerless([50, 0.02, 0], ColorSpaceMath.Space.Lch));
            Assert.False(ColorSpaceMath.IsHuePowerless([50, 0.021, 0], ColorSpaceMath.Space.Lch));
        }

        [Fact]
        public void ToSpaceFromOklab_KeepsAnOutOfRangeColourOutOfRange()
        {
            // oklch(0.7 0.3 150) is far outside sRGB: the red channel is negative, not clipped to 0.
            var wide = ColorSpaceMath.ToOklab([0.7, 0.3, 150], ColorSpaceMath.Space.Oklch);
            Assert.True(ColorSpaceMath.ToSpaceFromOklab(wide, ColorSpaceMath.Space.Srgb)[0] < -0.1);
            Assert.True(ColorSpaceMath.ToSpaceFromOklab(wide, ColorSpaceMath.Space.SrgbLinear)[0] < -0.01);
        }

        [Theory]
        [InlineData("color-mix(in srgb, red)")]
        [InlineData("color-mix(red, blue)")]
        [InlineData("color-mix(in srgb, red 0%, blue 0%)")]
        public void AMalformedColorMix_IsNotAColourEither(string text)
        {
            Assert.False(new CssValueParser(new PdfRenderContext()).TryGetColor(text, 0, text.Length, out _));
        }

        [Fact]
        public void ToOklab_OfARectangularSpaceNotInTheFamily_GoesThroughSrgb()
        {
            var v = ColorSpaceMath.ToOklab([1, 0, 0], ColorSpaceMath.Space.Srgb);
            Assert.Equal(0.628, v[0], 3);
            Assert.Equal(0.2249, v[1], 3);
            Assert.Equal(0.1258, v[2], 3);
        }
    }
}

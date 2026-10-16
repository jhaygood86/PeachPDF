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
        public void ColorMix_InSrgb_IsConvertedThroughTheClippedColour()
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

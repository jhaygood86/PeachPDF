using PeachDrawing.Core;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Utils;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core.Utils
{
    /// <summary>
    /// Gradient stop interpolation is premultiplied by alpha (CSS Color 4 §12.3): a colour fading to
    /// <c>transparent</c> (0,0,0,0) stays that colour while it fades instead of darkening toward black.
    /// </summary>
    public class ColorSpaceConverterTests
    {
        private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);
        private static readonly PaintColor Transparent = PaintColor.FromArgb(0, 0, 0, 0);

        [Theory]
        [InlineData("Srgb")]
        [InlineData("SrgbLinear")]
        [InlineData("Oklab")]
        public void FadeToTransparent_KeepsTheColour(string spaceName)
        {
            var space = System.Enum.Parse<GradientColorSpace>(spaceName);
            var mid = ColorSpaceConverter.Interpolate(Red, Transparent, 0.5, space, HueInterpolationMethod.Shorter);

            Assert.InRange((int)mid.A, 127, 128);
            Assert.True(mid.R >= 250, $"red channel {mid.R}");
            Assert.True(mid.G <= 5 && mid.B <= 5, $"{mid.G},{mid.B}");
        }

        [Fact]
        public void LerpPremultiplied_FadeToTransparent_KeepsTheColour()
        {
            var q = ColorSpaceConverter.LerpPremultiplied(Red, Transparent, 0.25);

            Assert.Equal(255, q.R);
            Assert.Equal(0, q.G);
            Assert.InRange((int)q.A, 191, 192);
        }

        [Fact]
        public void LerpPremultiplied_WeighsByAlpha()
        {
            // opaque white to half-transparent black: premultiplied, 255 / (1 + .5 * .5 * ... ) = 170 where a straight lerp gives 128
            var white = PaintColor.FromArgb(255, 255, 255, 255);
            var halfBlack = PaintColor.FromArgb(128, 0, 0, 0);

            var mid = ColorSpaceConverter.LerpPremultiplied(white, halfBlack, 0.5);

            Assert.InRange((int)mid.A, 191, 192);
            Assert.InRange((int)mid.R, 168, 172);
        }

        [Fact]
        public void LerpPremultiplied_BothTransparent_FallsBackToStraightLerp()
        {
            var a = PaintColor.FromArgb(0, 200, 0, 0);
            var b = PaintColor.FromArgb(0, 0, 0, 100);

            var mid = ColorSpaceConverter.LerpPremultiplied(a, b, 0.5);

            Assert.Equal(0, mid.A);
            Assert.Equal(100, mid.R);
            Assert.Equal(50, mid.B);
        }

        [Fact]
        public void Interpolate_BothTransparent_FallsBackToStraightLerp()
        {
            var a = PaintColor.FromArgb(0, 255, 0, 0);
            var b = PaintColor.FromArgb(0, 0, 0, 255);

            var mid = ColorSpaceConverter.Interpolate(a, b, 0.5, GradientColorSpace.Srgb, HueInterpolationMethod.Shorter);

            Assert.Equal(0, mid.A);
            Assert.InRange((int)mid.R, 127, 128);
            Assert.InRange((int)mid.B, 127, 128);
        }

        [Fact]
        public void Lch_GrayStopTakesTheOtherStopsHue()
        {
            var gray = PaintColor.FromArgb(255, 128, 128, 128);
            var mid = ColorSpaceConverter.Interpolate(gray, Red, 0.5, GradientColorSpace.Lch, HueInterpolationMethod.Shorter);

            // Chrome measures rgb(200, 97, 71) here (the gray's hue is missing, so red's stands in for it)
            Assert.InRange((int)mid.R, 196, 204);
            Assert.InRange((int)mid.G, 93, 101);
            Assert.InRange((int)mid.B, 67, 75);
        }
    }
}

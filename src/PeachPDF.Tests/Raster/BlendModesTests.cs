using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;

namespace PeachPDF.Tests.Raster
{
    /// <summary>The 16 blend modes of W3C Compositing and Blending Level 1, against values worked out from the spec's own formulas.</summary>
    public class BlendModesTests
    {
        private static byte[] Blend(RBlendMode mode, (int R, int G, int B, int A) backdrop, (int R, int G, int B, int A) source)
        {
            var dst = new[] { (byte)backdrop.R, (byte)backdrop.G, (byte)backdrop.B, (byte)backdrop.A };
            BlendModes.Blend(mode, dst, source.R, source.G, source.B, source.A);
            return dst;
        }

        // Backdrop Cb = (0.8, 0.4, 0.2) and source Cs = (0.5, 0.6, 0.7), both opaque.
        private static readonly (int, int, int, int) Cb = (204, 102, 51, 255);
        private static readonly (int, int, int, int) Cs = (128, 153, 179, 255);

        private static void AssertColor(byte[] actual, int r, int g, int b, int a = 255, int tolerance = 2)
        {
            Assert.InRange(actual[0], r - tolerance, r + tolerance);
            Assert.InRange(actual[1], g - tolerance, g + tolerance);
            Assert.InRange(actual[2], b - tolerance, b + tolerance);
            Assert.InRange(actual[3], a - tolerance, a + tolerance);
        }

        [Fact]
        public void Multiply() => AssertColor(Blend(RBlendMode.Multiply, Cb, Cs), 102, 61, 36);

        [Fact]
        public void Screen() => AssertColor(Blend(RBlendMode.Screen, Cb, Cs), 230, 194, 194);

        [Fact]
        public void Overlay() => AssertColor(Blend(RBlendMode.Overlay, Cb, Cs), 204, 122, 71);

        [Fact]
        public void HardLight() => AssertColor(Blend(RBlendMode.HardLight, Cb, Cs), 204, 133, 133);

        [Fact]
        public void Darken() => AssertColor(Blend(RBlendMode.Darken, Cb, Cs), 128, 102, 51);

        [Fact]
        public void Lighten() => AssertColor(Blend(RBlendMode.Lighten, Cb, Cs), 204, 153, 179);

        [Fact]
        public void Difference() => AssertColor(Blend(RBlendMode.Difference, Cb, Cs), 76, 51, 128);

        [Fact]
        public void Exclusion() => AssertColor(Blend(RBlendMode.Exclusion, Cb, Cs), 128, 133, 158);

        [Fact]
        public void ColorDodge() => AssertColor(Blend(RBlendMode.ColorDodge, Cb, Cs), 255, 255, 170);

        [Fact]
        public void ColorBurn() => AssertColor(Blend(RBlendMode.ColorBurn, Cb, Cs), 153, 0, 0);

        [Fact]
        public void SoftLight()
        {
            // Source above 0.5 lightens; a source of exactly 0.5 leaves the backdrop alone.
            var lighter = Blend(RBlendMode.SoftLight, Cb, (204, 204, 204, 255));
            Assert.True(lighter[0] >= 204 && lighter[1] > 102 && lighter[2] > 51);

            AssertColor(Blend(RBlendMode.SoftLight, Cb, (128, 128, 128, 255)), 204, 102, 51, tolerance: 3);

            // A darker-than-half source darkens; the quarter-luminance branch of the formula is exercised by the low backdrop channel.
            var darker = Blend(RBlendMode.SoftLight, (51, 51, 51, 255), (26, 26, 26, 255));
            Assert.True(darker[0] < 51);
        }

        [Theory]
        [InlineData("ColorDodge")]
        [InlineData("ColorBurn")]
        public void DodgeAndBurn_HandleTheExtremesWithoutDividingByZero(string modeName)
        {
            var mode = Enum.Parse<RBlendMode>(modeName);
            foreach (var backdrop in new[] { 0, 255 })
            foreach (var source in new[] { 0, 255 })
            {
                var result = Blend(mode, (backdrop, backdrop, backdrop, 255), (source, source, source, 255));
                Assert.All(result, channel => Assert.InRange(channel, 0, 255));
            }
        }

        private static double Lum(byte[] c) => (0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2]) / 255;

        private static double Sat(byte[] c) => (Math.Max(c[0], Math.Max(c[1], c[2])) - Math.Min(c[0], Math.Min(c[1], c[2]))) / 255.0;

        [Fact]
        public void Luminosity_TakesTheSourceLuminosityAndTheBackdropHue()
        {
            var result = Blend(RBlendMode.Luminosity, Cb, Cs);

            Assert.Equal(Lum(new byte[] { 128, 153, 179 }), Lum(result), 1);
        }

        [Fact]
        public void Color_KeepsTheBackdropLuminosity()
        {
            var result = Blend(RBlendMode.Color, Cb, Cs);

            Assert.Equal(Lum(new byte[] { 204, 102, 51 }), Lum(result), 1);
        }

        [Fact]
        public void Hue_KeepsBackdropSaturationAndLuminosity()
        {
            var result = Blend(RBlendMode.Hue, Cb, Cs);

            Assert.Equal(Lum(new byte[] { 204, 102, 51 }), Lum(result), 1);
            Assert.Equal(Sat(new byte[] { 204, 102, 51 }), Sat(result), 1);
        }

        [Fact]
        public void Saturation_TakesTheSourceSaturation_KeepsBackdropLuminosity()
        {
            var result = Blend(RBlendMode.Saturation, Cb, (255, 0, 0, 255));

            Assert.Equal(Lum(new byte[] { 204, 102, 51 }), Lum(result), 1);
            Assert.True(Sat(result) > Sat(new byte[] { 204, 102, 51 }));
        }

        [Fact]
        public void NonSeparableModes_OnAGreySource_HaveNoSaturationToTransfer()
        {
            var result = Blend(RBlendMode.Saturation, Cb, (100, 100, 100, 255));

            Assert.Equal(0, Sat(result), 2);
        }

        [Fact]
        public void PartiallyTransparentSource_MixesWithTheBackdrop()
        {
            // Half-opaque multiply source over an opaque backdrop: 50% backdrop + 50% multiplied result.
            var result = Blend(RBlendMode.Multiply, Cb, (64, 76, 89, 128));

            AssertColor(result, 153, 82, 44, tolerance: 3);
            Assert.Equal(255, result[3]);
        }

        [Fact]
        public void TransparentBackdrop_ShowsTheSourceUnchanged()
        {
            var result = Blend(RBlendMode.Multiply, (0, 0, 0, 0), (100, 50, 25, 200));

            AssertColor(result, 100, 50, 25, 200, tolerance: 1);
        }

        [Fact]
        public void FullyTransparentSource_LeavesTheBackdropAlone()
        {
            var result = Blend(RBlendMode.Difference, Cb, (0, 0, 0, 0));

            Assert.Equal(new byte[] { 204, 102, 51, 255 }, result);
        }
    }
}

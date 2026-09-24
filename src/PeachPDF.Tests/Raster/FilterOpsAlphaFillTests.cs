using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Raster;
using PeachPDF.Raster.Filters;

namespace PeachPDF.Tests.Raster
{
    /// <summary>The colour-clearing and solid-fill kernels: each fast form must produce the same bytes as its per-pixel reference.</summary>
    public class FilterOpsAlphaFillTests
    {
        private static RasterSurface Noise(int pixels, int seed)
        {
            var surface = new RasterSurface(pixels, 1, 0, 0, 1, 1);
            new Random(seed).NextBytes(surface.Pixels);
            return surface;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(15)]
        [InlineData(16)]
        [InlineData(37)]
        [InlineData(70)]
        public void ZeroColor_MatchesTheScalarReference_AtEverySizeAndTail(int pixels)
        {
            using var actual = Noise(pixels, pixels);
            using var expected = FilterOps.Clone(actual);

            FilterOps.ZeroColor(actual);
            FilterOps.ZeroColorScalar(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(expected.Pixels));

            Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());
        }

        [Fact]
        public void ZeroColor_KeepsAlphaAndClearsColour()
        {
            using var surface = new RasterSurface(1, 1, 0, 0, 1, 1);
            surface.Pixels[0] = 10;
            surface.Pixels[1] = 20;
            surface.Pixels[2] = 30;
            surface.Pixels[3] = 200;

            FilterOps.ZeroColor(surface);

            Assert.Equal([0, 0, 0, 200], surface.Pixels.ToArray());
        }

        [Theory]
        [InlineData(1, 255, 0, 0, 255, 1.0)]
        [InlineData(7, 12, 200, 99, 128, 0.5)]
        [InlineData(33, 255, 255, 255, 0, 1.0)]
        [InlineData(64, 1, 2, 3, 77, 0.3)]
        public void Fill_MatchesTheScalarReference(int pixels, int r, int g, int b, int a, double opacity)
        {
            using var actual = Noise(pixels, 1);
            using var expected = Noise(pixels, 2);
            var color = RColor.FromArgb(a, r, g, b);

            FilterOps.Fill(actual, color, opacity);
            FilterOps.FillScalar(expected, color, opacity);

            Assert.Equal(expected.Pixels.ToArray(), actual.Pixels.ToArray());
        }

        [Fact]
        public void Fill_PremultipliesTheColour()
        {
            using var surface = new RasterSurface(2, 1, 0, 0, 1, 1);

            FilterOps.Fill(surface, RColor.FromArgb(255, 200, 100, 50), 0.5);

            Assert.Equal([100, 50, 25, 128, 100, 50, 25, 128], surface.Pixels.ToArray());
        }
    }
}

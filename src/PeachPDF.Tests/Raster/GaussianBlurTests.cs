using PeachPDF.Raster;
using PeachPDF.Raster.Filters;

namespace PeachPDF.Tests.Raster
{
    public class GaussianBlurTests
    {
        private static RasterSurface Surface(int size)
        {
            var s = new RasterSurface(size, size, 0, 0, 1, 1);
            return s;
        }

        private static void FillRect(RasterSurface s, int l, int t, int r, int b, byte value = 255)
        {
            for (var y = t; y < b; y++)
            {
                for (var x = l; x < r; x++)
                {
                    var p = (y * s.Width + x) * 4;
                    s.Pixels[p] = value;
                    s.Pixels[p + 1] = value;
                    s.Pixels[p + 2] = value;
                    s.Pixels[p + 3] = value;
                }
            }
        }

        private static long AlphaSum(RasterSurface s)
        {
            long sum = 0;
            for (var i = 3; i < s.Pixels.Length; i += 4)
                sum += s.Pixels[i];
            return sum;
        }

        private static int Alpha(RasterSurface s, int x, int y) => s.Pixels[(y * s.Width + x) * 4 + 3];

        [Theory]
        [InlineData(1.0)]
        [InlineData(2.0)]
        [InlineData(3.5)]
        [InlineData(6.0)]
        public void Blur_ConservesTheTotalAlpha_WhenTheImageHasRoomToSpread(double sigma)
        {
            var s = Surface(120);
            FillRect(s, 50, 50, 70, 70);
            var before = AlphaSum(s);

            GaussianBlur.Apply(s, sigma, sigma);

            // Integer rounding loses a little on every pass; well under half a percent overall.
            Assert.InRange((double)AlphaSum(s) / before, 0.99, 1.01);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(4.0)]
        public void Blur_OfASymmetricShape_IsSymmetric(double sigma)
        {
            var s = Surface(61);
            FillRect(s, 25, 25, 36, 36);

            GaussianBlur.Apply(s, sigma, sigma);

            for (var d = 1; d < 15; d++)
            {
                Assert.InRange(Alpha(s, 30 - d, 30) - Alpha(s, 30 + d, 30), -2, 2);
                Assert.InRange(Alpha(s, 30, 30 - d) - Alpha(s, 30, 30 + d), -2, 2);
                Assert.InRange(Alpha(s, 30 + d, 30) - Alpha(s, 30, 30 + d), -2, 2);
            }
        }

        [Fact]
        public void Blur_SoftensAnEdge_MonotonicallyAcrossIt()
        {
            var s = Surface(80);
            FillRect(s, 0, 0, 40, 80);

            GaussianBlur.Apply(s, 5, 5);

            var previous = 256;
            for (var x = 20; x < 60; x++)
            {
                var a = Alpha(s, x, 40);
                Assert.True(a <= previous, $"alpha rose at x={x}: {previous} -> {a}");
                previous = a;
            }

            Assert.InRange(Alpha(s, 10, 40), 253, 255);
            Assert.Equal(0, Alpha(s, 70, 40));
            Assert.InRange(Alpha(s, 39, 40), 120, 145);
        }

        [Fact]
        public void BoxApproximationAndExactKernel_AgreeAtTheThreshold()
        {
            // Just above and just below the switch-over the two methods must produce nearly the same picture.
            var below = Surface(60);
            var above = Surface(60);
            FillRect(below, 25, 25, 35, 35);
            FillRect(above, 25, 25, 35, 35);

            GaussianBlur.Apply(below, 1.98, 1.98);
            GaussianBlur.Apply(above, 2.02, 2.02);

            var maxDifference = 0;
            for (var i = 3; i < below.Pixels.Length; i += 4)
                maxDifference = Math.Max(maxDifference, Math.Abs(below.Pixels[i] - above.Pixels[i]));

            Assert.True(maxDifference <= 12, $"max difference {maxDifference}");
        }

        [Fact]
        public void ZeroSigma_IsANoOp_AndOneAxisBlursOnlyThatAxis()
        {
            var s = Surface(40);
            FillRect(s, 10, 10, 30, 30);
            var copy = s.Pixels.ToArray();

            GaussianBlur.Apply(s, 0, 0);
            Assert.Equal(copy, s.Pixels.ToArray());

            GaussianBlur.Apply(s, 3, 0);
            Assert.NotEqual(0, Alpha(s, 8, 20));
            Assert.Equal(0, Alpha(s, 20, 8));
        }

        [Fact]
        public void Blur_IsDeterministic()
        {
            var a = Surface(50);
            var b = Surface(50);
            FillRect(a, 10, 10, 40, 40);
            FillRect(b, 10, 10, 40, 40);

            GaussianBlur.Apply(a, 3.7, 2.2);
            GaussianBlur.Apply(b, 3.7, 2.2);

            Assert.Equal(a.Pixels.ToArray(), b.Pixels.ToArray());
        }

        private static byte[] RandomPremultipliedPixels(Random random, int width, int height)
        {
            var bytes = new byte[width * height * 4];
            for (var i = 0; i < bytes.Length; i += 4)
            {
                var a = random.Next(256);
                bytes[i] = (byte)random.Next(a + 1);
                bytes[i + 1] = (byte)random.Next(a + 1);
                bytes[i + 2] = (byte)random.Next(a + 1);
                bytes[i + 3] = (byte)a;
            }

            return bytes;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BoxBlur_VectorFormMatchesTheScalarReference_ForEverySizeAndOffset(bool horizontal)
        {
            var random = new Random(horizontal ? 11 : 12);
            foreach (var (w, h) in new[] { (1, 1), (5, 3), (16, 9), (37, 29), (64, 5), (3, 60) })
            {
                var source = RandomPremultipliedPixels(random, w, h);
                for (var size = 1; size <= 25; size += size < 6 ? 1 : 4)
                {
                    for (var before = 0; before < size; before++)
                    {
                        var expected = new byte[source.Length];
                        var actual = new byte[source.Length];
                        GaussianBlur.BoxScalar(source, expected, w, h, size, before, horizontal);
                        GaussianBlur.BoxVector(source, actual, w, h, size, before, horizontal);

                        Assert.True(expected.AsSpan().SequenceEqual(actual), $"{w}x{h} size {size} before {before} horizontal {horizontal}");
                    }
                }
            }
        }

        [Theory]
        [InlineData(0.5, true)]
        [InlineData(1.0, false)]
        [InlineData(1.9, true)]
        [InlineData(1.4, false)]
        public void ExactKernel_VectorFormMatchesTheScalarReference(double sigma, bool horizontal)
        {
            var random = new Random(77);
            var (radius, weights) = GaussianBlur.KernelWeights(sigma);
            foreach (var (w, h) in new[] { (1, 1), (4, 4), (17, 9), (40, 31) })
            {
                var pixels = RandomPremultipliedPixels(random, w, h);
                var expected = (byte[])pixels.Clone();
                var actual = (byte[])pixels.Clone();

                GaussianBlur.KernelScalar(expected, new byte[pixels.Length], w, h, radius, weights, horizontal);
                GaussianBlur.KernelVector(actual, new byte[pixels.Length], w, h, radius, weights, horizontal);

                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{w}x{h} sigma {sigma}");
            }
        }
    }
}

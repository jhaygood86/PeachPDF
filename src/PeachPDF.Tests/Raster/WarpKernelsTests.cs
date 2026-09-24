using PeachPDF.Raster;
using System.Numerics;
using System.Runtime.Intrinsics;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// The per-pixel kernels behind <see cref="Warp"/>: each has a scalar reference and a <see cref="Vector128"/> implementation that must
    /// agree bit for bit, so a PDF is the same on every CPU.
    /// </summary>
    public class WarpKernelsTests
    {
        private static byte[] RandomPremultiplied(Random random, int pixels)
        {
            var bytes = new byte[pixels * 4];
            for (var i = 0; i < pixels; i++)
            {
                var a = random.Next(5) switch { 0 => 0, 1 => 255, _ => random.Next(256) };
                bytes[i * 4] = (byte)random.Next(a + 1);
                bytes[i * 4 + 1] = (byte)random.Next(a + 1);
                bytes[i * 4 + 2] = (byte)random.Next(a + 1);
                bytes[i * 4 + 3] = (byte)a;
            }

            return bytes;
        }

        // ---- BilinearTexels --------------------------------------------------------------------------------------------

        [Fact]
        public void BilinearTexels_VectorMatchesScalar_InsideAtTheEdgesAndBeyond()
        {
            var random = new Random(4711);
            for (var trial = 0; trial < 4000; trial++)
            {
                var width = random.Next(1, 10);
                var height = random.Next(1, 10);
                var pixels = RandomPremultiplied(random, width * height);
                var x0 = random.Next(-3, width + 3);
                var y0 = random.Next(-3, height + 3);
                var tx = random.Next(0, 257);
                var ty = random.Next(0, 257);

                var (r, g, b, a) = PixelKernels.BilinearTexelsScalar(pixels, width, height, x0, y0, tx, ty);
                var vector = PixelKernels.BilinearTexels(pixels, width, height, x0, y0, tx, ty);

                Assert.Equal((r, g, b, a), (vector.GetElement(0), vector.GetElement(1), vector.GetElement(2), vector.GetElement(3)));
            }
        }

        [Fact]
        public void BilinearTexels_Vector128_MatchesScalar_WhenHardwareAccelerated()
        {
            if (!Vector128.IsHardwareAccelerated)
                return;

            var random = new Random(99);
            for (var trial = 0; trial < 2000; trial++)
            {
                var width = random.Next(1, 8);
                var height = random.Next(1, 8);
                var pixels = RandomPremultiplied(random, width * height);
                var x0 = random.Next(-2, width + 2);
                var y0 = random.Next(-2, height + 2);
                var tx = random.Next(0, 257);
                var ty = random.Next(0, 257);

                var (r, g, b, a) = PixelKernels.BilinearTexelsScalar(pixels, width, height, x0, y0, tx, ty);
                var vector = PixelKernels.BilinearTexelsVector128(pixels, width, height, x0, y0, tx, ty);

                Assert.Equal(new[] { r, g, b, a }, new[] { vector.GetElement(0), vector.GetElement(1), vector.GetElement(2), vector.GetElement(3) });
            }
        }

        [Fact]
        public void BilinearTexels_AtAWholeTexel_ReturnsThatTexel_AndBeyondTheImageIsTransparent()
        {
            byte[] pixels = [10, 20, 30, 40, 50, 60, 70, 80];

            var inside = PixelKernels.BilinearTexels(pixels, 2, 1, 1, 0, 0, 0);
            var outside = PixelKernels.BilinearTexels(pixels, 2, 1, 5, 5, 128, 128);

            Assert.Equal(new[] { 50, 60, 70, 80 }, new[] { inside.GetElement(0), inside.GetElement(1), inside.GetElement(2), inside.GetElement(3) });
            Assert.Equal(Vector128<int>.Zero, outside);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(9)]
        [InlineData(16)]
        public void StoreAveraged_RoundsToNearest(int divisor)
        {
            var sum = Vector128.Create(255 * divisor, divisor + divisor / 2, divisor / 2, 0);
            var destination = new byte[4];

            PixelKernels.StoreAveraged(sum, divisor, destination);

            Assert.Equal(255, destination[0]);
            Assert.Equal(divisor == 1 ? 1 : (divisor + divisor / 2 + divisor / 2) / divisor, destination[1]);
            Assert.Equal(divisor == 1 ? 0 : (divisor / 2 + divisor / 2) / divisor, destination[2]);
            Assert.Equal(0, destination[3]);
        }

        // ---- DepthTestRow ----------------------------------------------------------------------------------------------

        private static (float[] Depth, byte[] Coverage) RunDepthTest(bool vector, float[] depth, byte[] coverage, byte[] samples, float z0, float zs, float epsilon)
        {
            var d = (float[])depth.Clone();
            var c = (byte[])coverage.Clone();
            if (vector)
                PixelKernels.DepthTestRow(d, c, samples, z0, zs, epsilon);
            else
                PixelKernels.DepthTestRowScalar(d, c, samples, z0, zs, epsilon);
            return (d, c);
        }

        [Fact]
        public void DepthTestRow_VectorMatchesScalar_ForEveryLengthAndPattern()
        {
            var random = new Random(31337);
            for (var length = 0; length <= 37; length++)
            {
                for (var trial = 0; trial < 60; trial++)
                {
                    var depth = new float[length];
                    var coverage = new byte[length];
                    for (var i = 0; i < length; i++)
                    {
                        depth[i] = random.Next(6) switch { 0 => float.NegativeInfinity, 1 => float.PositiveInfinity, 2 => float.NaN, _ => random.Next(-50, 50) + (float)random.NextDouble() };
                        coverage[i] = random.Next(3) == 0 ? (byte)0 : (byte)255;
                    }

                    var samples = RandomPremultiplied(random, length);
                    var z0 = random.Next(8) == 0 ? float.NaN : random.Next(-40, 40) + (float)random.NextDouble();
                    var zs = (float)(random.NextDouble() - 0.5) * 3;

                    var scalar = RunDepthTest(false, depth, coverage, samples, z0, zs, 0.01f);
                    var vector = RunDepthTest(true, depth, coverage, samples, z0, zs, 0.01f);

                    Assert.Equal(scalar.Coverage, vector.Coverage);
                    // NaN and infinities compare unequal or equal in awkward ways: compare the bit patterns.
                    Assert.Equal(scalar.Depth.Select(BitConverter.SingleToInt32Bits), vector.Depth.Select(BitConverter.SingleToInt32Bits));
                }
            }
        }

        [Fact]
        public void DepthTestRow_Vector128_MatchesScalar_OnAWholeRow()
        {
            if (!Vector128.IsHardwareAccelerated)
                return;

            var random = new Random(5);
            const int length = 64;
            var depth = Enumerable.Range(0, length).Select(_ => (float)random.Next(-20, 20)).ToArray();
            var coverage = Enumerable.Repeat((byte)255, length).ToArray();
            var samples = RandomPremultiplied(random, length);

            var scalar = RunDepthTest(false, depth, coverage, samples, -3.5f, 0.31f, 0.01f);
            var d = (float[])depth.Clone();
            var c = (byte[])coverage.Clone();
            var done = PixelKernels.DepthTestRowVector128(d, c, samples, -3.5f, 0.31f, 0.01f);

            Assert.Equal(length, done);
            Assert.Equal(scalar.Coverage, c);
            Assert.Equal(scalar.Depth, d);
        }

        [Fact]
        public void DepthTestRow_KeepsWhatIsInFront_AndCullsWhatIsBehind()
        {
            float[] depth = [5, 5, 5, 5];
            byte[] coverage = [255, 255, 255, 255];
            byte[] samples = new byte[16];
            for (var i = 3; i < 16; i += 4)
                samples[i] = 255;

            // Depth along the row: 4, 5, 6, 7. Against a held depth of 5 the first is behind; the second ties (within epsilon); the rest are in front.
            PixelKernels.DepthTestRow(depth, coverage, samples, 4, 1, 0.01f);

            Assert.Equal(new byte[] { 0, 255, 255, 255 }, coverage);
            Assert.Equal(new float[] { 5, 5, 6, 7 }, depth);
        }

        [Fact]
        public void DepthTestRow_ATranslucentPixel_IsDrawnButDoesNotHideWhatIsBehindIt()
        {
            float[] depth = [float.NegativeInfinity];
            byte[] coverage = [255];
            byte[] samples = [10, 10, 10, 128];

            PixelKernels.DepthTestRow(depth, coverage, samples, 3, 0, 0.01f);

            Assert.Equal(255, coverage[0]);
            Assert.Equal(float.NegativeInfinity, depth[0]);
        }

        [Fact]
        public void DepthTestRow_AFullyTransparentPixel_IsCulled_AndAnUncoveredOneStaysCulled()
        {
            float[] depth = [float.NegativeInfinity, float.NegativeInfinity];
            byte[] coverage = [255, 0];
            byte[] samples = [0, 0, 0, 0, 9, 9, 9, 255];

            PixelKernels.DepthTestRow(depth, coverage, samples, 3, 0, 0.01f);

            Assert.Equal(new byte[] { 0, 0 }, coverage);
        }

        [Fact]
        public void DepthTestRow_RejectsBuffersThatAreTooShort()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PixelKernels.DepthTestRow(new float[1], new byte[2], new byte[8], 0, 0, 0.01f));
            Assert.Throws<ArgumentOutOfRangeException>(() => PixelKernels.DepthTestRow(new float[2], new byte[2], new byte[4], 0, 0, 0.01f));
        }

        // ---- TryProjectRectangleBounds ---------------------------------------------------------------------------------

        [Fact]
        public void TryProjectRectangleBounds_IsTheBoundingBoxOfTheProjectedPolygon()
        {
            var random = new Random(2024);
            for (var trial = 0; trial < 500; trial++)
            {
                var perspective = Matrix4x4.Identity;
                perspective.M34 = -1f / random.Next(60, 400);
                var m = Matrix4x4.CreateTranslation(-50, -40, 0) * Matrix4x4.CreateRotationY((float)(random.NextDouble() * 3 - 1.5)) *
                        Matrix4x4.CreateRotationX((float)(random.NextDouble() * 2 - 1)) * Matrix4x4.CreateTranslation(0, 0, random.Next(-100, 200)) *
                        perspective * Matrix4x4.CreateTranslation(60, 50, 0);
                var h = Homography.FromMatrix4(m);

                var polygon = h.ProjectRectangle(0, 0, 100, 80);
                var found = h.TryProjectRectangleBounds(0, 0, 100, 80, out var minX, out var minY, out var maxX, out var maxY);

                Assert.Equal(polygon.Count >= 3, found);
                if (!found)
                    continue;

                Assert.Equal(polygon.Min(p => p.X), minX, 9);
                Assert.Equal(polygon.Min(p => p.Y), minY, 9);
                Assert.Equal(polygon.Max(p => p.X), maxX, 9);
                Assert.Equal(polygon.Max(p => p.Y), maxY, 9);
            }
        }

        [Fact]
        public void TryProjectRectangleBounds_WhollyBehindTheViewer_IsFalse()
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 100f;
            var h = Homography.FromMatrix4(Matrix4x4.CreateTranslation(0, 0, 500) * perspective);

            Assert.False(h.TryProjectRectangleBounds(0, 0, 50, 50, out _, out _, out _, out _));
        }
    }
}

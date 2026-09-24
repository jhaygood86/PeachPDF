using PeachPDF.Raster;
using PeachPDF.Tests.TestSupport;
using System.Numerics;

namespace PeachPDF.Tests.Raster
{
    /// <summary><see cref="Warp.Composite"/>: one plane of a 3D rendering context drawn over what is there, through a depth test.</summary>
    public class WarpCompositeTests
    {
        private static RasterSurface Solid(int width, int height, byte r, byte g, byte b, byte a = 255)
        {
            var surface = new RasterSurface(width, height, 0, 0, 1, 1);
            var pixels = surface.Pixels;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                // Premultiplied.
                pixels[i] = (byte)(r * a / 255);
                pixels[i + 1] = (byte)(g * a / 255);
                pixels[i + 2] = (byte)(b * a / 255);
                pixels[i + 3] = a;
            }

            return surface;
        }

        private static byte[] Pixel(RasterSurface s, int x, int y) => s.Pixels.Slice((y * s.Width + x) * 4, 4).ToArray();

        /// <summary>The map and depth of a plane pushed <paramref name="z"/> towards the viewer (with no perspective, so it only changes its depth).</summary>
        private static (Homography Map, DepthPlane Depth) AtDepth(float z)
        {
            var m = Matrix4x4.CreateTranslation(0, 0, z);
            return (Homography.FromMatrix4(m), DepthPlane.FromMatrix4(m));
        }

        [Fact]
        public void TheNearerPlaneWins_InEitherOrder()
        {
            foreach (var nearFirst in new[] { true, false })
            {
                using var near = Solid(8, 8, 255, 0, 0);
                using var far = Solid(8, 8, 0, 0, 255);
                using var destination = new RasterSurface(8, 8, 0, 0, 1, 1);
                using var depth = new DepthBuffer(8, 8);
                var (nearMap, nearDepth) = AtDepth(10);
                var (farMap, farDepth) = AtDepth(0);

                if (nearFirst)
                {
                    Warp.Composite(near, destination, nearMap, nearDepth, depth);
                    Warp.Composite(far, destination, farMap, farDepth, depth);
                }
                else
                {
                    Warp.Composite(far, destination, farMap, farDepth, depth);
                    Warp.Composite(near, destination, nearMap, nearDepth, depth);
                }

                Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(destination, 4, 4));
            }
        }

        [Fact]
        public void ACoplanarPlaneDrawnLater_IsOnTop()
        {
            using var first = Solid(8, 8, 255, 0, 0);
            using var second = Solid(8, 8, 0, 255, 0);
            using var destination = new RasterSurface(8, 8, 0, 0, 1, 1);
            using var depth = new DepthBuffer(8, 8);
            var (map, plane) = AtDepth(0);

            Warp.Composite(first, destination, map, plane, depth);
            Warp.Composite(second, destination, map, plane, depth);

            Assert.Equal(new byte[] { 0, 255, 0, 255 }, Pixel(destination, 3, 3));
        }

        [Fact]
        public void PlanesThatCross_ShowTheNearerOneOnEachSide()
        {
            // Two 40-wide planes hinged on the centre line of a 40-wide picture, tilted opposite ways: each is nearer on one side.
            using var left = Solid(40, 20, 255, 0, 0);
            using var right = Solid(40, 20, 0, 0, 255);
            using var destination = new RasterSurface(40, 20, 0, 0, 1, 1);
            using var depth = new DepthBuffer(40, 20);

            Matrix4x4 Tilt(float angle) => Matrix4x4.CreateTranslation(-20, 0, 0) * Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateTranslation(20, 0, 0);
            var a = Tilt(0.7f);
            var b = Tilt(-0.7f);

            Warp.Composite(left, destination, Homography.FromMatrix4(a), DepthPlane.FromMatrix4(a), depth);
            Warp.Composite(right, destination, Homography.FromMatrix4(b), DepthPlane.FromMatrix4(b), depth);

            // rotateY with a positive angle takes +x away from the viewer: red is nearer on the left, blue on the right.
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(destination, 12, 10));
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(destination, 27, 10));
        }

        [Fact]
        public void ATranslucentPlane_BlendsOverWhatWasDrawnBefore_AndDoesNotRecordItsDepth()
        {
            using var back = Solid(4, 4, 255, 0, 0);
            using var front = Solid(4, 4, 0, 0, 255, 128);
            using var destination = new RasterSurface(4, 4, 0, 0, 1, 1);
            using var depth = new DepthBuffer(4, 4);
            var (backMap, backDepth) = AtDepth(0);
            var (frontMap, frontDepth) = AtDepth(10);

            Warp.Composite(back, destination, backMap, backDepth, depth);
            Warp.Composite(front, destination, frontMap, frontDepth, depth);

            var p = Pixel(destination, 2, 2);
            Assert.Equal(255, p[3]);
            Assert.InRange((int)p[0], 125, 129);
            Assert.InRange((int)p[2], 126, 130);
            Assert.Equal(0, p[1]);
            // The back plane's depth is what is still recorded.
            Assert.Equal(0f, depth.Row(2)[2], 3);
        }

        [Fact]
        public void APlaneBehindTheViewer_IsNotDrawn()
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 10f;
            var m = Matrix4x4.CreateTranslation(0, 0, 30) * perspective;
            using var source = Solid(8, 8, 255, 255, 255);
            using var destination = new RasterSurface(8, 8, 0, 0, 1, 1);
            using var depth = new DepthBuffer(8, 8);

            Warp.Composite(source, destination, Homography.FromMatrix4(m), DepthPlane.FromMatrix4(m), depth);

            Assert.All(destination.Pixels.ToArray(), b => Assert.Equal(0, b));
        }

        [Fact]
        public void ASingularMap_DrawsNothing()
        {
            using var source = Solid(4, 4, 1, 1, 1);
            using var destination = new RasterSurface(4, 4, 0, 0, 1, 1);
            using var depth = new DepthBuffer(4, 4);

            Warp.Composite(source, destination, new Homography(1, 2, 0, 2, 4, 0, 0, 0, 1), DepthPlane.FromMatrix4(Matrix4x4.Identity), depth);

            Assert.All(destination.Pixels.ToArray(), b => Assert.Equal(0, b));
        }

        [Fact]
        public void OnAnEmptyDestination_ItDrawsWhatApplyDraws()
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 90f;
            var m = Matrix4x4.CreateTranslation(-20, -15, 0) * Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateRotationX(0.3f) * perspective * Matrix4x4.CreateTranslation(45, 40, 0);
            var random = new Random(8);
            using var source = new RasterSurface(40, 30, 0, 0, 1, 1);
            for (var i = 0; i < 40 * 30; i++)
            {
                var a = random.Next(256);
                source.Pixels[i * 4] = (byte)random.Next(a + 1);
                source.Pixels[i * 4 + 1] = (byte)random.Next(a + 1);
                source.Pixels[i * 4 + 2] = (byte)random.Next(a + 1);
                source.Pixels[i * 4 + 3] = (byte)a;
            }

            using var applied = new RasterSurface(100, 90, 0, 0, 1, 1);
            using var composited = new RasterSurface(100, 90, 0, 0, 1, 1);
            using var depth = new DepthBuffer(100, 90);

            Warp.Apply(source, applied, Homography.FromMatrix4(m));
            Warp.Composite(source, composited, Homography.FromMatrix4(m), DepthPlane.FromMatrix4(m), depth);

            Assert.Equal(applied.Pixels.ToArray(), composited.Pixels.ToArray());
            Assert.Contains(composited.Pixels.ToArray(), b => b != 0);
        }

        [Theory]
        [InlineData(64)]
        [InlineData(1100)]
        public void Composite_AllocatesNothingInSteadyState(int width)
        {
            var m = Matrix4x4.CreateTranslation(-20, -10, 0) * Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(30, 12, 0);
            var map = Homography.FromMatrix4(m);
            var plane = DepthPlane.FromMatrix4(m);
            using var source = Solid(40, 20, 200, 100, 50);
            using var destination = new RasterSurface(width, 24, 0, 0, 1, 1);
            using var depth = new DepthBuffer(width, 24);

            var bytes = AllocationProbe.Bytes(() => Warp.Composite(source, destination, map, plane, depth), 20);

            Assert.True(bytes < 4096, $"allocated {bytes} bytes over 20 calls");
        }

        [Fact]
        public void ADepthBuffer_StartsAtNegativeInfinity_AndRefusesUseAfterDisposal()
        {
            var depth = new DepthBuffer(3, 2);

            Assert.All(depth.Row(1).ToArray(), d => Assert.Equal(float.NegativeInfinity, d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DepthBuffer(0, 1));

            depth.Dispose();
            Assert.Throws<ObjectDisposedException>(() => depth.Row(0));
            depth.Dispose();
        }

        [Fact]
        public void ADepthPlane_ReportsItsDepth_AndNaNBehindTheViewer()
        {
            var perspective = Matrix4x4.Identity;
            perspective.M34 = -1f / 100f;

            var plane = DepthPlane.FromMatrix4(Matrix4x4.CreateTranslation(0, 0, 50) * perspective);
            var behind = DepthPlane.FromMatrix4(Matrix4x4.CreateTranslation(0, 0, 150) * perspective);

            // z over 1 - z/d: 50 / 0.5.
            Assert.Equal(100, plane.At(3, 4), 3);
            Assert.True(double.IsNaN(behind.At(3, 4)));
        }
    }
}

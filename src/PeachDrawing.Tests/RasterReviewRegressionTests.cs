using PeachDrawing.Core;
using PeachDrawing;
using System.Numerics;
using System.Text.RegularExpressions;

namespace PeachDrawing.Tests
{
    /// <summary>Regressions for defects a review of the raster backend found: each pins the behaviour that was wrong.</summary>
    public class RasterReviewRegressionTests
    {
        private static readonly RasterRenderContext Adapter = new();

        private static RasterCanvas NewGraphics(int width, int height) =>
            new(Adapter, new RasterSurface(width, height, 0, 0, 1, 1), 1);

        private static byte[] Pixel(RasterCanvas g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private struct ArraySink(byte[] buffer, int width) : ICoverageSink
        {
            public void Span(int y, int x0, ReadOnlySpan<byte> coverage) => coverage.CopyTo(buffer.AsSpan(y * width + x0));
        }

        private static byte[] StrokeToCoverage(FlatPath path, StrokeStyle style, int width, int height)
        {
            var polygons = new PolygonSet();
            Stroker.Stroke(path, style, Affine.Identity, polygons);
            polygons.NormalizeWinding();

            var buffer = new byte[width * height];
            var sink = new ArraySink(buffer, width);
            ScanlineRasterizer.Fill(polygons, false, new IntRect(0, 0, width, height), ref sink);
            return buffer;
        }


        [Fact]
        public void ConicGradient_WithANegativeStartAngle_WrapsTheEarlierAnglesToTheEndOfTheTurn()
        {
            // conic-gradient(from -20deg, red, blue): the turn starts at 340 degrees.
            var start = -20 * Math.PI / 180;
            var g = NewGraphics(40, 40);
            var brush = g.GetConicGradientBrush(new PaintPoint(20, 20), 20,
                [PaintColor.FromArgb(255, 255, 0, 0), PaintColor.FromArgb(255, 0, 0, 255)], [start, start + 2 * Math.PI]);

            g.DrawRectangle(brush, 0, 0, 40, 40);

            static (int, int) At(double degrees) =>
                (20 + (int)Math.Round(15 * Math.Sin(degrees * Math.PI / 180)), 20 - (int)Math.Round(15 * Math.Cos(degrees * Math.PI / 180)));

            var (rx, ry) = At(350);
            var (bx, by) = At(330);
            Assert.True(Pixel(g, rx, ry)[0] > 200, string.Join(',', Pixel(g, rx, ry)) + $" at {rx},{ry}");
            Assert.True(Pixel(g, bx, by)[2] > 200, string.Join(',', Pixel(g, bx, by)) + $" at {bx},{by}");
        }

        [Fact]
        public void DashEndingExactlyOnAVertex_ContinuesTheNextLegWithTheFollowingDash()
        {
            var path = new FlatPath();
            path.AddContour([(2, 2), (12, 2), (12, 12)], closed: false);

            var cov = StrokeToCoverage(path, new StrokeStyle(2, StrokeCap.Butt, StrokeJoin.Miter, 10, [5, 5], 0), 16, 16);

            // Horizontal leg: on for x 2..7, off for 7..12. Vertical leg: on for y 2..7 (the dash after the gap), off for 7..12.
            Assert.Equal(255, cov[2 * 16 + 4]);
            Assert.Equal(0, cov[2 * 16 + 9]);
            Assert.Equal(255, cov[4 * 16 + 12]);
            Assert.Equal(0, cov[9 * 16 + 12]);
        }

        [Fact]
        public void MinifiedBitmap_CountsTheTrailingPixelsThatDoNotFillAWholeBlock()
        {
            // 5 x 1: only the last pixel is opaque. Each device pixel covers 2 bitmap pixels, so device pixel 2 covers
            // bitmap pixel 4 (and the missing 5th) and must see it.
            var pixels = new byte[5 * 4];
            pixels[16] = 255;
            pixels[19] = 255;
            var bitmap = new Bitmap(5, 1, pixels);

            var paint = new BitmapPaint(bitmap, Affine.Scale(2, 2), smooth: true);
            var destination = new byte[4];
            paint.FillSpan(2, 0, 1, destination);

            Assert.True(destination[3] > 0);
        }



        [Fact]
        public void NestedRasterEffect_IsAnExactCopyOfItsBitmap()
        {
            var outer = NewGraphics(8, 8);
            var inner = new RasterSurface(4, 4, 0, 0, 1, 1);
            for (var i = 0; i < 16; i++)
            {
                inner.Pixels[i * 4] = (byte)(i * 15);
                inner.Pixels[i * 4 + 3] = 255;
            }

            outer.DrawRaster(inner);

            for (var i = 0; i < 16; i++)
                Assert.Equal(i * 15, Pixel(outer, i % 4, i / 4)[0]);
        }

        [Fact]
        public void TileCreation_HonoursTheConfiguredPixelBudget()
        {
            var adapter = new RasterRenderContext { MaxRasterPixels = 1000 };
            var g = new RasterCanvas(adapter, new RasterSurface(10, 10, 0, 0, 1, 1), 1);

            Assert.Null(g.CreateTile(100, 100));
            Assert.NotNull(g.CreateTile(10, 10));
        }
    }
}

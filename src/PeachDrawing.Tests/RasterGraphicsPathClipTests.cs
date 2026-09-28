using PeachDrawing;
using PeachDrawing.Core;

namespace PeachDrawing.Tests
{
    /// <summary>
    /// <see cref="RasterGraphicsPath.ClipToRect"/> - the Sutherland-Hodgman rectangle clip that backs the
    /// raster canvas's own <see cref="GraphicsPath.ClipToRect"/> (used by vertical-text cell clipping).
    /// </summary>
    public class RasterGraphicsPathClipTests
    {
        private static RasterGraphicsPath Square(double x, double y, double size)
        {
            var path = new RasterGraphicsPath();
            path.Start(x, y);
            path.LineTo(x + size, y);
            path.LineTo(x + size, y + size);
            path.LineTo(x, y + size);
            path.CloseFigure();
            return path;
        }

        [Fact]
        public void ClipToRect_ASquareFullyInsideTheRect_IsUnchanged()
        {
            using var square = Square(10, 10, 20);

            using var clipped = square.ClipToRect(new Rect(0, 0, 100, 100));

            var contour = Assert.Single(clipped.Flatten(0.1));
            Assert.True(contour.Closed);
            Assert.Equal(4, contour.Points.Count);
        }

        [Fact]
        public void ClipToRect_ASquareFullyOutsideTheRect_ProducesNoContours()
        {
            using var square = Square(200, 200, 20);

            using var clipped = square.ClipToRect(new Rect(0, 0, 100, 100));

            Assert.Empty(clipped.Flatten(0.1));
        }

        [Fact]
        public void ClipToRect_ASquareStraddlingOneEdge_IsCutAtThatEdge()
        {
            // x from 40 to 60, clip right edge at x = 50: every point's X must land at or below 50.
            using var square = Square(40, 10, 20);

            using var clipped = square.ClipToRect(new Rect(0, 0, 50, 100));

            var contour = Assert.Single(clipped.Flatten(0.1));
            Assert.All(contour.Points, p => Assert.True(p.X <= 50 + 1e-9));
            // The clip must have actually introduced the new edge at x = 50, not just passed the square through.
            Assert.Contains(contour.Points, p => System.Math.Abs(p.X - 50) < 1e-9);
        }

        [Fact]
        public void ClipToRect_ARectLargerThanTheClipOnEverySide_IsCutToExactlyTheClipRect()
        {
            using var square = Square(-50, -50, 200); // covers (-50,-50) to (150,150)

            using var clipped = square.ClipToRect(new Rect(0, 0, 100, 100));

            var contour = Assert.Single(clipped.Flatten(0.1));
            Assert.All(contour.Points, p => Assert.True(p.X is >= -1e-9 and <= 100 + 1e-9 && p.Y is >= -1e-9 and <= 100 + 1e-9));
            // All four clip-rect corners must appear, since the square's edges crossed all four boundaries.
            Assert.Contains(contour.Points, p => Near(p, 0, 0));
            Assert.Contains(contour.Points, p => Near(p, 100, 0));
            Assert.Contains(contour.Points, p => Near(p, 100, 100));
            Assert.Contains(contour.Points, p => Near(p, 0, 100));
        }

        [Fact]
        public void ClipToRect_PreservesFillMode()
        {
            using var square = Square(0, 0, 10);
            square.FillMode = FillMode.EvenOdd;

            using var clipped = square.ClipToRect(new Rect(0, 0, 100, 100));

            Assert.Equal(FillMode.EvenOdd, clipped.FillMode);
        }

        private static bool Near(PaintPoint p, double x, double y) =>
            System.Math.Abs(p.X - x) < 1e-9 && System.Math.Abs(p.Y - y) < 1e-9;
    }
}

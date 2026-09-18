using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.PdfSharpCoreTests.Drawing
{
    /// <summary>
    /// <see cref="RGraphicsPath.ClipToRect"/> (issue #1194) - the axis-aligned-rectangle path clip
    /// <c>FragmentPainter.Decorations.cs</c>'s <c>CollectUprightWord</c> needs to reproduce, in a
    /// <c>background-clip: text</c> clip-path union, the same per-cell clip
    /// <c>PaintUprightVerticalRun</c> already applies via <c>PushClip</c>/<c>PopClip</c> at paint time
    /// for a font with real vertical metrics. Exercised at the concrete <see cref="GraphicsPathAdapter"/>
    /// level (real production implementation, not a test double) so these tests prove the actual
    /// Sutherland-Hodgman-against-flattened-contours algorithm, not a mock's approximation of it.
    /// </summary>
    public class GraphicsPathRectClipTests
    {
        private static GraphicsPathAdapter Rectangle(double x, double y, double width, double height)
        {
            var path = new GraphicsPathAdapter();
            path.Start(x, y);
            path.LineTo(x + width, y);
            path.LineTo(x + width, y + height);
            path.LineTo(x, y + height);
            path.CloseFigure();
            return path;
        }

        private static List<RPoint> PointsOf(RGraphicsPath path) => ((GraphicsPathAdapter)path).GraphicsPath._corePath.PathPoints
            .Select(p => new RPoint(p.X, p.Y)).ToList();

        [Fact]
        public void EntirelyInsideRect_IsUnchanged()
        {
            var square = Rectangle(10, 10, 5, 5);
            var clipped = square.ClipToRect(new RRect(0, 0, 100, 100));

            var points = PointsOf(clipped);
            Assert.NotEmpty(points);
            Assert.All(points, p =>
            {
                Assert.InRange(p.X, 10, 15);
                Assert.InRange(p.Y, 10, 15);
            });
        }

        [Fact]
        public void EntirelyOutsideRect_ProducesEmptyPath()
        {
            var square = Rectangle(200, 200, 10, 10);
            var clipped = square.ClipToRect(new RRect(0, 0, 100, 100));

            Assert.Empty(PointsOf(clipped));
        }

        [Fact]
        public void StraddlingRect_IsClippedToRectBounds()
        {
            // A square centered on the clip rect's own right edge - half survives.
            var square = Rectangle(80, 40, 40, 20);
            var clipped = square.ClipToRect(new RRect(0, 0, 100, 100));

            var points = PointsOf(clipped);
            Assert.NotEmpty(points);
            Assert.All(points, p => Assert.InRange(p.X, 79.999, 100.001));
            // At least one vertex landed exactly on the clip edge - proves an intersection point was
            // actually computed, not just that the whole shape happened to already fit.
            Assert.Contains(points, p => p.X is > 99.9 and < 100.1);
        }

        [Fact]
        public void ClipRectFullyInsideSubject_ProducesTheClipRectItself()
        {
            var big = Rectangle(0, 0, 100, 100);
            var clipped = big.ClipToRect(new RRect(20, 20, 10, 10));

            var points = PointsOf(clipped);
            Assert.NotEmpty(points);
            Assert.All(points, p =>
            {
                Assert.InRange(p.X, 20, 30);
                Assert.InRange(p.Y, 20, 30);
            });

            // The clip rectangle's own four corners must all be reproduced.
            Assert.Contains(points, p => p.X == 20 && p.Y == 20);
            Assert.Contains(points, p => p.X == 30 && p.Y == 20);
            Assert.Contains(points, p => p.X == 30 && p.Y == 30);
            Assert.Contains(points, p => p.X == 20 && p.Y == 30);
        }

        [Fact]
        public void CurvedContourCrossingBoundary_FlattensAndClipsWithinRect()
        {
            // A circle-ish shape (four cubic Bézier quadrants) of radius 50 centered at (50, 50) -
            // straddles a clip rect that only covers its left half, so the curve genuinely crosses the
            // boundary and must be flattened before it can be clipped at all.
            var path = new GraphicsPathAdapter();
            const double cx = 50, cy = 50, r = 50;
            const double k = 0.5522847498; // cubic Bézier circle-approximation constant
            path.Start(cx + r, cy);
            path.AddBezierTo(cx + r, cy + r * k, cx + r * k, cy + r, cx, cy + r);
            path.AddBezierTo(cx - r * k, cy + r, cx - r, cy + r * k, cx - r, cy);
            path.AddBezierTo(cx - r, cy - r * k, cx - r * k, cy - r, cx, cy - r);
            path.AddBezierTo(cx + r * k, cy - r, cx + r, cy - r * k, cx + r, cy);
            path.CloseFigure();

            var clipped = path.ClipToRect(new RRect(0, 0, 50, 100));

            var points = PointsOf(clipped);
            Assert.NotEmpty(points);
            Assert.All(points, p => Assert.InRange(p.X, -0.001, 50.001));

            // The flattened curve's own points (not just the two straight clip-edge crossings) must
            // still be present - e.g. a point near the circle's own leftmost extent (x=0, y=50).
            Assert.Contains(points, p => p.X < 1 && p.Y is > 45 and < 55);
        }

        [Fact]
        public void MultiContourPath_ClipsEachContourIndependently()
        {
            // Two disjoint squares (as separate subpaths, mirroring how a multi-glyph outline is
            // built) - one entirely inside the clip rect, one entirely outside.
            var path = new GraphicsPathAdapter();
            path.Start(10, 10);
            path.LineTo(20, 10);
            path.LineTo(20, 20);
            path.LineTo(10, 20);
            path.CloseFigure();

            path.AddMove(500, 500);
            path.LineTo(510, 500);
            path.LineTo(510, 510);
            path.LineTo(500, 510);
            path.CloseFigure();

            var clipped = path.ClipToRect(new RRect(0, 0, 100, 100));

            var points = PointsOf(clipped);
            Assert.NotEmpty(points);
            Assert.All(points, p =>
            {
                Assert.InRange(p.X, 10, 20);
                Assert.InRange(p.Y, 10, 20);
            });
        }

        [Fact]
        public void EmptyPath_ProducesEmptyPath()
        {
            var path = new GraphicsPathAdapter();
            var clipped = path.ClipToRect(new RRect(0, 0, 100, 100));

            Assert.Empty(PointsOf(clipped));
        }

        [Fact]
        public void ZeroWidthRect_ProducesNoUsableGeometry()
        {
            var square = Rectangle(0, 0, 10, 10);
            var clipped = square.ClipToRect(new RRect(5, 0, 0, 10));

            // A zero-width clip window degenerates every clipped contour below the 3-point minimum a
            // fillable polygon needs - this must not throw, and must not fabricate area from nothing.
            Assert.Empty(PointsOf(clipped));
        }

        [Fact]
        public void MalformedPath_BezierWithNoPrecedingPoint_DoesNotThrow()
        {
            // A Bezier-type point with nothing before it to start the curve from - shouldn't be
            // reachable through the public RGraphicsPath API (every real caller's AddBezierTo follows a
            // Start/AddMove/LineTo), but CoreGraphicsPath.EnumerateFlattenedContours defends against it
            // anyway, since a clip-shape utility should degrade to "less clipped" on corrupted input
            // rather than throw mid-paint.
            var path = new CoreGraphicsPath();
            path.BezierTo(1, 1, 2, 2, 3, 3, false);

            var clipped = path.ClipToRect(0, 0, 100, 100);

            Assert.Empty(clipped.PathPoints);
        }

        [Fact]
        public void ClippedPath_ContinuingToBuildOntoIt_StartsFromItsOwnLastPoint()
        {
            // GraphicsPathAdapter.ClipToRect hands back a *new* adapter wrapping the clipped geometry -
            // its private last-point-tracking field must be seeded from that geometry's own last vertex,
            // not left at the default (0, 0), or a caller that keeps building onto the returned path
            // (e.g. a further LineTo) would silently draw a spurious segment from the origin instead of
            // continuing from where the clipped shape actually ends.
            var square = Rectangle(10, 10, 5, 5); // corners (10,10)-(15,10)-(15,15)-(10,15)
            var clipped = (GraphicsPathAdapter)square.ClipToRect(new RRect(0, 0, 100, 100)); // fully inside - unchanged

            clipped.LineTo(50, 50);

            var points = PointsOf(clipped);
            Assert.Equal(new RPoint(50, 50), points[^1]);
            Assert.Equal(new RPoint(10, 15), points[^2]);
        }

        [Fact]
        public void BezierFlattener_ZeroLengthChord_DoesNotThrowAndProducesPoints()
        {
            // A cubic Bézier whose start and end point coincide (a "loop" control-point configuration) -
            // IsFlatEnough's chord-based flatness test has no direction to measure control-point
            // deviation against in that case, so it falls back to raw distance from the shared point
            // instead of dividing by a zero chord length.
            var output = new List<XPoint>();

            BezierFlattener.Flatten(new XPoint(0, 0), new XPoint(0, 5), new XPoint(5, 5), new XPoint(0, 0), output);

            Assert.NotEmpty(output);
        }
    }
}

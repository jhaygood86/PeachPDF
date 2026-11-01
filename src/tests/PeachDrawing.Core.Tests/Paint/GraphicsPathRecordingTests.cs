using System.Numerics;

namespace PeachDrawing.Core.Tests.Paint
{
    public class GraphicsPathRecordingTests
    {
        private sealed class TestGraphicsPath : GraphicsPath
        {
            public override FillMode FillMode { get; set; }
            public override GraphicsPath ClipToRect(Rect rect) => this;
            public override void Dispose() { }
        }

        [Fact]
        public void ArcTo_ACornerArc_EndsAtTheGivenPoint()
        {
            // TopLeft's bounding box is anchored at (min(x, lastX), min(y, lastY)), so the last point and
            // the target must actually sit on the box's left/top edges for a consistent quarter-circle -
            // here that means starting at (0, 10) (the left edge) and arcing to (10, 0) (the top edge)
            // around a center at (10, 10).
            using var path = new TestGraphicsPath();
            path.Start(0, 10);
            path.ArcTo(10, 0, radiusX: 10, radiusY: 10, GraphicsPath.Corner.TopLeft);

            var contour = Assert.Single(path.Flatten(0.1));

            AssertPoint(new PaintPoint(0, 10), contour.Points[0]);
            AssertPoint(new PaintPoint(10, 0), contour.Points[^1]);
            // A quarter-circle arc must actually be subdivided, not passed through as a straight chord.
            Assert.True(contour.Points.Count > 2);
        }

        private static void AssertPoint(PaintPoint expected, PaintPoint actual, double tolerance = 1e-9)
        {
            Assert.Equal(expected.X, actual.X, tolerance);
            Assert.Equal(expected.Y, actual.Y, tolerance);
        }

        [Fact]
        public void ArcTo_TheUniformRadiusOverload_MatchesTheTwoRadiusOverload()
        {
            using var withOneRadius = new TestGraphicsPath();
            withOneRadius.Start(10, 0);
            withOneRadius.ArcTo(0, 10, size: 10, GraphicsPath.Corner.TopLeft);

            using var withTwoRadii = new TestGraphicsPath();
            withTwoRadii.Start(10, 0);
            withTwoRadii.ArcTo(0, 10, radiusX: 10, radiusY: 10, GraphicsPath.Corner.TopLeft);

            Assert.Equal(withTwoRadii.Flatten(0.1).Single().Points, withOneRadius.Flatten(0.1).Single().Points);
        }

        [Fact]
        public void AddArc_ASemicircle_EndsAtTheGivenPointAndIsSubdivided()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.AddArc(20, 0, radiusX: 10, radiusY: 10, rotationAngle: 0, isLargeArc: false, sweepClockwise: true);

            var contour = Assert.Single(path.Flatten(0.1));

            AssertPoint(new PaintPoint(0, 0), contour.Points[0]);
            AssertPoint(new PaintPoint(20, 0), contour.Points[^1]);
            Assert.True(contour.Points.Count > 2);
        }

        [Fact]
        public void AddArc_ADegenerateArc_FallsBackToAStraightLine()
        {
            // Coincident endpoints (or a zero radius) has no valid center parameterization - SVG treats
            // this as a straight line, per AddArc's own doc remarks.
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.AddArc(10, 10, radiusX: 0, radiusY: 0, rotationAngle: 0, isLargeArc: false, sweepClockwise: true);

            var contour = Assert.Single(path.Flatten(0.1));

            Assert.Equal([new PaintPoint(0, 0), new PaintPoint(10, 10)], contour.Points);
        }

        [Fact]
        public void Transform_ALine_MapsBothEndpoints()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.LineTo(10, 0);

            path.Transform(Matrix3x2.CreateTranslation(5, 5));

            var contour = Assert.Single(path.Flatten(0.1));
            Assert.Equal(new PaintPoint(5, 5), contour.Points[0]);
            Assert.Equal(new PaintPoint(15, 5), contour.Points[^1]);
        }

        [Fact]
        public void Transform_ABezierCurve_MapsItsControlPointsToo()
        {
            using var untransformed = new TestGraphicsPath();
            untransformed.Start(0, 0);
            untransformed.AddBezierTo(0, 10, 10, 10, 10, 0);

            using var translated = new TestGraphicsPath();
            translated.Start(0, 0);
            translated.AddBezierTo(0, 10, 10, 10, 10, 0);
            translated.Transform(Matrix3x2.CreateTranslation(100, 0));

            var expected = untransformed.Flatten(0.1).Single().Points.Select(p => new PaintPoint(p.X + 100, p.Y)).ToList();
            var actual = translated.Flatten(0.1).Single().Points;

            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < expected.Count; i++)
                AssertPoint(expected[i], actual[i]);
        }

        [Fact]
        public void AddPath_MergesTheOtherPathsContoursAsDisjointSubpaths()
        {
            using var a = new TestGraphicsPath();
            a.Start(0, 0);
            a.LineTo(10, 0);

            using var b = new TestGraphicsPath();
            b.Start(100, 100);
            b.LineTo(110, 100);

            a.AddPath(b);

            var contours = a.Flatten(0.1);
            Assert.Equal(2, contours.Count);
            Assert.Equal(new PaintPoint(0, 0), contours[0].Points[0]);
            Assert.Equal(new PaintPoint(100, 100), contours[1].Points[0]);
        }

        [Fact]
        public void AddPath_ALineToAfterwardsContinuesTheAddedPathsLastSubpathNotTheReceivers()
        {
            // AddPath copies the other path's subpaths in as a union, and leaves its own *last* copied
            // subpath open as the receiver's current one (it does not reset to "no current subpath") - so
            // a LineTo right after it extends that last added subpath, not the receiver's own pre-AddPath
            // subpath, and does not start a brand new one either.
            using var a = new TestGraphicsPath();
            a.Start(0, 0);
            a.LineTo(10, 0);

            using var b = new TestGraphicsPath();
            b.Start(50, 50);
            b.LineTo(60, 50);

            a.AddPath(b);
            a.LineTo(20, 0);

            var contours = a.Flatten(0.1);
            Assert.Equal(2, contours.Count);
            Assert.Equal([new PaintPoint(0, 0), new PaintPoint(10, 0)], contours[0].Points);
            Assert.Equal([new PaintPoint(50, 50), new PaintPoint(60, 50), new PaintPoint(20, 0)], contours[1].Points);
        }
    }
}

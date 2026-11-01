using PeachDrawing.Core.Geometry;

namespace PeachDrawing.Core.Tests.Geometry
{
    public class PathShapesAndMeasureTests
    {
        private sealed class TestGraphicsPath : GraphicsPath
        {
            public override FillMode FillMode { get; set; }
            public override GraphicsPath ClipToRect(Rect rect) => this;
            public override void Dispose() { }
        }

        private static TestGraphicsPath NewPath() => new();

        [Fact]
        public void Circle_HasTheAnalyticLengthAreaAndBounds()
        {
            using var path = NewPath();
            path.AddCircle(50, 40, 10);

            var m = new PathMeasure(path);

            Assert.Equal(2 * Math.PI * 10, m.Length, 1);
            Assert.Equal(Math.PI * 100, m.Area, 0);
            Assert.Equal(40, m.Bounds.X, 3);
            Assert.Equal(30, m.Bounds.Y, 3);
            Assert.Equal(20, m.Bounds.Width, 3);
            Assert.Equal(20, m.Bounds.Height, 3);
        }

        [Fact]
        public void Ellipse_BoundsAreTheRadii()
        {
            using var path = NewPath();
            path.AddEllipse(0, 0, 30, 10);

            var m = new PathMeasure(path);

            Assert.Equal(-30, m.Bounds.Left, 3);
            Assert.Equal(30, m.Bounds.Right, 3);
            Assert.Equal(-10, m.Bounds.Top, 3);
            Assert.Equal(10, m.Bounds.Bottom, 3);
            Assert.Equal(Math.PI * 30 * 10, m.Area, 1.0);
        }

        [Fact]
        public void RoundedRectangle_KeepsTheRectanglesBoundsAndLosesTheCornerArea()
        {
            using var path = NewPath();
            path.AddRoundedRectangle(new Rect(0, 0, 100, 50), 10);

            var m = new PathMeasure(path);

            Assert.Equal(new Rect(0, 0, 100, 50), m.Bounds);
            Assert.Equal(100 * 50 - (4 - Math.PI) * 100, m.Area, 0);
        }

        [Fact]
        public void RoundedRectangle_WithNoRadii_IsAPlainRectangle()
        {
            using var path = NewPath();
            path.AddRoundedRectangle(new Rect(5, 5, 20, 10), 0);

            var m = new PathMeasure(path);

            Assert.Equal(200, m.Area, 6);
            Assert.Equal(60, m.Length, 6);
        }

        [Fact]
        public void RoundedRectangle_WithElongatedCornerRadii_StaysWithinItsRectangle()
        {
            using var path = NewPath();
            path.AddRoundedRectangle(new Rect(0, 0, 100, 60), 20, 10, 5, 25, 0, 0, 12, 12);

            var m = new PathMeasure(path);

            Assert.Equal(new Rect(0, 0, 100, 60), m.Bounds);
        }

        [Fact]
        public void Pie_HasAreaOfItsSector()
        {
            using var path = NewPath();
            path.AddPie(0, 0, 10, 10, 0, Math.PI / 2);

            Assert.Equal(Math.PI * 100 / 4, new PathMeasure(path).Area, 0);
        }

        [Fact]
        public void Pie_OfAFullTurn_IsAnEllipse()
        {
            using var path = NewPath();
            path.AddPie(0, 0, 10, 10, 0, 2 * Math.PI);

            Assert.Equal(Math.PI * 100, new PathMeasure(path).Area, 0);
        }

        [Fact]
        public void Pie_OfMoreThanAHalfTurn_UsesTheLargeArc()
        {
            using var path = NewPath();
            path.AddPie(0, 0, 10, 10, 0, 1.5 * Math.PI);

            Assert.Equal(Math.PI * 100 * 0.75, new PathMeasure(path).Area, 0);
        }

        [Fact]
        public void RegularPolygon_HasTheAnalyticArea()
        {
            using var path = NewPath();
            path.AddRegularPolygon(0, 0, 10, 6);

            Assert.Equal(6 * 0.5 * 100 * Math.Sin(2 * Math.PI / 6), new PathMeasure(path).Area, 6);
        }

        [Fact]
        public void RegularPolygon_FirstVertexPointsUp()
        {
            using var path = NewPath();
            path.AddRegularPolygon(0, 0, 10, 5);

            var start = Assert.Single(path.GetCurveContours()).Start;
            Assert.Equal(0, start.X, 9);
            Assert.Equal(-10, start.Y, 9);
        }

        [Fact]
        public void Star_HasTwiceAsManyVerticesAsPoints()
        {
            using var path = NewPath();
            path.AddStar(0, 0, 10, 4, 5);

            var contour = Assert.Single(path.GetCurveContours());
            Assert.Equal(10, contour.Commands.Count + 1);
            Assert.True(contour.Closed);
        }

        [Fact]
        public void Shapes_ValidateTheirArguments()
        {
            using var path = NewPath();

            Assert.Throws<ArgumentOutOfRangeException>(() => path.AddRegularPolygon(0, 0, 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => path.AddStar(0, 0, 2, 1, 2));
            Assert.Throws<ArgumentNullException>(() => PathShapes.AddCircle(null!, 0, 0, 1));
        }

        [Fact]
        public void Polygon_OfOnePoint_AddsNothing()
        {
            using var path = NewPath();
            path.AddPolygon([new PaintPoint(1, 1)]);

            Assert.Empty(path.GetCurveContours());
        }

        [Fact]
        public void Wave_PeaksAtItsAmplitudeOnBothSides()
        {
            using var path = NewPath();
            path.AddWave(new PaintPoint(0, 0), new PaintPoint(40, 0), amplitude: 3, wavelength: 20);

            var m = new PathMeasure(path);

            Assert.Equal(0, m.Bounds.Left, 6);
            Assert.Equal(40, m.Bounds.Right, 6);
            Assert.Equal(-3, m.Bounds.Top, 3);
            Assert.Equal(3, m.Bounds.Bottom, 3);
        }

        [Fact]
        public void Wave_OnADegenerateLine_IsJustTheStartPoint()
        {
            using var path = NewPath();
            path.AddWave(new PaintPoint(2, 2), new PaintPoint(2, 2), 3, 10);

            Assert.True(new PathMeasure(path).IsEmpty);
        }

        [Fact]
        public void Bounds_OfACurve_IncludeItsBulge()
        {
            using var path = NewPath();
            path.Start(0, 0);
            path.AddBezierTo(0, 20, 30, 20, 30, 0);

            var m = new PathMeasure(path);

            // The curve peaks at 3/4 of the control height, well outside the box its end points make.
            Assert.Equal(15, m.Bounds.Bottom, 3);
            Assert.Equal(0, m.Bounds.Top, 6);
        }

        [Fact]
        public void Bounds_OfACurveWithAHorizontalOvershoot_IncludeIt()
        {
            using var path = NewPath();
            path.Start(0, 0);
            path.AddBezierTo(40, 0, -10, 10, 10, 10);

            var m = new PathMeasure(path);

            Assert.True(m.Bounds.Right > 10);
            Assert.Equal(0, m.Bounds.Left, 6);
        }

        [Fact]
        public void Length_OfALine_IsItsDistance()
        {
            using var path = NewPath();
            path.Start(0, 0);
            path.LineTo(3, 4);

            var m = new PathMeasure(path);

            Assert.Equal(5, m.Length, 9);
            Assert.False(m.IsEmpty);
            Assert.Equal(0, m.Area, 9);
        }

        [Fact]
        public void Length_DoesNotCountTheJumpBetweenSubpaths()
        {
            using var path = NewPath();
            path.Start(0, 0);
            path.LineTo(10, 0);
            path.AddMove(100, 100);
            path.LineTo(100, 105);

            Assert.Equal(15, new PathMeasure(path).Length, 9);
        }

        [Fact]
        public void Length_OfAClosedFigure_IncludesTheClosingEdge()
        {
            using var path = NewPath();
            path.Start(0, 0);
            path.LineTo(10, 0);
            path.LineTo(10, 10);
            path.CloseFigure();

            Assert.Equal(10 + 10 + Math.Sqrt(200), new PathMeasure(path).Length, 9);
        }

        [Fact]
        public void Area_ASubpathWoundTheOtherWay_IsAHole()
        {
            using var path = NewPath();
            path.AddPolygon([new PaintPoint(0, 0), new PaintPoint(10, 0), new PaintPoint(10, 10), new PaintPoint(0, 10)]);
            path.AddPolygon([new PaintPoint(2, 2), new PaintPoint(2, 8), new PaintPoint(8, 8), new PaintPoint(8, 2)]);

            Assert.Equal(100 - 36, new PathMeasure(path).Area, 9);
        }

        [Fact]
        public void PointAtLength_WalksALineAndReportsItsDirection()
        {
            using var path = NewPath();
            path.Start(0, 0);
            path.LineTo(10, 0);
            path.LineTo(10, 10);

            var m = new PathMeasure(path);

            var mid = m.PointAtLength(5);
            Assert.Equal(5, mid.X, 9);
            Assert.Equal(0, mid.Y, 9);
            Assert.Equal(0, mid.TangentDegrees, 9);

            var onSecond = m.PointAtLength(15);
            Assert.Equal(10, onSecond.X, 9);
            Assert.Equal(5, onSecond.Y, 9);
            Assert.Equal(90, onSecond.TangentDegrees, 9);
        }

        [Fact]
        public void PointAtLength_ClampsToTheEnds()
        {
            using var path = NewPath();
            path.Start(0, 0);
            path.LineTo(10, 0);

            var m = new PathMeasure(path);

            Assert.Equal(0, m.PointAtLength(-5).X, 9);
            Assert.Equal(10, m.PointAtLength(99).X, 9);
        }

        [Fact]
        public void PointAtLength_OnACircle_StaysOnTheCircle()
        {
            using var path = NewPath();
            path.AddCircle(0, 0, 10);

            var m = new PathMeasure(path);

            for (var i = 0; i <= 10; i++)
            {
                var s = m.PointAtLength(m.Length * i / 10);
                Assert.Equal(10, Math.Sqrt(s.X * s.X + s.Y * s.Y), 1);
            }
        }

        [Fact]
        public void EmptyPath_IsEmptyAndSafeToQuery()
        {
            using var path = NewPath();

            var m = new PathMeasure(path);

            Assert.True(m.IsEmpty);
            Assert.Equal(0, m.Length);
            Assert.Equal(default, m.Bounds);
            Assert.Equal(new PathSample(0, 0, 0), m.PointAtLength(3));
        }

        [Fact]
        public void PathWithOnlyAMove_IsEmptyButKeepsItsFirstPoint()
        {
            using var path = NewPath();
            path.Start(7, 8);

            var m = new PathMeasure(path);

            Assert.True(m.IsEmpty);
            Assert.Equal(new PathSample(7, 8, 0), m.PointAtLength(1));
        }

        [Fact]
        public void PathMeasure_IsASnapshot()
        {
            using var path = NewPath();
            path.Start(0, 0);
            path.LineTo(10, 0);
            var m = new PathMeasure(path);

            path.LineTo(10, 10);

            Assert.Equal(10, m.Length, 9);
        }

        [Fact]
        public void PathMeasure_RejectsANullPathAndFallsBackOnABadTolerance()
        {
            Assert.Throws<ArgumentNullException>(() => new PathMeasure(null!));

            using var path = NewPath();
            path.AddCircle(0, 0, 10);
            Assert.Equal(2 * Math.PI * 10, new PathMeasure(path, tolerance: -1).Length, 1);
            Assert.Equal(2 * Math.PI * 10, new PathMeasure(path, tolerance: double.NaN).Length, 1);
        }
    }
}

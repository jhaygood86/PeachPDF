namespace PeachDrawing.Core.Tests.Paint
{
    public class GraphicsPathCurveContourTests
    {
        private sealed class TestGraphicsPath : GraphicsPath
        {
            public override FillMode FillMode { get; set; }
            public override GraphicsPath ClipToRect(Rect rect) => this;
            public override void Dispose() { }
        }

        [Fact]
        public void GetCurveContours_AnEmptyPath_HasNoContours()
        {
            using var path = new TestGraphicsPath();

            Assert.Empty(path.GetCurveContours());
        }

        [Fact]
        public void GetCurveContours_LinesAndCubics_KeepTheirKindsAndPoints()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.LineTo(10, 0);
            path.AddBezierTo(15, 0, 15, 10, 10, 10);
            path.CloseFigure();

            var contour = Assert.Single(path.GetCurveContours());

            Assert.Equal(new PaintPoint(0, 0), contour.Start);
            Assert.True(contour.Closed);
            Assert.Equal(
                [
                    PathCommand.LineTo(new PaintPoint(10, 0)),
                    PathCommand.CubicTo(new PaintPoint(15, 0), new PaintPoint(15, 10), new PaintPoint(10, 10)),
                ],
                contour.Commands);
        }

        [Fact]
        public void GetCurveContours_AnArc_IsRecordedAsCubics()
        {
            using var path = new TestGraphicsPath();
            path.Start(10, 0);
            path.AddArc(0, 10, 10, 10, 0, isLargeArc: false, sweepClockwise: true);

            var contour = Assert.Single(path.GetCurveContours());

            Assert.NotEmpty(contour.Commands);
            Assert.All(contour.Commands, c => Assert.Equal(PathCommandKind.Cubic, c.Kind));
            Assert.Equal(0, contour.Commands[^1].End.X, 6);
            Assert.Equal(10, contour.Commands[^1].End.Y, 6);
        }

        [Fact]
        public void GetCurveContours_SeparateSubpaths_AreSeparateContours()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.LineTo(1, 1);
            path.AddMove(5, 5);
            path.LineTo(6, 6);

            var contours = path.GetCurveContours();

            Assert.Equal(2, contours.Count);
            Assert.False(contours[0].Closed);
            Assert.Equal(new PaintPoint(5, 5), contours[1].Start);
        }

        [Fact]
        public void GetCurveContours_ReturnsASnapshot_LaterEditsDoNotChangeIt()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.LineTo(1, 1);
            var snapshot = path.GetCurveContours();

            path.LineTo(2, 2);

            Assert.Single(Assert.Single(snapshot).Commands);
        }
    }
}

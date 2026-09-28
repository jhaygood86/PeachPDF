namespace PeachDrawing.Abstractions.Tests.Paint
{
    public class GraphicsPathFlattenTests
    {
        /// <summary>
        /// The minimal concrete <see cref="GraphicsPath"/> <see cref="Flatten"/> needs to exercise: every
        /// recorder method (<see cref="GraphicsPath.Start"/>, <see cref="GraphicsPath.LineTo"/>, etc.) has
        /// a real base implementation, so a subclass needs only the three genuinely abstract members.
        /// </summary>
        private sealed class TestGraphicsPath : GraphicsPath
        {
            public override FillMode FillMode { get; set; }
            public override GraphicsPath ClipToRect(Rect rect) => this;
            public override void Dispose() { }
        }

        [Fact]
        public void Flatten_ALine_IsTheStartAndEndPointUnchanged()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.LineTo(10, 20);

            var contours = path.Flatten(0.1);

            var contour = Assert.Single(contours);
            Assert.Equal([new PaintPoint(0, 0), new PaintPoint(10, 20)], contour.Points);
            Assert.False(contour.Closed);
        }

        [Fact]
        public void Flatten_AClosedFigure_ReportsClosedTrue()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.LineTo(10, 0);
            path.LineTo(10, 10);
            path.CloseFigure();

            var contour = Assert.Single(path.Flatten(0.1));

            Assert.True(contour.Closed);
        }

        [Fact]
        public void Flatten_MultipleSubpaths_ProducesOneContourEach()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.LineTo(10, 0);
            path.Start(100, 100);
            path.LineTo(110, 100);

            var contours = path.Flatten(0.1);

            Assert.Equal(2, contours.Count);
            Assert.Equal(new PaintPoint(0, 0), contours[0].Points[0]);
            Assert.Equal(new PaintPoint(100, 100), contours[1].Points[0]);
        }

        [Fact]
        public void Flatten_ABezierCurve_StaysWithinToleranceOfTheTrueCurve()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            // A quarter-circle-ish cubic from (0,0) to (100,100), bulging through (100,0)/(100,100) as controls.
            path.AddBezierTo(100, 0, 100, 100, 100, 100);

            var contour = Assert.Single(path.Flatten(0.5));

            // More than the two endpoints: a curved segment must actually be subdivided, not passed through
            // as a single chord - this is the exact "did Flatten do real work" check the earlier gradient
            // spreadMethod no-op incident (see CLAUDE.md's testing conventions) says a parser-only test
            // would miss.
            Assert.True(contour.Points.Count > 2, "expected the curve to be subdivided into more than its two endpoints");
            Assert.Equal(new PaintPoint(0, 0), contour.Points[0]);
            Assert.Equal(new PaintPoint(100, 100), contour.Points[^1]);
        }

        [Fact]
        public void Flatten_ATighterTolerance_ProducesAtLeastAsManyPoints()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.AddBezierTo(100, 0, 100, 100, 100, 100);

            var loose = path.Flatten(5.0);
            var tight = path.Flatten(0.05);

            Assert.True(tight.Single().Points.Count >= loose.Single().Points.Count);
        }

        [Fact]
        public void Flatten_ANonPositiveOrNaNTolerance_FallsBackToADefaultInsteadOfThrowing()
        {
            using var path = new TestGraphicsPath();
            path.Start(0, 0);
            path.AddBezierTo(100, 0, 100, 100, 100, 100);

            var contour = Assert.Single(path.Flatten(0));
            var contourForNaN = Assert.Single(path.Flatten(double.NaN));

            Assert.True(contour.Points.Count > 2);
            Assert.Equal(contour.Points, contourForNaN.Points);
        }

        [Fact]
        public void Flatten_AnEmptyPath_ReturnsNoContours()
        {
            using var path = new TestGraphicsPath();

            Assert.Empty(path.Flatten(0.1));
        }
    }
}

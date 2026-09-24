using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Raster;

namespace PeachPDF.Tests.Raster
{
    public class RasterGeometryTests
    {
        [Fact]
        public void Affine_Then_AppliesTheFirstTransformFirst()
        {
            var translateThenScale = Affine.Then(new Affine(1, 0, 0, 1, 3, 4), Affine.Scale(2, 10));

            var (x, y) = translateThenScale.Apply(1, 1);

            Assert.Equal(8, x);
            Assert.Equal(50, y);
        }

        [Fact]
        public void Affine_Invert_RoundTripsAPoint()
        {
            var m = new Affine(2, 0.5, -0.25, 3, 10, -7);

            var inverse = m.Invert();
            Assert.NotNull(inverse);

            var (x, y) = m.Apply(4.5, -2);
            var (rx, ry) = inverse.Value.Apply(x, y);
            Assert.Equal(4.5, rx, 9);
            Assert.Equal(-2, ry, 9);
        }

        [Fact]
        public void Affine_SingularOrNonFinite_HasNoInverse()
        {
            Assert.Null(new Affine(1, 2, 2, 4, 0, 0).Invert());
            Assert.Null(new Affine(double.NaN, 0, 0, 1, 0, 0).Invert());
            Assert.Null(new Affine(double.PositiveInfinity, 0, 0, 1, 0, 0).Invert());
        }

        [Fact]
        public void Affine_ScaleMeasures()
        {
            var m = new Affine(3, 0, 0, 4, 0, 0);

            Assert.Equal(4, m.MaxScale, 9);
            Assert.Equal(12, m.Determinant, 9);
            Assert.True(m.IsAxisAligned);
            Assert.False(new Affine(1, 1, 0, 1, 0, 0).IsAxisAligned);
            Assert.True(Affine.Identity.IsIdentity);
            Assert.False(new Affine(1, 0, 0, 1, 1, 0).IsIdentity);
        }

        [Fact]
        public void IntRect_Intersect()
        {
            var a = new IntRect(0, 0, 10, 10);
            var b = new IntRect(5, 5, 20, 20);

            Assert.Equal(new IntRect(5, 5, 10, 10), a.Intersect(b));
            Assert.True(a.Intersect(new IntRect(20, 20, 30, 30)).IsEmpty);
            Assert.Equal(10, a.Width);
            Assert.Equal(10, a.Height);
        }

        [Fact]
        public void PolygonSet_TracksBoundsContoursAndWinding()
        {
            var set = new PolygonSet();
            Assert.Null(set.GetBounds());

            set.BeginContour();
            set.Add(0, 0);
            set.Add(4, 0);
            set.Add(4, 3);
            set.Add(0, 3);

            Assert.Equal(1, set.ContourCount);
            Assert.Equal(4, set.PointCount);
            Assert.Equal((0, 0, 4, 3), set.GetBounds());
            Assert.Equal(12, set.SignedArea(0), 9);

            set.ReverseContour(0);
            Assert.Equal(-12, set.SignedArea(0), 9);
            set.NormalizeWinding();
            Assert.Equal(12, set.SignedArea(0), 9);

            var moved = new PolygonSet();
            moved.AddTransformed(set, new Affine(1, 0, 0, 1, 10, 20));
            Assert.Equal((10, 20, 14, 23), moved.GetBounds());

            moved.Clear();
            Assert.Equal(0, moved.ContourCount);
        }

        [Fact]
        public void PolygonSet_AddWithoutBeginContour_StartsOne()
        {
            var set = new PolygonSet();
            set.Add(1, 1);

            Assert.Equal(1, set.ContourCount);
            Assert.Equal((0, 1), set.GetContour(0));
        }

        private static FlatPath Flatten(XGraphicsPath path, double tolerance = 0.05) => FlatPath.From(path, tolerance);

        [Fact]
        public void FlatPath_FlattensACurveIntoManySegmentsWithinTolerance()
        {
            var path = new XGraphicsPath();
            path.AddBezier(0, 0, 0, 10, 10, 10, 10, 0);

            var flat = Flatten(path);

            Assert.Equal(1, flat.ContourCount);
            Assert.False(flat.Closed[0]);
            var (start, end) = flat.Contours.GetContour(0);
            Assert.True(end - start > 8);

            // Every flattened point lies on the true curve's side of the chord: the apex is near y = 7.5.
            double maxY = 0;
            for (var i = start; i < end; i++)
                maxY = Math.Max(maxY, flat.Contours.GetPoint(i).Y);
            Assert.InRange(maxY, 7.4, 7.6);
        }

        [Fact]
        public void FlatPath_RecordsClosedAndOpenSubpaths_InOrder()
        {
            var path = new XGraphicsPath();
            path.AddLine(0, 0, 5, 0);
            path.AddLine(5, 0, 5, 5);
            path.CloseFigure();
            path.StartFigure();
            path.AddLine(10, 10, 20, 10);

            var flat = Flatten(path);

            Assert.Equal(2, flat.ContourCount);
            Assert.True(flat.Closed[0]);
            Assert.False(flat.Closed[1]);
        }

        [Fact]
        public void FlatPath_AddsEllipsesRectanglesAndArcs()
        {
            var path = new XGraphicsPath();
            path.AddEllipse(0, 0, 20, 10);
            path.AddRectangle(new XRect(30, 0, 5, 5));
            path.AddArc(new XPoint(40, 0), new XPoint(50, 10), new XSize(10, 10), 0, false, XSweepDirection.Clockwise);

            var flat = Flatten(path);

            Assert.True(flat.ContourCount >= 3);
            var bounds = flat.Contours.GetBounds();
            Assert.NotNull(bounds);
            Assert.InRange(bounds.Value.MaxX, 49.9, 50.1);
        }

        [Fact]
        public void FlatPath_BuildsContoursDirectly()
        {
            var flat = new FlatPath();
            flat.AddContour([], closed: true);
            Assert.Equal(0, flat.ContourCount);

            flat.MoveTo(0, 0);
            flat.LineTo(4, 0);
            flat.CubicTo(4, 0, 4, 4, 0, 4, 0, 0, 0.1);
            flat.Close();

            Assert.Equal(1, flat.ContourCount);
            Assert.True(flat.Closed[0]);
            Assert.True(flat.Contours.PointCount > 4);
        }

        [Fact]
        public void FlatPath_DegenerateToleranceFallsBackToADefault()
        {
            var path = new XGraphicsPath();
            path.AddBezier(0, 0, 0, 10, 10, 10, 10, 0);

            Assert.True(Flatten(path, 0).Contours.PointCount > 2);
            Assert.True(Flatten(path, double.NaN).Contours.PointCount > 2);
        }
    }
}

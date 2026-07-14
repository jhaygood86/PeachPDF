using PeachPDF.Svg;

namespace PeachPDF.Tests.Svg
{
    public class SvgGeometryBoundsTests
    {
        [Fact]
        public void Circle_ReturnsSquareBoundingBoxAroundCenter()
        {
            var circle = new SvgCircleElement { Cx = 10, Cy = 20, R = 5 };
            var bounds = SvgGeometryBounds.GetBoundingBox(circle);

            Assert.NotNull(bounds);
            Assert.Equal(5, bounds!.Value.X);
            Assert.Equal(15, bounds.Value.Y);
            Assert.Equal(10, bounds.Value.Width);
            Assert.Equal(10, bounds.Value.Height);
        }

        [Fact]
        public void Circle_ZeroRadius_ReturnsNull()
        {
            var circle = new SvgCircleElement { Cx = 10, Cy = 20, R = 0 };
            Assert.Null(SvgGeometryBounds.GetBoundingBox(circle));
        }

        [Fact]
        public void Rect_ReturnsItsOwnGeometry()
        {
            var rect = new SvgRectElement { X = 1, Y = 2, Width = 30, Height = 40 };
            var bounds = SvgGeometryBounds.GetBoundingBox(rect);

            Assert.NotNull(bounds);
            Assert.Equal(1, bounds!.Value.X);
            Assert.Equal(2, bounds.Value.Y);
            Assert.Equal(30, bounds.Value.Width);
            Assert.Equal(40, bounds.Value.Height);
        }

        [Fact]
        public void Polygon_ReturnsEnvelopeOfPoints()
        {
            var polygon = new SvgPolygonElement
            {
                Points = [new(0, 0), new(10, 5), new(3, -4)],
            };
            var bounds = SvgGeometryBounds.GetBoundingBox(polygon);

            Assert.NotNull(bounds);
            Assert.Equal(0, bounds!.Value.X);
            Assert.Equal(-4, bounds.Value.Y);
            Assert.Equal(10, bounds.Value.Width);
            Assert.Equal(9, bounds.Value.Height);
        }

        [Fact]
        public void Path_IncludesBezierControlPoints()
        {
            var path = new SvgPathElement
            {
                Segments =
                [
                    PathSegment.MoveTo(0, 0),
                    PathSegment.CubicBezierTo(x1: -5, y1: 0, x2: 15, y2: 0, x: 10, y: 0),
                ],
            };
            var bounds = SvgGeometryBounds.GetBoundingBox(path);

            Assert.NotNull(bounds);
            // The control points (-5 and 15) extend past both endpoints (0 and 10).
            Assert.Equal(-5, bounds!.Value.X);
            Assert.Equal(20, bounds.Value.Width);
        }

        [Fact]
        public void Group_ReturnsUnionOfChildren()
        {
            var group = new SvgGroupElement
            {
                Children = { new SvgCircleElement { Cx = 0, Cy = 0, R = 5 }, new SvgRectElement { X = 20, Y = 20, Width = 10, Height = 10 } },
            };
            var bounds = SvgGeometryBounds.GetBoundingBox(group);

            Assert.NotNull(bounds);
            Assert.Equal(-5, bounds!.Value.X);
            Assert.Equal(-5, bounds.Value.Y);
            Assert.Equal(35, bounds.Value.Width);
            Assert.Equal(35, bounds.Value.Height);
        }

        [Fact]
        public void Use_OffsetsTargetBoundsByXY()
        {
            var use = new SvgUseElement { X = 100, Y = 200, Target = new SvgCircleElement { Cx = 0, Cy = 0, R = 5 } };
            var bounds = SvgGeometryBounds.GetBoundingBox(use);

            Assert.NotNull(bounds);
            Assert.Equal(95, bounds!.Value.X);
            Assert.Equal(195, bounds.Value.Y);
        }
    }
}

using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;

namespace PeachPDF.Tests.Svg
{
    public class SvgMarkerGeometryTests
    {
        [Fact]
        public void ComputeForLine_ReturnsStartAndEndWithSameAngle()
        {
            var vertices = SvgMarkerGeometry.ComputeForLine(0, 0, 10, 0);

            Assert.Equal(2, vertices.Count);
            Assert.True(vertices[0].IsStart);
            Assert.False(vertices[0].IsEnd);
            Assert.False(vertices[1].IsStart);
            Assert.True(vertices[1].IsEnd);
            Assert.Equal(0, vertices[0].AngleDegrees, 3);
            Assert.Equal(0, vertices[1].AngleDegrees, 3);
        }

        [Fact]
        public void ComputeForLine_Vertical_AngleIsNinetyDegrees()
        {
            var vertices = SvgMarkerGeometry.ComputeForLine(0, 0, 0, 10);
            Assert.Equal(90, vertices[0].AngleDegrees, 3);
        }

        [Fact]
        public void ComputeForPoints_Polyline_MidVertexBisectsIncomingAndOutgoing()
        {
            // A right-angle turn: (0,0) -> (10,0) -> (10,10). Incoming angle 0°, outgoing 90° ->
            // bisector 45°.
            var points = new[] { new RPoint(0, 0), new RPoint(10, 0), new RPoint(10, 10) };
            var vertices = SvgMarkerGeometry.ComputeForPoints(points, closed: false);

            Assert.Equal(3, vertices.Count);
            Assert.Equal(0, vertices[0].AngleDegrees, 3);
            Assert.Equal(45, vertices[1].AngleDegrees, 3);
            Assert.Equal(90, vertices[2].AngleDegrees, 3);
            Assert.True(vertices[0].IsStart);
            Assert.False(vertices[1].IsStart || vertices[1].IsEnd);
            Assert.True(vertices[2].IsEnd);
        }

        [Fact]
        public void ComputeForPoints_Polygon_ClosedWrapsAroundForFirstAndLastVertex()
        {
            // A closed square: first/last vertex should each bisect with the WRAPPING closing edge,
            // not just their single open-path-style neighbor.
            var points = new[] { new RPoint(0, 0), new RPoint(10, 0), new RPoint(10, 10), new RPoint(0, 10) };
            var vertices = SvgMarkerGeometry.ComputeForPoints(points, closed: true);

            Assert.Equal(4, vertices.Count);
            // At vertex 0: incoming (wrap) from (0,10)->(0,0) = angle -90°(=270°); outgoing (0,0)->(10,0) = 0°.
            // Bisector of -90° and 0° is -45°.
            Assert.Equal(-45, vertices[0].AngleDegrees, 3);
        }

        [Fact]
        public void ComputeForPath_StartAndEndUseOnlyAvailableDirection()
        {
            var segments = new[]
            {
                PathSegment.MoveTo(0, 0),
                PathSegment.LineTo(10, 0),
                PathSegment.LineTo(10, 10),
            };
            var vertices = SvgMarkerGeometry.ComputeForPath(segments);

            Assert.Equal(3, vertices.Count);
            Assert.True(vertices[0].IsStart);
            Assert.Equal(0, vertices[0].AngleDegrees, 3); // only outgoing available
            Assert.Equal(45, vertices[1].AngleDegrees, 3); // bisector of 0 and 90
            Assert.True(vertices[2].IsEnd);
            Assert.Equal(90, vertices[2].AngleDegrees, 3); // only incoming available
        }

        [Fact]
        public void ComputeForPath_MultiSubpath_OnlyFirstAndLastVertexOfWholePathAreStartEnd()
        {
            var segments = new[]
            {
                PathSegment.MoveTo(0, 0),
                PathSegment.LineTo(10, 0),
                PathSegment.MoveTo(20, 20),
                PathSegment.LineTo(30, 20),
            };
            var vertices = SvgMarkerGeometry.ComputeForPath(segments);

            Assert.Equal(4, vertices.Count);
            Assert.True(vertices[0].IsStart);
            Assert.False(vertices[1].IsStart || vertices[1].IsEnd);
            Assert.False(vertices[2].IsStart || vertices[2].IsEnd);
            Assert.True(vertices[3].IsEnd);
        }

        [Fact]
        public void ComputeForPath_ClosePath_JoinsStartAndEndTangents()
        {
            // A closed triangle: M0,0 L10,0 L5,10 Z - the start vertex (0,0) should get an incoming
            // tangent from the implicit closing segment (5,10)->(0,0), bisected with its outgoing
            // (0,0)->(10,0) - i.e. it should NOT simply equal the raw outgoing angle (0°) once closed.
            var segments = new[]
            {
                PathSegment.MoveTo(0, 0),
                PathSegment.LineTo(10, 0),
                PathSegment.LineTo(5, 10),
                PathSegment.ClosePath(),
            };
            var vertices = SvgMarkerGeometry.ComputeForPath(segments);

            Assert.Equal(3, vertices.Count);
            Assert.NotEqual(0, vertices[0].AngleDegrees, 3);
        }

        [Fact]
        public void ComputeForPath_CubicBezier_UsesControlPointDirection()
        {
            // A curve M0,0 C0,10 10,10 10,0 - outgoing tangent at the start should point toward the
            // first control point (0,10), i.e. straight up (90°), not toward the curve's end (0°).
            var segments = new[]
            {
                PathSegment.MoveTo(0, 0),
                PathSegment.CubicBezierTo(x1: 0, y1: 10, x2: 10, y2: 10, x: 10, y: 0),
            };
            var vertices = SvgMarkerGeometry.ComputeForPath(segments);

            Assert.Equal(90, vertices[0].AngleDegrees, 3);
        }
    }
}

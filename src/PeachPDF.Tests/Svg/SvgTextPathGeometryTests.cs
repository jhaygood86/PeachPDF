using PeachPDF.Svg;
using System;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Unit coverage for <see cref="SvgTextPathGeometry"/>: arc-length total, and point/tangent lookup
    /// along lines, polylines, cubics, and elliptical arcs.
    /// </summary>
    public class SvgTextPathGeometryTests
    {
        private static SvgTextPathGeometry Build(string d) => new(SvgPathDataParser.Parse(d));

        [Fact]
        public void StraightLine_TotalLengthAndMidpoint()
        {
            var g = Build("M0,0 L100,0");

            Assert.Equal(100, g.TotalLength, 3);

            var (x, y, angle) = g.PointAtLength(50);
            Assert.Equal(50, x, 3);
            Assert.Equal(0, y, 3);
            Assert.Equal(0, angle, 3);
        }

        [Fact]
        public void PointAtLength_ClampsToPathEnds()
        {
            var g = Build("M0,0 L100,0");

            var (x0, _, _) = g.PointAtLength(-50);
            Assert.Equal(0, x0, 3);

            var (x1, _, _) = g.PointAtLength(1000);
            Assert.Equal(100, x1, 3);
        }

        [Fact]
        public void Polyline_LengthAccumulatesAcrossSegments_AndTangentTurns()
        {
            // Right then down: total 200. At length 150 we're 50 into the vertical segment, heading +y
            // (tangent 90 degrees).
            var g = Build("M0,0 L100,0 L100,100");

            Assert.Equal(200, g.TotalLength, 3);

            var (x, y, angle) = g.PointAtLength(150);
            Assert.Equal(100, x, 3);
            Assert.Equal(50, y, 3);
            Assert.Equal(90, angle, 3);
        }

        [Fact]
        public void MoveTo_GapDoesNotCountTowardLength()
        {
            // Two disjoint unit-length subpaths with a big pen-up jump between them: only the drawn
            // lengths (1 + 1) count, not the jump.
            var g = Build("M0,0 L10,0 M100,0 L110,0");

            Assert.Equal(20, g.TotalLength, 3);
        }

        [Fact]
        public void Cubic_StraightControlPoints_BehaveLikeALine()
        {
            // Control points collinear on the x-axis: the flattened curve is just a 100-long line.
            var g = Build("M0,0 C33,0 66,0 100,0");

            Assert.Equal(100, g.TotalLength, 1);
            var (x, y, angle) = g.PointAtLength(50);
            Assert.Equal(50, x, 1);
            Assert.Equal(0, y, 1);
            Assert.Equal(0, angle, 1);
        }

        [Fact]
        public void Cubic_SymmetricArch_MidpointIsCenteredWithHorizontalTangent()
        {
            // Symmetric arch from (0,0) to (100,0): the arc-length midpoint sits at x=50 with a
            // horizontal tangent, and the curve is longer than the 100-unit chord.
            var g = Build("M0,0 C0,-100 100,-100 100,0");

            Assert.True(g.TotalLength > 100);
            var (x, _, angle) = g.PointAtLength(g.TotalLength / 2);
            Assert.Equal(50, x, 1);
            // Near-horizontal at the apex (a few degrees of flattening-chord slope is expected).
            Assert.True(Math.Abs(NormalizeTangent(angle)) < 6, $"expected near-horizontal tangent, got {angle}");
        }

        [Fact]
        public void Arc_Semicircle_HasArcLengthAndMidpointOnTheRim()
        {
            // Semicircle of radius 50 from (0,0) to (100,0): length pi*r ~ 157.08, and the midpoint is
            // on the rim at x=50, |y|=50 (center at (50,0)).
            var g = Build("M0,0 A50,50 0 0 1 100,0");

            Assert.Equal(Math.PI * 50, g.TotalLength, 0);

            var (x, y, _) = g.PointAtLength(g.TotalLength / 2);
            Assert.Equal(50, x, 0);
            Assert.Equal(50, Math.Abs(y), 0);
        }

        [Fact]
        public void Arc_ZeroRadius_DegeneratesToLine()
        {
            var g = Build("M0,0 A0,0 0 0 1 100,0");

            Assert.Equal(100, g.TotalLength, 3);
        }

        [Fact]
        public void Arc_CounterClockwiseSweep_MirrorsTheMidpoint()
        {
            // Same semicircle as above but sweep-flag 0 (the other arc): still length pi*r, but the
            // midpoint bulges to the opposite side of the chord.
            var cw = Build("M0,0 A50,50 0 0 1 100,0");
            var ccw = Build("M0,0 A50,50 0 0 0 100,0");

            Assert.Equal(Math.PI * 50, ccw.TotalLength, 0);

            var (_, yCw, _) = cw.PointAtLength(cw.TotalLength / 2);
            var (_, yCcw, _) = ccw.PointAtLength(ccw.TotalLength / 2);
            // Opposite sweep directions put the rim on opposite sides of the y=0 chord.
            Assert.True(Math.Sign(yCw) == -Math.Sign(yCcw) && yCw != 0, $"expected mirrored midpoints, got {yCw} and {yCcw}");
        }

        [Fact]
        public void Arc_UndersizedRadii_AreScaledUpToSpanTheEndpoints()
        {
            // Radii too small to reach from (0,0) to (100,0) are enlarged (SVG F.6.6) until the arc
            // exactly spans the chord - here to a radius-50 semicircle (length pi*50).
            var g = Build("M0,0 A10,10 0 0 1 100,0");

            Assert.Equal(Math.PI * 50, g.TotalLength, 0);
        }

        [Fact]
        public void ClosedSubpath_AddsTheClosingSegmentLength()
        {
            // Z closes the triangle back to its start, so the closing leg (sqrt(200)) is counted.
            var g = Build("M0,0 L10,0 L10,10 Z");

            Assert.Equal(20 + Math.Sqrt(200), g.TotalLength, 3);
        }

        [Fact]
        public void EmptyPath_HasZeroLength_AndSafeLookup()
        {
            var g = Build("");

            Assert.True(g.IsEmpty);
            Assert.Equal(0, g.TotalLength, 3);
            var (x, y, angle) = g.PointAtLength(10);
            Assert.Equal(0, x, 3);
            Assert.Equal(0, y, 3);
            Assert.Equal(0, angle, 3);
        }

        // Fold a tangent that may read as ~180 (curve direction) to its acute magnitude for the
        // "horizontal" assertion.
        private static double NormalizeTangent(double degrees)
        {
            degrees %= 180;
            if (degrees > 90) degrees -= 180;
            if (degrees < -90) degrees += 180;
            return degrees;
        }
    }
}

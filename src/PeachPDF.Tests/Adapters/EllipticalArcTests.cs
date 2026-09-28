using PeachDrawing.Abstractions;
using System;

namespace PeachPDF.Tests.Adapters
{
    /// <summary>
    /// <see cref="EllipticalArc.TryGetCenterParameterization"/> - the SVG 1.1 Appendix F.6.5 endpoint-to-
    /// center conversion <see cref="GraphicsPath.AddArc"/> relies on to record an equivalent cubic-Bézier
    /// approximation. Verified against exact, hand-computed geometry (not just bounding-box/point-count
    /// checks) since a sign error in the sweep/large-arc handling would silently draw the wrong one of the
    /// four possible arcs between two endpoints.
    /// </summary>
    public class EllipticalArcTests
    {
        [Fact]
        public void QuarterCircle_SmallArc_Clockwise_PassesThroughTheExpectedMidpoint()
        {
            // A unit circle centred at the origin: start (1, 0), end (0, 1). The small, clockwise arc
            // between them (sweeping through increasing angle in this y-down frame) is the quarter in the
            // first quadrant, whose own midpoint is at 45 degrees: (cos45, sin45).
            var ok = EllipticalArc.TryGetCenterParameterization(
                1, 0, 0, 1, 1, 1, 0, isLargeArc: false, sweepClockwise: true, out var arc);

            Assert.True(ok);
            Assert.Equal(0, arc.CenterX, 9);
            Assert.Equal(0, arc.CenterY, 9);
            Assert.Equal(1, arc.RadiusX, 9);
            Assert.Equal(1, arc.RadiusY, 9);

            var midAngle = (arc.StartAngle + arc.EndAngle) / 2;
            var midX = arc.CenterX + arc.RadiusX * Math.Cos(midAngle);
            var midY = arc.CenterY + arc.RadiusY * Math.Sin(midAngle);
            Assert.Equal(Math.Cos(Math.PI / 4), midX, 9);
            Assert.Equal(Math.Sin(Math.PI / 4), midY, 9);

            // A 90-degree sweep.
            Assert.Equal(Math.PI / 2, Math.Abs(arc.EndAngle - arc.StartAngle), 9);
        }

        [Fact]
        public void QuarterCircle_SmallArc_CounterClockwise_UsesTheOtherValidCentre()
        {
            // Same two endpoints and radius, opposite sweep flag: two points and a radius admit exactly
            // two possible circle centres (mirror images across the chord) - (0,0) for the clockwise small
            // arc (the case above), (1,1) for this one - each with its own short way around. Verified by
            // exact reconstruction from the SVG F.6.5 formulas by hand, not just plausibility.
            var ok = EllipticalArc.TryGetCenterParameterization(
                1, 0, 0, 1, 1, 1, 0, isLargeArc: false, sweepClockwise: false, out var arc);

            Assert.True(ok);
            Assert.Equal(1, arc.CenterX, 9);
            Assert.Equal(1, arc.CenterY, 9);
            Assert.Equal(Math.PI / 2, Math.Abs(arc.EndAngle - arc.StartAngle), 9);

            var midAngle = (arc.StartAngle + arc.EndAngle) / 2;
            var midX = arc.CenterX + arc.RadiusX * Math.Cos(midAngle);
            var midY = arc.CenterY + arc.RadiusY * Math.Sin(midAngle);
            Assert.Equal(1 - Math.Sqrt(2) / 2, midX, 9);
            Assert.Equal(1 - Math.Sqrt(2) / 2, midY, 9);
        }

        [Fact]
        public void QuarterCircle_LargeArc_SameSweepFlag_UsesTheSameCentreAsTheComplementarySmallArc()
        {
            // (isLargeArc: true, sweepClockwise: true) selects the same centre as
            // (isLargeArc: false, sweepClockwise: false) above (the sign the F.6.5 formulas pick depends
            // on isLargeArc != sweepClockwise, which is false in both cases) - so this is the other,
            // 270-degree way around that same (1, 1) centre, complementary to the 90-degree small arc.
            var ok = EllipticalArc.TryGetCenterParameterization(
                1, 0, 0, 1, 1, 1, 0, isLargeArc: true, sweepClockwise: true, out var arc);

            Assert.True(ok);
            Assert.Equal(1, arc.CenterX, 9);
            Assert.Equal(1, arc.CenterY, 9);
            Assert.Equal(3 * Math.PI / 2, Math.Abs(arc.EndAngle - arc.StartAngle), 9);
        }

        [Fact]
        public void Semicircle_LargeAndSmallArc_AreIndistinguishable()
        {
            // Diametrically opposite endpoints admit only one possible centre (the chord's own midpoint),
            // so - unlike the general case above - the large-arc-flag has no other centre to select
            // between and both flag values give the exact same 180-degree arc. A known, spec-correct
            // degenerate case (real SVG renderers agree), not a bug in either flag being ignored.
            EllipticalArc.TryGetCenterParameterization(-1, 0, 1, 0, 1, 1, 0, isLargeArc: false, sweepClockwise: true, out var small);
            EllipticalArc.TryGetCenterParameterization(-1, 0, 1, 0, 1, 1, 0, isLargeArc: true, sweepClockwise: true, out var large);

            Assert.Equal(0, small.CenterX, 9);
            Assert.Equal(0, small.CenterY, 9);
            Assert.Equal(Math.PI, Math.Abs(small.EndAngle - small.StartAngle), 9);
            Assert.Equal(small.CenterX, large.CenterX, 9);
            Assert.Equal(small.CenterY, large.CenterY, 9);
            Assert.Equal(Math.Abs(small.EndAngle - small.StartAngle), Math.Abs(large.EndAngle - large.StartAngle), 9);
        }

        [Fact]
        public void CoincidentEndpoints_IsDegenerate()
        {
            Assert.False(EllipticalArc.TryGetCenterParameterization(5, 5, 5, 5, 1, 1, 0, false, true, out _));
        }

        [Fact]
        public void ZeroRadius_IsDegenerate()
        {
            Assert.False(EllipticalArc.TryGetCenterParameterization(0, 0, 10, 0, 0, 1, 0, false, true, out _));
        }

        [Fact]
        public void RadiiTooSmallForTheChord_AreScaledUpToJustReachIt()
        {
            // A chord of length 10 can't be spanned by radius-1 circles at all; SVG's own rule (F.6.6) is
            // to scale both radii up just enough that it can, rather than reject the arc.
            var ok = EllipticalArc.TryGetCenterParameterization(-5, 0, 5, 0, 1, 1, 0, false, true, out var arc);

            Assert.True(ok);
            Assert.True(arc.RadiusX >= 5 - 1e-9);
            Assert.True(arc.RadiusY >= 5 - 1e-9);
        }
    }
}

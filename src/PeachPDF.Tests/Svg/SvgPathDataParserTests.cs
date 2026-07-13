using PeachPDF.Svg;
using System.Linq;

namespace PeachPDF.Tests.Svg
{
    public class SvgPathDataParserTests
    {
        [Fact]
        public void Parse_MoveAndLine_ProducesAbsoluteSegments()
        {
            var segments = SvgPathDataParser.Parse("M10 20 L30 40");

            Assert.Equal(2, segments.Count);
            Assert.Equal(PathSegmentKind.MoveTo, segments[0].Kind);
            Assert.Equal(10, segments[0].X);
            Assert.Equal(20, segments[0].Y);
            Assert.Equal(PathSegmentKind.LineTo, segments[1].Kind);
            Assert.Equal(30, segments[1].X);
            Assert.Equal(40, segments[1].Y);
        }

        [Fact]
        public void Parse_RelativeCommands_ResolveToAbsoluteCoordinates()
        {
            var segments = SvgPathDataParser.Parse("m10 10 l5 5");

            Assert.Equal(10, segments[0].X);
            Assert.Equal(10, segments[0].Y);
            Assert.Equal(15, segments[1].X);
            Assert.Equal(15, segments[1].Y);
        }

        [Fact]
        public void Parse_ImplicitCommandRepetition_TreatsExtraMoveArgsAsLineTo()
        {
            var segments = SvgPathDataParser.Parse("M0 0 10 10 20 20");

            Assert.Equal(3, segments.Count);
            Assert.Equal(PathSegmentKind.MoveTo, segments[0].Kind);
            Assert.Equal(PathSegmentKind.LineTo, segments[1].Kind);
            Assert.Equal(10, segments[1].X);
            Assert.Equal(10, segments[1].Y);
            Assert.Equal(PathSegmentKind.LineTo, segments[2].Kind);
            Assert.Equal(20, segments[2].X);
            Assert.Equal(20, segments[2].Y);
        }

        [Fact]
        public void Parse_ImplicitCommandRepetition_RepeatsNonMoveCommand()
        {
            var segments = SvgPathDataParser.Parse("M0 0 L10 0 20 0 30 0");

            Assert.Equal(4, segments.Count);
            Assert.All(segments.Skip(1), s => Assert.Equal(PathSegmentKind.LineTo, s.Kind));
            Assert.Equal(10, segments[1].X);
            Assert.Equal(20, segments[2].X);
            Assert.Equal(30, segments[3].X);
        }

        [Fact]
        public void Parse_HorizontalAndVertical_OnlyChangeTheRelevantCoordinate()
        {
            var segments = SvgPathDataParser.Parse("M5 5 H15 V25");

            Assert.Equal(15, segments[1].X);
            Assert.Equal(5, segments[1].Y);
            Assert.Equal(15, segments[2].X);
            Assert.Equal(25, segments[2].Y);
        }

        [Fact]
        public void Parse_RelativeHorizontalAndVertical_OffsetFromCurrentPoint()
        {
            var segments = SvgPathDataParser.Parse("M5 5 h10 v10");

            Assert.Equal(15, segments[1].X);
            Assert.Equal(5, segments[1].Y);
            Assert.Equal(15, segments[2].X);
            Assert.Equal(15, segments[2].Y);
        }

        [Fact]
        public void Parse_CubicBezier_ProducesControlPointsAndEndPoint()
        {
            var segments = SvgPathDataParser.Parse("M0 0 C1 2 3 4 5 6");

            var curve = segments[1];
            Assert.Equal(PathSegmentKind.CubicBezierTo, curve.Kind);
            Assert.Equal(1, curve.X1); Assert.Equal(2, curve.Y1);
            Assert.Equal(3, curve.X2); Assert.Equal(4, curve.Y2);
            Assert.Equal(5, curve.X); Assert.Equal(6, curve.Y);
        }

        [Fact]
        public void Parse_SmoothCubicAfterCubic_ReflectsPreviousControlPoint()
        {
            // After "C0 0 10 0 10 10" the reflected first control point of "S" should be
            // 2*(10,10) - (10,0) = (10,20).
            var segments = SvgPathDataParser.Parse("M0 0 C0 0 10 0 10 10 S20 20 20 10");

            var smooth = segments[2];
            Assert.Equal(PathSegmentKind.CubicBezierTo, smooth.Kind);
            Assert.Equal(10, smooth.X1);
            Assert.Equal(20, smooth.Y1);
            Assert.Equal(20, smooth.X2);
            Assert.Equal(20, smooth.Y2);
            Assert.Equal(20, smooth.X);
            Assert.Equal(10, smooth.Y);
        }

        [Fact]
        public void Parse_SmoothCubicWithoutPrecedingCubic_ReflectsFromCurrentPoint()
        {
            var segments = SvgPathDataParser.Parse("M5 5 S10 0 15 5");

            var smooth = segments[1];
            Assert.Equal(5, smooth.X1);
            Assert.Equal(5, smooth.Y1);
        }

        [Fact]
        public void Parse_QuadraticBezier_ConvertsToExactEquivalentCubic()
        {
            // Q at (0,0) -> control (10,10) -> end (20,0).
            // Expected cubic controls: C1 = (0,0) + 2/3*(10,10) = (6.666.., 6.666..)
            //                          C2 = (20,0) + 2/3*(10-20, 10-0) = (13.333.., 6.666..)
            var segments = SvgPathDataParser.Parse("M0 0 Q10 10 20 0");

            var cubic = segments[1];
            Assert.Equal(PathSegmentKind.CubicBezierTo, cubic.Kind);
            Assert.Equal(20.0 / 3.0, cubic.X1, 6);
            Assert.Equal(20.0 / 3.0, cubic.Y1, 6);
            Assert.Equal(40.0 / 3.0, cubic.X2, 6);
            Assert.Equal(20.0 / 3.0, cubic.Y2, 6);
            Assert.Equal(20, cubic.X);
            Assert.Equal(0, cubic.Y);
        }

        [Fact]
        public void Parse_SmoothQuadraticAfterQuadratic_ReflectsPreviousControlPoint()
        {
            var withReflection = SvgPathDataParser.Parse("M0 0 Q10 10 20 0 T40 0");
            var withoutReflection = SvgPathDataParser.Parse("M0 0 Q10 10 20 0 Q30 -10 40 0");

            // T's reflected control should be 2*(20,0) - (10,10) = (30,-10), matching an
            // explicit Q with that same reflected control point.
            var reflected = withReflection[2];
            var explicitEquivalent = withoutReflection[2];

            Assert.Equal(explicitEquivalent.X1, reflected.X1, 6);
            Assert.Equal(explicitEquivalent.Y1, reflected.Y1, 6);
        }

        [Fact]
        public void Parse_Arc_ReadsGluedFlagsCorrectly()
        {
            // "00" glued together must parse as largeArc=false, sweep=false, not fail on the
            // second flag or misread it as part of the radius/rotation numbers.
            var segments = SvgPathDataParser.Parse("M0 0 A5 5 0 00 5 5");

            var arc = segments[1];
            Assert.Equal(PathSegmentKind.ArcTo, arc.Kind);
            Assert.Equal(5, arc.RadiusX);
            Assert.Equal(5, arc.RadiusY);
            Assert.False(arc.IsLargeArc);
            Assert.False(arc.SweepClockwise);
            Assert.Equal(5, arc.X);
            Assert.Equal(5, arc.Y);
        }

        [Fact]
        public void Parse_Arc_ReadsSetFlagsCorrectly()
        {
            var segments = SvgPathDataParser.Parse("M0 0 A5 5 0 11 5 5");

            var arc = segments[1];
            Assert.True(arc.IsLargeArc);
            Assert.True(arc.SweepClockwise);
        }

        [Fact]
        public void Parse_GluedNumbers_SplitCorrectly()
        {
            // "1.5.5" must be read as two numbers: 1.5 and .5.
            var segments = SvgPathDataParser.Parse("M0 0 L1.5.5");

            Assert.Equal(1.5, segments[1].X);
            Assert.Equal(0.5, segments[1].Y);
        }

        [Fact]
        public void Parse_ClosePath_ReturnsCurrentPointToSubpathStart()
        {
            var segments = SvgPathDataParser.Parse("M0 0 L10 0 L10 10 Z l-5-5");

            Assert.Equal(PathSegmentKind.ClosePath, segments[3].Kind);

            // The relative lineto after Z should be offset from the subpath start (0,0), not
            // from the point before Z (10,10).
            var lineAfterClose = segments[4];
            Assert.Equal(-5, lineAfterClose.X);
            Assert.Equal(-5, lineAfterClose.Y);
        }

        [Fact]
        public void Parse_MultipleSubpaths_EachStartsFresh()
        {
            var segments = SvgPathDataParser.Parse("M0 0 L10 10 Z M20 20 L30 30");

            Assert.Equal(5, segments.Count);
            Assert.Equal(PathSegmentKind.ClosePath, segments[2].Kind);
            Assert.Equal(PathSegmentKind.MoveTo, segments[3].Kind);
            Assert.Equal(20, segments[3].X);
            Assert.Equal(20, segments[3].Y);
        }

        [Fact]
        public void Parse_EmptyOrNull_ReturnsNoSegments()
        {
            Assert.Empty(SvgPathDataParser.Parse(null));
            Assert.Empty(SvgPathDataParser.Parse(""));
            Assert.Empty(SvgPathDataParser.Parse("   "));
        }

        [Fact]
        public void Parse_TruncatedInput_ReturnsPartialResultsWithoutThrowing()
        {
            var segments = SvgPathDataParser.Parse("M0 0 L10 10 L5");

            Assert.Equal(2, segments.Count);
        }
    }
}

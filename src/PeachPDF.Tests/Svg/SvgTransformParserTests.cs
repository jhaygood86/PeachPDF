using PeachDrawing.Core;
using PeachPDF.Svg;
using System.Numerics;

namespace PeachPDF.Tests.Svg
{
    public class SvgTransformParserTests
    {
        [Fact]
        public void Parse_Matrix_ReadsAllSixComponents()
        {
            // Shape of gradientTransform value seen in real-world SVGs for a vertical flip
            // (a=1, b=0, c=0, d=-1, e=0, f=800). Composition goes through System.Numerics.Matrix4x4
            // (single precision, matching the existing CSS `transform` pipeline), so the offset is
            // deliberately a value that round-trips exactly through float rather than a case landing
            // on a decimal rounding boundary.
            var matrix = SvgTransformParser.Parse("matrix(1 0 0 -1 0 800)");

            Assert.NotNull(matrix);
            var m = matrix!.Value;
            Assert.Equal(1, m.M11);
            Assert.Equal(0, m.M12);
            Assert.Equal(0, m.M21);
            Assert.Equal(-1, m.M22);
            Assert.Equal(0, m.M31);
            Assert.Equal(800, m.M32, 3);
        }

        [Fact]
        public void Parse_MatrixWithCommas_ParsesTheSame()
        {
            var matrix = SvgTransformParser.Parse("matrix(1,0,0,-1,0,800)");

            Assert.NotNull(matrix);
            Assert.Equal(-1, matrix!.Value.M22);
        }

        [Fact]
        public void Parse_Translate_SetsOffsetOnly()
        {
            var matrix = SvgTransformParser.Parse("translate(10, 20)");

            Assert.NotNull(matrix);
            var m = matrix!.Value;
            Assert.Equal(1, m.M11);
            Assert.Equal(1, m.M22);
            Assert.Equal(10, m.M31);
            Assert.Equal(20, m.M32);
        }

        [Fact]
        public void Parse_ScaleWithOneArgument_IsUniform()
        {
            var matrix = SvgTransformParser.Parse("scale(2)");

            Assert.NotNull(matrix);
            var m = matrix!.Value;
            Assert.Equal(2, m.M11);
            Assert.Equal(2, m.M22);
        }

        [Fact]
        public void Parse_ScaleWithTwoArguments_ScalesEachAxisIndependently()
        {
            var matrix = SvgTransformParser.Parse("scale(2, 3)");

            Assert.NotNull(matrix);
            var m = matrix!.Value;
            Assert.Equal(2, m.M11);
            Assert.Equal(3, m.M22);
        }

        [Fact]
        public void Parse_TranslateThenScale_ComposesLastWrittenInnermost()
        {
            // Per SVG/CSS transform-list semantics, later-written functions apply first
            // (innermost) - equivalent to nested <g transform="translate(10,0)">
            // <g transform="scale(2)">point</g></g>: a local point is scaled first, then the
            // result is translated.
            var matrix = SvgTransformParser.Parse("translate(10,0) scale(2)");

            Assert.NotNull(matrix);
            var m = matrix!.Value;

            var x = 1 * m.M11 + 0 * m.M21 + m.M31;
            var y = 1 * m.M12 + 0 * m.M22 + m.M32;

            Assert.Equal(12, x, 6);
            Assert.Equal(0, y, 6);
        }

        [Fact]
        public void Parse_UnrecognizedFunctionAlone_ReturnsNull()
        {
            Assert.Null(SvgTransformParser.Parse("frobnicate(500)"));
        }

        [Fact]
        public void Parse_UnrecognizedFunctionMixedWithSupportedOne_ContributesNothing()
        {
            var matrix = SvgTransformParser.Parse("frobnicate(500) translate(5,5)");

            Assert.NotNull(matrix);
            var m = matrix!.Value;
            Assert.Equal(1, m.M11);
            Assert.Equal(1, m.M22);
            Assert.Equal(5, m.M31);
            Assert.Equal(5, m.M32);
        }

        [Theory]
        [InlineData("perspective(500)")]
        [InlineData("translate(5,5) rotateY(30deg)")]
        [InlineData("translate3d(1px, 2px, 3px) scale(2)")]
        public void Parse_ListWithA3DFunction_IsIgnoredWhole_WhereOnlyA2DMatrixFits(string value)
        {
            // gradientTransform, patternTransform and a clipPath's transform: CSS Transforms 2 section 11 ignores the whole list.
            Assert.Null(SvgTransformParser.Parse(value));
            Assert.NotNull(SvgTransformParser.ParseFull(value)!.Value.Full);
        }

        [Fact]
        public void ParseFull_A2DList_HasNoFullMatrix()
        {
            var parsed = SvgTransformParser.ParseFull("translate(5,5) rotate(30deg)")!.Value;

            Assert.Null(parsed.Full);
            Assert.Equal(5, parsed.Affine.M31);
        }

        [Fact]
        public void ParseFull_Translate3d_ComposesAndProjectsOrthographically()
        {
            var parsed = SvgTransformParser.ParseFull("translate3d(10px, 20px, 30px)")!.Value;

            Assert.Equal(30, parsed.Full!.Value.M43);
            Assert.Equal(10, parsed.Affine.M31);
            Assert.Equal(20, parsed.Affine.M32);
        }

        [Fact]
        public void ParseFull_RotateY_FoldsTheDepthIntoTheMatrix()
        {
            var full = SvgTransformParser.ParseFull("rotateY(90deg)")!.Value.Full!.Value;

            Assert.Equal(0, full.M11, 5);
            Assert.Equal(-1, full.M13, 5);
            Assert.Equal(1, full.M31, 5);
        }

        [Fact]
        public void ParseFull_Perspective_SetsTheDepthDivisor()
        {
            Assert.Equal(-1f / 500f, SvgTransformParser.ParseFull("perspective(500px)")!.Value.Full!.Value.M34, 6);
        }

        [Fact]
        public void ParseFull_PerspectiveUnderOnePixel_IsOnePixel()
        {
            Assert.Equal(-1f, SvgTransformParser.ParseFull("perspective(0.25px)")!.Value.Full!.Value.M34, 6);
            Assert.Null(SvgTransformParser.ParseFull("perspective(0)")); // zero does nothing, as in a browser
        }

        [Fact]
        public void ParseFull_PerspectiveNone_ContributesNothing()
        {
            Assert.Null(SvgTransformParser.ParseFull("perspective(none)"));
        }

        [Fact]
        public void ParseFull_Matrix3d_ReadsTheSixteenNumbersColumnMajor()
        {
            var full = SvgTransformParser.ParseFull("matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,0, 7,8,9,1)")!.Value.Full!.Value;

            Assert.Equal(7, full.M41);
            Assert.Equal(8, full.M42);
            Assert.Equal(9, full.M43);
        }

        [Fact]
        public void ParseFull_Rotate3d_ZeroAxisTurnsNothing()
        {
            Assert.Equal(Matrix4x4.Identity, SvgTransformParser.ParseFull("rotate3d(0, 0, 0, 45deg)")!.Value.Full);
        }

        [Fact]
        public void ParseFull_Rotate3d_AboutTheYAxisMatchesRotateY()
        {
            var a = SvgTransformParser.ParseFull("rotate3d(0, 1, 0, 40deg)")!.Value.Full!.Value;
            var b = SvgTransformParser.ParseFull("rotateY(40deg)")!.Value.Full!.Value;

            Assert.Equal(b.M11, a.M11, 5);
            Assert.Equal(b.M13, a.M13, 5);
            Assert.Equal(b.M31, a.M31, 5);
            Assert.Equal(b.M33, a.M33, 5);
        }

        [Fact]
        public void ParseFull_ScaleZAndScale3d_ScaleDepth()
        {
            Assert.Equal(3, SvgTransformParser.ParseFull("scaleZ(3)")!.Value.Full!.Value.M33);
            Assert.Equal(4, SvgTransformParser.ParseFull("scale3d(2, 3, 4)")!.Value.Full!.Value.M33);
        }

        [Fact]
        public void ParseFull_TranslateZ_RefusesAPercentage()
        {
            Assert.Null(SvgTransformParser.ParseFull("translateZ(10%)", new SvgTransformContext(16, 16, 100, 100)));
        }

        [Fact]
        public void ParseFull_ANegativePerspective_ContributesNothing()
        {
            Assert.Null(SvgTransformParser.ParseFull("perspective(-5px)"));
        }

        [Fact]
        public void Parse_EmptyOrNull_ReturnsNull()
        {
            Assert.Null(SvgTransformParser.Parse(null));
            Assert.Null(SvgTransformParser.Parse(""));
            Assert.Null(SvgTransformParser.Parse("   "));
        }

        private static (double X, double Y) Apply(Matrix3x2 m, double x, double y) =>
            (x * m.M11 + y * m.M21 + m.M31, x * m.M12 + y * m.M22 + m.M32);

        [Fact]
        public void Parse_RotateAboutOrigin_MatchesSpecMatrixForm()
        {
            // Per spec, rotate(a) == matrix(cos(a), sin(a), -sin(a), cos(a), 0, 0). Using 90 degrees
            // keeps expected component values clean (0/1/-1) rather than landing on an arbitrary
            // floating-point cosine/sine value.
            var matrix = SvgTransformParser.Parse("rotate(90)");

            Assert.NotNull(matrix);
            var m = matrix!.Value;
            Assert.Equal(0, m.M11, 5);
            Assert.Equal(1, m.M12, 5);
            Assert.Equal(-1, m.M21, 5);
            Assert.Equal(0, m.M22, 5);
            Assert.Equal(0, m.M31, 5);
            Assert.Equal(0, m.M32, 5);
        }

        [Fact]
        public void Parse_RotateWithCenter_LeavesCenterPointFixed()
        {
            var matrix = SvgTransformParser.Parse("rotate(90, 10, 10)");

            Assert.NotNull(matrix);
            var (cx, cy) = Apply(matrix!.Value, 10, 10);
            Assert.Equal(10, cx, 5);
            Assert.Equal(10, cy, 5);

            // A point 10 units "above" the center (smaller y, SVG's y-down space) rotates 90 degrees
            // clockwise to 10 units to its right.
            var (x, y) = Apply(matrix!.Value, 10, 0);
            Assert.Equal(20, x, 5);
            Assert.Equal(10, y, 5);
        }

        [Fact]
        public void Parse_SkewX_ShearsXBasedOnY()
        {
            // Per spec, skewX(a) == matrix(1, 0, tan(a), 1, 0, 0).
            var matrix = SvgTransformParser.Parse("skewX(45)");

            Assert.NotNull(matrix);
            var (x, y) = Apply(matrix!.Value, 0, 10);
            Assert.Equal(10, x, 4);
            Assert.Equal(10, y, 4);
        }

        [Fact]
        public void Parse_SkewY_ShearsYBasedOnX()
        {
            // Per spec, skewY(a) == matrix(1, tan(a), 0, 1, 0, 0).
            var matrix = SvgTransformParser.Parse("skewY(45)");

            Assert.NotNull(matrix);
            var (x, y) = Apply(matrix!.Value, 10, 0);
            Assert.Equal(10, x, 4);
            Assert.Equal(10, y, 4);
        }
    }
}

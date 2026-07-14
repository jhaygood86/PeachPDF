using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;

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
            Assert.Equal(0, m.OffsetX);
            Assert.Equal(800, m.OffsetY, 3);
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
            Assert.Equal(10, m.OffsetX);
            Assert.Equal(20, m.OffsetY);
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

            var x = 1 * m.M11 + 0 * m.M21 + m.OffsetX;
            var y = 1 * m.M12 + 0 * m.M22 + m.OffsetY;

            Assert.Equal(12, x, 6);
            Assert.Equal(0, y, 6);
        }

        [Fact]
        public void Parse_UnrecognizedFunctionAlone_ReturnsNull()
        {
            Assert.Null(SvgTransformParser.Parse("perspective(500)"));
        }

        [Fact]
        public void Parse_UnrecognizedFunctionMixedWithSupportedOne_ContributesNothing()
        {
            var matrix = SvgTransformParser.Parse("perspective(500) translate(5,5)");

            Assert.NotNull(matrix);
            var m = matrix!.Value;
            Assert.Equal(1, m.M11);
            Assert.Equal(1, m.M22);
            Assert.Equal(5, m.OffsetX);
            Assert.Equal(5, m.OffsetY);
        }

        [Fact]
        public void Parse_EmptyOrNull_ReturnsNull()
        {
            Assert.Null(SvgTransformParser.Parse(null));
            Assert.Null(SvgTransformParser.Parse(""));
            Assert.Null(SvgTransformParser.Parse("   "));
        }

        private static (double X, double Y) Apply(RMatrix m, double x, double y) =>
            (x * m.M11 + y * m.M21 + m.OffsetX, x * m.M12 + y * m.M22 + m.OffsetY);

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
            Assert.Equal(0, m.OffsetX, 5);
            Assert.Equal(0, m.OffsetY, 5);
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

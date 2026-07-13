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
            Assert.Null(SvgTransformParser.Parse("rotate(45)"));
        }

        [Fact]
        public void Parse_UnrecognizedFunctionMixedWithSupportedOne_ContributesNothing()
        {
            var matrix = SvgTransformParser.Parse("rotate(45) translate(5,5)");

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
    }
}

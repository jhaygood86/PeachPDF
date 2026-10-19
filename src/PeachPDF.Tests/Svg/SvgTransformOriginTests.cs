using PeachPDF.PdfBackend;
using PeachPDF.Svg;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// <c>transform-origin</c>, <c>transform-box</c> and the relative lengths of a CSS <c>transform</c> on an SVG element. The expected matrices are Chrome's computed values for a
    /// <c>rect</c> in a 200 x 100 <c>svg</c> (<c>font-size: 10px</c>, root 16px).
    /// </summary>
    public class SvgTransformOriginTests
    {
        private static readonly PdfRenderContext RenderContext = new();

        private static SvgElement Build(string rule, string attributes = "", string rootAttributes = "width=\"200\" height=\"100\" style=\"font-size:10px\"")
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {rootAttributes}><style>#r {{ {rule} }}</style><rect id=\"r\" x=\"20\" y=\"10\" width=\"40\" height=\"20\" {attributes}/></svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext).Children.Single();
        }

        private static void AssertMatrix(Matrix3x2? actual, float a, float b, float c, float d, float e, float f)
        {
            Assert.NotNull(actual);
            var m = actual!.Value;
            Assert.Equal(a, m.M11, 3);
            Assert.Equal(b, m.M12, 3);
            Assert.Equal(c, m.M21, 3);
            Assert.Equal(d, m.M22, 3);
            Assert.Equal(e, m.M31, 3);
            Assert.Equal(f, m.M32, 3);
        }

        // rotate(90deg) about (50, 0): translate(50,0) rotate translate(-50,0) is matrix(0, 1, -1, 0, 50, -50).
        [Theory]
        [InlineData("transform: rotate(90deg); transform-origin: 50px 0px", 0, 1, -1, 0, 50, -50)]
        [InlineData("transform: rotate(90deg); transform-origin: 50px 0", 0, 1, -1, 0, 50, -50)]
        [InlineData("transform: rotate(90deg); transform-origin: 0 0", 0, 1, -1, 0, 0, 0)]
        [InlineData("transform: rotate(90deg)", 0, 1, -1, 0, 0, 0)]
        [InlineData("transform: rotate(90deg); transform-origin: 50% 50%", 0, 1, -1, 0, 150, -50)]
        [InlineData("transform: rotate(90deg); transform-origin: right bottom", 0, 1, -1, 0, 300, -100)]
        [InlineData("transform: rotate(90deg); transform-origin: bottom right", 0, 1, -1, 0, 300, -100)]
        [InlineData("transform: scale(2); transform-origin: 10px 10px", 2, 0, 0, 2, -10, -10)]
        [InlineData("transform: scale(2); transform-origin: center", 2, 0, 0, 2, -100, -50)]
        public void TransformOrigin_MovesThePivot(string css, float a, float b, float c, float d, float e, float f)
        {
            AssertMatrix(Build(css).Transform, a, b, c, d, e, f);
        }

        // transform-box: fill-box measures against the rect's own box (20, 10, 40 x 20).
        [Theory]
        [InlineData("transform: scale(2); transform-box: fill-box; transform-origin: 0 0", 2, 0, 0, 2, -20, -10)]
        [InlineData("transform: scale(2); transform-box: fill-box; transform-origin: 50% 50%", 2, 0, 0, 2, -40, -20)]
        [InlineData("transform: scale(2); transform-box: fill-box; transform-origin: 100% 100%", 2, 0, 0, 2, -60, -30)]
        [InlineData("transform: scale(2); transform-box: stroke-box; transform-origin: 0 0; stroke: black; stroke-width: 4px", 2, 0, 0, 2, -18, -8)]
        public void TransformBox_MeasuresAgainstTheElementsBox(string css, float a, float b, float c, float d, float e, float f)
        {
            AssertMatrix(Build(css).Transform, a, b, c, d, e, f);
        }

        // Without a transform-origin the box does not matter: an SVG element's initial origin is the user-space origin.
        [Fact]
        public void TransformBox_WithoutAnOrigin_ChangesNothing()
        {
            AssertMatrix(Build("transform: scale(2); transform-box: fill-box").Transform, 2, 0, 0, 2, 0, 0);
        }

        [Fact]
        public void TransformOrigin_FromAnInlineStyle_BeatsARule()
        {
            AssertMatrix(Build("transform: scale(2); transform-origin: 10px 10px", "style=\"transform-origin: 20px 20px\"").Transform, 2, 0, 0, 2, -20, -20);
        }

        [Fact]
        public void TransformOrigin_ViewBoxIsMeasuredInTheViewBox()
        {
            // A viewBox of 0 0 400 200 drawn at 200 x 100: 50% is 200 user units.
            AssertMatrix(Build("transform: scale(2); transform-origin: 50% 50%", "", "width=\"200\" height=\"100\" viewBox=\"0 0 400 200\"").Transform, 2, 0, 0, 2, -200, -100);
        }

        // em is the element's font size (10px here), rem the root element's (here the svg itself, 10px), a percentage a fraction of the reference box (the 200 x 100 view box).
        [Theory]
        [InlineData("transform: translate(1em, 2em)", 1, 0, 0, 1, 10, 20)]
        [InlineData("transform: translate(1rem)", 1, 0, 0, 1, 10, 0)]
        [InlineData("transform: translate(10%, 5%)", 1, 0, 0, 1, 20, 5)]
        [InlineData("transform: translateX(50%)", 1, 0, 0, 1, 100, 0)]
        [InlineData("transform: translateY(50%)", 1, 0, 0, 1, 0, 50)]
        public void RelativeLengths_ResolveInATranslate(string css, float a, float b, float c, float d, float e, float f)
        {
            AssertMatrix(Build(css).Transform, a, b, c, d, e, f);
        }

        [Fact]
        public void APercentageTranslate_WithAFillBox_DoesNothingRatherThanGuess()
        {
            Assert.Null(Build("transform: translate(10%); transform-box: fill-box").Transform);
        }

        // Chrome drops style="transform: rotate(10)" (no unit) as invalid, so the rule answers; the attribute form takes the bare number.
        [Theory]
        [InlineData("style=\"transform: rotate(10)\"")]
        [InlineData("style=\"transform: translate(10, 20)\"")]
        public void AnInlineTransformWithoutUnits_IsDroppedAsInvalid(string attributes)
        {
            AssertMatrix(Build("transform: translateX(7px)", attributes).Transform, 1, 0, 0, 1, 7, 0);
        }

        [Fact]
        public void TheTransformAttribute_StillTakesBareNumbers()
        {
            AssertMatrix(Build("", "transform=\"translate(10, 20)\"").Transform, 1, 0, 0, 1, 10, 20);
        }

        [Fact]
        public void TheTransformAttribute_TakesTransformOriginToo()
        {
            AssertMatrix(Build("transform-origin: 10px 10px", "transform=\"scale(2)\"").Transform, 2, 0, 0, 2, -10, -10);
        }

        [Theory]
        [InlineData("rotate(10deg)", true)]
        [InlineData("rotate(0)", true)]
        [InlineData("rotate(10)", false)]
        [InlineData("rotate(10deg, 5, 5)", false)]
        [InlineData("translate(10px, 20px)", true)]
        [InlineData("translate(10, 20)", false)]
        [InlineData("translate(0, 0)", true)]
        [InlineData("scale(2)", true)]
        [InlineData("scale(50%)", true)]
        [InlineData("matrix(1, 0, 0, 1, 5, 6)", true)]
        [InlineData("matrix(1, 0, 0, 1, 5px, 6)", false)]
        [InlineData("none", true)]
        [InlineData("translate3d(1px, 2px, 3px)", true)]
        [InlineData("", false)]
        public void IsValidCssTransform_FollowsTheCssGrammar(string value, bool valid)
        {
            Assert.Equal(valid, SvgTransformParser.IsValidCssTransform(value));
        }
    }
}

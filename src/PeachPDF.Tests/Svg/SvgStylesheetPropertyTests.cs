using PeachDrawing.Core;
using PeachPDF.PdfBackend;
using PeachPDF.Svg;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// What a stylesheet rule (or a keyframe) can say about an SVG element, against Chrome 155 (computed style of a <c>rect</c> in a 200 x 100
    /// <c>svg</c> with <c>font-size: 10px</c>): the unitless numbers of <c>stroke-width</c> and <c>stroke-dashoffset</c>, the CSS syntax of <c>transform</c>
    /// (units, the single-axis functions, <c>skew()</c>), and <c>stop-color</c>/<c>stop-opacity</c> on a gradient stop.
    /// </summary>
    public class SvgStylesheetPropertyTests
    {
        private static readonly PdfRenderContext RenderContext = new();

        private static SvgDocument Build(string rule, string element, string rootAttributes = "width=\"200\" height=\"100\" style=\"font-size:10px\"")
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" {rootAttributes}><style>{rule}</style>{element}</svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root));
            if (cssData is not null) SvgCssStyling.CascadeCustomProperties(root, cssData, "print");
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
        }

        private static SvgElement Rect(string css, string attributes = "") =>
            Build($"#r {{ {css} }}", $"<rect id=\"r\" width=\"10\" height=\"10\" {attributes}/>").Children.Single();

        // Chrome: `stroke-width: 6` and `6px` are both 6px; a negative width is invalid, so the declaration is dropped and the attribute (or 1) stays.
        [Theory]
        [InlineData("stroke-width: 6", "", 6)]
        [InlineData("stroke-width: 6px", "", 6)]
        [InlineData("stroke-width: 6", "stroke-width=\"3\"", 6)]
        [InlineData("stroke-width: -2", "", 1)]
        [InlineData("stroke-width: -2", "stroke-width=\"3\"", 3)]
        [InlineData("stroke-width: calc(2 + 3)", "", 5)]
        [InlineData("stroke-width: 0", "", 0)]
        public void StrokeWidth_AcceptsAUnitlessNumber_AndRefusesANegativeOne(string css, string attributes, double expected)
        {
            Assert.Equal(expected, Rect(css, attributes).StrokeWidth, 3);
        }

        // Chrome: `stroke-dashoffset: 7` and `7px` are 7px; a negative offset is valid.
        [Theory]
        [InlineData("stroke-dashoffset: 7", 7)]
        [InlineData("stroke-dashoffset: 7px", 7)]
        [InlineData("stroke-dashoffset: -3", -3)]
        [InlineData("stroke-dashoffset: calc(1px + 2px)", 3)]
        public void StrokeDashoffset_AcceptsAUnitlessNumber(string css, double expected)
        {
            Assert.Equal(expected, Rect(css).StrokeDashOffset, 3);
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

        // Chrome's computed matrix(a, b, c, d, e, f) for the same declarations.
        [Theory]
        [InlineData("translateX(30px)", 1, 0, 0, 1, 30, 0)]
        [InlineData("translateY(5px)", 1, 0, 0, 1, 0, 5)]
        [InlineData("rotate(0.25turn)", 0, 1, -1, 0, 0, 0)]
        [InlineData("rotate(90deg)", 0, 1, -1, 0, 0, 0)]
        [InlineData("rotate(100grad)", 0, 1, -1, 0, 0, 0)]
        [InlineData("rotate(1.5708rad)", 0, 1, -1, 0, 0, 0)]
        [InlineData("scaleX(2) translateY(5px)", 2, 0, 0, 1, 0, 5)]
        [InlineData("translate(10px, 20px) skew(10deg, 5deg)", 1, 0.0874887f, 0.176327f, 1, 10, 20)]
        [InlineData("rotate(90deg) translate(10px)", 0, 1, -1, 0, 0, 10)]
        [InlineData("matrix(1, 0, 0, 1, 5, 6)", 1, 0, 0, 1, 5, 6)]
        [InlineData("skewX(0.1turn)", 1, 0, 0.726543f, 1, 0, 0)]
        [InlineData("skew(10deg)", 1, 0, 0.176327f, 1, 0, 0)]
        [InlineData("translate(1in, 0)", 1, 0, 0, 1, 96, 0)]
        [InlineData("scaleY(2)", 1, 0, 0, 2, 0, 0)]
        [InlineData("rotateZ(90deg)", 0, 1, -1, 0, 0, 0)]
        [InlineData("skewY(10deg)", 1, 0.176327f, 0, 1, 0, 0)]
        [InlineData("skewX(10deg)", 1, 0, 0.176327f, 1, 0, 0)]
        [InlineData("translate(1pt, 2mm)", 1, 0, 0, 1, 1.33333f, 7.55906f)]
        [InlineData("translate(1cm, 1pc)", 1, 0, 0, 1, 37.7953f, 16)]
        [InlineData("scale(2, 3)", 2, 0, 0, 3, 0, 0)]
        [InlineData("translate(5px)", 1, 0, 0, 1, 5, 0)]
        public void ACssTransform_IsReadAsChromeReadsIt(string transform, float a, float b, float c, float d, float e, float f)
        {
            AssertMatrix(Rect($"transform: {transform}").Transform, a, b, c, d, e, f);
        }

        // A quarter turn is exact: Chrome's computed matrix is 0 and 1, not 1.7e-7 and 1.
        [Theory]
        [InlineData("rotate(0.25turn)")]
        [InlineData("rotate(90deg)")]
        [InlineData("rotate(100grad)")]
        [InlineData("rotate(-270deg)")]
        public void AQuarterTurn_IsExact(string transform)
        {
            var matrix = Rect($"transform: {transform}").Transform!.Value;
            Assert.Equal(0f, matrix.M11);
            Assert.Equal(1f, matrix.M12);
            Assert.Equal(-1f, matrix.M21);
            Assert.Equal(0f, matrix.M22);
        }

        [Theory]
        [InlineData("rotate(180deg)", -1, 0, 0, -1)]
        [InlineData("rotate(0.75turn)", 0, -1, 1, 0)]
        [InlineData("rotate(360deg)", 1, 0, 0, 1)]
        [InlineData("rotate(0deg)", 1, 0, 0, 1)]
        [InlineData("rotate(-90deg)", 0, -1, 1, 0)]
        public void TheOtherQuarterTurns_AreExact(string transform, float a, float b, float c, float d)
        {
            var matrix = Rect($"transform: {transform}").Transform!.Value;
            Assert.Equal(a, matrix.M11);
            Assert.Equal(b, matrix.M12);
            Assert.Equal(c, matrix.M21);
            Assert.Equal(d, matrix.M22);
        }

        // The attribute grammar stays: bare numbers, a rotation about a point, a space between the arguments.
        [Theory]
        [InlineData("translate(10 20)", 1, 0, 0, 1, 10, 20)]
        [InlineData("translate(10,20)", 1, 0, 0, 1, 10, 20)]
        [InlineData("rotate(90)", 0, 1, -1, 0, 0, 0)]
        [InlineData("rotate(90 5 5)", 0, 1, -1, 0, 10, 0)]
        [InlineData("scale(2) translate(3)", 2, 0, 0, 2, 6, 0)]
        public void ATransformAttribute_StillReadsItsOwnGrammar(string transform, float a, float b, float c, float d, float e, float f)
        {
            var document = Build(string.Empty, $"<rect width=\"10\" height=\"10\" transform=\"{transform}\"/>");
            AssertMatrix(document.Children.Single().Transform, a, b, c, d, e, f);
        }

        [Theory]
        [InlineData("scale(2em)")]
        [InlineData("rotate(1em)")]
        [InlineData("skewX(1em)")]
        [InlineData("rotate(90deg 5em 5em)")]
        [InlineData("matrix(1deg, 0, 0, 1, 5, 6)")]
        [InlineData("rotate(90 calc(1))")]
        [InlineData("translateX(10px calc(1px))")]
        public void AFunctionWithAnArgumentThatCannotBeResolvedHere_DoesNothing(string transform)
        {
            Assert.Null(Rect(string.Empty, $"transform=\"{transform}\"").Transform);
        }

        // calc() and the 3D functions are not read here (a rule folds calc() first, so these reach the parser from style="" or an attribute); the functions after them still apply.
        [Theory]
        [InlineData("translate(10px, calc(1px + 2px)) scale(2)", 2, 0, 0, 2, 0, 0)]
        [InlineData("scale(2, calc(3)) translate(5)", 1, 0, 0, 1, 5, 0)]
        [InlineData("translate(1vw) scale(2)", 2, 0, 0, 2, 0, 0)]
        [InlineData("scale(2) translate3d(1px, 2px, 3px) translate(5px)", 2, 0, 0, 2, 10, 0)]
        [InlineData("perspective(100px) translate(5px)", 1, 0, 0, 1, 5, 0)]
        public void AFunctionItCannotResolve_DoesNotStopTheOnesAfterIt(string transform, float a, float b, float c, float d, float e, float f)
        {
            AssertMatrix(Rect(string.Empty, $"transform=\"{transform}\"").Transform, a, b, c, d, e, f);
        }

        private static SvgGradientStop[] Stops(string rule, string stops) =>
            Build(rule, $"<defs><linearGradient id=\"g\">{stops}</linearGradient></defs>").Gradients["g"].Stops.ToArray();

        // Chrome: a rule overrides the presentation attribute; stop-opacity multiplies the colour's alpha.
        [Fact]
        public void AStopRule_OverridesThePresentationAttribute()
        {
            var stops = Stops("stop { stop-color: #0000ff; stop-opacity: .5 }",
                "<stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#00ff00\" stop-opacity=\"1\"/>");

            Assert.All(stops, stop =>
            {
                Assert.Equal(0, stop.PaintColor.R);
                Assert.Equal(0, stop.PaintColor.G);
                Assert.Equal(255, stop.PaintColor.B);
                Assert.InRange(stop.PaintColor.A, 127, 128);
            });
        }

        [Fact]
        public void AnInlineStyle_BeatsAStopRule_WhichBeatsTheAttribute()
        {
            var stops = Stops("stop.a { stop-color: #0000ff } stop { stop-color: #ffff00 }",
                "<stop class=\"a\" offset=\"0\" style=\"stop-color: #00ff00\" stop-color=\"#ff0000\"/><stop class=\"a\" offset=\"0.5\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#ff0000\"/>");

            Assert.Equal(PaintColor.FromArgb(255, 0, 255, 0), stops[0].PaintColor);
            Assert.Equal(PaintColor.FromArgb(255, 0, 0, 255), stops[1].PaintColor);
            Assert.Equal(PaintColor.FromArgb(255, 255, 255, 0), stops[2].PaintColor);
        }

        [Fact]
        public void AStopColorFromAVariable_Resolves()
        {
            var stops = Stops(":root { --c: #123456 } stop { stop-color: var(--c) }", "<stop offset=\"0\" stop-color=\"#ff0000\"/>");

            Assert.Equal(PaintColor.FromArgb(255, 0x12, 0x34, 0x56), stops[0].PaintColor);
        }

        [Fact]
        public void AStopOpacityPercentage_IsReadAsChromeReadsIt()
        {
            var stops = Stops("stop { stop-opacity: 50% }", "<stop offset=\"0\" stop-color=\"#ff0000\"/>");

            Assert.InRange(stops[0].PaintColor.A, 127, 128);
        }

        [Fact]
        public void AStopColorOfCurrentcolor_InAStandaloneSvg_IsBlack()
        {
            // A standalone SVG has no host to take a colour from.
            var stops = Stops("stop { stop-color: currentcolor }", "<stop offset=\"0\"/>");

            Assert.Equal(PaintColor.FromArgb(255, 0, 0, 0), stops[0].PaintColor);
        }

        // Chrome: `inherit` on a stop takes the gradient's own stop-color / stop-opacity (neither is inherited by default).
        [Fact]
        public void AStopThatInherits_TakesTheGradientsValue()
        {
            var markup = "<defs><linearGradient id=\"g\" stop-color=\"#ff0000\" stop-opacity=\"0.5\"><stop offset=\"0\" stop-color=\"inherit\" stop-opacity=\"inherit\"/></linearGradient></defs>";
            var stops = Build("stop { stop-color: inherit; stop-opacity: inherit }", markup).Gradients["g"].Stops;

            Assert.Equal(255, stops[0].PaintColor.R);
            Assert.Equal(0, stops[0].PaintColor.B);
            Assert.InRange(stops[0].PaintColor.A, 127, 128);
        }

        [Fact]
        public void AStopWithoutARule_StillReadsItsAttributes()
        {
            var stops = Stops("rect { fill: red }", "<stop offset=\"0\" stop-color=\"#ff0000\" stop-opacity=\"0.5\"/>");

            Assert.Equal(255, stops[0].PaintColor.R);
            Assert.InRange(stops[0].PaintColor.A, 127, 128);
        }
    }
}

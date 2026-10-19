using PeachDrawing.Core;
using PeachPDF.PdfBackend;
using PeachPDF.Svg;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>How the inline style, the matching rules and the presentation attribute settle one property (against Chrome 155).</summary>
    public class SvgStyleCascadeTierTests
    {
        private static readonly PdfRenderContext RenderContext = new();

        private static SvgGradientStop[] Stops(string rule, string gradientAttributes, string stops, string wrapStart = "", string wrapEnd = "")
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\"><style>{rule}</style>{wrapStart}<defs><linearGradient id=\"g\" {gradientAttributes}>{stops}</linearGradient></defs>{wrapEnd}</svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.PdfBackend.PdfRenderContext());
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
            return document.Gradients["g"].Stops.ToArray();
        }

        private static readonly PaintColor Lime = PaintColor.FromArgb(255, 0, 255, 0);
        private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);

        // Chrome: an !important rule beats an inline declaration that is not important.
        [Fact]
        public void AnImportantRule_BeatsAnInlineDeclaration()
        {
            var stops = Stops("stop { stop-color: lime !important }", "", "<stop offset=\"0\" style=\"stop-color: red\"/>");
            Assert.Equal(Lime, stops[0].PaintColor);
        }

        [Fact]
        public void AnImportantInlineDeclaration_BeatsAnImportantRule()
        {
            var stops = Stops("stop { stop-color: lime !important }", "", "<stop offset=\"0\" style=\"stop-color: red !important\"/>");
            Assert.Equal(Red, stops[0].PaintColor);
        }

        [Fact]
        public void AnImportantInlineDeclaration_BeatsANormalRule_AndIsNotBlack()
        {
            var stops = Stops("stop { stop-color: lime }", "", "<stop offset=\"0\" style=\"stop-color:red !important\"/>");
            Assert.Equal(Red, stops[0].PaintColor);
        }

        [Fact]
        public void ANormalRule_StillLosesToAnInlineDeclaration()
        {
            var stops = Stops("stop { stop-color: lime }", "", "<stop offset=\"0\" style=\"stop-color: red\"/>");
            Assert.Equal(Red, stops[0].PaintColor);
        }

        // Chrome drops an invalid declaration, so the rule (or the attribute) answers.
        [Theory]
        [InlineData("stop-color: none")]
        [InlineData("stop-color: 5px")]
        [InlineData("stop-color: #abcd1")]
        public void AnInvalidInlineColor_FallsBackToTheRule(string inline)
        {
            var stops = Stops("stop { stop-color: lime }", "", $"<stop offset=\"0\" style=\"{inline}\" stop-color=\"red\"/>");
            Assert.Equal(Lime, stops[0].PaintColor);
        }

        [Fact]
        public void AnInvalidInlineColor_FallsBackToTheAttribute()
        {
            var stops = Stops("", "", "<stop offset=\"0\" style=\"stop-color: none\" stop-color=\"red\"/>");
            Assert.Equal(Red, stops[0].PaintColor);
        }

        [Fact]
        public void AnInvalidInlineOpacity_FallsBackToTheAttribute()
        {
            var stops = Stops("", "", "<stop offset=\"0\" style=\"stop-opacity: abc\" stop-opacity=\"0.5\" stop-color=\"red\"/>");
            Assert.InRange(stops[0].PaintColor.A, 127, 128);
        }

        // Chrome: currentColor on a stop is the stop's computed `color`, inherited from the gradient and the elements around it.
        [Fact]
        public void CurrentColorOnAStop_UsesTheStopsOwnColor()
        {
            var stops = Stops("", "", "<stop offset=\"0\" stop-color=\"currentColor\" style=\"color: lime\"/>");
            Assert.Equal(Lime, stops[0].PaintColor);
        }

        [Fact]
        public void CurrentColorOnAStop_UsesTheGradientsColor()
        {
            var stops = Stops("", "style=\"color: lime\"", "<stop offset=\"0\" stop-color=\"currentColor\"/>");
            Assert.Equal(Lime, stops[0].PaintColor);
        }

        [Fact]
        public void CurrentColorOnAStop_UsesAColorFromARule()
        {
            var stops = Stops("linearGradient { color: lime }", "", "<stop offset=\"0\" stop-color=\"currentColor\"/>");
            Assert.Equal(Lime, stops[0].PaintColor);
        }

        [Fact]
        public void CurrentColorOnAStop_UsesAnAncestorsColor()
        {
            var stops = Stops("", "", "<stop offset=\"0\" stop-color=\"currentColor\"/>", "<g color=\"lime\">", "</g>");
            Assert.Equal(Lime, stops[0].PaintColor);
        }
    }
}

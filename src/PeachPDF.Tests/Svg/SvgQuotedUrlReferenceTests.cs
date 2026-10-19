using PeachPDF.Rendering;
using PeachDrawing.PDF;
using PeachBrackets.Svg;
using System.Linq;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// A <c>url()</c> reference that reaches the SVG builder through the CSS-OM (a stylesheet rule) is serialised with quotes,
    /// <c>url("#id")</c>, where the presentation attribute is raw: the reference must resolve to the same id either way.
    /// </summary>
    public class SvgQuotedUrlReferenceTests
    {
        private static readonly HtmlRenderContext RenderContext = new();

        private static SvgDocument Build(string rule, string element)
        {
            var markup = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\"><style>" + rule + "</style>"
                + "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"1\" stop-color=\"blue\"/></linearGradient>"
                + "<clipPath id=\"c\"><rect width=\"5\" height=\"5\"/></clipPath><mask id=\"m\"><rect width=\"5\" height=\"5\" fill=\"white\"/></mask></defs>"
                + element + "</svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.Rendering.HtmlRenderContext());
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
        }

        [Theory]
        [InlineData("#r { fill: url(#g) }", "")]
        [InlineData("", "style=\"fill: url(#g)\"")]
        [InlineData("#r { fill: url('#g') }", "")]
        [InlineData("#r { fill: url(\"#g\") }", "")]
        [InlineData("", "fill=\"url(#g)\"")]
        public void FillReference_ResolvesToTheGradientWhateverTheQuoting(string rule, string attributes)
        {
            var rect = Build(rule, $"<rect id=\"r\" width=\"100\" height=\"50\" {attributes}/>").Children.Single();

            Assert.Equal(SvgPaintKind.GradientRef, rect.Fill.Kind);
            Assert.Equal("g", rect.Fill.ReferenceId);
        }

        [Fact]
        public void ClipPathMaskAndFilterReferences_AreUnquoted()
        {
            var rect = Build("#r { clip-path: url(\"#c\") }", "<rect id=\"r\" width=\"10\" height=\"10\" style=\"mask: url('#m')\"/>").Children.Single();

            Assert.Equal("c", rect.ClipPathRef);
            Assert.Equal("m", rect.MaskRef);
        }

        [Theory]
        [InlineData("url(#a)", "a")]
        [InlineData("url( #a )", "a")]
        [InlineData("url(\"#a\") red", "a")]
        [InlineData("url('file:///x.html#a')", "a")]
        [InlineData("url()", null)]
        [InlineData("url(#)", null)]
        [InlineData("url(a)", null)]
        public void TryParseUrlFragment_ReadsTheFragmentId(string value, string? expected)
        {
            Assert.Equal(expected, SvgValueParsers.TryParseUrlFragment(value));
        }
    }
}

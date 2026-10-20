using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachBrackets.Html.Parse;
using PeachDrawing.PDF;
using PeachBrackets.Svg;
using System.Linq;
using System.Xml.Linq;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>SVG-only properties written in a stylesheet rule or a <c>@keyframes</c> block are kept, and the SVG builder reads them from there.</summary>
    public class SvgOnlyStylesheetPropertyTests
    {
        private static readonly HtmlRenderContext RenderContext = new();

        private static SvgDocument Build(string rule, string content)
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\"><style>{rule}</style>{content}</svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.Rendering.HtmlRenderContext());
            return SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
        }

        [Theory]
        [InlineData("flood-color", "red")]
        [InlineData("flood-opacity", ".5")]
        [InlineData("lighting-color", "#ff0")]
        [InlineData("paint-order", "stroke fill")]
        [InlineData("mask", "url(#m)")]
        [InlineData("marker", "url(#m)")]
        [InlineData("marker-start", "none")]
        [InlineData("marker-mid", "url(#m)")]
        [InlineData("marker-end", "url(#m)")]
        [InlineData("clip-rule", "evenodd")]
        [InlineData("color-interpolation-filters", "linearRGB")]
        [InlineData("transform-box", "fill-box")]
        [InlineData("alignment-baseline", "middle")]
        [InlineData("baseline-shift", "super")]
        [InlineData("dominant-baseline", "hanging")]
        [InlineData("inline-size", "100px")]
        [InlineData("shape-inside", "circle(50px)")]
        [InlineData("shape-subtract", "rect(0 0 10px 10px)")]
        public void AStylesheetRule_KeepsTheDeclaration(string property, string value)
        {
            var sheet = CssParser.ParseStyleSheet($"r {{ {property}: {value} }} @keyframes k {{ to {{ {property}: {value} }} }}");

            var rule = sheet.Rules.OfType<PeachBrackets.Css.StyleRule>().Single();
            Assert.Contains(rule.Style, p => p.Name == property);
            var keyframes = sheet.Rules.OfType<PeachBrackets.Css.KeyframesRule>().Single();
            Assert.Contains(keyframes.Rules.OfType<PeachBrackets.Css.KeyframeRule>().SelectMany(r => r.Style), p => p.Name == property);
        }

        [Theory]
        [InlineData("flood-color", "5")]
        [InlineData("marker-end", "12px")]
        [InlineData("clip-rule", "sideways")]
        public void AStylesheetRule_StillRefusesAValueThatIsNotTheProperties(string property, string value)
        {
            var sheet = CssParser.ParseStyleSheet($"r {{ {property}: {value} }}");

            Assert.DoesNotContain(sheet.Rules.OfType<PeachBrackets.Css.StyleRule>().Single().Style, p => p.Name == property);
        }

        [Fact]
        public void FloodColorAndOpacityFromARule_ReachTheFilterPrimitive()
        {
            var document = Build("feFlood { flood-color: #0000ff; flood-opacity: .5 }",
                "<defs><filter id=\"f\"><feFlood/></filter></defs><rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>");

            var flood = Assert.IsType<FeFlood>(document.Filters["f"].Primitives.Single());
            Assert.Equal(0, flood.PaintColor.R);
            Assert.Equal(255, flood.PaintColor.B);
            Assert.Equal(0.5, flood.Opacity, 3);
        }

        [Fact]
        public void FloodColorFromInlineStyle_BeatsARule()
        {
            var document = Build("feFlood { flood-color: #0000ff }",
                "<defs><filter id=\"f\"><feFlood style=\"flood-color: #00ff00\"/></filter></defs><rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>");

            var flood = Assert.IsType<FeFlood>(document.Filters["f"].Primitives.Single());
            Assert.Equal(255, flood.PaintColor.G);
            Assert.Equal(0, flood.PaintColor.B);
        }

        [Fact]
        public void MarkerAndMaskFromARule_ReachTheElement()
        {
            var document = Build("#p { marker-end: url(#m); mask: url(#k) }",
                "<defs><marker id=\"m\"><circle r=\"2\"/></marker><mask id=\"k\"><rect width=\"5\" height=\"5\" fill=\"white\"/></mask></defs><path id=\"p\" d=\"M0 0 L50 50\" stroke=\"black\"/>");

            var path = document.Children.Single();
            Assert.Equal("m", path.MarkerEndRef);
            Assert.Equal("k", path.MaskRef);
        }

        [Fact]
        public void ColorInterpolationFiltersFromARule_SelectsSrgb()
        {
            var document = Build("feFlood { color-interpolation-filters: sRGB }",
                "<defs><filter id=\"f\"><feFlood/></filter></defs><rect width=\"10\" height=\"10\" filter=\"url(#f)\"/>");

            Assert.False(document.Filters["f"].Primitives.Single().LinearRgb);
        }
    }
}

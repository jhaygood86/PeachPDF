using PeachBrackets.Css;
using PeachBrackets.Svg;
using PeachPDF.Rendering;
using System.Linq;
using System.Xml.Linq;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>
    /// <c>font-size</c> keywords on an SVG element: <c>initial</c> and <c>medium</c> are the 16px medium size rather than the parent's, the other absolute
    /// sizes are a fixed table, <c>smaller</c>/<c>larger</c> step the parent's size, and an unparseable value computes to the inherited size
    /// (CSS Fonts 4 section 2.5, CSS Cascade 4 section 7.3). Every case puts a 30px parent over the text so "inherited" and "initial" differ.
    /// </summary>
    public class SvgFontSizeKeywordTests
    {
        private static readonly HtmlRenderContext RenderContext = new();

        private static double FontSizeOf(string textAttributes, string rule = "")
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\"><style>{rule}</style>" +
                         $"<g style=\"font-size: 30px\"><text class=\"t\" {textAttributes} x=\"0\" y=\"40\">A</text></g></svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new HtmlRenderContext());
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
            var group = Assert.IsType<SvgGroupElement>(document.Children.Single());
            return Assert.IsType<SvgTextElement>(group.Children.Single()).Font!.Size;
        }

        [Theory]
        [InlineData("initial", 16)]
        [InlineData("medium", 16)]
        [InlineData("MEDIUM", 16)]
        [InlineData("xx-small", 9)]
        [InlineData("x-small", 10)]
        [InlineData("small", 13)]
        [InlineData("large", 18)]
        [InlineData("x-large", 24)]
        [InlineData("xx-large", 32)]
        [InlineData("larger", 36)]
        [InlineData("smaller", 25)]
        [InlineData("inherit", 30)]
        [InlineData("unset", 30)]
        [InlineData("bogus", 30)]
        public void TheKeyword_InAnyDeclarationForm_ResolvesTheSame(string keyword, double expected)
        {
            Assert.Equal(expected, FontSizeOf($"font-size=\"{keyword}\""), 3);
            Assert.Equal(expected, FontSizeOf($"style=\"font-size: {keyword}\""), 3);
            Assert.Equal(expected, FontSizeOf("", $".t {{ font-size: {keyword} }}"), 3);
        }

        [Fact]
        public void Xxx_Large_InAnAttributeOrStyle_Is48()
        {
            // The stylesheet form is not offered: the CSS-OM font-size grammar does not carry xxx-large (as for HTML).
            Assert.Equal(48, FontSizeOf("font-size=\"xxx-large\""), 3);
            Assert.Equal(48, FontSizeOf("style=\"font-size: xxx-large\""), 3);
        }

        [Fact]
        public void ALargeKeywordOnAText_IsItsOwnSizeNotItsParents()
        {
            var markup = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\"><g style=\"font-size: 30px\">" +
                         "<text font-size=\"large\" x=\"0\" y=\"40\"><tspan font-size=\"2em\">A</tspan></text></g></svg>";
            var root = XDocument.Parse(markup).Root!;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, SvgCssStyling.BuildStyleData("", new HtmlRenderContext()), "print"), RenderContext);
            var text = Assert.IsType<SvgTextElement>(Assert.IsType<SvgGroupElement>(document.Children.Single()).Children.Single());
            Assert.Equal(18, text.Font!.Size, 3);
        }
    }
}

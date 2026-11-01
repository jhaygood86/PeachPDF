using PeachBrackets.Css;
using PeachBrackets.Svg;
using PeachPDF.Rendering;
using System.Linq;
using System.Xml.Linq;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>
    /// A <c>letter-spacing</c>/<c>word-spacing</c> value that is not valid is dropped, so the next cascade tier (or the inherited value) answers
    /// (CSS Cascade 4, filtering declarations): an invalid <c>style</c> declaration falls through to the presentation attribute, and a malformed
    /// <c>calc()</c> (a binary <c>+</c> needs whitespace on both sides, CSS Values 4 section 10.8) computes to the inherited spacing, not 0.
    /// </summary>
    public class SvgSpacingValidityTests
    {
        private static readonly HtmlRenderContext RenderContext = new();

        private static SvgTextElement BuildText(string content)
        {
            var root = XDocument.Parse($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"30\">{content}</svg>").Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new HtmlRenderContext());
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
            var first = document.Children.Single();
            return Assert.IsType<SvgTextElement>(first is SvgGroupElement group ? group.Children.Single() : first);
        }

        [Theory]
        [InlineData("letter-spacing")]
        [InlineData("word-spacing")]
        public void AnInvalidStyleDeclaration_FallsThroughToThePresentationAttribute(string property)
        {
            var text = BuildText($"<text style=\"{property}: foo\" {property}=\"3px\" x=\"0\" y=\"20\">Ab</text>");
            Assert.Equal(3, property == "letter-spacing" ? text.LetterSpacing : text.WordSpacing, 3);
        }

        [Fact]
        public void AValidStyleDeclaration_StillBeatsTheAttribute()
        {
            var text = BuildText("<text style=\"letter-spacing: 4px\" letter-spacing=\"3px\" x=\"0\" y=\"20\">Ab</text>");
            Assert.Equal(4, text.LetterSpacing, 3);
        }

        [Fact]
        public void AnInvalidStyleDeclarationAndNoAttribute_ComputesToTheInheritedSpacing()
        {
            var text = BuildText("<g letter-spacing=\"5px\"><text style=\"letter-spacing: foo\" x=\"0\" y=\"20\">Ab</text></g>");
            Assert.Equal(5, text.LetterSpacing, 3);
        }

        [Theory]
        [InlineData("calc(1em+2px)")]
        [InlineData("5 px")]
        public void AMalformedValue_ComputesToTheInheritedSpacing(string value)
        {
            var text = BuildText($"<g letter-spacing=\"5px\"><text letter-spacing=\"{value}\" font-size=\"20\" x=\"0\" y=\"20\">Ab</text></g>");
            Assert.Equal(5, text.LetterSpacing, 3);
        }

        [Fact]
        public void AWellFormedCalc_StillResolves()
        {
            var text = BuildText("<g letter-spacing=\"5px\"><text letter-spacing=\"calc(1em + 2px)\" font-size=\"20\" x=\"0\" y=\"20\">Ab</text></g>");
            Assert.Equal(22, text.LetterSpacing, 3);
        }

        [Fact]
        public void AValidStyleDeclaration_AcceptsNormalAndAPercentage()
        {
            Assert.Equal(0, BuildText("<g letter-spacing=\"5px\"><text style=\"letter-spacing: normal\" x=\"0\" y=\"20\">Ab</text></g>").LetterSpacing, 3);
        }
    }
}

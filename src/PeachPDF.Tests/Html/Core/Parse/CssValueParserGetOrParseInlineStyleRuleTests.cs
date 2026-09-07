using PeachPDF.Adapters;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Direct unit tests for <see cref="CssValueParser.GetOrParseInlineStyleRule"/> (issue #912):
    /// machine-generated markup often repeats the exact same <c>style="..."</c> attribute text across
    /// many elements, so the parsed rule is cached by that text instead of being re-tokenized per box.
    /// </summary>
    public class CssValueParserGetOrParseInlineStyleRuleTests
    {
        [Fact]
        public void SameAttributeText_ReturnsCachedInstance()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());

            var first = parser.GetOrParseInlineStyleRule("color: red; font-size: 12pt");
            var second = parser.GetOrParseInlineStyleRule("color: red; font-size: 12pt");

            Assert.Same(first, second);
        }

        [Fact]
        public void DifferentAttributeText_ReturnsDistinctInstancesWithCorrectDeclarations()
        {
            var parser = new CssValueParser(new PdfSharpAdapter());

            var red = parser.GetOrParseInlineStyleRule("color: red");
            var blue = parser.GetOrParseInlineStyleRule("color: blue");

            Assert.NotSame(red, blue);
            Assert.Equal("rgb(255, 0, 0)", red.Style.GetPropertyValue("color"));
            Assert.Equal("rgb(0, 0, 255)", blue.Style.GetPropertyValue("color"));
        }

        [Fact]
        public void SeparateParserInstances_DoNotShareCache()
        {
            var first = new CssValueParser(new PdfSharpAdapter());
            var second = new CssValueParser(new PdfSharpAdapter());

            var firstRule = first.GetOrParseInlineStyleRule("color: red");
            var secondRule = second.GetOrParseInlineStyleRule("color: red");

            Assert.NotSame(firstRule, secondRule);
            Assert.Equal("rgb(255, 0, 0)", secondRule.Style.GetPropertyValue("color"));
        }
    }
}

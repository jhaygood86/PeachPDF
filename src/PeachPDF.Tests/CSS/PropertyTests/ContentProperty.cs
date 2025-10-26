namespace PeachPDF.Tests.CSS.PropertyTests
{
    using PeachPDF.CSS;
using Xunit;

    public class CssContentPropertyTests
    {
        static StyleRule Parse(string source)
        {
            var parser = new StylesheetParser();
            var rule = parser.Parse(source).Rules[0];
            return rule as StyleRule;
        }

        [Fact]
        public void CssContentParseStringWithDoubleQuoteEscape()
        {
            var source = "a{content:\"\\\"\"}";
            var parsed = Parse(source);
            Assert.Equal("\"\\\"\"", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringWithSingleQuoteEscape()
        {
            var source = "a{content:'\\''}";
            var parsed = Parse(source);
            Assert.Equal("\"'\"", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringWithDoubleQuoteMultipleEscapes()
        {
            var source = "a{content:\"abc\\\"\\\"d\\\"ef\"}";
            var parsed = Parse(source);
            Assert.Equal("\"abc\\\"\\\"d\\\"ef\"", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringWithSingleQuoteMultipleEscapes()
        {
            var source = "a{content:'abc\\'\\'d\\'ef'}";
            var parsed = Parse(source);
            Assert.Equal("\"abc''d'ef\"", parsed.Style.Content);
        }
    }
}









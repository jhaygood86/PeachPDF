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

        [Fact]
        public void CssContentParseContentFunctionText()
        {
            var source = "a::before{content:content(text)}";
            var parsed = Parse(source);
            Assert.Equal("content()", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseContentFunctionBefore()
        {
            var source = "a::before{content:content(before)}";
            var parsed = Parse(source);
            Assert.Equal("content(before)", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseContentFunctionAfter()
        {
            var source = "a::before{content:content(after)}";
            var parsed = Parse(source);
            Assert.Equal("content(after)", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseContentFunctionFirstLetter()
        {
            var source = "a::before{content:content(first-letter)}";
            var parsed = Parse(source);
            Assert.Equal("content(first-letter)", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseContentFunctionWithString()
        {
            var source = "a::before{content:\"Chapter \" content(text)}";
            var parsed = Parse(source);
            Assert.Equal("\"Chapter \" content()", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseContentFunctionWithCounter()
        {
            var source = "a::before{content:content(before) \" - \" counter(page)}";
            var parsed = Parse(source);
            Assert.Equal("content(before) \" - \" counter(page)", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringFunction()
        {
            var source = "a::before{content:string(chapter)}";
            var parsed = Parse(source);
            Assert.Equal("string(chapter)", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringFunctionWithFirstKeyword()
        {
            var source = "a::before{content:string(chapter, first)}";
            var parsed = Parse(source);
            Assert.Contains("string(chapter", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringFunctionWithLastKeyword()
        {
            var source = "a::before{content:string(chapter, last)}";
            var parsed = Parse(source);
            Assert.Contains("string(chapter", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringFunctionWithStartKeyword()
        {
            var source = "a::before{content:string(chapter, start)}";
            var parsed = Parse(source);
            Assert.Contains("string(chapter", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringFunctionWithFirstExceptKeyword()
        {
            var source = "a::before{content:string(chapter, first-except)}";
            var parsed = Parse(source);
            Assert.Contains("string(chapter", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringFunctionWithStringLiteral()
        {
            var source = "a::before{content:\"Chapter: \" string(chapter)}";
            var parsed = Parse(source);
            Assert.Equal("\"Chapter: \" string(chapter)", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringFunctionWithCounter()
        {
            var source = "a::before{content:string(chapter) \" - Page \" counter(page)}";
            var parsed = Parse(source);
            Assert.Equal("string(chapter) \" - Page \" counter(page)", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseMultipleStringFunctions()
        {
            var source = "a::before{content:string(chapter) \" / \" string(section)}";
            var parsed = Parse(source);
            Assert.Equal("string(chapter) \" / \" string(section)", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringFunctionWithContentFunction()
        {
            var source = "a::before{content:string(chapter) \": \" content(text)}";
            var parsed = Parse(source);
            Assert.Equal("string(chapter) \": \" content()", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseStringFunctionWithAttr()
        {
            var source = "a::before{content:string(chapter) \" - \" attr(title)}";
            var parsed = Parse(source);
            Assert.Equal("string(chapter) \" - \" attr(title)", parsed.Style.Content);
        }

        [Fact]
        public void CssContentParseComplexCombination()
        {
            var source = "a::before{content:\"Part \" string(part, first) \" - Chapter \" string(chapter) \" (Page \" counter(page) \")\"}";
            var parsed = Parse(source);
            // Verify the main components are present
            Assert.Contains("\"Part \"", parsed.Style.Content);
            Assert.Contains("string(part", parsed.Style.Content);
            Assert.Contains("\" - Chapter \"", parsed.Style.Content);
            Assert.Contains("string(chapter)", parsed.Style.Content);
            Assert.Contains("counter(page)", parsed.Style.Content);
        }
    }
}
















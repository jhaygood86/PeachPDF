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
    }
}









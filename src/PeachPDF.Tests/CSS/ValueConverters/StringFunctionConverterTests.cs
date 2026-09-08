using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.CSS.ValueConverters
{
    /// <summary>
    /// Tests for the StringFunctionConverter that parses string() CSS function.
    /// The string() function retrieves named strings set by the string-set property.
    /// Syntax: string(&lt;custom-ident&gt; [, [ first | start | last | first-except ] ]?)
    /// </summary>
    public class StringFunctionConverterTests
    {
        [Fact]
        public void StringFunction_WithNameOnly_ParsesCorrectly()
        {
            var input = "string(chapter)";
            var tokens = CssValueParser.GetCssTokens(input);

            Assert.Single(tokens);
            var token = tokens[0];
            Assert.Equal(TokenType.Function, token.Type);

            var functionToken = token;
            Assert.Equal("string", functionToken.Data);
            Assert.NotEmpty(functionToken.ArgumentTokens);

            // First argument should be the identifier "chapter"
            var firstArg = functionToken.ArgumentTokens.First(t => t.Type != TokenType.Whitespace);
            Assert.True(firstArg.Type is TokenType.Hash or TokenType.AtKeyword or TokenType.Ident);
            Assert.Equal("chapter", firstArg.Data);
        }

        [Fact]
        public void StringFunction_WithFirstKeyword_ParsesCorrectly()
        {
            var input = "string(chapter, first)";
            var tokens = CssValueParser.GetCssTokens(input);

            Assert.Single(tokens);
            var token = tokens[0];
            Assert.Equal(TokenType.Function, token.Type);

            var functionToken = token;
            Assert.Equal("string", functionToken.Data);

            var args = functionToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
         .ToArray();

            Assert.Equal(2, args.Length);
            Assert.Equal("chapter", args[0].Data);
            Assert.Equal("first", args[1].Data);
        }

        [Fact]
        public void StringFunction_WithLastKeyword_ParsesCorrectly()
        {
            var input = "string(chapter, last)";
            var tokens = CssValueParser.GetCssTokens(input);

            var functionToken = tokens[0];
            var args = functionToken.ArgumentTokens
         .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
                     .ToArray();

            Assert.Equal(2, args.Length);
            Assert.Equal("chapter", args[0].Data);
            Assert.Equal("last", args[1].Data);
        }

        [Fact]
        public void StringFunction_WithStartKeyword_ParsesCorrectly()
        {
            var input = "string(chapter, start)";
            var tokens = CssValueParser.GetCssTokens(input);

            var functionToken = tokens[0];
            var args = functionToken.ArgumentTokens
           .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
           .ToArray();

            Assert.Equal(2, args.Length);
            Assert.Equal("chapter", args[0].Data);
            Assert.Equal("start", args[1].Data);
        }

        [Fact]
        public void StringFunction_WithFirstExceptKeyword_ParsesCorrectly()
        {
            var input = "string(chapter, first-except)";
            var tokens = CssValueParser.GetCssTokens(input);

            var functionToken = tokens[0];
            var args = functionToken.ArgumentTokens
        .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
        .ToArray();

            Assert.Equal(2, args.Length);
            Assert.Equal("chapter", args[0].Data);
            Assert.Equal("first-except", args[1].Data);
        }

        [Fact]
        public void StringFunction_WithHyphenatedName_ParsesCorrectly()
        {
            var input = "string(my-chapter-title)";
            var tokens = CssValueParser.GetCssTokens(input);

            var functionToken = tokens[0];
            var args = functionToken.ArgumentTokens
       .Where(t => t.Type != TokenType.Whitespace)
     .ToArray();

            Assert.Single(args);
            Assert.Equal("my-chapter-title", args[0].Data);
        }

        [Fact]
        public void StringFunction_WithUnderscoreName_ParsesCorrectly()
        {
            var input = "string(chapter_title)";
            var tokens = CssValueParser.GetCssTokens(input);

            var functionToken = tokens[0];
            var args = functionToken.ArgumentTokens
            .Where(t => t.Type != TokenType.Whitespace)
           .ToArray();

            Assert.Single(args);
            Assert.Equal("chapter_title", args[0].Data);
        }

        [Fact]
        public void StringFunction_InContentProperty_ParsesCorrectly()
        {
            var input = "content: string(chapter)";
            var parser = new StylesheetParser();
            var stylesheet = parser.Parse($"div {{ {input} }}");
            var rule = stylesheet.Rules[0] as StyleRule;

            Assert.NotNull(rule);
            Assert.Equal("string(chapter)", rule.Style.Content);
        }

        [Fact]
        public void StringFunction_CombinedWithStringLiteral_ParsesCorrectly()
        {
            var input = "content: \"Chapter: \" string(chapter)";
            var parser = new StylesheetParser();
            var stylesheet = parser.Parse($"div {{ {input} }}");
            var rule = stylesheet.Rules[0] as StyleRule;

            Assert.NotNull(rule);
            Assert.Equal("\"Chapter: \" string(chapter)", rule.Style.Content);
        }

        [Fact]
        public void StringFunction_CombinedWithCounter_ParsesCorrectly()
        {
            var input = "content: string(chapter) \" - Page \" counter(page)";
            var parser = new StylesheetParser();
            var stylesheet = parser.Parse($"div {{ {input} }}");
            var rule = stylesheet.Rules[0] as StyleRule;

            Assert.NotNull(rule);
            Assert.Equal("string(chapter) \" - Page \" counter(page)", rule.Style.Content);
        }

        [Fact]
        public void StringFunction_MultipleInContent_ParsesCorrectly()
        {
            var input = "content: string(chapter) \" / \" string(section)";
            var parser = new StylesheetParser();
            var stylesheet = parser.Parse($"div {{ {input} }}");
            var rule = stylesheet.Rules[0] as StyleRule;

            Assert.NotNull(rule);
            Assert.Equal("string(chapter) \" / \" string(section)", rule.Style.Content);
        }

        [Fact]
        public void StringFunction_WithWhitespaceAroundComma_ParsesCorrectly()
        {
            var input = "string(chapter , last)";
            var tokens = CssValueParser.GetCssTokens(input);

            var functionToken = tokens[0];
            var args = functionToken.ArgumentTokens
     .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
        .ToArray();

            Assert.Equal(2, args.Length);
            Assert.Equal("chapter", args[0].Data);
            Assert.Equal("last", args[1].Data);
        }

        [Fact]
        public void StringFunction_CaseInsensitiveKeyword_ParsesCorrectly()
        {
            var input = "string(chapter, LAST)";
            var tokens = CssValueParser.GetCssTokens(input);

            var functionToken = tokens[0];
            var args = functionToken.ArgumentTokens
  .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
     .ToArray();

            Assert.Equal(2, args.Length);
            // Keywords are typically lowercased during parsing
            Assert.Equal("LAST", args[1].Data);
        }
    }
}

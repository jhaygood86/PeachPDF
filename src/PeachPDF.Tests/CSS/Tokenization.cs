namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssTokenizationTests
    {
        [Fact]
        public void CssParserIdentifier()
        {
            var teststring = "h1 { background: blue; }";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(TokenType.Ident, token.Type);
        }

        [Fact]
        public void CssParserAtRule()
        {
            var teststring = "@media { background: blue; }";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(TokenType.AtKeyword, token.Type);
        }

        [Fact]
        public void CssParserUrlUnquoted()
        {
            var url = "http://someurl";
            var teststring = "url(" + url + ")";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(url, token.Data);
        }

        [Fact]
        public void CssParserUrlDoubleQuoted()
        {
            var url = "http://someurl";
            var teststring = "url(\"" + url + "\")";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(url, token.Data);
        }

        [Fact]
        public void CssParserUrlSingleQuoted()
        {
            var url = "http://someurl";
            var teststring = "url('" + url + "')";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal(url, token.Data);
        }

        // Exercises Lexer.UrlBad, the CSS Syntax bad-url-token recovery state: consume and discard
        // characters until the url()'s matching close-paren (or an earlier ';'/unmatched '}') so the
        // tokenizer resynchronizes rather than getting stuck on malformed input.
        [Fact]
        public void CssParserUrlBad_UnexpectedQuoteInUnquotedUrl_RecoversAtClosingParen()
        {
            var teststring = "url(bad\"value) next";
            var tokenizer = new Lexer(new TextSource(teststring));

            var urlToken = tokenizer.Get();
            Assert.Equal(TokenType.Url, urlToken.Type);

            Assert.Equal(TokenType.Whitespace, tokenizer.Get().Type);

            var nextToken = tokenizer.Get();
            Assert.Equal(TokenType.Ident, nextToken.Type);
            Assert.Equal("next", nextToken.Data);
        }

        [Fact]
        public void CssParserUrlBad_LineBreakInsideQuotedUrl_RecoversAtClosingParen()
        {
            var teststring = "url(\"unterminated\nstring\") next";
            var tokenizer = new Lexer(new TextSource(teststring));

            var urlToken = tokenizer.Get();
            Assert.Equal(TokenType.Url, urlToken.Type);

            Assert.Equal(TokenType.Whitespace, tokenizer.Get().Type);

            var nextToken = tokenizer.Get();
            Assert.Equal(TokenType.Ident, nextToken.Type);
            Assert.Equal("next", nextToken.Data);
        }

        [Fact]
        public void CssParserUrlBad_SemicolonInsideBadUrl_StopsAtSemicolon()
        {
            var teststring = "url(bad\"value; next";
            var tokenizer = new Lexer(new TextSource(teststring));

            var urlToken = tokenizer.Get();
            Assert.Equal(TokenType.Url, urlToken.Type);

            var nextToken = tokenizer.Get();
            Assert.Equal(TokenType.Semicolon, nextToken.Type);
        }

        [Fact]
        public void CssParserUrlBad_NestedParenInsideBadUrl_RequiresMatchingCloseParen()
        {
            // The stray '(' bumps UrlBad's paren-depth counter, so the first ')' just closes the
            // nested paren rather than ending the url() - only the second ')' does.
            var teststring = "url(bad\"va(lue)) next";
            var tokenizer = new Lexer(new TextSource(teststring));

            var urlToken = tokenizer.Get();
            Assert.Equal(TokenType.Url, urlToken.Type);

            Assert.Equal(TokenType.Whitespace, tokenizer.Get().Type);

            var nextToken = tokenizer.Get();
            Assert.Equal(TokenType.Ident, nextToken.Type);
            Assert.Equal("next", nextToken.Data);
        }

        // In a value context, '#' begins a <hash-token> (CSS Syntax §4.3.4): an all-hex name is a color
        // literal, any other name stays an id hash-token (e.g. the '#id' inside element()). Previously a
        // non-hex hash was truncated at the first non-hex char into an empty color + a stray ident.
        [Fact]
        public void ValueContextHash()
        {
            static void Check(string input, TokenType expectedType, string expectedData)
            {
                var lexer = new Lexer(new TextSource(input)) { IsInValue = true };
                var token = lexer.Get();
                Assert.Equal(expectedType, token.Type);
                Assert.Equal(expectedData, token.Data);
                Assert.Equal(TokenType.EndOfFile, lexer.Get().Type); // whole name is one token, no trailing ident
            }

            Check("#f00", TokenType.Color, "f00");
            Check("#abc123", TokenType.Color, "abc123");
            Check("#deadbeef", TokenType.Color, "deadbeef");
            Check("#hero", TokenType.Hash, "hero");
            Check("#top", TokenType.Hash, "top");
            Check("#f00bar", TokenType.Hash, "f00bar");
            Check("#\\41", TokenType.Hash, "A"); // an escape in the name → id hash-token (not a color)

            // '#' not followed by a name code point or valid escape is a plain '#' delimiter, not a hash-token.
            var delim = new Lexer(new TextSource("# ")) { IsInValue = true };
            Assert.Equal(TokenType.Delim, delim.Get().Type);
        }

        [Fact]
        public void LexerOnlyCarriageReturn()
        {
            var teststring = "\r";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal("\n", token.Data);
        }

        [Fact]
        public void LexerCarriageReturnLineFeed()
        {
            var teststring = "\r\n";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal("\n", token.Data);
        }

        [Fact]
        public void LexerOnlyLineFeed()
        {
            var teststring = "\n";
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();
            Assert.Equal("\n", token.Data);
        }
    }
}








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

        // A lone trailing '\r' (nothing after it) is the one case where TextSource's cursor position,
        // not just the returned token's text, can go wrong: NormalizeForward peeks one more character to
        // rule out a following '\n', and must land exactly on the true end of input when there is none.
        // A prior version of the string-backed TextSource fast path left the cursor one short there,
        // which made this second Get() call re-read the same '\r' as a whole new Whitespace token
        // instead of reaching EndOfFile - an infinite loop for any caller (e.g. CssValueParser.
        // GetCssTokens's `do { ... } while (token.Type != TokenType.EndOfFile)`) that loops to EndOfFile.
        [Fact]
        public void LexerOnlyCarriageReturn_PositionsAtTrueEndOfInput()
        {
            var tokenizer = new Lexer(new TextSource("\r"));

            var first = tokenizer.Get();
            Assert.Equal(TokenType.Whitespace, first.Type);
            Assert.Equal("\n", first.Data);

            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }

        [Fact]
        public void GetCssTokens_TrailingLoneCarriageReturn_DoesNotHangAndProducesNoTokens()
        {
            var tokens = PeachPDF.Html.Core.Parse.CssValueParser.GetCssTokens("foo\r");

            Assert.Single(tokens);
            Assert.Equal(TokenType.Ident, tokens[0].Type);
            Assert.Equal("foo", tokens[0].Data);
        }

        [Fact]
        public void GetCssTokens_DisposesTheUnderlyingLexerAndTextSource()
        {
            // GetCssTokens `using`s its Lexer (issue #910) - this must not throw or leave the pooled
            // scratch StringBuilder in a bad state across repeated calls.
            for (var i = 0; i < 3; i++)
            {
                var tokens = PeachPDF.Html.Core.Parse.CssValueParser.GetCssTokens("12px solid red");
                Assert.NotEmpty(tokens);
            }
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

        // Issue #921: CSS Syntax Level 3 §4.3.13 treats an exponent ('e'/'E', optional sign, digits)
        // as part of a <number>, so "1e2" is the single number 100 - not a Dimension("1", "e")
        // followed by a separate Number("2"). NumberRest/NumberFraction's scanning loop used to claim
        // any 'e'/'E' for the general unit-start branch before the exponent-handling switch case
        // could ever run.
        [Theory]
        [InlineData("1e2", 100d)]
        [InlineData("1E2", 100d)]
        [InlineData("1e+2", 100d)]
        [InlineData("1e-2", 0.01d)]
        [InlineData("1.5e2", 150d)]
        public void LexerScientificNotation_TokenizesAsSingleNumber(string teststring, double expected)
        {
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Number, token.Type);
            Assert.Equal(expected, ((NumberToken)token).Value, 5);
            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }

        // "1em" is an ordinary dimension - unaffected by the exponent fix since 'm' never starts an
        // exponent. "1e" has no digit after the 'e', so per spec it stays a dimension with unit "e"
        // rather than becoming (invalid) scientific notation.
        [Theory]
        [InlineData("1em", "em")]
        [InlineData("1e", "e")]
        public void LexerScientificNotation_NoExponentDigit_StaysADimension(string teststring, string expectedUnit)
        {
            var tokenizer = new Lexer(new TextSource(teststring));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Dimension, token.Type);
            var unitToken = (UnitToken)token;
            Assert.Equal(1d, unitToken.Value, 5);
            Assert.Equal(expectedUnit, unitToken.Unit);
            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }

        [Fact]
        public void LexerScientificNotation_SignWithNoDigit_StaysADimensionPlusLeftoverSign()
        {
            var tokenizer = new Lexer(new TextSource("1e+"));

            var first = tokenizer.Get();
            Assert.Equal(TokenType.Dimension, first.Type);
            var unitToken = (UnitToken)first;
            Assert.Equal(1d, unitToken.Value, 5);
            Assert.Equal("e", unitToken.Unit);

            var second = tokenizer.Get();
            Assert.Equal(TokenType.Delim, second.Type);
            Assert.Equal("+", second.Data);
        }

        // Exercises the SciNotation() fix: once the exponent digits are consumed, whatever follows
        // still needs the same dimension/percentage/number disambiguation the non-exponent paths
        // apply - SciNotation used to unconditionally return a plain number, which would have
        // silently split "1e2px" into Number("1e2") + a stray "px" ident token.
        [Fact]
        public void LexerScientificNotation_FollowedByUnit_TokenizesAsSingleDimension()
        {
            var tokenizer = new Lexer(new TextSource("1e2px"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Dimension, token.Type);
            var unitToken = (UnitToken)token;
            Assert.Equal(100d, unitToken.Value, 5);
            Assert.Equal("px", unitToken.Unit);
            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }

        // Exercises SciNotation()'s dash-led-unit branch (NumberDash): a unit may start with '-' after
        // digits, exactly like the non-exponent case "1-foo" already does - this must behave the same
        // regardless of whether the digits that precede it came from a plain integer or an exponent.
        [Fact]
        public void LexerScientificNotation_FollowedByDashLedUnit_TokenizesAsSingleDimension()
        {
            var tokenizer = new Lexer(new TextSource("1e2-foo"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Dimension, token.Type);
            var unitToken = (UnitToken)token;
            Assert.Equal(100d, unitToken.Value, 5);
            Assert.Equal("-foo", unitToken.Unit);
            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }

        // Exercises SciNotation()'s escape-sequence branch: a unit starting right after the exponent
        // digits with an escape ("\70 " = hex 0x70 = 'p') rather than a plain letter, mirroring the
        // escape handling NumberRest/NumberFraction already have for the non-exponent case.
        [Fact]
        public void LexerScientificNotation_FollowedByEscapedUnitStart_TokenizesAsSingleDimension()
        {
            var tokenizer = new Lexer(new TextSource("1e2\\70 x"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Dimension, token.Type);
            var unitToken = (UnitToken)token;
            Assert.Equal(100d, unitToken.Value, 5);
            Assert.Equal("px", unitToken.Unit);
            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }

        [Fact]
        public void LexerScientificNotation_FollowedByPercent_TokenizesAsSinglePercentage()
        {
            var tokenizer = new Lexer(new TextSource("1e2%"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Percentage, token.Type);
            var unitToken = (UnitToken)token;
            Assert.Equal(100d, unitToken.Value, 5);
            Assert.Equal("%", unitToken.Unit);
            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }
    }
}








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
            Assert.Equal(expected, token.Value, 5);
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
            var unitToken = token;
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
            var unitToken = first;
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
            var unitToken = token;
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
            var unitToken = token;
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
            var unitToken = token;
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
            var unitToken = token;
            Assert.Equal(100d, unitToken.Value, 5);
            Assert.Equal("%", unitToken.Unit);
            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }

        // The following tests target scan branches the Token.Data memory-slice redesign (BeginContentAt/
        // AppendLiteral/EndContent) added or changed the shape of - each is a rare recovery/escape/EOF
        // combination that pre-dates the redesign but hadn't been exercised by a dedicated test before.

        [Theory]
        [InlineData("\"abc")] // EndOfFile with no closing quote
        [InlineData("\"abc\n")] // raw, unescaped line break inside the string
        public void StringDoubleQuote_UnterminatedOrLineBreak_ReturnsBadString(string input)
        {
            var tokenizer = new Lexer(new TextSource(input));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal("abc", token.Data);
        }

        [Fact]
        public void StringDoubleQuote_DanglingBackslashAtEof_ReturnsBadString()
        {
            var tokenizer = new Lexer(new TextSource("\"abc\\"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal("abc", token.Data);
        }

        [Theory]
        [InlineData("'abc")]
        [InlineData("'abc\n")]
        public void StringSingleQuote_UnterminatedOrLineBreak_ReturnsBadString(string input)
        {
            var tokenizer = new Lexer(new TextSource(input));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal("abc", token.Data);
        }

        [Fact]
        public void StringSingleQuote_DanglingBackslashAtEof_ReturnsBadString()
        {
            var tokenizer = new Lexer(new TextSource("'abc\\"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal("abc", token.Data);
        }

        [Fact]
        public void StringSingleQuote_EscapedLineContinuation_InsertsThePlatformLineTerminator()
        {
            var tokenizer = new Lexer(new TextSource("'a\\\nb'"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal("a" + System.Environment.NewLine + "b", token.Data);
        }

        // HashRest's own escape branch (as opposed to HashStart's, already covered by ValueContextHash) -
        // an escape after at least one literal name character has already been appended.
        [Fact]
        public void HashRest_EscapeAfterLiteralNameCharacter_DecodesCorrectly()
        {
            var tokenizer = new Lexer(new TextSource("#a\\41")) { IsInValue = true };
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Hash, token.Type);
            Assert.Equal("aA", token.Data);
        }

        [Fact]
        public void HashRest_BackslashNotAValidEscape_EndsHashAndRaisesError()
        {
            // A backslash immediately followed by a line break is not a valid escape start (CSS Syntax
            // §4.3.4's "would start an escape" check) - the hash-token ends there instead of consuming it.
            var tokenizer = new Lexer(new TextSource("#a\\\n"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Hash, token.Type);
            Assert.Equal("a", token.Data);
        }

        [Fact]
        public void Comment_UnterminatedAtEndOfFile_ReturnsComment()
        {
            var tokenizer = new Lexer(new TextSource("/* abc"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Comment, token.Type);
            Assert.Equal(" abc", token.Data);
            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }

        [Fact]
        public void AtKeywordStart_EscapeAsFirstCharacter_DecodesCorrectly()
        {
            var tokenizer = new Lexer(new TextSource("@\\41 bc"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.AtKeyword, token.Type);
            Assert.Equal("Abc", token.Data);
        }

        [Fact]
        public void AtKeywordRest_EscapeAfterLiteralNameCharacter_DecodesCorrectly()
        {
            var tokenizer = new Lexer(new TextSource("@a\\41"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.AtKeyword, token.Type);
            Assert.Equal("aA", token.Data);
        }

        [Fact]
        public void NumberRest_DotNotFollowedByDigit_StaysAPlainNumber()
        {
            var tokenizer = new Lexer(new TextSource("3.x"));

            var number = tokenizer.Get();
            Assert.Equal(TokenType.Number, number.Type);
            Assert.Equal(3d, number.Value, 5);

            // The '.' itself is not re-surfaced as its own token here (a pre-existing NumberRest
            // behavior, unrelated to the Token.Data redesign) - the next token starts from 'x'.
            var next = tokenizer.Get();
            Assert.Equal(TokenType.Ident, next.Type);
            Assert.Equal("x", next.Data);
        }

        [Fact]
        public void Dimension_UnitContainingEscapeAfterLiteralCharacter_DecodesCorrectly()
        {
            // Unit "p" + escaped "\78" ('x') + literal "y" => unit "pxy".
            var tokenizer = new Lexer(new TextSource("1p\\78 y"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Dimension, token.Type);
            Assert.Equal(1d, token.Value, 5);
            Assert.Equal("pxy", token.Unit);
        }

        [Fact]
        public void UrlStart_EndOfFileImmediatelyAfterOpenParen_ReturnsEmptyBadUrl()
        {
            var tokenizer = new Lexer(new TextSource("url("));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal(string.Empty, token.Data);
        }

        [Fact]
        public void UrlDoubleQuote_UnterminatedAtEndOfFile_ReturnsUrlWithScannedContent()
        {
            var tokenizer = new Lexer(new TextSource("url(\"abc"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal("abc", token.Data);
        }

        [Fact]
        public void UrlDoubleQuote_DanglingBackslashAtEndOfFile_ReturnsBadUrlWithScannedContent()
        {
            var tokenizer = new Lexer(new TextSource("url(\"abc\\"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal("abc", token.Data);
        }

        [Fact]
        public void UrlDoubleQuote_EscapedLineContinuation_InsertsThePlatformLineTerminator()
        {
            // A backslash immediately followed by a newline is a "line continuation" (CSS Syntax
            // §4.3.7); this codebase's escape handling inserts StringBuilder.AppendLine()'s platform
            // default terminator rather than dropping the newline entirely, a pre-existing quirk
            // unrelated to the Token.Data redesign - AppendLineContinuation() just forces materialization
            // instead of a source slice, since the two would otherwise disagree here.
            var tokenizer = new Lexer(new TextSource("url(\"a\\\nb\")"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal("a" + System.Environment.NewLine + "b", token.Data);
        }

        [Fact]
        public void UrlSingleQuote_UnterminatedAtEndOfFile_ReturnsUrlWithScannedContent()
        {
            var tokenizer = new Lexer(new TextSource("url('abc"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal("abc", token.Data);
        }

        [Fact]
        public void UrlSingleQuote_DanglingBackslashAtEndOfFile_ReturnsBadUrlWithScannedContent()
        {
            var tokenizer = new Lexer(new TextSource("url('abc\\"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal("abc", token.Data);
        }

        [Fact]
        public void UrlSingleQuote_EscapedLineContinuation_InsertsThePlatformLineTerminator()
        {
            var tokenizer = new Lexer(new TextSource("url('a\\\nb')"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal("a" + System.Environment.NewLine + "b", token.Data);
        }

        [Fact]
        public void UrlSingleQuote_ContainingEscapeSequence_DecodesCorrectly()
        {
            var tokenizer = new Lexer(new TextSource("url('a\\41 b')"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal("aAb", token.Data);
        }

        [Fact]
        public void UrlUnquoted_ContainingEscapeSequence_DecodesCorrectly()
        {
            var tokenizer = new Lexer(new TextSource("url(a\\41 b)"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal("aAb", token.Data);
        }

        [Fact]
        public void UrlBad_ContainingEscapeSequence_StillRecoversAtClosingParen()
        {
            var tokenizer = new Lexer(new TextSource("url(bad\"va\\6c ue) next"));

            var urlToken = tokenizer.Get();
            Assert.Equal(TokenType.Url, urlToken.Type);

            Assert.Equal(TokenType.Whitespace, tokenizer.Get().Type);

            var nextToken = tokenizer.Get();
            Assert.Equal(TokenType.Ident, nextToken.Type);
            Assert.Equal("next", nextToken.Data);
        }

        [Fact]
        public void UrlBad_UnmatchedCurlyBraceClose_StopsBeforeIt()
        {
            var tokenizer = new Lexer(new TextSource("url(bad\"value} next"));

            var urlToken = tokenizer.Get();
            Assert.Equal(TokenType.Url, urlToken.Type);

            var nextToken = tokenizer.Get();
            Assert.Equal(TokenType.CurlyBracketClose, nextToken.Type);
        }

        [Fact]
        public void UrlBad_UnterminatedAtEndOfFile_ReturnsBadUrlWithScannedContent()
        {
            var tokenizer = new Lexer(new TextSource("url(bad\"value"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Url, token.Type);
            Assert.Equal(TokenType.EndOfFile, tokenizer.Get().Type);
        }

        [Fact]
        public void UnicodeRange_DashNotFollowedByHex_StaysAWildcardlessStartOnly()
        {
            var tokenizer = new Lexer(new TextSource("U+41-Z"));

            var range = tokenizer.Get();
            Assert.Equal(TokenType.Range, range.Type);
            Assert.Equal("41", range.Data);

            // Both the '-' and the non-hex lookahead character are put back for re-tokenization; since
            // 'Z' is itself a name-start character, '-Z' re-lexes as a single ident, not a bare delimiter.
            var next = tokenizer.Get();
            Assert.Equal(TokenType.Ident, next.Type);
            Assert.Equal("-Z", next.Data);
        }

        [Fact]
        public void NumberDash_EscapeAfterDash_DecodesIntoTheUnit()
        {
            // "1" + dash-led unit whose first character is an escaped '4', continued by literal "x".
            var tokenizer = new Lexer(new TextSource("1-\\34 x"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.Dimension, token.Type);
            Assert.Equal(1d, token.Value, 5);
            Assert.Equal("-4x", token.Unit);
        }

        [Fact]
        public void AppendEscape_SupplementaryPlaneCodePoint_DecodesAsSurrogatePair()
        {
            // U+1F600 (GRINNING FACE) needs a UTF-16 surrogate pair - exercises AppendEscape's
            // code > 0xFFFF branch, distinct from the single-char BMP case every other escape test uses.
            var tokenizer = new Lexer(new TextSource("\"\\1F600 \""));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.String, token.Type);
            Assert.Equal(char.ConvertFromUtf32(0x1F600), token.Data);
        }

        [Fact]
        public void ToValue_AtKeyword_PrependsAtSign()
        {
            var tokenizer = new Lexer(new TextSource("@media"));
            var token = tokenizer.Get();

            Assert.Equal(TokenType.AtKeyword, token.Type);
            Assert.Equal("@media", token.ToValue());
        }

        [Fact]
        public void ColumnCombinator_DoublePipe_TokenizesAsColumn()
        {
            var tokenizer = new Lexer(new TextSource("col || td"));
            tokenizer.Get(); // "col"
            tokenizer.Get(); // whitespace

            var token = tokenizer.Get();
            Assert.Equal(TokenType.Column, token.Type);
        }
    }
}








using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Direct unit tests for <see cref="CssValueParser.TryParseSingleString"/>. Regression: this method
    /// used to deliberately skip gating on <see cref="PeachPDF.CSS.Token.IsValid"/>, working around a bug
    /// where <c>Lexer.NewString</c> passed its own "bad" flag straight into <c>Token.NewString</c>'s
    /// "valid" parameter with no inversion - a well-formed string came back <c>IsValid: false</c> and an
    /// unterminated (bad-string-token) one came back <c>IsValid: true</c>. Now that the underlying
    /// inversion is fixed (see <c>PeachPDF.Tests.CSS.CssTokenizationTests</c>), this gates on
    /// <c>IsValid</c> normally.
    /// </summary>
    public class CssValueParserTryParseSingleStringTests
    {
        [Theory]
        [InlineData("\"hello\"", "hello")]
        [InlineData("'hello'", "hello")]
        [InlineData("\"\"", "")]
        // CSS Syntax's consume-a-string-token algorithm treats hitting EOF before a closing quote as a
        // parse error that still returns an ordinary <string-token> (not a <bad-string-token>) - so this
        // succeeds the same as a cleanly-closed one, using whatever content was scanned before EOF.
        [InlineData("\"hello", "hello")]
        public void SingleWellFormedStringToken_Succeeds(string value, string expectedContent)
        {
            Assert.True(CssValueParser.TryParseSingleString(value, out var content));
            Assert.Equal(expectedContent, content);
        }

        [Theory]
        // A raw, unescaped newline before the closing quote is the one case CSS Syntax actually turns
        // into a <bad-string-token> - the only string malformation TryParseSingleString now rejects.
        [InlineData("\"hello\nworld\"")]
        [InlineData("'hello\nworld'")]
        public void BadStringToken_LineBreakBeforeClosingQuote_Fails(string value)
        {
            Assert.False(CssValueParser.TryParseSingleString(value, out var content));
            Assert.Equal("", content);
        }

        [Theory]
        [InlineData("")]
        [InlineData("none")]
        [InlineData("auto")]
        [InlineData("\"a\" \"b\"")] // more than one token
        public void NoTokensOrNotExactlyOneString_Fails(string value)
        {
            Assert.False(CssValueParser.TryParseSingleString(value, out var content));
            Assert.Equal("", content);
        }
    }
}

using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Direct unit tests for <see cref="CssValueParser.IsValidLength"/>. Regression: the prior
    /// hand-rolled implementation (chop the last 1-2 characters off the string and try
    /// <c>double.TryParse</c> on what's left, gated by a manual "string length &lt;= 1" cutoff)
    /// rejected a bare unitless "0" outright, even though CSS2.1 §4.3.2 / CSS Values §6.2 make the
    /// unit identifier optional after a zero length. Since callers like
    /// <c>CssUtils.SetPropertyValue</c>'s "height"/"width"/"max-height"/"max-width" cases gate
    /// assignment behind this check, "height: 0" (a real Acid2 declaration on "#eyes-a") was silently
    /// never applied at all. Fixed by deferring to the same CSS-OM length/percentage grammar
    /// (<c>ValueExtensions.ToDistance</c>) every *Property converter already uses, instead of
    /// maintaining a second, independent length-syntax implementation.
    /// </summary>
    public class CssValueParserIsValidLengthTests
    {
        [Theory]
        [InlineData("0")]
        [InlineData("0px")]
        [InlineData("0.5em")]
        [InlineData("10px")]
        [InlineData("-5px")]
        [InlineData("50%")]
        [InlineData("1in")]
        [InlineData("calc(10px + 1em)")]
        public void ValidLengthOrPercentageOrCalc_ReturnsTrue(string value)
        {
            Assert.True(CssValueParser.IsValidLength(value));
        }

        [Theory]
        [InlineData("")]
        [InlineData("auto")]
        [InlineData("normal")]
        [InlineData("px")]
        [InlineData("abc")]
        public void InvalidOrNonLengthKeyword_ReturnsFalse(string value)
        {
            Assert.False(CssValueParser.IsValidLength(value));
        }
    }
}

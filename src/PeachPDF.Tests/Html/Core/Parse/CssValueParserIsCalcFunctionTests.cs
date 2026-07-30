using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Direct unit tests for <see cref="CssValueParser.IsCalcFunction"/>, covering both sides of the
    /// <c>Contains('(')</c> fast path in its private <c>TryGetCalcFunction</c> helper: an ordinary
    /// length/keyword with no parenthesis at all must never reach the tokenizer (a `dotnet-trace`
    /// allocation profile of the full showcase corpus found the previously-unconditional tokenize
    /// responsible for the largest share of the whole pipeline's allocation - a CSS function token can
    /// only ever be produced from an ident immediately followed by <c>(</c>, so the short-circuit is
    /// exact, not approximate), while a value that does contain a parenthesis - calc-family or not -
    /// must still be classified correctly by the full tokenize/grammar check the fast path doesn't touch.
    /// </summary>
    public class CssValueParserIsCalcFunctionTests
    {
        [Theory]
        [InlineData("10px")]
        [InlineData("50%")]
        [InlineData("auto")]
        [InlineData("0")]
        [InlineData("")]
        public void NoParenthesis_ReturnsFalseWithoutTokenizing(string value)
        {
            Assert.False(CssValueParser.IsCalcFunction(value));
        }

        [Theory]
        [InlineData("calc(10px + 1em)")]
        [InlineData("min(10px, 5%)")]
        [InlineData("max(10px, 5%)")]
        [InlineData("clamp(1px, 5%, 10px)")]
        [InlineData("CALC(10px + 1em)")]
        public void CalcFamilyFunction_ReturnsTrue(string value)
        {
            Assert.True(CssValueParser.IsCalcFunction(value));
        }

        [Theory]
        [InlineData("rgb(1, 2, 3)")]
        [InlineData("var(--x)")]
        [InlineData("url(a.png)")]
        public void ParenthesisPresentButNotCalcFamily_ReturnsFalse(string value)
        {
            Assert.False(CssValueParser.IsCalcFunction(value));
        }
    }
}

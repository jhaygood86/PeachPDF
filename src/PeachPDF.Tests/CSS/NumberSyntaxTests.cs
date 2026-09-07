namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class NumberSyntaxTests
    {
        [Theory]
        [InlineData("0", 1, 0d)]
        [InlineData("1", 1, 1d)]
        [InlineData("12", 2, 12d)]
        [InlineData("+12", 3, 12d)]
        [InlineData("-12", 3, -12d)]
        [InlineData("1.5", 3, 1.5d)]
        [InlineData(".5", 2, 0.5d)]
        [InlineData("-.5", 3, -0.5d)]
        [InlineData("+.5", 3, 0.5d)]
        [InlineData("0.100", 5, 0.1d)]
        public void TryConsumeNumber_ValidNumber_ConsumesExactlyTheNumberAndParsesIt(string input, int expectedLength, double expectedValue)
        {
            var success = NumberSyntax.TryConsumeNumber(input, out var length, out var value);

            Assert.True(success);
            Assert.Equal(expectedLength, length);
            Assert.Equal(expectedValue, value, 6);
        }

        [Theory]
        [InlineData("12px", 2, 12d)]
        [InlineData("1.5em", 3, 1.5d)]
        [InlineData("-3px", 2, -3d)]
        [InlineData("50%", 2, 50d)]
        public void TryConsumeNumber_TrailingText_OnlyConsumesTheNumericPrefix(string input, int expectedLength, double expectedValue)
        {
            var success = NumberSyntax.TryConsumeNumber(input, out var length, out var value);

            Assert.True(success);
            Assert.Equal(expectedLength, length);
            Assert.Equal(expectedValue, value, 6);
        }

        // The real Lexer's NumberRest/NumberFraction now correctly consume a trailing exponent
        // (issue #921), but this fast path deliberately still doesn't: TryClassifyLengthFast's
        // "is the remainder all letters" check already rejects any exponent shape (a digit follows
        // the 'e'/'E') and falls through to Inconclusive - the real tokenizer - so duplicating
        // exponent-consumption here would add complexity with no classification ever reaching it.
        [Theory]
        [InlineData("1e2", 1, 1d)]
        [InlineData("1E2", 1, 1d)]
        [InlineData("1e+2", 1, 1d)]
        [InlineData("1e-2", 1, 1d)]
        [InlineData("1.5e2", 3, 1.5d)]
        public void TryConsumeNumber_DoesNotConsumeAnExponent(string input, int expectedLength, double expectedValue)
        {
            var success = NumberSyntax.TryConsumeNumber(input, out var length, out var value);

            Assert.True(success);
            Assert.Equal(expectedLength, length);
            Assert.Equal(expectedValue, value, 6);
        }

        [Theory]
        [InlineData("")]
        [InlineData("px")]
        [InlineData("-")]
        [InlineData("+")]
        [InlineData(".")]
        [InlineData("-.")]
        [InlineData(".px")]
        [InlineData("auto")]
        public void TryConsumeNumber_NoLeadingNumber_ReturnsFalse(string input)
        {
            var success = NumberSyntax.TryConsumeNumber(input, out var length, out var value);

            Assert.False(success);
            Assert.Equal(0, length);
            Assert.Equal(0d, value);
        }
    }
}

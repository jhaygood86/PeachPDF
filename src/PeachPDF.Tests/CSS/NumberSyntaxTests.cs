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

        // The real Lexer's NumberRest/NumberFraction never reach their own exponent-handling code
        // (NumberExponential/SciNotation): any 'e'/'E' right after the digits is claimed by the
        // unit-starting branch first (CharExtensions.IsNameStart treats 'e'/'E' like any other
        // identifier-start letter), so this deliberately does NOT consume an exponent - matching that
        // real (if not CSS-Syntax-3-conformant) behavior exactly rather than the spec's own grammar.
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

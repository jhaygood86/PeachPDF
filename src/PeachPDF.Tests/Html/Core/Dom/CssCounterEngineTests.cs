using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Unit tests for <see cref="CssCounterEngine.FormatCounterValue"/>, the shared counter-style
    /// resolver used by both <c>content: counter()</c> and the list-item marker. In particular this
    /// pins the CSS Counter Styles Level 3 §2 requirement that an unknown/invalid style falls back to
    /// <c>decimal</c> rather than rendering nothing (regression guard for issue #128).
    /// </summary>
    public class CssCounterEngineTests
    {
        [Theory]
        [InlineData(1, "decimal", "1")]
        [InlineData(12, "decimal", "12")]
        [InlineData(1, "decimal-leading-zero", "01")]
        [InlineData(9, "decimal-leading-zero", "09")]
        [InlineData(12, "decimal-leading-zero", "12")]      // already two digits - no over-padding
        [InlineData(100, "decimal-leading-zero", "100")]
        [InlineData(4, "lower-roman", "iv")]
        [InlineData(4, "upper-roman", "IV")]
        [InlineData(1, "lower-alpha", "a")]
        [InlineData(3, "upper-alpha", "C")]
        public void FormatCounterValue_KnownStyles_FormatAsExpected(int number, string style, string expected)
        {
            Assert.Equal(expected, CssCounterEngine.FormatCounterValue(number, style));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(42)]
        public void FormatCounterValue_UnknownStyle_FallsBackToDecimal(int number)
        {
            // CSS Counter Styles Level 3 §2: an unknown/invalid counter style renders as decimal,
            // never empty. This is the crux of issue #128.
            Assert.Equal(number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CssCounterEngine.FormatCounterValue(number, "not-a-real-style"));
        }

        [Theory]
        [InlineData(0, "upper-roman", "0")]
        [InlineData(-5, "lower-alpha", "-5")]
        [InlineData(0, "lower-greek", "0")]
        public void FormatCounterValue_AlphabeticStyleOutOfRange_FallsBackToDecimal(int number, string style, string expected)
        {
            // Alphabetic/symbolic styles can't represent 0 or negatives; CSS Counter Styles L3 §2
            // says such out-of-range values render with the fallback style (decimal), not empty.
            Assert.Equal(expected, CssCounterEngine.FormatCounterValue(number, style));
        }

        [Fact]
        public void FormatCounterValue_StyleMatchIsCaseInsensitive()
        {
            Assert.Equal("01", CssCounterEngine.FormatCounterValue(1, "DECIMAL-LEADING-ZERO"));
            Assert.Equal("iv", CssCounterEngine.FormatCounterValue(4, "Lower-Roman"));
        }
    }
}

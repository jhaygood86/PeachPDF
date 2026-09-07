using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Direct unit tests for <see cref="CssValueParser.IsValidLengthList"/> — the span-based
    /// (no tokenizer) validator behind css-properties.json's "length-list" cssDataType
    /// (border-*-radius's &lt;length-percentage&gt;{1,2}, border-spacing's &lt;length&gt;{1,2}).
    /// </summary>
    public class CssValueParserIsValidLengthListTests
    {
        [Theory]
        [InlineData("10px", 1, 2, true)]
        [InlineData("10px 20px", 1, 2, true)]
        [InlineData("50%", 1, 2, true)]
        [InlineData("10px 50%", 1, 2, true)]
        [InlineData("0", 1, 2, true)]
        public void WithinBoundsAndAllowedComponents_ReturnsTrue(string value, int min, int max, bool allowPercentage)
        {
            Assert.True(CssValueParser.IsValidLengthList(value, min, max, allowPercentage));
        }

        [Theory]
        [InlineData("", 1, 2, true)]
        [InlineData("10px 20px 30px", 1, 2, true)] // too many components
        [InlineData("not-a-length", 1, 2, true)]
        [InlineData("10px not-a-length", 1, 2, true)] // second component invalid
        public void OutOfBoundsOrInvalidComponent_ReturnsFalse(string value, int min, int max, bool allowPercentage)
        {
            Assert.False(CssValueParser.IsValidLengthList(value, min, max, allowPercentage));
        }

        [Theory]
        [InlineData("50%")]
        [InlineData("10px 50%")]
        [InlineData("50% 10px")]
        public void PercentageDisallowed_RejectsAnyPercentageComponent(string value)
        {
            Assert.False(CssValueParser.IsValidLengthList(value, 1, 2, allowPercentage: false));
        }

        [Theory]
        [InlineData("10pt")]
        [InlineData("10pt 20pt")]
        [InlineData("0")]
        public void PercentageDisallowed_StillAcceptsPlainLengths(string value)
        {
            Assert.True(CssValueParser.IsValidLengthList(value, 1, 2, allowPercentage: false));
        }

        [Fact]
        public void MinBoundNotMet_ReturnsFalse()
        {
            // min:2 requires at least two components; a single one is rejected.
            Assert.False(CssValueParser.IsValidLengthList("10px", 2, 2, allowPercentage: true));
        }

        // calc()-wrapped percentage isn't excluded by the '%'-suffix guard - a documented, pre-existing
        // looseness inherited from IsValidLength's own calc-family shortcut (not a new gap).
        [Fact]
        public void PercentageDisallowed_CalcWrappedPercentage_StillAcceptedByExistingLooseness()
        {
            Assert.True(CssValueParser.IsValidLengthList("calc(50% + 10px)", 1, 2, allowPercentage: false));
        }
    }
}

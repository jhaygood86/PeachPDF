namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Tests for the shared <see cref="AspectRatioGrammar"/> (the <c>aspect-ratio</c> value grammar
    /// <c>[ auto || &lt;ratio&gt; ]</c>) and its Layer-A accept/reject via the full parser.
    /// </summary>
    public class AspectRatioGrammarTests : CssConstructionFunctions
    {
        private static bool TryParse(string value, out double? ratio) =>
            AspectRatioGrammar.TryParse(CssValueParser.GetCssTokens(value), out ratio);

        [Theory]
        [InlineData("2", 2.0)]
        [InlineData("16 / 9", 16.0 / 9.0)]
        [InlineData("16/9", 16.0 / 9.0)]
        [InlineData("1.5", 1.5)]
        [InlineData("3 / 2", 1.5)]
        [InlineData("auto 21 / 9", 21.0 / 9.0)]
        [InlineData("21 / 9 auto", 21.0 / 9.0)]
        public void ValidRatio_ParsesToWidthOverHeight(string value, double expected)
        {
            Assert.True(TryParse(value, out var ratio));
            Assert.NotNull(ratio);
            Assert.Equal(expected, ratio!.Value, 5);
        }

        [Theory]
        [InlineData("auto")]
        [InlineData("1 / 0")] // a zero term => no usable ratio
        [InlineData("0")]
        [InlineData("0 / 5")]
        public void AutoOrZeroTerm_IsValidButHasNoUsableRatio(string value)
        {
            Assert.True(TryParse(value, out var ratio));
            Assert.Null(ratio);
        }

        [Theory]
        [InlineData("")]
        [InlineData("banana")]
        [InlineData("2 3")]        // two numbers without a slash
        [InlineData("16 /")]        // slash without a second number
        [InlineData("/ 9")]         // slash without a first number
        [InlineData("-2")]          // negative number
        [InlineData("2 / -3")]      // negative second term
        [InlineData("auto auto")]   // two autos
        [InlineData("2px")]         // a length, not a number
        public void Invalid_ReturnsFalse(string value)
        {
            Assert.False(TryParse(value, out _));
        }

        [Theory]
        [InlineData("aspect-ratio: 16 / 9", true)]
        [InlineData("aspect-ratio: auto", true)]
        [InlineData("aspect-ratio: 2", true)]
        [InlineData("aspect-ratio: banana", false)]
        [InlineData("aspect-ratio: 2px", false)]
        public void LayerA_AcceptsValid_RejectsInvalid(string declaration, bool shouldApply)
        {
            var sheet = ParseStyleSheet($"div {{ {declaration}; }}");
            var style = sheet.Rules.OfType<StyleRule>().Single().Style;
            var applied = !string.IsNullOrEmpty(style.GetPropertyValue("aspect-ratio"));
            Assert.Equal(shouldApply, applied);
        }
    }
}

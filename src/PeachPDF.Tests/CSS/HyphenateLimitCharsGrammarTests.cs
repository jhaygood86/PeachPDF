namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using System.Linq;
    using Xunit;

    /// <summary>
    /// Tests for the shared <see cref="HyphenateLimitCharsGrammar"/> (the <c>hyphenate-limit-chars</c>
    /// value grammar <c>[ auto | &lt;integer&gt; ]{1,3}</c>) and its Layer-A accept/reject via the full
    /// parser.
    /// </summary>
    public class HyphenateLimitCharsGrammarTests : CssConstructionFunctions
    {
        private static bool TryParse(string value, out int? word, out int? before, out int? after) =>
            HyphenateLimitCharsGrammar.TryParse(CssValueParser.GetCssTokens(value), out word, out before, out after);

        [Fact]
        public void Auto_AllThreeComponentsAreNull()
        {
            Assert.True(TryParse("auto", out var word, out var before, out var after));
            Assert.Null(word);
            Assert.Null(before);
            Assert.Null(after);
        }

        [Fact]
        public void ThreeIntegers_EachComponentParsed()
        {
            Assert.True(TryParse("5 2 3", out var word, out var before, out var after));
            Assert.Equal(5, word);
            Assert.Equal(2, before);
            Assert.Equal(3, after);
        }

        [Fact]
        public void TwoValues_AfterDefaultsToBefore()
        {
            Assert.True(TryParse("10 4", out var word, out var before, out var after));
            Assert.Equal(10, word);
            Assert.Equal(4, before);
            Assert.Equal(4, after);
        }

        [Fact]
        public void OneValue_BeforeAndAfterAreAuto()
        {
            Assert.True(TryParse("10", out var word, out var before, out var after));
            Assert.Equal(10, word);
            Assert.Null(before);
            Assert.Null(after);
        }

        [Fact]
        public void MixedAutoAndIntegers_EachComponentIndependent()
        {
            Assert.True(TryParse("auto 3 4", out var word, out var before, out var after));
            Assert.Null(word);
            Assert.Equal(3, before);
            Assert.Equal(4, after);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1 2 3 4")] // more than 3 values
        [InlineData("-1")] // negative integer
        [InlineData("banana")]
        [InlineData("1.5")] // not an integer
        public void Invalid_ReturnsFalse(string value)
        {
            Assert.False(TryParse(value, out _, out _, out _));
        }

        [Theory]
        [InlineData(5, 2, 2, "5 2 2")]
        [InlineData(null, null, null, "auto auto auto")]
        [InlineData(5, null, 4, "5 auto 4")]
        public void Serialize_RoundTripsThroughTryParse(int? word, int? before, int? after, string expected)
        {
            var serialized = HyphenateLimitCharsGrammar.Serialize(word, before, after);
            Assert.Equal(expected, serialized);

            Assert.True(TryParse(serialized, out var parsedWord, out var parsedBefore, out var parsedAfter));
            Assert.Equal(word, parsedWord);
            Assert.Equal(before, parsedBefore);
            Assert.Equal(after, parsedAfter);
        }

        [Fact]
        public void WithBefore_ReplacesOnlyTheBeforeComponent()
        {
            var merged = HyphenateLimitCharsGrammar.WithBefore("5 2 4", "9");
            Assert.True(TryParse(merged, out var word, out var before, out var after));
            Assert.Equal(5, word);
            Assert.Equal(9, before);
            Assert.Equal(4, after);
        }

        [Fact]
        public void WithAfter_ReplacesOnlyTheAfterComponent()
        {
            var merged = HyphenateLimitCharsGrammar.WithAfter("5 2 4", "9");
            Assert.True(TryParse(merged, out var word, out var before, out var after));
            Assert.Equal(5, word);
            Assert.Equal(2, before);
            Assert.Equal(9, after);
        }

        [Fact]
        public void WithBefore_StartingFromAuto_LeavesOtherComponentsAuto()
        {
            var merged = HyphenateLimitCharsGrammar.WithBefore("auto", "3");
            Assert.True(TryParse(merged, out var word, out var before, out var after));
            Assert.Null(word);
            Assert.Equal(3, before);
            Assert.Null(after);
        }

        [Theory]
        [InlineData("hyphenate-limit-chars: auto", true)]
        [InlineData("hyphenate-limit-chars: 5 2 2", true)]
        [InlineData("hyphenate-limit-chars: auto 3", true)]
        [InlineData("hyphenate-limit-chars: banana", false)]
        [InlineData("hyphenate-limit-chars: 1 2 3 4", false)]
        [InlineData("hyphenate-limit-chars: -1", false)]
        public void LayerA_AcceptsValid_RejectsInvalid(string declaration, bool shouldApply)
        {
            var sheet = ParseStyleSheet($"div {{ {declaration}; }}");
            var style = sheet.Rules.OfType<StyleRule>().Single().Style;
            var applied = !string.IsNullOrEmpty(style.GetPropertyValue("hyphenate-limit-chars"));
            Assert.Equal(shouldApply, applied);
        }
    }
}

using PeachPDF.CSS;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Unit tests for <see cref="FontStretchResolver"/> - the CSS Fonts 4 <c>font-stretch</c> keyword or percentage
    /// to the percentage of the normal width it stands for (the scale of a variable font's <c>wdth</c> axis).
    /// </summary>
    public class FontStretchResolverTests
    {
        [Theory]
        [InlineData(Keywords.UltraCondensed, 50)]
        [InlineData(Keywords.ExtraCondensed, 62.5)]
        [InlineData(Keywords.Condensed, 75)]
        [InlineData(Keywords.SemiCondensed, 87.5)]
        [InlineData(Keywords.Normal, 100)]
        [InlineData(Keywords.SemiExpanded, 112.5)]
        [InlineData(Keywords.Expanded, 125)]
        [InlineData(Keywords.ExtraExpanded, 150)]
        [InlineData(Keywords.UltraExpanded, 200)]
        public void Keyword_ResolvesToItsPercentage(string keyword, double expected)
        {
            Assert.Equal(expected, FontStretchResolver.Resolve(keyword));
        }

        [Theory]
        [InlineData("87.5%", 87.5)]
        [InlineData("  110% ", 110)]
        [InlineData("0%", 0)]
        [InlineData("300%", 300)]
        public void Percentage_ResolvesToTheNumberWritten(string value, double expected)
        {
            Assert.Equal(expected, FontStretchResolver.Resolve(value));
        }

        [Theory]
        [InlineData("not-a-real-keyword")]
        [InlineData("-5%")]
        [InlineData("%")]
        [InlineData("12")]
        [InlineData("wide%")]
        public void UnrecognizedValue_ResolvesToNormal(string value)
        {
            Assert.Equal(FontStretchResolver.Normal, FontStretchResolver.Resolve(value));
            Assert.False(FontStretchResolver.TryResolve(value, out _));
        }

        [Fact]
        public void TypedValue_ResolvesFromTheKeywordOrThePercentage()
        {
            Assert.Equal(75, FontStretchResolver.Resolve(new CssKeywordOrValue<FontStretch, double>(FontStretch.Condensed, null)));
            Assert.Equal(93.5, FontStretchResolver.Resolve(new CssKeywordOrValue<FontStretch, double>(null, 93.5)));
            Assert.Equal(FontStretchResolver.Normal, FontStretchResolver.Resolve(default(CssKeywordOrValue<FontStretch, double>)));
        }
    }
}

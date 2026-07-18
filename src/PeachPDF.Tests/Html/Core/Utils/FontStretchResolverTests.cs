using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Unit tests for <see cref="FontStretchResolver"/> - the CSS Fonts Level 3 <c>font-stretch</c>
    /// keyword to OS/2 <c>usWidthClass</c>-matching 1-9 numeric scale mapping.
    /// </summary>
    public class FontStretchResolverTests
    {
        [Theory]
        [InlineData(CssConstants.UltraCondensed, 1)]
        [InlineData(CssConstants.ExtraCondensed, 2)]
        [InlineData(CssConstants.Condensed, 3)]
        [InlineData(CssConstants.SemiCondensed, 4)]
        [InlineData(CssConstants.Normal, 5)]
        [InlineData(CssConstants.SemiExpanded, 6)]
        [InlineData(CssConstants.Expanded, 7)]
        [InlineData(CssConstants.ExtraExpanded, 8)]
        [InlineData(CssConstants.UltraExpanded, 9)]
        public void Keyword_ResolvesToExpectedNumericScale(string keyword, int expected)
        {
            Assert.Equal(expected, FontStretchResolver.Resolve(keyword));
        }

        [Fact]
        public void UnrecognizedKeyword_ResolvesToNormal()
        {
            Assert.Equal(FontStretchResolver.Normal, FontStretchResolver.Resolve("not-a-real-keyword"));
        }
    }
}

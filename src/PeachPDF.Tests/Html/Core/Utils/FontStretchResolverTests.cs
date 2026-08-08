using PeachPDF.CSS;
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
        [InlineData(Keywords.UltraCondensed, 1)]
        [InlineData(Keywords.ExtraCondensed, 2)]
        [InlineData(Keywords.Condensed, 3)]
        [InlineData(Keywords.SemiCondensed, 4)]
        [InlineData(Keywords.Normal, 5)]
        [InlineData(Keywords.SemiExpanded, 6)]
        [InlineData(Keywords.Expanded, 7)]
        [InlineData(Keywords.ExtraExpanded, 8)]
        [InlineData(Keywords.UltraExpanded, 9)]
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

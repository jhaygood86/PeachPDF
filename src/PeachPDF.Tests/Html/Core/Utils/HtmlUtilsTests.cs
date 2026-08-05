namespace PeachPDF.Tests.Html.Core.Utils
{
    using HtmlUtils = PeachPDF.Html.Core.Utils.HtmlUtils;

    public class HtmlUtilsTests
    {
        [Theory]
        [InlineData("&#65;", "A")]
        [InlineData("&#65;&#66;&#67;", "ABC")]
        [InlineData("&#x41;", "A")]
        [InlineData("&#X41;", "A")]
        [InlineData("Hello &#65; World", "Hello A World")]
        public void DecodeHtml_NumericCharacterReference_DecodesToCharacter(string input, string expected)
        {
            Assert.Equal(expected, HtmlUtils.DecodeHtml(input));
        }

        [Fact]
        public void DecodeHtml_NumericCharacterReferenceWithTrailingSemicolon_ConsumesSemicolon()
        {
            Assert.Equal("A rest", HtmlUtils.DecodeHtml("&#65; rest"));
        }

        [Fact]
        public void DecodeHtml_OutOfRangeCodePoint_DecodesToReplacementCharacter()
        {
            // WHATWG HTML 13.2.5.80 numeric-character-reference-end-state: an out-of-range code point
            // resolves to U+FFFD REPLACEMENT CHARACTER, not to nothing.
            Assert.Equal("�", HtmlUtils.DecodeHtml("&#x110000;"));
        }

        [Fact]
        public void DecodeHtml_SurrogateRangeCodePoint_DecodesToReplacementCharacter()
        {
            Assert.Equal("�", HtmlUtils.DecodeHtml("&#xD800;"));
        }

        [Fact]
        public void DecodeHtml_NullCodePoint_DecodesToReplacementCharacter()
        {
            Assert.Equal("�", HtmlUtils.DecodeHtml("&#0;"));
        }

        [Fact]
        public void DecodeHtml_NoEntities_ReturnsUnchanged()
        {
            Assert.Equal("plain text", HtmlUtils.DecodeHtml("plain text"));
        }

        [Fact]
        public void DecodeHtml_NullOrEmpty_ReturnsInput()
        {
            Assert.Null(HtmlUtils.DecodeHtml(null!));
            Assert.Equal(string.Empty, HtmlUtils.DecodeHtml(string.Empty));
        }
    }
}

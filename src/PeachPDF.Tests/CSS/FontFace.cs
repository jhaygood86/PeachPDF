namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class FontFaceTests : CssConstructionFunctions
    {
        [Fact]
        public void FontFaceOpenSansWithSource()
        {
            var src = "@font-face{font-family:'Open Sans';src:url(fonts/OpenSans-Light.eot);src:local('Open Sans Light'),local('OpenSans-Light'),url(fonts/OpenSans-Light.ttf) format('truetype'),url(fonts/OpenSans-Light.woff) format('woff');font-style:normal}";
            var sheet = ParseStyleSheet(src);
            Assert.NotNull(sheet);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<FontFaceRule>(sheet.Rules[0]);
            var fontface = (IFontFaceRule)sheet.Rules[0];
            Assert.Equal("\"Open Sans\"", fontface.Family);
            Assert.Equal("", fontface.Features);
            Assert.Equal("", fontface.Range);
            Assert.NotEqual("", fontface.Source);
            Assert.Equal("", fontface.Stretch);
            Assert.Equal("normal", fontface.Style);
            Assert.Equal("", fontface.Variant);
            Assert.Equal("", fontface.Weight);
        }

        [Fact]
        public void FontFaceWithWoff2Source()
        {
            var src = "@font-face{font-family:'Inter';src:url(fonts/Inter-Medium.woff2) format('woff2');font-weight:500}";
            var sheet = ParseStyleSheet(src);
            Assert.NotNull(sheet);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<FontFaceRule>(sheet.Rules[0]);
            var fontface = (IFontFaceRule)sheet.Rules[0];
            Assert.Equal("\"Inter\"", fontface.Family);
            Assert.Contains("Inter-Medium.woff2", fontface.Source);
            Assert.Contains("woff2", fontface.Source);
            Assert.Equal("500", fontface.Weight);
        }

        [Fact]
        public void FontFaceOpenSansNoSource()
        {
            var src = "@font-face{font-family:'Open Sans';font-style:normal}";
            var sheet = ParseStyleSheet(src);
            Assert.NotNull(sheet);
            Assert.Equal(1, sheet.Rules.Length);
            Assert.IsType<FontFaceRule>(sheet.Rules[0]);
            var fontface = (IFontFaceRule)sheet.Rules[0];
            Assert.Equal("\"Open Sans\"", fontface.Family);
            Assert.Equal("", fontface.Features);
            Assert.Equal("", fontface.Range);
            Assert.Equal("", fontface.Source);
            Assert.Equal("", fontface.Stretch);
            Assert.Equal("normal", fontface.Style);
            Assert.Equal("", fontface.Variant);
            Assert.Equal("", fontface.Weight);
        }

        [Theory]
        [InlineData("U+41-5A")]
        [InlineData("U+0-7F")]
        [InlineData("U+4??")]
        [InlineData("U+000041")]
        [InlineData("U+0025-00FF, U+4??")]
        public void FontFaceUnicodeRangeIsRetained(string range)
        {
            // Data holds the range without its "U+" prefix, so a retained descriptor used to serialize as
            // "41-5A" - not a valid <urange> and not what was authored.
            var src = "@font-face{font-family:'Open Sans';unicode-range:" + range + "}";
            var sheet = ParseStyleSheet(src);
            Assert.Equal(1, sheet.Rules.Length);
            var fontface = (IFontFaceRule)sheet.Rules[0];

            Assert.Equal(range, fontface.Range);
        }

        [Theory]
        // The wildcard budget is captured up front; re-evaluating "6 - StringBuffer.Length" while appending
        // to that same buffer consumed only half the available wildcards, so U+?????? stopped after three.
        [InlineData("U+4??", "400", "4FF")]
        [InlineData("U+??????", "000000", "FFFFFF")]
        [InlineData("U+41", "41", "41")]
        [InlineData("U+41-5A", "41", "5A")]
        [InlineData("U+0-7F", "0", "7F")]
        public void UnicodeRangeTokenStartAndEnd(string source, string start, string end)
        {
            var lexer = new Lexer(new TextSource(source));
            var range = Assert.IsType<RangeToken>(lexer.Get());

            Assert.Equal(start, range.Start);
            Assert.Equal(end, range.End);
            Assert.Equal(TokenType.EndOfFile, lexer.Get().Type);
        }
    }
}








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
    }
}








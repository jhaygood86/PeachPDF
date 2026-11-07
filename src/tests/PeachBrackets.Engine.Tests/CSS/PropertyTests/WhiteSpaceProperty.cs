namespace PeachBrackets.Engine.Tests.CSS.PropertyTests
{
    using PeachBrackets.Css;
    using System.Linq;
    using Xunit;

    public class WhiteSpacePropertyTests : CssConstructionFunctions
    {
        private StyleDeclaration ParseStyle(string declaration) =>
            ((StyleRule)ParseStyleSheet($".test {{ {declaration}; }}").StyleRules.First()).Style;

        [Theory]
        [InlineData("normal")]
        [InlineData("pre")]
        [InlineData("nowrap")]
        [InlineData("pre-wrap")]
        [InlineData("pre-line")]
        public void WhiteSpaceKeywordLegal(string keyword)
        {
            var property = ParseDeclaration($"white-space: {keyword}");
            Assert.Equal("white-space", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<WhiteSpaceProperty>(property);
            Assert.True(property.HasValue);
            Assert.Equal(keyword, property.Value);
        }

        [Fact]
        public void WhiteSpaceInvalidKeywordIllegal()
        {
            var property = ParseDeclaration("white-space: wavy");
            Assert.IsType<WhiteSpaceProperty>(property);
            Assert.False(property.HasValue);
        }

        [Theory]
        [InlineData("white-space: pre", "preserve", "nowrap")]
        [InlineData("white-space: pre-wrap", "preserve", "wrap")]
        [InlineData("white-space: pre-line", "preserve-breaks", "wrap")]
        [InlineData("white-space: nowrap", "collapse", "nowrap")]
        [InlineData("white-space: normal", "collapse", "wrap")]
        [InlineData("white-space: break-spaces nowrap", "break-spaces", "nowrap")]
        [InlineData("white-space: nowrap preserve-spaces", "preserve-spaces", "nowrap")]
        [InlineData("white-space: preserve-breaks", "preserve-breaks", "initial")]
        public void WhiteSpaceExpandsToLonghands(string declaration, string collapse, string mode)
        {
            var style = ParseStyle(declaration);
            Assert.Equal(collapse, style.GetPropertyValue("white-space-collapse"));
            Assert.Equal(mode, style.GetPropertyValue("text-wrap-mode"));
        }

        [Theory]
        [InlineData("text-wrap: balance", "initial", "balance")]
        [InlineData("text-wrap: nowrap", "nowrap", "initial")]
        [InlineData("text-wrap: nowrap pretty", "nowrap", "pretty")]
        [InlineData("text-wrap: stable wrap", "wrap", "stable")]
        [InlineData("text-wrap: wrap", "wrap", "initial")]
        public void TextWrapExpandsToLonghands(string declaration, string mode, string style)
        {
            var parsed = ParseStyle(declaration);
            Assert.Equal(mode, parsed.GetPropertyValue("text-wrap-mode"));
            Assert.Equal(style, parsed.GetPropertyValue("text-wrap-style"));
        }

        [Theory]
        [InlineData("white-space-collapse", "preserve-spaces")]
        [InlineData("white-space-collapse", "break-spaces")]
        [InlineData("text-wrap-mode", "nowrap")]
        [InlineData("text-wrap-style", "pretty")]
        public void LonghandKeywordLegal(string name, string keyword)
        {
            var property = ParseDeclaration($"{name}: {keyword}");
            Assert.Equal(name, property.Name);
            Assert.True(property.HasValue);
            Assert.Equal(keyword, property.Value);
        }

        [Fact]
        public void TextWrapInvalidKeywordIllegal()
        {
            Assert.False(ParseDeclaration("text-wrap: wavy").HasValue);
        }
    }
}

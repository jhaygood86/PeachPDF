namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    public class FontPaletteParsingTests : CssConstructionFunctions
    {
        private string FontPalette(string value)
        {
            var sheet = ParseStyleSheet($".x {{ font-palette: {value}; }}");
            var rule = sheet.Rules.OfType<StyleRule>().Single();
            return rule.Style.GetPropertyValue("font-palette");
        }

        [Theory]
        [InlineData("normal")]
        [InlineData("light")]
        [InlineData("dark")]
        [InlineData("--my-palette")]
        [InlineData("palette-mix(in oklab, --a, --b)")]
        [InlineData("palette-mix(in lch longer hue, normal 30%, --b 70%)")]
        public void FontPalette_AcceptsValidValues(string value)
        {
            Assert.Equal(value, FontPalette(value));
        }

        [Theory]
        [InlineData("5px")]
        [InlineData("#fff")]
        [InlineData("rgb(0,0,0)")]
        [InlineData("palette-mix(in oklab, --a)")]              // needs two operands
        [InlineData("palette-mix(in bogusspace, --a, --b)")]   // unknown color space
        [InlineData("palette-mix(--a, --b)")]                  // missing color-interpolation-method
        public void FontPalette_DropsInvalidValues(string value)
        {
            Assert.Equal(string.Empty, FontPalette(value));
        }

        [Fact]
        public void FontPaletteValues_ParsesNameAndDescriptors()
        {
            var src = "@font-palette-values --brand { font-family: \"Nabla\"; base-palette: 2; override-colors: 0 #ff0000, 1 rgb(0, 255, 0); }";
            var sheet = ParseStyleSheet(src);

            var rule = Assert.IsType<FontPaletteValuesRule>(sheet.Rules.OfType<FontPaletteValuesRule>().Single());
            Assert.Equal("--brand", rule.Name);
            Assert.Contains("Nabla", rule.Family);
            Assert.Equal("2", rule.BasePalette);
            Assert.Contains("#ff0000", rule.OverrideColors);
            Assert.Contains("rgb(0, 255, 0)", rule.OverrideColors);
        }

        [Fact]
        public void FontPaletteValues_LightDarkBasePalette()
        {
            var sheet = ParseStyleSheet("@font-palette-values --d { font-family: Nabla; base-palette: dark; }");
            var rule = sheet.Rules.OfType<FontPaletteValuesRule>().Single();
            Assert.Equal("dark", rule.BasePalette);
        }

        [Fact]
        public void FontPaletteValues_DoesNotDerailFollowingRules()
        {
            var src = "@font-palette-values --p { font-family: Nabla; base-palette: 1; } .after { color: red; }";
            var sheet = ParseStyleSheet(src);

            Assert.Single(sheet.Rules.OfType<FontPaletteValuesRule>());
            var styleRule = sheet.Rules.OfType<StyleRule>().Single();
            Assert.Equal(".after", styleRule.SelectorText);
            Assert.Equal("rgb(255, 0, 0)", styleRule.Style.GetPropertyValue("color"));
        }

        [Fact]
        public void FontPaletteValues_NoDeclarationBlock_DoesNotCrashAndFollowingRuleApplies()
        {
            var sheet = ParseStyleSheet("@font-palette-values --x; .after { color: red; }");
            var styleRule = sheet.Rules.OfType<StyleRule>().Single();
            Assert.Equal(".after", styleRule.SelectorText);
        }
    }
}

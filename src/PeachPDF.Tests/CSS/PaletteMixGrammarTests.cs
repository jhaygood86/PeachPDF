namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using Xunit;

    public class PaletteMixGrammarTests
    {
        private static ParsedPaletteMix? Parse(string value) =>
            PaletteMixGrammar.TryParse(CssValueParser.GetCssTokens(value));

        [Fact]
        public void Parses_SpaceAndTwoOperands()
        {
            var mix = Parse("palette-mix(in oklab, normal, --brand)");
            Assert.NotNull(mix);
            Assert.Equal("oklab", mix!.ColorSpace);
            Assert.Null(mix.HueMethod);
            Assert.Equal("normal", mix.First.Palette);
            Assert.Null(mix.First.Percentage);
            Assert.Equal("--brand", mix.Second.Palette);
        }

        [Fact]
        public void Parses_MixedCaseSpaceAndKeywords()
        {
            // CSS keywords are case-insensitive; the space/hue names must still parse (and later map).
            var mix = Parse("palette-mix(in OKLCH LONGER HUE, --a, --b)");
            Assert.NotNull(mix);
            Assert.Equal("OKLCH", mix!.ColorSpace);
            Assert.Equal("LONGER", mix.HueMethod);
        }

        [Fact]
        public void Parses_PercentagesAndPolarHueMethod()
        {
            var mix = Parse("palette-mix(in lch longer hue, --a 25%, --b 75%)");
            Assert.NotNull(mix);
            Assert.Equal("lch", mix!.ColorSpace);
            Assert.Equal("longer", mix.HueMethod);
            Assert.Equal(25, mix.First.Percentage);
            Assert.Equal(75, mix.Second.Percentage);
        }

        [Theory]
        [InlineData("palette-mix(in oklab, --a)")]                 // one operand
        [InlineData("palette-mix(in oklab, --a, --b, --c)")]       // three operands
        [InlineData("palette-mix(--a, --b)")]                      // no interpolation method
        [InlineData("palette-mix(in bogus, --a, --b)")]            // unknown space
        [InlineData("palette-mix(in srgb longer hue, --a, --b)")]  // hue method on rectangular space
        [InlineData("palette-mix(in lch longer, --a, --b)")]       // hue method missing trailing 'hue'
        [InlineData("palette-mix(in oklab, 5px, --b)")]            // non-palette operand
        [InlineData("palette-mix(in oklab, normal 0%, --b 0%)")]   // both percentages zero
        [InlineData("not-a-mix(in oklab, --a, --b)")]              // wrong function name
        public void Rejects_Malformed(string value)
        {
            Assert.Null(Parse(value));
        }
    }
}

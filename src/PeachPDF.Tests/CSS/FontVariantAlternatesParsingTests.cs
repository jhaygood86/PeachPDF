namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    public class FontVariantAlternatesParsingTests : CssConstructionFunctions
    {
        private string FontVariantAlternates(string value)
        {
            var sheet = ParseStyleSheet($".x {{ font-variant-alternates: {value}; }}");
            var rule = sheet.Rules.OfType<StyleRule>().Single();
            return rule.Style.GetPropertyValue("font-variant-alternates");
        }

        [Theory]
        [InlineData("normal")]
        [InlineData("historical-forms")]
        [InlineData("stylistic(foo)")]
        [InlineData("swash(bar)")]
        [InlineData("ornaments(baz)")]
        [InlineData("annotation(qux)")]
        [InlineData("styleset(nice-style)")]
        [InlineData("styleset(nice-style, other-style)")]
        [InlineData("character-variant(flourish)")]
        [InlineData("character-variant(flourish, swoosh)")]
        [InlineData("stylistic(foo) historical-forms")]
        [InlineData("historical-forms styleset(a, b) swash(c)")]
        public void FontVariantAlternates_AcceptsValidValues(string value)
        {
            Assert.Equal(value, FontVariantAlternates(value));
        }

        [Theory]
        [InlineData("5px")]
        [InlineData("bogus")]
        [InlineData("stylistic()")]                       // needs exactly one ident
        [InlineData("stylistic(foo, bar)")]                // stylistic() only takes one ident
        [InlineData("styleset()")]                         // needs at least one ident
        [InlineData("styleset(1)")]                        // not an ident
        [InlineData("stylistic(foo) stylistic(bar)")]       // each clause at most once
        [InlineData("historical-forms historical-forms")]  // each clause at most once
        public void FontVariantAlternates_DropsInvalidValues(string value)
        {
            Assert.Equal(string.Empty, FontVariantAlternates(value));
        }

        [Fact]
        public void FontVariant_ShorthandIncludesAlternates()
        {
            var decl = ParseDeclarations("font-variant: small-caps styleset(nice-style) historical-forms;");
            Assert.Equal("small-caps", decl.GetPropertyValue("font-variant-caps"));
            Assert.Equal("styleset(nice-style) historical-forms", decl.GetPropertyValue("font-variant-alternates"));
        }

        [Fact]
        public void FontVariantAlternatesGrammar_Construct_DelegatesToGuard()
        {
            // Construct(Property[]) is the shorthand-reconstruction half of IValueConverter - exercised
            // directly, mirroring FontVariantLigaturesValueConverter's own coverage-closing test.
            var converter = new FontVariantAlternatesGrammar();
            var value = converter.Construct([]);

            Assert.Null(value);
        }

        [Fact]
        public void FontVariant_ShorthandResetsAlternatesWhenNotMentioned()
        {
            // A longhand a later shorthand declaration doesn't mention is reset to the CSS-wide "initial"
            // keyword at this raw-declaration layer (resolved to the property's actual initial value -
            // "normal" - later in the cascade), the same shape every other reset-but-unmentioned
            // font-variant-* longhand already takes here.
            var decl = ParseDeclarations("font-variant-alternates: styleset(nice-style); font-variant: small-caps;");
            Assert.Equal("initial", decl.GetPropertyValue("font-variant-alternates"));
        }
    }
}

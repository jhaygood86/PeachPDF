namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System.Linq;
    using Xunit;

    public class FontFeatureValuesParsingTests : CssConstructionFunctions
    {
        [Fact]
        public void FontFeatureValues_ParsesSingleFamilyAndBlock()
        {
            var src = "@font-feature-values MyFont { @styleset { nice-style: 12; } }";
            var sheet = ParseStyleSheet(src);

            var rule = Assert.IsType<FontFeatureValuesRule>(sheet.Rules.OfType<FontFeatureValuesRule>().Single());
            Assert.Contains("MyFont", rule.FamilyList);

            var block = Assert.IsType<FontFeatureValueSetRule>(rule.Rules.OfType<FontFeatureValueSetRule>().Single());
            Assert.Equal("styleset", block.BlockName);

            var declaration = block.Declarations.Single();
            Assert.Equal("nice-style", declaration.Name);
            Assert.Equal("12", declaration.Value);
        }

        [Fact]
        public void FontFeatureValues_ParsesMultipleFamilies()
        {
            var src = "@font-feature-values Font One, \"Font Two\" { @swash { flowing: 1; } }";
            var sheet = ParseStyleSheet(src);

            var rule = sheet.Rules.OfType<FontFeatureValuesRule>().Single();
            Assert.Contains("Font One", rule.FamilyList);
            Assert.Contains("Font Two", rule.FamilyList);
        }

        [Theory]
        [InlineData("styleset")]
        [InlineData("character-variant")]
        [InlineData("swash")]
        [InlineData("ornaments")]
        [InlineData("annotation")]
        [InlineData("stylistic")]
        public void FontFeatureValues_ParsesEachBlockKind(string blockName)
        {
            var src = $"@font-feature-values MyFont {{ @{blockName} {{ foo: 1; }} }}";
            var sheet = ParseStyleSheet(src);

            var rule = sheet.Rules.OfType<FontFeatureValuesRule>().Single();
            var block = rule.Rules.OfType<FontFeatureValueSetRule>().Single();
            Assert.Equal(blockName, block.BlockName);
            Assert.Equal("1", block.Declarations.Single().Value);
        }

        [Fact]
        public void FontFeatureValues_ParsesMultipleIntegerValues()
        {
            var src = "@font-feature-values MyFont { @styleset { nice-style: 1 7; } }";
            var sheet = ParseStyleSheet(src);

            var block = sheet.Rules.OfType<FontFeatureValuesRule>().Single().Rules.OfType<FontFeatureValueSetRule>().Single();
            Assert.Equal("1 7", block.Declarations.Single().Value);
        }

        [Fact]
        public void FontFeatureValues_ParsesMultipleBlocksAndDeclarations()
        {
            var src = "@font-feature-values MyFont { " +
                      "@styleset { nice-style: 1; alternate-style: 2; } " +
                      "@character-variant { flourish: 12 3; } " +
                      "}";
            var sheet = ParseStyleSheet(src);

            var rule = sheet.Rules.OfType<FontFeatureValuesRule>().Single();
            var blocks = rule.Rules.OfType<FontFeatureValueSetRule>().ToList();
            Assert.Equal(2, blocks.Count);

            var styleset = blocks.Single(b => b.BlockName == "styleset");
            Assert.Equal(2, styleset.Declarations.Count());

            var characterVariant = blocks.Single(b => b.BlockName == "character-variant");
            Assert.Equal("12 3", characterVariant.Declarations.Single().Value);
        }

        [Fact]
        public void FontFeatureValues_UnknownNestedBlockIsIgnored()
        {
            var src = "@font-feature-values MyFont { @bogus { foo: 1; } @styleset { real: 1; } }";
            var sheet = ParseStyleSheet(src);

            var rule = sheet.Rules.OfType<FontFeatureValuesRule>().Single();
            var block = rule.Rules.OfType<FontFeatureValueSetRule>().Single();
            Assert.Equal("styleset", block.BlockName);
        }

        [Fact]
        public void FontFeatureValues_DoesNotDerailFollowingRules()
        {
            var src = "@font-feature-values MyFont { @styleset { nice-style: 1; } } .after { color: red; }";
            var sheet = ParseStyleSheet(src);

            Assert.Single(sheet.Rules.OfType<FontFeatureValuesRule>());
            var styleRule = sheet.Rules.OfType<StyleRule>().Single();
            Assert.Equal(".after", styleRule.SelectorText);
            Assert.Equal("rgb(255, 0, 0)", styleRule.Style.GetPropertyValue("color"));
        }

        [Fact]
        public void FontFeatureValues_NoDeclarationBlock_DoesNotCrashAndFollowingRuleApplies()
        {
            var sheet = ParseStyleSheet("@font-feature-values MyFont; .after { color: red; }");
            var styleRule = sheet.Rules.OfType<StyleRule>().Single();
            Assert.Equal(".after", styleRule.SelectorText);
        }

        [Fact]
        public void FontFeatureValues_DoublyNestedStrayContent_IsSkippedAsAWhole()
        {
            var src = "@font-feature-values MyFont { @bogus { @nested { x: 1; } } @styleset { real: 1; } }";
            var sheet = ParseStyleSheet(src);

            var block = sheet.Rules.OfType<FontFeatureValuesRule>().Single().Rules.OfType<FontFeatureValueSetRule>().Single();
            Assert.Equal("styleset", block.BlockName);
            Assert.Equal("1", block.Declarations.Single().Value);
        }

        [Fact]
        public void FontFeatureValues_BareStrayContentBeforeClosingBrace_DoesNotSwallowFollowingRules()
        {
            // Regression: bare stray content with no ';'/'{...}' of its own that runs directly into
            // @font-feature-values's own closing '}' must not be mistaken for that block's own content -
            // the whole rest of the stylesheet was previously silently dropped.
            var sheet = ParseStyleSheet("@font-feature-values MyFont { @styleset { nice-style: 1; } bogus } .after { color: red; }");

            var block = sheet.Rules.OfType<FontFeatureValuesRule>().Single().Rules.OfType<FontFeatureValueSetRule>().Single();
            Assert.Equal("1", block.Declarations.Single().Value);

            var styleRule = sheet.Rules.OfType<StyleRule>().Single();
            Assert.Equal(".after", styleRule.SelectorText);
        }

        [Fact]
        public void FontFeatureValues_NestedBlockWithNoDeclarationBlock_DoesNotCrashAndFollowingRuleApplies()
        {
            var sheet = ParseStyleSheet("@font-feature-values MyFont { @styleset; @swash { real: 1; } } .after { color: red; }");
            var styleRule = sheet.Rules.OfType<StyleRule>().Single();
            Assert.Equal(".after", styleRule.SelectorText);
        }

        [Fact]
        public void FontFeatureValuesRule_ToCssAndReplaceWith_RoundTrip()
        {
            var sheet = ParseStyleSheet("@font-feature-values MyFont { @styleset { nice-style: 1; } }");
            var rule = sheet.Rules.OfType<FontFeatureValuesRule>().Single();

            var css = rule.Text;
            Assert.Contains("@font-feature-values", css);
            Assert.Contains("MyFont", css);
            Assert.Contains("@styleset", css);
            Assert.Contains("nice-style", css);

            rule.Text = "@font-feature-values OtherFont { @swash { flowing: 2; } }";
            Assert.Contains("OtherFont", rule.FamilyList);
        }

        [Fact]
        public void FontFeatureValueSetRule_SetPropertyUsesCreateNewProperty()
        {
            var sheet = ParseStyleSheet("@font-feature-values MyFont { @styleset { nice-style: 1; } }");
            var block = sheet.Rules.OfType<FontFeatureValuesRule>().Single().Rules.OfType<FontFeatureValueSetRule>().Single();

            block.SetProperty("another-style", "3", null);

            Assert.Equal("3", block.GetPropertyValue("another-style"));
            Assert.Contains("another-style", block.Text);
        }
    }
}

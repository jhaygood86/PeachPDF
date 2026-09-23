using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Unit tests for <see cref="RegisteredFontFeatureValues"/> — the <c>@font-feature-values</c>
    /// registration model: family-list expansion, block-kind/name keying, and integer-list parsing.
    /// </summary>
    public class RegisteredFontFeatureValuesTests
    {
        private static CssData Data(string css)
        {
            var data = new CssData();
            data.Stylesheets.Add(CssParser.ParseStyleSheet(css));
            return data;
        }

        [Fact]
        public void BuildRegistry_RegistersSingleFamilyAndBlock()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values MyFont { @styleset { nice-style: 12; } }"));

            var key = RegisteredFontFeatureValues.MakeKey("MyFont", FontFeatureValueBlockKind.Styleset, "nice-style");
            Assert.True(registry.TryGetValue(key, out var registered));
            Assert.Equal("MyFont", registered!.Family);
            Assert.Equal(FontFeatureValueBlockKind.Styleset, registered.Kind);
            Assert.Equal("nice-style", registered.Name);
            Assert.Equal([12], registered.Values);
        }

        [Fact]
        public void BuildRegistry_ExpandsMultipleFamilies()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values \"Font One\", \"Font Two\" { @swash { fancy: 1; } }"));

            Assert.True(registry.ContainsKey(RegisteredFontFeatureValues.MakeKey("Font One", FontFeatureValueBlockKind.Swash, "fancy")));
            Assert.True(registry.ContainsKey(RegisteredFontFeatureValues.MakeKey("Font Two", FontFeatureValueBlockKind.Swash, "fancy")));
        }

        [Fact]
        public void BuildRegistry_ParsesMultipleIntegerValues()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values MyFont { @styleset { nice-style: 1 7; } }"));

            var key = RegisteredFontFeatureValues.MakeKey("MyFont", FontFeatureValueBlockKind.Styleset, "nice-style");
            Assert.Equal([1, 7], registry[key].Values);
        }

        [Fact]
        public void BuildRegistry_FamilyIsCaseInsensitiveNameIsCaseSensitive()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values MyFont { @styleset { Nice-Style: 1; } }"));

            Assert.True(registry.ContainsKey(RegisteredFontFeatureValues.MakeKey("MYFONT", FontFeatureValueBlockKind.Styleset, "Nice-Style")));
            Assert.False(registry.ContainsKey(RegisteredFontFeatureValues.MakeKey("MyFont", FontFeatureValueBlockKind.Styleset, "nice-style")));
        }

        [Fact]
        public void BuildRegistry_SameNameDifferentBlockKindsDoNotCollide()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values MyFont { @styleset { shared: 1; } @swash { shared: 2; } }"));

            var stylesetKey = RegisteredFontFeatureValues.MakeKey("MyFont", FontFeatureValueBlockKind.Styleset, "shared");
            var swashKey = RegisteredFontFeatureValues.MakeKey("MyFont", FontFeatureValueBlockKind.Swash, "shared");

            Assert.Equal([1], registry[stylesetKey].Values);
            Assert.Equal([2], registry[swashKey].Values);
        }

        [Fact]
        public void BuildRegistry_NonIntegerValueIsDropped()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values MyFont { @styleset { bogus: not-a-number; } }"));

            Assert.Empty(registry);
        }

        [Fact]
        public void BuildRegistry_NoFamilyIsDropped()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values { @styleset { nice-style: 1; } }"));

            Assert.Empty(registry);
        }

        [Fact]
        public void BuildRegistry_IgnoresABlockWhoseNameNormalParsingCanNeverProduce()
        {
            // The composer only ever creates a FontFeatureValueSetRule for one of the six known block
            // names (see FontFeatureValueSetRule.TryGetBlockKind, the single source of truth both the
            // composer and this registry call), so this constructs one directly - the only way to reach
            // that method's own defensive "not a known block name" fallback.
            var parser = new StylesheetParser();
            var outerRule = new FontFeatureValuesRule(parser) { FamilyList = "MyFont" };
            var rogueBlock = new FontFeatureValueSetRule(parser, "bogus-block");
            rogueBlock.SetProperty("foo", "1", null);
            outerRule.Rules.Add(rogueBlock);

            var sheet = new Stylesheet(parser);
            sheet.Rules.Add(outerRule);
            var data = new CssData();
            data.Stylesheets.Add(sheet);

            var registry = RegisteredFontFeatureValues.BuildRegistry(data);

            Assert.Empty(registry);
        }

        [Fact]
        public void BuildRegistry_LaterDuplicateRegistrationWins()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(Data(
                "@font-feature-values MyFont { @styleset { nice-style: 1; } } " +
                "@font-feature-values MyFont { @styleset { nice-style: 9; } }"));

            var key = RegisteredFontFeatureValues.MakeKey("MyFont", FontFeatureValueBlockKind.Styleset, "nice-style");
            Assert.Equal([9], registry[key].Values);
        }

        [Fact]
        public void BuildRegistry_QuotedFamilyNameContainingALiteralComma_IsNotMisSplit()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values \"My, Font\" { @styleset { nice-style: 1; } }"));

            var key = RegisteredFontFeatureValues.MakeKey("My, Font", FontFeatureValueBlockKind.Styleset, "nice-style");
            Assert.True(registry.ContainsKey(key));
        }

        [Fact]
        public void BuildRegistry_UnquotedMultiWordFamilyName_CollapsesInternalWhitespace()
        {
            // Style.Font.FontFamily (the lookup-side "used family") always joins ident-sequence family
            // names with exactly one space (ValueExtensions.ToLiterals) - a double space/tab in the
            // prelude must register under that same normalized key.
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values Times  New\tRoman { @styleset { nice-style: 1; } }"));

            var key = RegisteredFontFeatureValues.MakeKey("Times New Roman", FontFeatureValueBlockKind.Styleset, "nice-style");
            Assert.True(registry.ContainsKey(key));
        }

        [Theory]
        [InlineData("initial")]
        [InlineData("inherit")]
        [InlineData("unset")]
        [InlineData("revert")]
        [InlineData("default")]
        public void BuildRegistry_CssWideKeywordAsFeatureValueName_IsRejected(string name)
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data($"@font-feature-values MyFont {{ @styleset {{ {name}: 1; }} }}"));

            Assert.Empty(registry);
        }

        [Fact]
        public void BuildRegistry_StylisticWithTooManyIntegers_IsDroppedEntirely()
        {
            // @stylistic/@swash/@ornaments/@annotation each take exactly one integer (CSS Fonts 4 §6.8) -
            // an invalid declaration is dropped whole, not silently truncated to the first integer.
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values MyFont { @stylistic { fancy: 3 4; } }"));

            Assert.Empty(registry);
        }

        [Fact]
        public void BuildRegistry_CharacterVariantWithThreeIntegers_IsDroppedEntirely()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values MyFont { @character-variant { fancy: 1 2 3; } }"));

            Assert.Empty(registry);
        }

        [Fact]
        public void BuildRegistry_StylesetWithManyIntegers_IsAccepted()
        {
            var registry = RegisteredFontFeatureValues.BuildRegistry(
                Data("@font-feature-values MyFont { @styleset { fancy: 1 2 3 4 5; } }"));

            var key = RegisteredFontFeatureValues.MakeKey("MyFont", FontFeatureValueBlockKind.Styleset, "fancy");
            Assert.Equal([1, 2, 3, 4, 5], registry[key].Values);
        }
    }
}

using System.Collections.Generic;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Unit tests for <see cref="FontVariantAlternatesResolver"/> — resolving a cascaded
    /// <c>font-variant-alternates</c> value against a <c>@font-feature-values</c> registry into the
    /// OpenType GSUB <c>(tag, value)</c> pairs <see cref="PeachPDF.Text.TextShapingFeatures.ExplicitFeatures"/>
    /// consumes.
    /// </summary>
    public class FontVariantAlternatesResolverTests
    {
        private static IReadOnlyDictionary<(string, FontFeatureValueBlockKind, string), RegisteredFontFeatureValues> RegistryFor(string css)
        {
            var data = new CssData();
            data.Stylesheets.Add(CssParser.ParseStyleSheet(css));
            return RegisteredFontFeatureValues.BuildRegistry(data);
        }

        [Fact]
        public void Normal_ResolvesToEmpty()
        {
            var result = FontVariantAlternatesResolver.Resolve("normal", "MyFont", RegistryFor("@font-feature-values MyFont { @styleset { a: 1; } }"));
            Assert.Empty(result);
        }

        [Fact]
        public void HistoricalForms_ResolvesToHist()
        {
            var result = FontVariantAlternatesResolver.Resolve("historical-forms", "MyFont", null);
            Assert.Equal([("hist", 1)], result);
        }

        [Fact]
        public void Stylistic_ResolvesToSaltWithRegisteredValue()
        {
            var registry = RegistryFor("@font-feature-values MyFont { @stylistic { fancy: 3; } }");
            var result = FontVariantAlternatesResolver.Resolve("stylistic(fancy)", "MyFont", registry);
            Assert.Equal([("salt", 3)], result);
        }

        [Theory]
        [InlineData("swash(fancy)", "swsh")]
        [InlineData("ornaments(fancy)", "ornm")]
        [InlineData("annotation(fancy)", "nalt")]
        public void SingleFeatureFunctions_ResolveToTheirTag(string clause, string expectedTag)
        {
            var blockName = clause[..clause.IndexOf('(')];
            var registry = RegistryFor($"@font-feature-values MyFont {{ @{blockName} {{ fancy: 5; }} }}");
            var result = FontVariantAlternatesResolver.Resolve(clause, "MyFont", registry);
            Assert.Equal([(expectedTag, 5)], result);
        }

        [Fact]
        public void Styleset_TurnsOnEachListedFeatureAtValueOne()
        {
            var registry = RegistryFor("@font-feature-values MyFont { @styleset { nice-style: 1 7; } }");
            var result = FontVariantAlternatesResolver.Resolve("styleset(nice-style)", "MyFont", registry);
            Assert.Equal([("ss01", 1), ("ss07", 1)], result);
        }

        [Fact]
        public void Styleset_MultipleNamesEachResolve()
        {
            var registry = RegistryFor("@font-feature-values MyFont { @styleset { a: 1; b: 2; } }");
            var result = FontVariantAlternatesResolver.Resolve("styleset(a, b)", "MyFont", registry);
            Assert.Equal([("ss01", 1), ("ss02", 1)], result);
        }

        [Fact]
        public void CharacterVariant_FirstIntegerSelectsFeatureSecondSelectsValue()
        {
            var registry = RegistryFor("@font-feature-values MyFont { @character-variant { flourish: 12 3; } }");
            var result = FontVariantAlternatesResolver.Resolve("character-variant(flourish)", "MyFont", registry);
            Assert.Equal([("cv12", 3)], result);
        }

        [Fact]
        public void CharacterVariant_MissingSecondIntegerDefaultsToOne()
        {
            var registry = RegistryFor("@font-feature-values MyFont { @character-variant { flourish: 12; } }");
            var result = FontVariantAlternatesResolver.Resolve("character-variant(flourish)", "MyFont", registry);
            Assert.Equal([("cv12", 1)], result);
        }

        [Fact]
        public void UnmatchedName_IsInert()
        {
            var registry = RegistryFor("@font-feature-values MyFont { @styleset { real: 1; } }");
            var result = FontVariantAlternatesResolver.Resolve("styleset(bogus)", "MyFont", registry);
            Assert.Empty(result);
        }

        [Fact]
        public void FamilyMismatch_IsInert()
        {
            var registry = RegistryFor("@font-feature-values MyFont { @styleset { real: 1; } }");
            var result = FontVariantAlternatesResolver.Resolve("styleset(real)", "OtherFont", registry);
            Assert.Empty(result);
        }

        [Fact]
        public void MultipleClauses_AllResolve()
        {
            var registry = RegistryFor("@font-feature-values MyFont { @styleset { a: 1; } @swash { s: 2; } }");
            var result = FontVariantAlternatesResolver.Resolve("styleset(a) swash(s) historical-forms", "MyFont", registry);
            Assert.Equal([("ss01", 1), ("swsh", 2), ("hist", 1)], result);
        }

        [Fact]
        public void NullRegistry_IsInert()
        {
            var result = FontVariantAlternatesResolver.Resolve("stylistic(fancy)", "MyFont", null);
            Assert.Empty(result);
        }
    }
}

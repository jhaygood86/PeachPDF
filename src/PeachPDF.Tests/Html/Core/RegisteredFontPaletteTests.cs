using System.Collections.Generic;
using System.Linq;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Unit tests for <see cref="RegisteredFontPalette"/> — the <c>@font-palette-values</c> registration model:
    /// base-palette + override-colors parsing, invalid-rule rejection, and <c>(name, family)</c> keying.
    /// </summary>
    public class RegisteredFontPaletteTests
    {
        private static readonly CssValueParser ValueParser = new(new PdfSharpAdapter());

        private static RegisteredFontPalette? Register(string body)
        {
            var sheet = CssParser.ParseStyleSheet($"@font-palette-values --p {{ {body} }}");
            var rule = (FontPaletteValuesRule)sheet.Rules[0]!;
            return RegisteredFontPalette.FromRule(rule, ValueParser);
        }

        [Fact]
        public void IntegerBasePalette_IsRegistered()
        {
            var reg = Register("font-family: Nabla; base-palette: 3;");
            Assert.NotNull(reg);
            Assert.Equal("--p", reg!.Name);
            Assert.Equal("Nabla", reg.Family);
            Assert.Equal(FontPaletteBaseKind.Index, reg.BaseKind);
            Assert.Equal(3, reg.BaseIndex);
            Assert.Empty(reg.Overrides);
        }

        [Theory]
        [InlineData("light", "Light")]
        [InlineData("dark", "Dark")]
        public void LightDarkBasePalette_IsRegistered(string value, string expectedKind)
        {
            var reg = Register($"font-family: Nabla; base-palette: {value};");
            Assert.NotNull(reg);
            Assert.Equal(expectedKind, reg!.BaseKind.ToString());
        }

        [Fact]
        public void OverrideColors_AreParsed()
        {
            var reg = Register("font-family: Nabla; override-colors: 0 #ff0000, 2 rgb(0, 255, 0);");
            Assert.NotNull(reg);
            Assert.Equal(2, reg!.Overrides.Count);
            Assert.Equal(new KeyValuePair<int, RColor>(0, RColor.FromArgb(255, 255, 0, 0)), reg.Overrides[0]);
            Assert.Equal(new KeyValuePair<int, RColor>(2, RColor.FromArgb(255, 0, 255, 0)), reg.Overrides[1]);
        }

        [Fact]
        public void OverrideColors_SkipsMalformedEntries()
        {
            // A missing color, a non-numeric index, and a negative index are each skipped; the valid pair stays.
            var reg = Register("font-family: Nabla; override-colors: 0, x #fff, -1 #000, 3 #0000ff;");
            Assert.NotNull(reg);
            var kv = Assert.Single(reg!.Overrides);
            Assert.Equal(3, kv.Key);
            Assert.Equal(RColor.FromArgb(255, 0, 0, 255), kv.Value);
        }

        [Fact]
        public void InvalidName_NotDashedIdent_IsInvalid()
        {
            var sheet = CssParser.ParseStyleSheet("@font-palette-values notdashed { font-family: Nabla; }");
            var rule = (FontPaletteValuesRule)sheet.Rules[0]!;
            Assert.Null(RegisteredFontPalette.FromRule(rule, ValueParser));
        }

        [Fact]
        public void MissingBasePalette_DefaultsToIndexZero()
        {
            var reg = Register("font-family: Nabla;");
            Assert.NotNull(reg);
            Assert.Equal(FontPaletteBaseKind.Index, reg!.BaseKind);
            Assert.Equal(0, reg.BaseIndex);
        }

        [Fact]
        public void MissingFontFamily_IsInvalid()
        {
            Assert.Null(Register("base-palette: 2;"));
        }

        [Fact]
        public void QuotedFontFamily_IsUnquoted()
        {
            var reg = Register("font-family: \"My Font\"; base-palette: 1;");
            Assert.NotNull(reg);
            Assert.Equal("My Font", reg!.Family);
        }

        [Fact]
        public void BuildRegistry_KeysByNameAndFamily_CaseInsensitiveFamily()
        {
            var data = new CssData();
            data.Stylesheets.Add(CssParser.ParseStyleSheet(
                "@font-palette-values --a { font-family: Nabla; base-palette: 1; }" +
                "@font-palette-values --b { font-family: Other; base-palette: 2; }"));

            var registry = RegisteredFontPalette.BuildRegistry(data, ValueParser);

            Assert.True(registry.ContainsKey(RegisteredFontPalette.MakeKey("--a", "Nabla")));
            Assert.True(registry.ContainsKey(RegisteredFontPalette.MakeKey("--a", "NABLA")));   // family case-insensitive
            Assert.False(registry.ContainsKey(RegisteredFontPalette.MakeKey("--A", "Nabla"))); // name case-sensitive
            Assert.Equal(2, registry[RegisteredFontPalette.MakeKey("--b", "Other")].BaseIndex);
        }
    }
}

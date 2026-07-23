namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using PeachPDF.Html.Core.Parse;
    using Xunit;

    /// <summary>
    /// Tests for <see cref="CssProperty{T}"/> (the typed box-side property value) and the Layer A →
    /// <see cref="ITypedPropertyValue{T}"/> carrier that threads a converter's parse to the box without
    /// re-parsing.
    /// </summary>
    public class TypedCssPropertyTests
    {
        [Fact]
        public void FromValue_IsResolvedValue()
        {
            var p = CssProperty<string>.FromValue("100px 200px", "parsed");
            Assert.False(p.IsGlobalValue);
            Assert.False(p.IsUnresolved);
            Assert.Null(p.GlobalValue);
            Assert.Equal("parsed", p.Value);
            Assert.Equal("100px 200px", p.ToString());
        }

        [Fact]
        public void Unresolved_HasNoValue_AndKeepsRawText()
        {
            var p = CssProperty<string>.Unresolved("var(--cols)");
            Assert.True(p.IsUnresolved);
            Assert.False(p.IsGlobalValue);
            Assert.Null(p.Value);
            Assert.Equal("var(--cols)", p.ToString());
        }

        [Theory]
        [InlineData("inherit")]
        [InlineData("initial")]
        [InlineData("unset")]
        [InlineData("revert")]
        [InlineData("revert-layer")]
        public void Global_ExposesKeyword_AndRoundTripsText(string text)
        {
            Assert.True(CssGlobalKeywords.TryParse(text, out var keyword));
            var p = CssProperty<string>.Global(keyword);
            Assert.True(p.IsGlobalValue);
            Assert.False(p.IsUnresolved);
            Assert.Equal(keyword, p.GlobalValue);
            Assert.Null(p.Value);
            Assert.Equal(text, p.ToString());
        }

        [Theory]
        [InlineData("inherit")]
        [InlineData("INITIAL")]      // case-insensitive
        [InlineData("revert-layer")]
        public void CssGlobalKeywords_RoundTrip(string text)
        {
            Assert.True(CssGlobalKeywords.TryParse(text, out var keyword));
            Assert.Equal(text.ToLowerInvariant(), CssGlobalKeywords.ToText(keyword));
        }

        [Theory]
        [InlineData("100px")]
        [InlineData("auto")]
        [InlineData("")]
        public void CssGlobalKeywords_TryParse_RejectsNonKeywords(string text)
        {
            Assert.False(CssGlobalKeywords.TryParse(text, out _));
        }

        // ─── Layer A converter → typed carrier ──────────────────────────────────────

        private static CssProperty<GridTemplate>? ConvertTyped(string value)
        {
            var result = new GridTemplateValueConverter().Convert(CssValueParser.GetCssTokens(value));
            Assert.NotNull(result);
            Assert.True(result!.TryGetValue<GridTemplate>(out var typed));
            return typed;
        }

        [Fact]
        public void Converter_TrackList_CarriesParsedTemplate()
        {
            var typed = ConvertTyped("100px 200px");
            Assert.NotNull(typed);
            Assert.False(typed!.IsGlobalValue);
            Assert.NotNull(typed.Value);
            Assert.Equal(2, typed.Value!.Tracks.Count);
        }

        [Fact]
        public void Converter_None_IsResolvedWithNullTemplate()
        {
            // `none` is a resolved value whose parsed template is null (no explicit tracks), not a global keyword.
            var typed = ConvertTyped("none");
            Assert.NotNull(typed);
            Assert.False(typed!.IsGlobalValue);
            Assert.False(typed.IsUnresolved);
            Assert.Null(typed.Value);
        }

        [Fact]
        public void Converter_Subgrid_CarriesSubgridTemplate()
        {
            var typed = ConvertTyped("subgrid");
            Assert.NotNull(typed);
            Assert.NotNull(typed!.Value);
            Assert.True(typed.Value!.IsSubgrid);
        }

        [Fact]
        public void Converter_RejectsInvalidTemplate()
        {
            Assert.Null(new GridTemplateValueConverter().Convert(CssValueParser.GetCssTokens("banana")));
        }

        [Fact]
        public void TryGetValue_WrongType_ReturnsFalse()
        {
            // The carrier is an ITypedPropertyValue<GridTemplate>; asking for a different T must not match.
            var result = new GridTemplateValueConverter().Convert(CssValueParser.GetCssTokens("100pt"));
            Assert.NotNull(result);
            Assert.False(result!.TryGetValue<int>(out var typed));
            Assert.Null(typed);
        }
    }
}

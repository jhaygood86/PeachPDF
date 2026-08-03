namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    /// <summary>
    /// Tests for <see cref="CssKeywordOrValue{TEnum,TValue}"/> and <see cref="CssKeywordOrValueParser"/> —
    /// the "&lt;value&gt; | keyword" union backing properties like <c>z-index</c> (see
    /// <see cref="CssPropertyTests"/> for the plain <see cref="CssProperty{T}"/> factories this mirrors).
    /// Note: <see cref="CssProperty{T}"/>'s own <c>Value</c> is <typeparamref name="T"/> itself, not
    /// <c>Nullable&lt;T&gt;</c> — T is unconstrained on that class, so a struct T isn't Nullable-wrapped
    /// the way <see cref="CssKeywordOrValue{TEnum,TValue}"/>'s own (struct-constrained) Keyword/Value
    /// members are. When <c>IsGlobalValue</c>/<c>IsUnresolved</c> is set, the outer Value is simply
    /// <c>default(CssKeywordOrValue&lt;TEnum,TValue&gt;)</c> — a zeroed struct where both sides are unset.
    /// </summary>
    public class CssKeywordOrValueTests
    {
        [Fact]
        public void Constructor_BothNull_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new CssKeywordOrValue<AutoKeyword, int>(null, null));
        }

        [Fact]
        public void Constructor_BothProvided_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => new CssKeywordOrValue<AutoKeyword, int>(AutoKeyword.Auto, 5));
        }

        [Fact]
        public void FromCssText_Keyword_IsKeywordSide()
        {
            var p = CssKeywordOrValueParser.FromCssText<AutoKeyword, int>("auto", Map.AutoKeywords, int.TryParse, AutoKeyword.Auto);

            Assert.False(p.IsGlobalValue);
            Assert.False(p.IsUnresolved);
            Assert.True(p.Value is { IsKeyword: true, IsValue: false });
            Assert.Equal(AutoKeyword.Auto, p.Value.Keyword);
            Assert.Equal("auto", p.ToString());
        }

        [Fact]
        public void FromCssText_Value_IsValueSide()
        {
            var p = CssKeywordOrValueParser.FromCssText<AutoKeyword, int>("7", Map.AutoKeywords, int.TryParse, AutoKeyword.Auto);

            Assert.True(p.Value is { IsKeyword: false, IsValue: true, Value: 7 });
            Assert.Equal("7", p.ToString());
        }

        [Theory]
        [InlineData("inherit")]
        [InlineData("initial")]
        [InlineData("unset")]
        public void FromCssText_GlobalKeyword_IsGlobalValue(string text)
        {
            var p = CssKeywordOrValueParser.FromCssText<AutoKeyword, int>(text, Map.AutoKeywords, int.TryParse, AutoKeyword.Auto);

            Assert.True(p.IsGlobalValue);
            Assert.False(p.IsUnresolved);
            Assert.False(p.Value.IsKeyword);
            Assert.False(p.Value.IsValue);
            Assert.Equal(text, p.ToString());
        }

        [Fact]
        public void FromCssText_VarReference_IsUnresolved()
        {
            var p = CssKeywordOrValueParser.FromCssText<AutoKeyword, int>("var(--z)", Map.AutoKeywords, int.TryParse, AutoKeyword.Auto);

            Assert.True(p.IsUnresolved);
            Assert.False(p.IsGlobalValue);
            Assert.False(p.Value.IsKeyword);
            Assert.False(p.Value.IsValue);
            Assert.Equal("var(--z)", p.ToString());
        }

        [Fact]
        public void FromCssText_NeitherKeywordNorValue_FallsBackToKeyword()
        {
            // Set_* only ever calls FromCssText after Validate_* has already confirmed the value matches
            // one side of the union, so this path is unreachable through the real cascade — this proves
            // the fallback itself is total (never throws) rather than that any caller hits it.
            var p = CssKeywordOrValueParser.FromCssText<AutoKeyword, int>("banana", Map.AutoKeywords, int.TryParse, AutoKeyword.Auto);

            Assert.True(p.Value is { IsKeyword: true, Value: null });
            Assert.Equal(AutoKeyword.Auto, p.Value.Keyword);
        }

        [Fact]
        public void Equals_SameKeyword_AreEqual()
        {
            var a = new CssKeywordOrValue<AutoKeyword, int>(AutoKeyword.Auto, null);
            var b = new CssKeywordOrValue<AutoKeyword, int>(AutoKeyword.Auto, null);

            Assert.Equal(a, b);
        }

        [Fact]
        public void Equals_KeywordVsValue_AreNotEqual()
        {
            var keyword = new CssKeywordOrValue<AutoKeyword, int>(AutoKeyword.Auto, null);
            var value = new CssKeywordOrValue<AutoKeyword, int>(null, 0);

            Assert.NotEqual(keyword, value);
        }
    }
}

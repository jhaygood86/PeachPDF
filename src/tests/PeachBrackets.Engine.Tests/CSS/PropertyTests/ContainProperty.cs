namespace PeachBrackets.Engine.Tests.CSS.PropertyTests
{
    using System;
    using PeachBrackets.Css;
    using Xunit;

    public class ContainPropertyTests : CssConstructionFunctions
    {
        [Theory]
        [InlineData("none", "None")]
        [InlineData("strict", "Strict")]
        [InlineData("content", "Content")]
        [InlineData("size", "Size")]
        [InlineData("layout", "Layout")]
        [InlineData("style", "Style")]
        [InlineData("paint", "Paint")]
        [InlineData("size layout", "Size, Layout")]
        [InlineData("paint layout style size", "Strict")]
        [InlineData("style  paint", "Style, Paint")]
        [InlineData("LAYOUT Paint", "Layout, Paint")]
        // The three keywords are expanded once the four are spelled out.
        [InlineData("layout paint style", "Content")]
        public void ContainLegal(string value, string expected)
        {
            var property = ParseDeclaration($"contain: {value}");

            Assert.Equal("contain", property.Name);
            Assert.IsType<ContainProperty>(property);
            Assert.True(((ContainProperty)property).HasValue);
            Assert.True(ContainGrammar.TryParseContain(value, out var parsed));
            Assert.Equal(Enum.Parse<Containment>(expected), parsed);
        }

        [Theory]
        [InlineData("")]
        [InlineData("layout layout")]
        [InlineData("none layout")]
        [InlineData("layout none")]
        [InlineData("strict layout")]
        [InlineData("layout content")]
        [InlineData("strict content")]
        [InlineData("inline-size")]
        [InlineData("layout, paint")]
        [InlineData("nonsense")]
        public void ContainIllegal(string value)
        {
            var property = ParseDeclaration($"contain: {value}");

            Assert.IsType<ContainProperty>(property);
            Assert.False(((ContainProperty)property).HasValue);
            Assert.False(ContainGrammar.TryParseContain(value, out var parsed));
            Assert.Equal(Containment.None, parsed);
        }

        [Theory]
        [InlineData("visible")]
        [InlineData("auto")]
        [InlineData("hidden")]
        public void ContentVisibilityLegal(string value)
        {
            var property = ParseDeclaration($"content-visibility: {value}");

            Assert.IsType<ContentVisibilityProperty>(property);
            Assert.True(((ContentVisibilityProperty)property).HasValue);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("auto hidden")]
        [InlineData("collapse")]
        public void ContentVisibilityIllegal(string value)
        {
            var property = ParseDeclaration($"content-visibility: {value}");

            Assert.IsType<ContentVisibilityProperty>(property);
            Assert.False(((ContentVisibilityProperty)property).HasValue);
        }

        [Theory]
        [InlineData(null, null, "None")]
        [InlineData("none", "visible", "None")]
        [InlineData("none", "auto", "Content")]
        [InlineData("size", "auto", "Size, Layout, Style, Paint")]
        [InlineData("none", "hidden", "Strict")]
        [InlineData("paint", "visible", "Paint")]
        [InlineData("garbage", "visible", "None")]
        public void ContentVisibilityAddsItsContainmentToContain(string? contain, string? contentVisibility, string expected)
        {
            Assert.Equal(Enum.Parse<Containment>(expected), ContainGrammar.Of(contain, contentVisibility));
        }
    }
}

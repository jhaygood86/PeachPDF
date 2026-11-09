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
        [InlineData("contain-intrinsic-width", "none")]
        [InlineData("contain-intrinsic-width", "100px")]
        [InlineData("contain-intrinsic-height", "0")]
        [InlineData("contain-intrinsic-height", "auto 3em")]
        [InlineData("contain-intrinsic-inline-size", "auto none")]
        [InlineData("contain-intrinsic-block-size", "20pt")]
        public void ContainIntrinsicLonghandLegal(string name, string value)
        {
            var property = ParseDeclaration($"{name}: {value}");

            Assert.Equal(name, property.Name);
            Assert.False(property.IsInherited);
            Assert.True(property.HasValue);
        }

        [Theory]
        [InlineData("contain-intrinsic-width", "-1px")]
        [InlineData("contain-intrinsic-width", "50%")]
        [InlineData("contain-intrinsic-width", "auto")]
        [InlineData("contain-intrinsic-height", "10px 20px")]
        [InlineData("contain-intrinsic-height", "auto auto")]
        [InlineData("contain-intrinsic-block-size", "1")]
        [InlineData("contain-intrinsic-inline-size", "wide")]
        public void ContainIntrinsicLonghandIllegal(string name, string value)
        {
            var property = ParseDeclaration($"{name}: {value}");

            Assert.Equal(name, property.Name);
            Assert.False(property.HasValue);
        }

        [Theory]
        [InlineData("10px")]
        [InlineData("none")]
        [InlineData("10px 20px")]
        [InlineData("auto 10px")]
        [InlineData("auto 10px auto 20px")]
        [InlineData("10px auto 20px")]
        [InlineData("auto none none")]
        public void ContainIntrinsicSizeShorthandLegal(string value)
        {
            var property = ParseDeclaration($"contain-intrinsic-size: {value}");

            Assert.IsType<ContainIntrinsicSizeProperty>(property);
            Assert.True(property.HasValue);
        }

        [Theory]
        [InlineData("")]
        [InlineData("10px 20px 30px")]
        [InlineData("auto")]
        [InlineData("auto auto 10px")]
        [InlineData("-5px")]
        [InlineData("50%")]
        public void ContainIntrinsicSizeShorthandIllegal(string value)
        {
            var property = ParseDeclaration($"contain-intrinsic-size: {value}");

            Assert.IsType<ContainIntrinsicSizeProperty>(property);
            Assert.False(property.HasValue);
        }

        [Theory]
        [InlineData("auto 10px", true, "10px")]
        [InlineData("AUTO 2em", true, "2em")]
        [InlineData("10px", false, "10px")]
        [InlineData("none", false, null)]
        [InlineData("auto none", true, null)]
        [InlineData("", false, null)]
        [InlineData(null, false, null)]
        public void ContainIntrinsicTextIsReadBackAsAutoAndALength(string? text, bool auto, string? length)
        {
            Assert.Equal(length, ContainGrammar.ParseIntrinsic(text, out var hasAuto));
            Assert.Equal(auto, hasAuto);
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

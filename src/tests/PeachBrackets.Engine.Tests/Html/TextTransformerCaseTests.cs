using PeachBrackets.Css;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.Html
{
    /// <summary>text-transform: uppercase and lowercase map a code point at a time with Unicode's simple case mapping.</summary>
    public class TextTransformerCaseTests
    {
        [Theory]
        [InlineData("abc", "ABC")]
        [InlineData("ı", "I")]                              // the dotless i, which the .NET invariant mapping leaves alone
        [InlineData("\U00010428", "\U00010400")]            // Deseret small long I: a surrogate pair, mapped as one letter
        [InlineData("a\U00010428b", "A\U00010400B")]
        public void Uppercase_MapsWholeCodePoints(string text, string expected) =>
            Assert.Equal(expected, TextTransformer.Apply(text, TextTransform.Uppercase));

        [Theory]
        [InlineData("ABC", "abc")]
        [InlineData("\U00010400", "\U00010428")]
        public void Lowercase_MapsWholeCodePoints(string text, string expected) =>
            Assert.Equal(expected, TextTransformer.Apply(text, TextTransform.Lowercase));

        [Fact]
        public void Mapping_KeepsTheTextsLength() =>
            Assert.Equal("a\U00010428ı".Length, TextTransformer.Apply("a\U00010428ı", TextTransform.Uppercase).Length);
    }
}

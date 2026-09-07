using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Direct unit tests for <see cref="CssValueParser.IsValidCommaKeywordList{TValue}"/> — the
    /// span-based (no tokenizer) validator behind css-properties.json's "keyword-list" cssDataType
    /// (background-origin/-clip's &lt;box&gt;#, background-repeat's &lt;repeat-style&gt;#).
    /// </summary>
    public class CssValueParserIsValidCommaKeywordListTests
    {
        [Theory]
        [InlineData("padding-box")]
        [InlineData("border-box, content-box")]
        [InlineData("PADDING-BOX")] // case-insensitive, matching Map.BoxModels' own comparer
        public void SingleKeywordPerSegment_ValidValues_ReturnTrue(string value)
        {
            Assert.True(CssValueParser.IsValidCommaKeywordList(value, Map.BoxModels));
        }

        [Theory]
        [InlineData("")]
        [InlineData("pad-box")]
        [InlineData("padding-box,")] // trailing empty segment
        [InlineData(",padding-box")] // leading empty segment
        [InlineData("padding-box border-box")] // two keywords in a maxPerSegment:1 segment
        public void SingleKeywordPerSegment_InvalidValues_ReturnFalse(string value)
        {
            Assert.False(CssValueParser.IsValidCommaKeywordList(value, Map.BoxModels));
        }

        [Theory]
        [InlineData("repeat")]
        [InlineData("repeat space")] // two keywords, within maxPerSegment:2
        [InlineData("repeat-x")] // alias
        [InlineData("repeat-y")] // alias
        [InlineData("repeat-x, no-repeat")] // alias in a multi-layer list
        [InlineData("REPEAT-X")] // alias match is case-insensitive too
        public void RepeatStyleWithAliases_ValidValues_ReturnTrue(string value)
        {
            Assert.True(CssValueParser.IsValidCommaKeywordList(value, Map.BackgroundRepeats,
                maxPerSegment: 2, aliasKeywords: ["repeat-x", "repeat-y"]));
        }

        [Theory]
        [InlineData("repeat space round")] // three keywords exceeds maxPerSegment:2
        [InlineData("repeat-x repeat-y")] // an alias is a whole segment, not combinable
        [InlineData("banana")]
        public void RepeatStyleWithAliases_InvalidValues_ReturnFalse(string value)
        {
            Assert.False(CssValueParser.IsValidCommaKeywordList(value, Map.BackgroundRepeats,
                maxPerSegment: 2, aliasKeywords: ["repeat-x", "repeat-y"]));
        }
    }
}

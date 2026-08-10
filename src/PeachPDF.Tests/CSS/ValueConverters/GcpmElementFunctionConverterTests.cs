using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.ValueConverters
{
    /// <summary>
    /// Tests for the GcpmElementFunctionConverter that parses css-gcpm-3's
    /// <c>element(&lt;custom-ident&gt; [, first|start|last|first-except]?)</c> <c>content</c> value -
    /// the running-element counterpart to <c>string()</c>. <c>content</c>'s registry storage is a raw
    /// string (<c>cssDataType: "cssom"</c>), so acceptance/rejection is observed through
    /// <c>StyleRule.Style.Content</c>, mirroring <see cref="StringFunctionConverterTests"/>'s own pattern.
    /// </summary>
    public class GcpmElementFunctionConverterTests
    {
        private static string? ParseContent(string declaration)
        {
            var parser = new StylesheetParser();
            var stylesheet = parser.Parse($"div {{ content: {declaration} }}");
            var rule = stylesheet.Rules[0] as StyleRule;
            return rule?.Style.Content;
        }

        [Fact]
        public void ElementFunction_WithNameOnly_ParsesCorrectly()
        {
            Assert.Equal("element(chapter-title)", ParseContent("element(chapter-title)"));
        }

        [Theory]
        [InlineData("start")]
        [InlineData("last")]
        [InlineData("first-except")]
        public void ElementFunction_WithSelectionKeyword_ParsesCorrectly(string keyword)
        {
            Assert.Equal($"element(chapter-title, {keyword})", ParseContent($"element(chapter-title, {keyword})"));
        }

        [Fact]
        public void ElementFunction_WithExplicitFirstKeyword_CanonicalizesToShortForm()
        {
            // "first" is the default, so an explicit "element(name, first)" round-trips to the short form -
            // mirrors StringFunctionValue.CssText's identical canonicalization for string().
            Assert.Equal("element(chapter-title)", ParseContent("element(chapter-title, first)"));
        }

        [Fact]
        public void ElementFunction_WithHyphenatedName_ParsesCorrectly()
        {
            Assert.Equal("element(my-chapter-title)", ParseContent("element(my-chapter-title)"));
        }

        [Fact]
        public void ElementFunction_InvalidSelectionKeyword_IsRejected()
        {
            Assert.NotEqual("element(chapter-title, bogus)", ParseContent("element(chapter-title, bogus)"));
        }

        [Fact]
        public void ElementFunction_NoArguments_IsRejected()
        {
            Assert.NotEqual("element()", ParseContent("element()"));
        }

        [Fact]
        public void ElementFunction_HashArgument_IsRejected()
        {
            // The CSS Images 4 element(#id) form (ElementImageConverter, a snapshot-image value) has a
            // different argument shape entirely - GCPM's element() must not misparse it as if "hero" were
            // a valid custom-ident, and ElementImageConverter is not wired into ContentProperty's own
            // converter chain at all, so this must fail to parse as `content` here regardless.
            Assert.NotEqual("element(#hero)", ParseContent("element(#hero)"));
        }

        [Fact]
        public void ElementFunction_CaseInsensitiveKeyword_ParsesCorrectly()
        {
            var content = ParseContent("element(chapter-title, LAST)");
            Assert.NotNull(content);
            Assert.Contains("chapter-title", content);
        }
    }
}

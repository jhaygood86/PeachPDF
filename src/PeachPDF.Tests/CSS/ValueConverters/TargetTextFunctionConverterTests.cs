using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.ValueConverters
{
    /// <summary>
    /// Tests for the TargetTextFunctionConverter that parses css-content-3's
    /// <c>target-text(&lt;target&gt; [, content | before | after | first-letter]?)</c> <c>content</c>
    /// value. See <see cref="TargetCounterFunctionConverterTests"/> for the shared
    /// <c>content:</c>-property-level test pattern (and its note on <c>url()</c> round-tripping quoted)
    /// this mirrors.
    /// </summary>
    public class TargetTextFunctionConverterTests
    {
        private static string? ParseContent(string declaration)
        {
            var parser = new StylesheetParser();
            var stylesheet = parser.Parse($"div {{ content: {declaration} }}");
            var rule = stylesheet.Rules[0] as StyleRule;
            return rule?.Style.Content;
        }

        [Fact]
        public void TargetText_WithUrlTargetOnly_ParsesCorrectly()
        {
            Assert.Equal("target-text(url(\"#ch2\"))", ParseContent("target-text(url(#ch2))"));
        }

        [Fact]
        public void TargetText_WithAttrTarget_ParsesCorrectly()
        {
            Assert.Equal("target-text(attr(href))", ParseContent("target-text(attr(href))"));
        }

        [Theory]
        [InlineData("content")]
        [InlineData("before")]
        [InlineData("after")]
        [InlineData("first-letter")]
        public void TargetText_WithExplicitMode_ParsesCorrectly(string mode)
        {
            Assert.Equal($"target-text(url(\"#ch2\"), {mode})", ParseContent($"target-text(url(#ch2), {mode})"));
        }

        [Fact]
        public void TargetText_InvalidMode_IsRejected()
        {
            Assert.NotEqual("target-text(url(#ch2), bogus)", ParseContent("target-text(url(#ch2), bogus)"));
        }

        [Fact]
        public void TargetText_NoArguments_IsRejected()
        {
            Assert.NotEqual("target-text()", ParseContent("target-text()"));
        }

        [Fact]
        public void TargetText_InvalidTargetShape_IsRejected()
        {
            Assert.NotEqual("target-text(42)", ParseContent("target-text(42)"));
        }

        [Fact]
        public void TargetText_CombinedWithStringLiteral_ParsesCorrectly()
        {
            var content = ParseContent("target-text(url(#ch2)) \" (see above)\"");
            Assert.Equal("target-text(url(\"#ch2\")) \" (see above)\"", content);
        }
    }
}

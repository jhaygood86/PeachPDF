using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.ValueConverters
{
    /// <summary>
    /// Tests for the TargetCounterFunctionConverter that parses css-content-3's
    /// <c>target-counter(&lt;target&gt;, &lt;counter-name&gt; [, &lt;counter-style&gt;]?)</c> <c>content</c>
    /// value. <c>content</c>'s registry storage is a raw string (<c>cssDataType: "cssom"</c>), so
    /// acceptance/rejection is observed through <c>StyleRule.Style.Content</c>, mirroring
    /// <see cref="GcpmElementFunctionConverterTests"/>'s own pattern. Authored <c>url(#id)</c> tokens
    /// round-trip through the quoted canonical form (<c>url("#id")</c>) - both are the same CSS value,
    /// this repo's tokenizer just always re-serializes a url() token quoted.
    /// </summary>
    public class TargetCounterFunctionConverterTests
    {
        private static string? ParseContent(string declaration)
        {
            var parser = new StylesheetParser();
            var stylesheet = parser.Parse($"div {{ content: {declaration} }}");
            var rule = stylesheet.Rules[0] as StyleRule;
            return rule?.Style.Content;
        }

        [Fact]
        public void TargetCounter_WithUrlTargetAndCounterName_ParsesCorrectly()
        {
            Assert.Equal("target-counter(url(\"#ch2\"), page)", ParseContent("target-counter(url(#ch2), page)"));
        }

        [Fact]
        public void TargetCounter_WithAttrTarget_ParsesCorrectly()
        {
            Assert.Equal("target-counter(attr(href), page)", ParseContent("target-counter(attr(href), page)"));
        }

        [Fact]
        public void TargetCounter_WithStringTarget_ParsesCorrectly()
        {
            Assert.Equal("target-counter(\"ch2\", page)", ParseContent("target-counter(\"ch2\", page)"));
        }

        [Fact]
        public void TargetCounter_WithExplicitStyle_ParsesCorrectly()
        {
            Assert.Equal("target-counter(url(\"#ch2\"), page, upper-roman)", ParseContent("target-counter(url(#ch2), page, upper-roman)"));
        }

        [Fact]
        public void TargetCounter_WithCustomCounterName_ParsesCorrectly()
        {
            Assert.Equal("target-counter(url(\"#ch2\"), chapter-number)", ParseContent("target-counter(url(#ch2), chapter-number)"));
        }

        [Fact]
        public void TargetCounter_MissingCounterName_IsRejected()
        {
            Assert.NotEqual("target-counter(url(#ch2))", ParseContent("target-counter(url(#ch2))"));
        }

        [Fact]
        public void TargetCounter_NoArguments_IsRejected()
        {
            Assert.NotEqual("target-counter()", ParseContent("target-counter()"));
        }

        [Fact]
        public void TargetCounter_InvalidTargetShape_IsRejected()
        {
            // A bare number is none of <string>/url()/attr() - not a valid <target>.
            Assert.NotEqual("target-counter(42, page)", ParseContent("target-counter(42, page)"));
        }

        [Fact]
        public void TargetCounter_CombinedWithLeaderAndText_ParsesAsWholeContentList()
        {
            var content = ParseContent("\"Chapter Two\" leader(dotted) target-counter(attr(href), page)");
            Assert.Equal("\"Chapter Two\" leader(dotted) target-counter(attr(href), page)", content);
        }
    }
}

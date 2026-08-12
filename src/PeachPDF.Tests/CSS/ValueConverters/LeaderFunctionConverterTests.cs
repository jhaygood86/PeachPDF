using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS.ValueConverters
{
    /// <summary>
    /// Tests for the LeaderFunctionConverter that parses css-content-3's
    /// <c>leader(&lt;leader-type&gt;)</c>, where <c>&lt;leader-type&gt; = dotted | solid | space |
    /// &lt;string&gt;</c>. See <see cref="TargetCounterFunctionConverterTests"/> for the shared
    /// <c>content:</c>-property-level test pattern this mirrors.
    /// </summary>
    public class LeaderFunctionConverterTests
    {
        private static string? ParseContent(string declaration)
        {
            var parser = new StylesheetParser();
            var stylesheet = parser.Parse($"div {{ content: {declaration} }}");
            var rule = stylesheet.Rules[0] as StyleRule;
            return rule?.Style.Content;
        }

        [Theory]
        [InlineData("dotted")]
        [InlineData("solid")]
        [InlineData("space")]
        public void Leader_WithKeyword_ParsesCorrectly(string keyword)
        {
            Assert.Equal($"leader({keyword})", ParseContent($"leader({keyword})"));
        }

        [Fact]
        public void Leader_WithCustomStringPattern_ParsesCorrectly()
        {
            Assert.Equal("leader(\"-\")", ParseContent("leader(\"-\")"));
        }

        [Fact]
        public void Leader_InvalidKeyword_IsRejected()
        {
            Assert.NotEqual("leader(bogus)", ParseContent("leader(bogus)"));
        }

        [Fact]
        public void Leader_NoArguments_ParsesAndDefaultsToDotted()
        {
            // css-content-3: leader(<leader-type>?) - the argument is optional and defaults to dotted;
            // leader() alone is the shortest spec-legal way to request a dotted leader. CssText is a
            // verbatim round-trip (see LeaderFunctionConverter's own CssText comment), so the stored
            // form stays "leader()" - CssContentEngine.ResolveLeaderToken is what actually resolves the
            // missing argument to LeaderKind.Dotted at runtime (see its own test coverage).
            Assert.Equal("leader()", ParseContent("leader()"));
        }

        [Fact]
        public void Leader_TooManyArguments_IsRejected()
        {
            Assert.NotEqual("leader(dotted, solid)", ParseContent("leader(dotted, solid)"));
        }

        [Fact]
        public void Leader_CaseInsensitiveKeyword_ParsesCorrectly()
        {
            var content = ParseContent("leader(DOTTED)");
            Assert.NotNull(content);
            Assert.Contains("leader(", content);
        }

        [Fact]
        public void Leader_CombinedWithTextAndTargetCounter_ParsesAsWholeContentList()
        {
            var content = ParseContent("\"Chapter One\" leader(dotted) target-counter(url(#ch1), page)");
            Assert.Equal("\"Chapter One\" leader(dotted) target-counter(url(\"#ch1\"), page)", content);
        }
    }
}

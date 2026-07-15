using PeachPDF.Text;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.Text
{
    /// <summary>
    /// Direct unit tests for <see cref="HyphenationEngine.FindHyphenationPoints"/> — Liang's
    /// pattern-based hyphenation algorithm against the embedded American-English pattern set. Exercised
    /// directly against well-known hyphenations rather than only through full layout, so a parsing or
    /// scoring bug in the algorithm itself is caught in isolation before verifying the layout wiring.
    /// </summary>
    public class HyphenationEngineTests
    {
        private static string ApplyBreaks(string word, System.Collections.Generic.IReadOnlyList<int> points)
        {
            var result = word;
            foreach (var p in points.OrderByDescending(x => x))
                result = result[..p] + "-" + result[p..];
            return result;
        }

        // Expected values are the pattern set's actual, verified output (each hand-traced against the
        // matching patterns in hyph-en-us.txt, e.g. "put3er" -> a break between "put"/"er"), not generic
        // dictionary hyphenation - pattern-based hyphenation is deliberately conservative and, combined
        // with this set's own stated hyphenmins (left=2, right=3), can produce fewer breaks than a
        // dictionary would (e.g. "com-put-er"'s second break is excluded here because it would leave
        // only 2 trailing characters ("er"), violating right=3).
        [Theory]
        [InlineData("hyphenation", "hy-phen-ation")]
        [InlineData("computer", "com-puter")]
        [InlineData("wonderful", "won-der-ful")]
        public void FindHyphenationPoints_KnownWords_ProducesExpectedBreakPattern(string word, string expected)
        {
            var points = HyphenationEngine.FindHyphenationPoints(word, "en-US");

            Assert.Equal(expected, ApplyBreaks(word, points));
        }

        [Fact]
        public void FindHyphenationPoints_UnsupportedLanguage_ReturnsEmpty()
        {
            var points = HyphenationEngine.FindHyphenationPoints("hyphenation", "is");
            Assert.Empty(points);
        }

        [Fact]
        public void FindHyphenationPoints_NullOrEmptyLanguage_ReturnsEmpty()
        {
            Assert.Empty(HyphenationEngine.FindHyphenationPoints("hyphenation", null));
            Assert.Empty(HyphenationEngine.FindHyphenationPoints("hyphenation", ""));
        }

        [Fact]
        public void FindHyphenationPoints_ShortWord_ReturnsEmpty()
        {
            // Shorter than LeftHyphenMin + RightHyphenMin (2 + 3) - no valid break point can exist.
            Assert.Empty(HyphenationEngine.FindHyphenationPoints("cat", "en-US"));
        }

        [Fact]
        public void FindHyphenationPoints_WordWithNonLetterCharacters_ReturnsEmpty()
        {
            // The pattern set is ASCII-letters-only; a word with digits/punctuation is left alone
            // rather than guessed at.
            Assert.Empty(HyphenationEngine.FindHyphenationPoints("don't-do-this2", "en-US"));
        }

        [Fact]
        public void FindHyphenationPoints_RespectsLeftAndRightHyphenMinimums()
        {
            var points = HyphenationEngine.FindHyphenationPoints("hyphenation", "en-US");

            Assert.All(points, p => Assert.True(p >= 2, $"break at {p} violates left minimum"));
            Assert.All(points, p => Assert.True(p <= "hyphenation".Length - 3, $"break at {p} violates right minimum"));
        }

        [Theory]
        [InlineData("en")]
        [InlineData("en-US")]
        [InlineData("en-GB")]
        [InlineData("EN-us")]
        public void FindHyphenationPoints_AnyEnglishVariant_UsesEnglishPatterns(string language)
        {
            var points = HyphenationEngine.FindHyphenationPoints("hyphenation", language);
            Assert.NotEmpty(points);
        }
    }
}

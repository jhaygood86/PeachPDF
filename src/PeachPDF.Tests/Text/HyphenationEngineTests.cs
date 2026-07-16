using PeachPDF.Text;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.Text
{
    /// <summary>
    /// Direct unit tests for <see cref="HyphenationEngine.FindHyphenationPoints"/> — Liang's
    /// pattern-based hyphenation algorithm against the embedded multi-language pattern sets (~70
    /// languages sourced from CTAN's hyph-utf8 package, see tools/Update-HyphenationPatterns.ps1).
    /// Exercised directly against well-known hyphenations rather than only through full layout, so a
    /// parsing or scoring bug in the algorithm itself is caught in isolation before verifying the
    /// layout wiring.
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
            // A tag that will never resolve to any shipped pattern set (as opposed to a real language
            // that simply isn't among them - most BCP-47-ish tags now resolve to something, given the
            // ~70-language pattern set).
            var points = HyphenationEngine.FindHyphenationPoints("hyphenation", "xx-zz");
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
            // Shorter than en-US's own hyphenmins (left=2, right=3) - no valid break point can exist.
            Assert.Empty(HyphenationEngine.FindHyphenationPoints("cat", "en-US"));
        }

        [Fact]
        public void FindHyphenationPoints_WordWithNonLetterCharacters_ReturnsEmpty()
        {
            // A word with digits/punctuation is left alone rather than guessed at.
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

        [Fact]
        public void FindHyphenationPoints_German_ProducesExpectedBreakPattern()
        {
            // Hand-verified against hyph-de-1996's actual pattern output for "Konstitution".
            var points = HyphenationEngine.FindHyphenationPoints("Konstitution", "de-DE");

            Assert.Equal("Kon-sti-tu-ti-on", ApplyBreaks("Konstitution", points));
        }

        [Fact]
        public void FindHyphenationPoints_NonLatinScript_Cyrillic_ProducesBreakPoints()
        {
            // Regression coverage for the Unicode-letter alphabet gate: the engine used to reject any
            // character outside ASCII a-z, which would have silently zeroed out every non-Latin
            // pattern set (Cyrillic, Greek, Armenian, Georgian, Ethiopic, Thai, ...) even with correct
            // pattern data loaded. Hand-verified against hyph-ru's actual pattern output.
            var points = HyphenationEngine.FindHyphenationPoints("хорошо", "ru");

            Assert.Equal("хо-ро-шо", ApplyBreaks("хорошо", points));
        }

        [Theory]
        [InlineData("de-AT")]
        [InlineData("de-DE")]
        [InlineData("de")]
        public void FindHyphenationPoints_RegionWithoutOwnPatternSet_FallsBackToBaseLanguageDefault(string language)
        {
            // Neither "de-AT" nor bare "de" has its own pattern file (only de-1901/de-1996/de-ch-1901
            // do) - language-tags.txt maps them to de-1996 (reformed orthography) as the default
            // variant, so all three must resolve to the exact same pattern set as an explicit
            // "de-1996"/"de-DE" request.
            var expected = HyphenationEngine.FindHyphenationPoints("Konstitution", "de-1996");

            var points = HyphenationEngine.FindHyphenationPoints("Konstitution", language);

            Assert.Equal(expected, points);
            Assert.NotEmpty(points);
        }

        [Fact]
        public void FindHyphenationPoints_PerLanguageHyphenMinimums_OverrideDefault()
        {
            // hyph-af states its own hyphenmins as left=1/right=2 (vs. the common left=2/right=3
            // default) - "april" has a real pattern match that breaks right after the first letter
            // (word-relative gap index 1), which only a left-minimum of 1 admits. Regression coverage
            // for hyphenmins moving from a single hard-coded constant pair to a per-pattern-set value
            // parsed from each file's own "# hyphenmins:" line.
            var points = HyphenationEngine.FindHyphenationPoints("april", "af");

            Assert.Contains(1, points);
        }
    }
}

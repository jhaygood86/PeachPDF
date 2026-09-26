using PeachDrawing.Text.Unicode;

namespace PeachDrawing.Text.Tests.PublicApi
{
    /// <summary>
    /// The segmentation entry points as a consumer outside the assembly calls them: the shape of the answers, the edges (empty text,
    /// surrogate pairs) and the CSS tailorings of the line breaker. The rules themselves are checked line by line against Unicode's
    /// conformance files in <c>SegmentationConformanceTests</c>.
    /// </summary>
    public class SegmentationPublicApiTests
    {
        private const string Grin = "\U0001F600";

        private static int[] Breaks(string text, LineBreakOptions options = default)
        {
            var opportunities = LineBreaker.FindOpportunities(text, options);
            return opportunities
                .Select((o, i) => (o, i))
                .Where(x => x.i > 0 && x.o != LineBreakOpportunity.Prohibited)
                .Select(x => x.i)
                .ToArray();
        }

        [Fact]
        public void EmptyText_HasNoSegmentBoundaries_AndOneMandatoryLineBreak()
        {
            Assert.Empty(Segmenter.FindGraphemeBoundaries(""));
            Assert.Empty(Segmenter.FindWordBoundaries(""));
            Assert.Empty(Segmenter.FindSentenceBoundaries(""));
            Assert.Equal([LineBreakOpportunity.Mandatory], LineBreaker.FindOpportunities(""));
        }

        [Fact]
        public void GraphemeBoundaries_KeepACharacterWithItsAccentsAndAFlagWhole()
        {
            Assert.Equal([0, 2, 3], Segmenter.FindGraphemeBoundaries("éx"));
            Assert.Equal([0, 4, 8], Segmenter.FindGraphemeBoundaries("\U0001F1FA\U0001F1F8\U0001F1E9\U0001F1EA"));
            Assert.Equal([0, 2, 4], Segmenter.FindGraphemeBoundaries(Grin + Grin));
        }

        [Fact]
        public void WordBoundaries_SeparatePunctuationAndSpacesFromWords()
        {
            Assert.Equal([0, 5, 6, 7, 12], Segmenter.FindWordBoundaries("Hello, world"));
            Assert.Equal([0, 5], Segmenter.FindWordBoundaries("can’t"));
            Assert.Equal([0, 4], Segmenter.FindWordBoundaries("3.14"));
        }

        [Fact]
        public void SentenceBoundaries_FollowTerminatorsButNotAbbreviationsBeforeLowercase()
        {
            Assert.Equal([0, 4, 7], Segmenter.FindSentenceBoundaries("Hi. Yo."));
            Assert.Equal([0, 20], Segmenter.FindSentenceBoundaries("It costs 3.5 dollars"));
        }

        [Fact]
        public void SegmentBoundaries_NeverFallInsideASurrogatePair()
        {
            foreach (var boundaries in new[]
            {
                Segmenter.FindGraphemeBoundaries("a" + Grin + "b"),
                Segmenter.FindWordBoundaries("a" + Grin + "b"),
                Segmenter.FindSentenceBoundaries("a" + Grin + "b"),
            })
            {
                Assert.DoesNotContain(2, boundaries);
            }

            Assert.Equal(LineBreakOpportunity.Prohibited, LineBreaker.FindOpportunities("a" + Grin + "b")[2]);
        }

        [Fact]
        public void ALoneSurrogate_IsTreatedAsACharacterOfItsOwn_AndNeverThrows()
        {
            const string text = "a\uD800b";

            Assert.Equal([0, 1, 2, 3], Segmenter.FindGraphemeBoundaries(text));
            Assert.Equal(text.Length + 1, LineBreaker.FindOpportunities(text).Length);
            Assert.NotEmpty(Segmenter.FindWordBoundaries(text));
            Assert.NotEmpty(Segmenter.FindSentenceBoundaries(text));
        }

        [Fact]
        public void LineBreaks_AreAllowedAfterSpaces_AndMandatoryAfterNewlines()
        {
            var text = LineBreaker.FindOpportunities("ab cd\nef");

            Assert.Equal(LineBreakOpportunity.Prohibited, text[0]);
            Assert.Equal(LineBreakOpportunity.Prohibited, text[1]);
            Assert.Equal(LineBreakOpportunity.Prohibited, text[2]);      // before the space
            Assert.Equal(LineBreakOpportunity.Allowed, text[3]);         // after it
            Assert.Equal(LineBreakOpportunity.Prohibited, text[5]);      // before the newline
            Assert.Equal(LineBreakOpportunity.Mandatory, text[6]);       // after it
            Assert.Equal(LineBreakOpportunity.Mandatory, text[^1]);      // the end of the text
        }

        [Fact]
        public void LineBreaks_TreatCrLfAsOneNewline()
        {
            Assert.Equal([3], Breaks("a\r\nb").Where(i => i < 4).ToArray());
        }

        [Fact]
        public void LineBreaks_BreakBetweenIdeographsButNotWithinWords()
        {
            Assert.Equal([1, 2, 3], Breaks("日本語"));
            Assert.Equal([3], Breaks("abc"));     // only the end of the text
        }

        [Fact]
        public void WordBreak_BreakAll_AllowsBreaksBetweenLettersAndDigits()
        {
            var options = new LineBreakOptions { WordBreak = WordBreakMode.BreakAll };

            Assert.Equal([1, 2, 3], Breaks("abc", options));
            Assert.Equal([1, 2, 3], Breaks("123", options));
        }

        [Fact]
        public void WordBreak_KeepAll_ForbidsBreaksBetweenIdeographsAndHangul()
        {
            var options = new LineBreakOptions { WordBreak = WordBreakMode.KeepAll };

            Assert.Equal([3], Breaks("日本語", options));
            Assert.Equal([3], Breaks("한글말", options));
            // The space still lets a line end, and punctuation still binds as the rules say.
            Assert.Equal([3, 5], Breaks("日本 語文", options));
        }

        [Fact]
        public void Strictness_DecidesWhetherASmallKanaMayStartALine()
        {
            const string text = "きゅ";     // ki, small yu

            Assert.Equal([2], Breaks(text, new LineBreakOptions { Strictness = LineBreakStrictness.Strict }));
            Assert.Equal([2], Breaks(text, new LineBreakOptions { Strictness = LineBreakStrictness.Normal }));
            Assert.Equal([2], Breaks(text, new LineBreakOptions { Strictness = LineBreakStrictness.Auto }));
            Assert.Equal([1, 2], Breaks(text, new LineBreakOptions { Strictness = LineBreakStrictness.Loose }));
        }

        [Fact]
        public void Strictness_NormalAndLoose_LetAWaveDashStartALine()
        {
            const string text = "日〜";     // a kanji and the wave dash

            Assert.Equal([2], Breaks(text, new LineBreakOptions { Strictness = LineBreakStrictness.Strict }));
            Assert.Equal([1, 2], Breaks(text, new LineBreakOptions { Strictness = LineBreakStrictness.Normal }));
            Assert.Equal([1, 2], Breaks(text, new LineBreakOptions { Strictness = LineBreakStrictness.Loose }));
        }

        [Fact]
        public void Strictness_Loose_LetsAHyphenStartALineOnlyAfterAnIdeograph()
        {
            var loose = new LineBreakOptions { Strictness = LineBreakStrictness.Loose };

            Assert.Equal([1, 2], Breaks("日‐", loose));
            Assert.Equal([2], Breaks("a‐", loose));
            Assert.Equal([2], Breaks("日‐", new LineBreakOptions { Strictness = LineBreakStrictness.Normal }));
        }

        [Fact]
        public void Strictness_Loose_LetsAnIterationMarkStartALine()
        {
            const string text = "日々";     // a kanji and the iteration mark

            Assert.Equal([2], Breaks(text, new LineBreakOptions { Strictness = LineBreakStrictness.Normal }));
            Assert.Equal([1, 2], Breaks(text, new LineBreakOptions { Strictness = LineBreakStrictness.Loose }));
        }

        [Fact]
        public void Strictness_Anywhere_BreaksAtEveryGraphemeBoundaryAndOnlyThere()
        {
            var options = new LineBreakOptions { Strictness = LineBreakStrictness.Anywhere };

            // Even a no-break space and a word joiner no longer hold a line together.
            Assert.Equal([1, 2, 3], Breaks("a b", options));
            Assert.Equal([1, 2, 3], Breaks("a⁠b", options));

            // But an accent stays with its letter and a surrogate pair stays whole.
            Assert.Equal([2, 3], Breaks("éx", options));
            Assert.Equal([2, 4], Breaks(Grin + Grin, options));

            // A hard break stays mandatory.
            Assert.Equal(LineBreakOpportunity.Mandatory, LineBreaker.FindOpportunities("a\nb", options)[2]);
        }

        [Fact]
        public void Strictness_Anywhere_DoesNotBreakBeforeAHardBreak()
        {
            var options = new LineBreakOptions { Strictness = LineBreakStrictness.Anywhere };

            var newline = LineBreaker.FindOpportunities("a\nb", options);
            Assert.Equal(LineBreakOpportunity.Prohibited, newline[1]);
            Assert.Equal(LineBreakOpportunity.Mandatory, newline[2]);

            var crlf = LineBreaker.FindOpportunities("a\r\nb", options);
            Assert.Equal(LineBreakOpportunity.Prohibited, crlf[1]);
            Assert.Equal(LineBreakOpportunity.Prohibited, crlf[2]);
            Assert.Equal(LineBreakOpportunity.Mandatory, crlf[3]);
        }

        [Theory]
        [InlineData(0x000B)]     // line tabulation (BK)
        [InlineData(0x000C)]     // form feed (BK)
        [InlineData(0x2028)]     // line separator (BK)
        [InlineData(0x2029)]     // paragraph separator (BK)
        [InlineData(0x0085)]     // next line (NL)
        [InlineData(0x000D)]     // carriage return
        [InlineData(0x000A)]     // line feed
        public void EveryKindOfHardBreak_IsMandatoryAfterItAndProhibitedBefore(int hardBreak)
        {
            var opportunities = LineBreaker.FindOpportunities("a" + char.ConvertFromUtf32(hardBreak) + "b");

            Assert.Equal(LineBreakOpportunity.Prohibited, opportunities[1]);
            Assert.Equal(LineBreakOpportunity.Mandatory, opportunities[2]);
        }

        [Fact]
        public void WordBreak_BreakAll_AlsoBreaksBetweenHebrewLetters()
        {
            var options = new LineBreakOptions { WordBreak = WordBreakMode.BreakAll };

            Assert.Equal([2], Breaks("אב"));
            Assert.Equal([1, 2], Breaks("אב", options));
        }

        [Fact]
        public void WordBreak_KeepAll_KeepsTheLettersOfBrahmicScriptsTogether()
        {
            const string balinese = "ᬅᬅ";     // two Balinese letters (line break class AK)

            Assert.Equal([1, 2], Breaks(balinese));
            Assert.Equal([2], Breaks(balinese, new LineBreakOptions { WordBreak = WordBreakMode.KeepAll }));
        }

        [Fact]
        public void Strictness_Loose_AlsoBreaksBeforeAnEllipsisAfterLatinText()
        {
            // The tailoring is not language-aware: see the accepted gap.
            Assert.Equal([1, 2], Breaks("a…", new LineBreakOptions { Strictness = LineBreakStrictness.Loose }));
            Assert.Equal([2], Breaks("a…"));
        }

        [Fact]
        public void LongRuns_AreLinear()
        {
            // Runs like these made a scan-back at every position quadratic; the limit is generous, the point is the order of growth.
            var watch = System.Diagnostics.Stopwatch.StartNew();

            var flags = string.Concat(Enumerable.Repeat("\U0001F1FA", 100_000));
            Assert.Equal(flags.Length + 1, LineBreaker.FindOpportunities(flags).Length);
            Assert.Equal(100_000 / 2 + 1, Segmenter.FindGraphemeBoundaries(flags).Length);     // pairs of regional indicators

            var spaces = "." + new string(' ', 100_000) + "X";
            Assert.Equal(3, Segmenter.FindSentenceBoundaries(spaces).Length);
            Assert.Equal(spaces.Length + 1, LineBreaker.FindOpportunities(spaces).Length);

            var closes = "." + new string(')', 100_000) + "X";
            Assert.Equal(3, Segmenter.FindSentenceBoundaries(closes).Length);

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
        }

        [Fact]
        public void LineBreakOptions_Default_IsNormalWithNoWordBreakTailoring()
        {
            var options = default(LineBreakOptions);

            Assert.Equal(WordBreakMode.Normal, options.WordBreak);
            Assert.Equal(LineBreakStrictness.Auto, options.Strictness);
        }
    }
}

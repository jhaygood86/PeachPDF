using PeachDrawing.Text.Unicode;

namespace PeachDrawing.Text.Tests.Text.Segmentation
{
    /// <summary>
    /// The tailorings of CSS Text 3's <c>line-break</c> that depend on the language of the text: the breaks allowed for
    /// <c>normal</c> and <c>loose</c> (before the wave dash and the katakana double hyphen) and for <c>loose</c> (before centred
    /// punctuation, before a suffix and after a prefix of East Asian width) only where the writing system is Chinese or Japanese.
    /// Each expectation writes the text with a <c>|</c> wherever a line may end, which is also what a browser's line breaker
    /// answers for the same text, language and strictness (the cases were compared with Chrome).
    /// </summary>
    public class LineBreakLanguageTests
    {
        private static string Render(string text, LineBreakStrictness strictness, string? language, WordBreakMode wordBreak = WordBreakMode.Normal)
        {
            var options = new LineBreakOptions { Strictness = strictness, Language = language, WordBreak = wordBreak };
            var opportunities = LineBreaker.FindOpportunities(text, options);
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && opportunities[i] != LineBreakOpportunity.Prohibited)
                {
                    result.Append('|');
                }

                result.Append(text[i]);
            }

            return result.ToString();
        }

        private const LineBreakStrictness Strict = LineBreakStrictness.Strict;
        private const LineBreakStrictness Normal = LineBreakStrictness.Normal;
        private const LineBreakStrictness Loose = LineBreakStrictness.Loose;

        [Theory]
        // The wave dash and the katakana double hyphen: normal and loose, Chinese and Japanese only.
        [InlineData("あいう〜えお", Strict, "ja", "あ|い|う〜|え|お")]
        [InlineData("あいう〜えお", Normal, "ja", "あ|い|う|〜|え|お")]
        [InlineData("あいう〜えお", Loose, "ja", "あ|い|う|〜|え|お")]
        [InlineData("あいう〜えお", Normal, "zh", "あ|い|う|〜|え|お")]
        [InlineData("あいう〜えお", Loose, "zh-Hant-TW", "あ|い|う|〜|え|お")]
        [InlineData("あいう〜えお", Normal, "en", "あ|い|う〜|え|お")]
        [InlineData("あいう〜えお", Loose, "en", "あ|い|う〜|え|お")]
        [InlineData("あいう〜えお", Normal, null, "あ|い|う〜|え|お")]
        [InlineData("あいう゠えお", Normal, "ja", "あ|い|う|゠|え|お")]
        [InlineData("あいう゠えお", Normal, "en", "あ|い|う゠|え|お")]
        [InlineData("あいう゠えお", Strict, "ja", "あ|い|う゠|え|お")]
        [InlineData("ab〜cd", Normal, "ja", "ab|〜|cd")]
        [InlineData("ab〜cd", Normal, "en", "ab〜|cd")]
        // Centred punctuation: loose, Chinese and Japanese only.
        [InlineData("あ・い", Loose, "ja", "あ|・|い")]
        [InlineData("あ・い", Normal, "ja", "あ・|い")]
        [InlineData("あ・い", Loose, "en", "あ・|い")]
        [InlineData("あ・い", Loose, null, "あ・|い")]
        [InlineData("abc・def", Loose, "ja", "abc|・|def")]
        [InlineData("abc・def", Loose, "en", "abc・|def")]
        [InlineData("あ！い", Loose, "ja", "あ|！|い")]
        [InlineData("あ！い", Loose, "en", "あ！|い")]
        [InlineData("abc！def", Loose, "zh", "abc|！|def")]
        [InlineData("abc！def", Loose, "en", "abc！|def")]
        [InlineData("abc‼def", Loose, "ja", "abc|‼|def")]
        [InlineData("abc‼def", Loose, "en", "abc‼|def")]
        [InlineData("あ：い；う", Loose, "ja", "あ|：|い|；|う")]
        [InlineData("あ：い；う", Loose, "en", "あ：|い；|う")]
        // Small kana, iteration marks, and hyphens after an ideograph: loose, whatever the language.
        [InlineData("あぃう", Loose, "ja", "あ|ぃ|う")]
        [InlineData("あぃう", Loose, "en", "あ|ぃ|う")]
        [InlineData("あぃう", Normal, "ja", "あぃ|う")]
        [InlineData("あ々い", Loose, "ja", "あ|々|い")]
        [InlineData("あ々い", Loose, "en", "あ|々|い")]
        [InlineData("あ々い", Normal, "ja", "あ々|い")]
        [InlineData("漢‐字", Loose, "ja", "漢|‐|字")]
        [InlineData("漢‐字", Normal, "ja", "漢‐|字")]
        [InlineData("ab‐cd", Loose, "ja", "ab‐|cd")]
        // Inseparable characters: loose breaks between two of them, never before the first, and never after Latin text.
        [InlineData("abc…def", Loose, "ja", "abc…|def")]
        [InlineData("abc…def", Loose, "en", "abc…|def")]
        [InlineData("abc…def", Loose, null, "abc…|def")]
        [InlineData("abc……def", Loose, "en", "abc…|…|def")]
        [InlineData("abc……def", Normal, "en", "abc……|def")]
        [InlineData("あ…い", Loose, "ja", "あ…|い")]
        [InlineData("あ……い", Loose, "ja", "あ…|…|い")]
        // Suffixes and prefixes of East Asian width: loose, Chinese and Japanese only.
        [InlineData("10％あ", Loose, "ja", "10|％|あ")]
        [InlineData("10％あ", Loose, "zh", "10|％|あ")]
        [InlineData("10％あ", Loose, "en", "10％|あ")]
        [InlineData("10％あ", Normal, "ja", "10％|あ")]
        [InlineData("10％", Loose, "ja", "10|％")]
        [InlineData("10℃あ", Loose, "ja", "10|℃|あ")]
        [InlineData("10′あ", Loose, "ja", "10|′|あ")]
        [InlineData("10%あ", Loose, "ja", "10%|あ")]
        [InlineData("10°あ", Loose, "ja", "10°|あ")]
        [InlineData("あ￥100", Loose, "ja", "あ|￥|100")]
        [InlineData("あ￥100", Loose, "en", "あ|￥100")]
        [InlineData("あ￥100", Normal, "ja", "あ|￥100")]
        [InlineData("￥100", Loose, "ja", "￥|100")]
        [InlineData("あ＄100", Loose, "ja", "あ|＄|100")]
        [InlineData("あ№5", Loose, "ja", "あ|№|5")]
        [InlineData("あ€5", Loose, "ja", "あ|€|5")]
        [InlineData("あ$100", Loose, "ja", "あ|$100")]
        [InlineData("あ±5", Loose, "ja", "あ|±5")]
        public void ATailoringThatNeedsAChineseOrJapaneseWritingSystem_AppliesOnlyToThoseLanguages(
            string text, LineBreakStrictness strictness, string? language, string expected)
        {
            Assert.Equal(expected, Render(text, strictness, language));
        }

        [Theory]
        [InlineData("ja")]
        [InlineData("JA")]
        [InlineData("ja-JP")]
        [InlineData("ja_JP")]
        [InlineData(" ja ")]
        [InlineData("zh")]
        [InlineData("zh-Hans-CN")]
        [InlineData("yue")]
        [InlineData("yue-HK")]
        [InlineData("cmn-Hans")]
        public void TheLanguageIsReadFromItsPrimarySubtag_WithoutRegardToCase(string language)
        {
            Assert.Equal("あ|い|う|〜|え|お", Render("あいう〜えお", Normal, language));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("en")]
        [InlineData("ko")]
        [InlineData("jam")]        // Jamaican Creole English, not Japanese
        [InlineData("zhx")]
        [InlineData("j")]
        [InlineData("-ja")]
        public void AnyOtherLanguage_OrNone_GetsNoWaveDashTailoring(string? language)
        {
            Assert.Equal("あ|い|う〜|え|お", Render("あいう〜えお", Normal, language));
        }

        [Theory]
        [InlineData("ja")]
        [InlineData("en")]
        [InlineData(null)]
        public void Auto_IsNormal_AndAnywhereBreaksAfterEveryCharacter_InEveryLanguage(string? language)
        {
            var normal = Render("あいう〜えお", Normal, language);
            Assert.Equal(normal, Render("あいう〜えお", LineBreakStrictness.Auto, language));
            Assert.Equal("あ|い|う|〜|え|お", Render("あいう〜えお", LineBreakStrictness.Anywhere, language));
            Assert.Equal("a|b|c", Render("abc", LineBreakStrictness.Anywhere, language));
        }

        [Fact]
        public void TheUnicodeDefault_IsUnchangedByTheLanguage()
        {
            // The conformance files are checked with strict and no language; a language must not move a strict break.
            const string text = "あいう〜え・お！10％ ￥5 …… a…b";
            Assert.Equal(Render(text, Strict, null), Render(text, Strict, "ja"));
        }

        [Fact]
        public void APrefixOrSuffixBreak_DoesNotOverrideARuleThatForbidsEveryBreak()
        {
            // After an opening bracket, next to a word joiner or a no-break space, the algorithm's prohibitions stand even in loose
            // Japanese text.
            Assert.Equal("あ|(￥|5", Render("あ(￥5", Loose, "ja"));
            Assert.Equal("10\u2060％", Render("10\u2060％", Loose, "ja"));
            Assert.Equal("10\u00A0％", Render("10\u00A0％", Loose, "ja"));
            Assert.Equal("￥\u2060100", Render("￥\u2060100", Loose, "ja"));
        }

        [Fact]
        public void APrefixOrSuffixBreak_DoesNotOverrideTheRulesForQuotesHyphensAndInseparableCharacters()
        {
            // The number rules are what loose Japanese text relaxes; the rules before them (LB19 quotes, LB21 hyphens and break-after
            // characters, LB22 inseparable characters) still hold next to a prefix or a suffix.
            Assert.Equal("￥-5", Render("￥-5", Loose, "ja"));
            Assert.Equal("￥…", Render("￥…", Loose, "ja"));
            Assert.Equal("10|％\"", Render("10％\"", Loose, "ja"));       // the break before the suffix is the tailoring; none before the quote
            Assert.Equal("￥\t|5", Render("￥\t5", Loose, "ja"));       // none before the tab (a break-after character), one after it
        }

        [Fact]
        public void APrefixOrSuffixBreak_FollowsTheSpaceRules()
        {
            // A line ends after a space and never before one.
            Assert.Equal("10 |％", Render("10 ％", Loose, "ja"));
            Assert.Equal("￥ |100", Render("￥ 100", Loose, "ja"));
        }

        [Fact]
        public void KeepAll_DoesNotHoldAPrefixOrSuffixOfEastAsianWidth_ToItsNumber()
        {
            // keep-all binds letters, not currency signs or percent signs: the Japanese tailoring shows through it.
            Assert.Equal("あ|￥|100", Render("あ￥100", Loose, "ja", WordBreakMode.KeepAll));
            Assert.Equal("あ|￥100", Render("あ￥100", Loose, "en", WordBreakMode.KeepAll));
        }

        [Fact]
        public void BreakAll_DoesNotChangeTheTailoredClasses()
        {
            Assert.Equal("a|b|c|・|d", Render("abc・d", Loose, "ja", WordBreakMode.BreakAll));
            Assert.Equal("a|b|c・|d", Render("abc・d", Loose, "en", WordBreakMode.BreakAll));
        }
    }
}
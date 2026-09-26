using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS Text 3's <c>line-break</c> tailorings that apply only where the writing system is Chinese or Japanese, reached from the
    /// document's <c>lang</c> attribute (its own, an ancestor's or the <c>html</c> element's): the same text and the same
    /// <c>line-break</c> break differently under <c>lang="ja"</c> and <c>lang="en"</c>. The rules themselves are checked in
    /// <c>PeachDrawing.Text.Tests</c>; these tests are about how the layout hands the language to them and what it does with the
    /// answer. Every character is set in <see cref="BundledFonts.LineBreak"/>, one em wide, so a line 50pt wide (three ems of 16pt
    /// and a little more) holds exactly three characters and the line a character lands on is the line break decision.
    /// </summary>
    public class LineBreakLanguageLayoutTests
    {
        private static string Page(string body, string htmlLang = "") =>
            "<!DOCTYPE html><html" + (htmlLang.Length > 0 ? $" lang='{htmlLang}'" : "") + "><head><style>"
            + BundledFonts.FontFaceRule(BundledFonts.LineBreak, "LineBreakTest", "font/truetype") + "</style></head><body style='margin:0'>" + body + "</body></html>";

        private static async Task<CssBox> LayOut(string body, string htmlLang = "")
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Page(body, htmlLang));
            return LayoutHarness.FindById(root, "p")!;
        }

        private static string[] WordTexts(CssBox box) =>
            LayoutHarness.Descendants(box).SelectMany(b => b.Words).Select(w => w.Text).Where(t => !string.IsNullOrEmpty(t)).Select(t => t!).ToArray();

        /// <summary>The text of each line, top to bottom.</summary>
        private static string[] LineTexts(CssBox box) =>
            box.LineBoxes
                .Select(line => string.Concat(line.Words.Select(word => word.Text)))
                .Where(text => text.Length > 0)
                .ToArray();

        private const string Style = "font-family:LineBreakTest; font-size:16pt; width:50pt; ";

        [Fact]
        public async Task WaveDash_StartsALine_OnlyInJapaneseText()
        {
            var japanese = await LayOut($"<p id='p' lang='ja' style='{Style}'>あいう〜えお</p>");
            Assert.Equal(["あいう", "〜えお"], LineTexts(japanese));

            var english = await LayOut($"<p id='p' lang='en' style='{Style}'>あいう〜えお</p>");
            Assert.Equal(["あい", "う〜え", "お"], LineTexts(english));

            var none = await LayOut($"<p id='p' style='{Style}'>あいう〜えお</p>");
            Assert.Equal(["あい", "う〜え", "お"], LineTexts(none));
        }

        [Fact]
        public async Task WaveDash_LandsAtTheStartOfTheSecondLine_InJapaneseText_AndTheParagraphIsTwoLinesTall()
        {
            var japanese = await LayOut($"<p id='p' lang='ja' style='{Style}'>あいう〜えお</p>");
            var english = await LayOut($"<p id='p' lang='en' style='{Style}'>あいう〜えお</p>");

            var words = LayoutHarness.Descendants(japanese).SelectMany(b => b.Words).Where(w => !string.IsNullOrEmpty(w.Text)).ToList();
            var u = words.Single(w => w.Text == "う");
            var dash = words.Single(w => w.Text == "〜");
            var e = words.Single(w => w.Text == "え");

            // The dash starts the second line, under the first kana, and the kana that follows it sits one em to its right.
            Assert.Equal(words[0].Left, dash.Left, 3);
            Assert.True(dash.Top > u.Top);
            Assert.Equal(dash.Top, e.Top, 3);
            Assert.Equal(dash.Left + dash.Width, e.Left, 3);

            var englishWords = LayoutHarness.Descendants(english).SelectMany(b => b.Words).Where(w => !string.IsNullOrEmpty(w.Text)).ToList();
            var englishPair = englishWords.Single(w => w.Text == "う〜");      // the dash is cut with the kana before it: no break between them
            Assert.Equal(englishWords[0].Left, englishPair.Left, 3);
            Assert.Equal(2 * dash.Width, englishPair.Width, 3);
            Assert.True(englishWords[0].Top < englishPair.Top);

            // Two lines against three: the paragraph is a line taller when the wave dash cannot start one.
            Assert.Equal(2, japanese.LineBoxes.Count);
            Assert.Equal(3, english.LineBoxes.Count);
            Assert.True(english.ActualBottom > japanese.ActualBottom);
        }

        [Fact]
        public async Task TheLanguage_IsInheritedFromAnAncestor_AndFromTheHtmlElement()
        {
            Assert.Equal(["あいう", "〜えお"], LineTexts(await LayOut($"<div lang='zh-Hant'><p id='p' style='{Style}'>あいう〜えお</p></div>")));
            Assert.Equal(["あいう", "〜えお"], LineTexts(await LayOut($"<p id='p' style='{Style}'>あいう〜えお</p>", htmlLang: "ja")));

            // The nearest lang wins.
            Assert.Equal(["あい", "う〜え", "お"], LineTexts(await LayOut($"<div lang='ja'><p id='p' lang='en' style='{Style}'>あいう〜えお</p></div>")));
            Assert.Equal(["あいう", "〜えお"], LineTexts(await LayOut($"<div lang='en'><p id='p' lang='ja' style='{Style}'>あいう〜えお</p></div>")));
        }

        [Theory]
        [InlineData("ja", "", 2)]      // the configured default applies to a document that declares no language
        [InlineData("en", "", 3)]
        [InlineData("en", "ja", 2)]    // the document's own language wins over the configured default
        [InlineData("ja", "en", 3)]
        public async Task TheConfiguredDefaultLanguage_AppliesWhenTheDocumentDeclaresNone(string defaultLanguage, string htmlLang, int lines)
        {
            // The words of every text box are cut when the document is parsed, so the default has to be known by then.
            var (root, _) = await PdfGeneratorLayoutHarness.LayoutAsync(
                Page($"<p id='p' style='{Style}'>あいう〜えお</p>", htmlLang),
                new PdfGenerateConfig { PageSize = PageSize.A4, DefaultLanguage = defaultLanguage });

            Assert.Equal(lines, LayoutHarness.FindById(root, "p")!.LineBoxes.Count);
        }

        [Fact]
        public async Task StrictLineBreak_KeepsTheWaveDashOffTheLine_InJapaneseText()
        {
            var box = await LayOut($"<p id='p' lang='ja' style='{Style} line-break:strict'>あいう〜えお</p>");

            Assert.Equal(["あい", "う〜え", "お"], LineTexts(box));
        }

        [Fact]
        public async Task LooseLineBreak_LetsCentredPunctuationStartALine_OnlyInJapaneseText()
        {
            var japanese = await LayOut($"<p id='p' lang='ja' style='{Style} line-break:loose'>あいう・えお</p>");
            Assert.Equal(["あいう", "・えお"], LineTexts(japanese));

            var normal = await LayOut($"<p id='p' lang='ja' style='{Style} line-break:normal'>あいう・えお</p>");
            Assert.Equal(["あい", "う・え", "お"], LineTexts(normal));

            var english = await LayOut($"<p id='p' lang='en' style='{Style} line-break:loose'>あいう・えお</p>");
            Assert.Equal(["あい", "う・え", "お"], LineTexts(english));
        }

        [Fact]
        public async Task LooseLineBreak_DoesNotBreakBeforeAnExclamationMarkAfterLatinText_InOtherLanguages()
        {
            // "wait" and the fullwidth exclamation mark are 5 ems: a line 50pt wide cannot hold them together.
            var english = await LayOut($"<p id='p' lang='en' style='{Style} line-break:loose'>wait！</p>");
            Assert.Equal(["wait！"], LineTexts(english));

            var japanese = await LayOut($"<p id='p' lang='ja' style='{Style} line-break:loose'>wait！</p>");
            Assert.Equal(["wait", "！"], LineTexts(japanese));
        }

        [Fact]
        public async Task LooseLineBreak_DoesNotBreakBeforeAnEllipsisAfterLatinText()
        {
            foreach (var lang in new[] { "en", "ja" })
            {
                var box = await LayOut($"<p id='p' lang='{lang}' style='{Style} line-break:loose'>wait…</p>");

                Assert.Equal(["wait…"], WordTexts(box));
                Assert.Equal(["wait…"], LineTexts(box));
            }
        }

        [Fact]
        public async Task LooseLineBreak_BreaksBeforeASuffixOfEastAsianWidth_OnlyInJapaneseText()
        {
            var japanese = await LayOut($"<p id='p' lang='ja' style='{Style} line-break:loose'>あ10％</p>");
            Assert.Equal(["あ10", "％"], LineTexts(japanese));

            var english = await LayOut($"<p id='p' lang='en' style='{Style} line-break:loose'>あ10％</p>");
            Assert.Equal(["あ", "10％"], LineTexts(english));

            var normal = await LayOut($"<p id='p' lang='ja' style='{Style} line-break:normal'>あ10％</p>");
            Assert.Equal(["あ", "10％"], LineTexts(normal));

            // A percent sign of Latin width is not a suffix of East Asian width.
            var ascii = await LayOut($"<p id='p' lang='ja' style='{Style} line-break:loose'>あ10%</p>");
            Assert.Equal(["あ", "10%"], LineTexts(ascii));
        }

        [Fact]
        public async Task LooseLineBreak_BreaksAfterAPrefixOfEastAsianWidth_OnlyInJapaneseText()
        {
            var japanese = await LayOut($"<p id='p' lang='ja' style='{Style} line-break:loose'>あ￥100</p>");
            Assert.Equal(["あ￥", "100"], LineTexts(japanese));

            var english = await LayOut($"<p id='p' lang='en' style='{Style} line-break:loose'>あ￥100</p>");
            Assert.Equal(["あ", "￥100"], LineTexts(english));
        }

        [Fact]
        public async Task AcrossAnElementBoundary_TheLanguageOfTheTextApplies()
        {
            // Loose Japanese text may end a line before the centred punctuation, even when it opens an inline element.
            var japanese = await LayOut($"<p id='p' lang='ja' style='{Style} line-break:loose'>あいう<b>・</b>えお</p>");
            Assert.Equal(["あいう", "・えお"], LineTexts(japanese));

            // In other languages the seam before the dot is no opportunity: the dot stays on the line of the kana before it (the
            // layout does not move an unbreakable pair of inline boxes to a line of its own, so that line overflows).
            var english = await LayOut($"<p id='p' lang='en' style='{Style} line-break:loose'>あいう<b>・</b>えお</p>");
            var words = LayoutHarness.Descendants(english).SelectMany(b => b.Words).Where(w => !string.IsNullOrEmpty(w.Text)).ToList();
            Assert.Equal(words.Single(w => w.Text == "う").Top, words.Single(w => w.Text == "・").Top, 3);
            Assert.Equal(["あいう・", "えお"], LineTexts(english));
        }

        [Fact]
        public async Task ALanguageSetOnAnInlineElement_AppliesToItsOwnText()
        {
            var box = await LayOut($"<p id='p' style='{Style}'>あいう<span lang='ja'>〜えお</span></p>");

            // The wave dash sits in the Japanese span, so the line may end before it.
            Assert.Equal(["あいう", "〜えお"], LineTexts(box));
        }
    }
}

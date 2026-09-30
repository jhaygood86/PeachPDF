using PeachDrawing.Text.Unicode;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// End-to-end wiring coverage for Khmer's coeng/subjoined-consonant shaping (issue #1493) - drives
    /// the real HTML layout pipeline (<see cref="CssBidiParagraphResolver"/> &#8594;
    /// <see cref="CssBox.AppendWordsFromText"/> &#8594; <see cref="DerivedStyle.ResolveWordShapingFeatures"/>
    /// &#8594; <c>GsubShaper.Shape</c>'s Khmer stage), so a genuinely broken wiring anywhere in that
    /// chain (not just a bug in <c>KhmerCategoryClassifier</c>/<c>KhmerSyllableScanner</c>/
    /// <c>KhmerReorderer</c> in isolation) would show up as the resolved
    /// <see cref="CssRectWord.EffectiveKhmerCategories"/>/measured width never reflecting real shaping -
    /// the same "prove it isn't a no-op" standard this repo's own paint/shaping-feature conventions ask
    /// for. The core algorithm itself is exhaustively verified against real HarfBuzz's own output in
    /// <c>KhmerUseShapingCharacterizationTests</c>; this file only proves the surrounding wiring reaches
    /// it. Named to match this repo's established per-script pair - see that class's own remarks on why.
    /// </summary>
    public class KhmerUseCharacterizationTests
    {
        private const string Ka = "ក";
        private const string Coeng = "្";
        private const string Ro = "រ";
        private const string VowelSignI = "ិ";

        private static string B64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

        private static async Task<CssRectWord> LayoutWord(string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html, pageWidth: 595, pageHeight: 842, margin: 0);
            var p = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name.Equals("p", StringComparison.OrdinalIgnoreCase) == true);
            return WordsOf(p).First(w => w.Text != "\n");
        }

        private static async Task<System.Collections.Generic.List<CssRectWord>> LayoutAllWords(string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html, pageWidth: 595, pageHeight: 842, margin: 0);
            var p = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name.Equals("p", StringComparison.OrdinalIgnoreCase) == true);
            return WordsOf(p);
        }

        private static System.Collections.Generic.List<CssRectWord> WordsOf(CssBox p)
        {
            var words = new System.Collections.Generic.List<CssRectWord>();
            Collect(p, words);
            return words;
        }

        private static void Collect(CssBox box, System.Collections.Generic.List<CssRectWord> words)
        {
            words.AddRange(box.Words.OfType<CssRectWord>());
            foreach (var child in box.Boxes)
                Collect(child, words);
        }

        [Fact]
        public async Task EndToEndLayout_KhmerWord_ResolvesScriptTagAndKhmerCategories()
        {
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'KhmerTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Khmer)}') format('truetype'); }}
body {{ font-family: 'KhmerTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{Ka}{Coeng}{Ro}</p></body>
</html>");

            Assert.Equal("khmr", word.ScriptTag);
            Assert.NotNull(word.EffectiveKhmerCategories);
            Assert.Equal([KhmerCategory.C, KhmerCategory.H, KhmerCategory.Ra], word.EffectiveKhmerCategories);
        }

        [Fact]
        public async Task EndToEndLayout_CoengRoWithVowel_MeasuresNarrowerThanTheSumOfIndependentGlyphs()
        {
            // Regression proof that real GSUB coeng+RO reordering/ligation actually ran (not a no-op):
            // if KA+COENG+RO+VOWEL_SIGN_I were measured as 4 independent, unshaped glyphs, it would be
            // noticeably wider than the real reordered+ligated 3-glyph result
            // (KhmerUseShapingCharacterizationTests pins the exact real glyph sequence this measurement
            // reflects).
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'KhmerTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Khmer)}') format('truetype'); }}
body {{ font-family: 'KhmerTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{Ka}{Coeng}{Ro}{VowelSignI}</p></body>
</html>");

            Assert.NotNull(word.EffectiveKhmerCategories);
            Assert.Equal(4, word.EffectiveKhmerCategories!.Length);
            // Four un-shaped Khmer letter-width glyphs at 14pt comfortably exceed 15pt; the real
            // reordered+ligated result (three glyphs) measures well under that.
            Assert.True(word.Width < 15, $"width={word.Width}pt is too wide for reordered/ligated Khmer - GSUB pref/reorder may not have run");
        }

        [Fact]
        public async Task EndToEndLayout_LatinWord_NoScriptTagOrKhmerCategories()
        {
            // Regression: this whole feature must be a complete no-op for ordinary (non-Khmer) text.
            var word = await LayoutWord(@"<!DOCTYPE html>
<html><body><p style=""width:400px; font-size:14pt"">Hello</p></body></html>");

            Assert.NotEqual("khmr", word.ScriptTag);
            Assert.Null(word.EffectiveKhmerCategories);
        }

        [Fact]
        public async Task EndToEndLayout_MixedKhmerAndLatinParagraph_LatinWordStaysUnaffected()
        {
            // Regression mirroring DevanagariUseCharacterizationTests' own equivalent case: CssBox.KhmerCategories
            // is allocated once per PARAGRAPH the moment ANY codepoint anywhere in it is Khmer, then
            // sliced onto every contributing box in that paragraph - including one whose own text is
            // pure Latin. Without CssBox.ToRuneIndexedKhmerCategories's own "does this word's own span
            // actually contain a non-Other category" guard, the Latin word here would get a spurious
            // non-null, all-KhmerCategory.Other array, and GsubShaper would run the whole Khmer pipeline
            // against ordinary English text under a "latn" script preference purely because Khmer text
            // happens to sit elsewhere in the same paragraph.
            var words = await LayoutAllWords($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'KhmerTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Khmer)}') format('truetype'); }}
body {{ font-family: 'KhmerTest', sans-serif; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>Hello {Ka}{Coeng}{Ro}</p></body>
</html>");

            var latinWord = words.First(w => w.Text == "Hello");
            var khmerWord = words.First(w => w.Text == $"{Ka}{Coeng}{Ro}");

            Assert.Null(latinWord.EffectiveKhmerCategories);
            Assert.NotEqual("khmr", latinWord.ScriptTag);
            Assert.NotNull(khmerWord.EffectiveKhmerCategories);
        }
    }
}

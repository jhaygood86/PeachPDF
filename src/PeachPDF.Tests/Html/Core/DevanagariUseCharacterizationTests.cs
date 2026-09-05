using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text.Shaping.Use;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// End-to-end wiring coverage for Devanagari's Universal Shaping Engine syllable reordering
    /// (issue #533, Phase 5b) - drives the real HTML layout pipeline
    /// (<see cref="CssBidiParagraphResolver"/> → <see cref="CssBox.AppendWordsFromText"/> →
    /// <see cref="DerivedStyle.ResolveWordShapingFeatures"/> → <c>GsubShaper.Shape</c>'s USE stage),
    /// so a genuinely broken wiring anywhere in that chain (not just a bug in
    /// <c>UseCategoryClassifier</c>/<c>UseSyllableScanner</c>/<c>UseReorderer</c> in isolation) would
    /// show up as the resolved <see cref="CssRectWord.EffectiveUseCategories"/>/measured width never
    /// reflecting real shaping - the same "prove it isn't a no-op" standard this repo's own
    /// paint/shaping-feature conventions ask for. The core algorithm itself is exhaustively verified
    /// against real HarfBuzz's own output in <c>DevanagariUseShapingCharacterizationTests</c>; this
    /// file only proves the surrounding wiring reaches it.
    /// </summary>
    public class DevanagariUseCharacterizationTests
    {
        private const string Ka = "क";
        private const string Virama = "्";
        private const string Ssa = "ष";
        private const string VowelSignI = "ि";

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
        public async Task EndToEndLayout_DevanagariWord_ResolvesScriptTagAndUseCategories()
        {
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'DevaTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Devanagari)}') format('truetype'); }}
body {{ font-family: 'DevaTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{Ka}{VowelSignI}</p></body>
</html>");

            Assert.Equal("deva", word.ScriptTag);
            Assert.NotNull(word.EffectiveUseCategories);
            Assert.Equal([UseCategory.B, UseCategory.VPre], word.EffectiveUseCategories);
        }

        [Fact]
        public async Task EndToEndLayout_ConjunctWithMatra_MeasuresNarrowerThanTheSumOfIndependentGlyphs()
        {
            // Regression proof that real GSUB conjunct ligation + reorder actually ran (not a no-op):
            // if क्षि ("KA+VIRAMA+SSA+VOWEL_SIGN_I") were measured as 4 independent, unshaped glyphs,
            // it would be noticeably wider than the real ligated+reordered 2-glyph result
            // (DevanagariUseShapingCharacterizationTests.ConjunctWithMatra_LigatesTheWholeConjunctAndMovesTheMatraBefore
            // pins the exact real glyph sequence this measurement reflects).
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'DevaTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Devanagari)}') format('truetype'); }}
body {{ font-family: 'DevaTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{Ka}{Virama}{Ssa}{VowelSignI}</p></body>
</html>");

            Assert.NotNull(word.EffectiveUseCategories);
            Assert.Equal(4, word.EffectiveUseCategories!.Length);
            // Four un-ligated Devanagari letter-width glyphs at 14pt comfortably exceed 15pt; the real
            // ligated+reordered result (matra + one fused conjunct glyph) measures well under that.
            Assert.True(word.Width < 15, $"width={word.Width}pt is too wide for a ligated conjunct - GSUB cjct/reorder may not have run");
        }

        [Fact]
        public async Task EndToEndLayout_LatinWord_NoScriptTagOrUseCategories()
        {
            // Regression: this whole feature must be a complete no-op for ordinary (non-Devanagari)
            // text - Latin content must never pick up a "deva" tag or non-null USE categories.
            var word = await LayoutWord(@"<!DOCTYPE html>
<html><body><p style=""width:400px; font-size:14pt"">Hello</p></body></html>");

            Assert.NotEqual("deva", word.ScriptTag);
            Assert.Null(word.EffectiveUseCategories);
        }

        [Fact]
        public async Task EndToEndLayout_MixedDevanagariAndLatinParagraph_LatinWordStaysUnaffected()
        {
            // Regression for a real bug an adversarial post-change review pass found: CssBox.UseCategories
            // is allocated once per PARAGRAPH the moment ANY codepoint anywhere in it is Devanagari, then
            // sliced onto every contributing box in that paragraph - including one whose own text is pure
            // Latin. Without CssBox.ToRuneIndexedUseCategories's own "does this word's own span actually
            // contain a non-O category" guard, the Latin word here would get a spurious non-null,
            // all-UseCategory.O array, and GsubShaper would run the whole USE pipeline (nukt/ccmp/locl/akhn,
            // a trial rphf, abvs/blws/haln/pres/psts) against ordinary English text under a "latn" script
            // preference purely because Devanagari text happens to sit elsewhere in the same paragraph.
            var words = await LayoutAllWords($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'DevaTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Devanagari)}') format('truetype'); }}
body {{ font-family: 'DevaTest', sans-serif; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>Hello {Ka}{VowelSignI}</p></body>
</html>");

            var latinWord = words.First(w => w.Text == "Hello");
            var devanagariWord = words.First(w => w.Text == $"{Ka}{VowelSignI}");

            Assert.Null(latinWord.EffectiveUseCategories);
            Assert.NotEqual("deva", latinWord.ScriptTag);
            Assert.NotNull(devanagariWord.EffectiveUseCategories);
        }
    }
}

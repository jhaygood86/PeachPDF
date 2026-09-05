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
    /// End-to-end wiring coverage for Tamil's Universal Shaping Engine syllable reordering (issue
    /// #533, Phase 5c) - drives the real HTML layout pipeline the same way
    /// <see cref="DevanagariUseCharacterizationTests"/> does, proving the generalized
    /// <c>CssBidiParagraphResolver.UseShapedScripts</c> gate reaches a fourth script's real font. The
    /// core algorithm itself is exhaustively verified against real HarfBuzz's own output in
    /// <c>TamilUseShapingCharacterizationTests</c>.
    /// </summary>
    public class TamilUseCharacterizationTests
    {
        private const string Ka = "க";
        private const string Virama = "்";
        private const string Ssa = "ஷ";
        private const string VowelSignE = "ெ";

        private static string B64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

        private static async Task<CssRectWord> LayoutWord(string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html, pageWidth: 595, pageHeight: 842, margin: 0);
            var p = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name.Equals("p", StringComparison.OrdinalIgnoreCase) == true);
            return WordsOf(p).First(w => w.Text != "\n");
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
        public async Task EndToEndLayout_TamilWord_ResolvesScriptTagAndUseCategories()
        {
            // Tamil's pre-base vowel sign (VowelSignE) is a real 3-codepoint word here (ெ is one Unicode
            // codepoint, U+0BC6); real HarfBuzz places the matra before the base the same way this
            // asserts for Bengali/Devanagari/Gujarati.
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TamlTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Tamil)}') format('truetype'); }}
body {{ font-family: 'TamlTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{Ka}{VowelSignE}</p></body>
</html>");

            Assert.Equal("taml", word.ScriptTag);
            Assert.NotNull(word.EffectiveUseCategories);
            Assert.Equal([UseCategory.B, UseCategory.VPre], word.EffectiveUseCategories);
        }

        [Fact]
        public async Task EndToEndLayout_ConjunctWithMatra_MeasuresNarrowerThanTheSumOfIndependentGlyphs()
        {
            // KA + VIRAMA + SSA (Grantha-origin loanword letter) + VOWEL SIGN E - the font's own
            // cjct/half features still ligate the conjunct (see
            // TamilUseShapingCharacterizationTests.ConjunctWithMatra_LigatesTheGranthaConjunctAndMovesTheMatraBefore),
            // proving real GSUB conjunct ligation + reorder ran for Tamil too, not just Indic scripts
            // whose conjuncts are native rather than Sanskrit-loanword-only. Compared against this
            // font's own measured one-codepoint-at-a-time widths rather than a hardcoded absolute pt
            // threshold - Tamil's own glyph advances in this bundled font are wider still than
            // Bengali's/Devanagari's own, so a threshold tuned for either of those fonts doesn't
            // transfer; a self-calibrating relative comparison is robust to that per-font difference.
            var conjunctWord = await LayoutWord(Html($"{Ka}{Virama}{Ssa}{VowelSignE}"));
            var kaWord = await LayoutWord(Html(Ka));
            var viramaWord = await LayoutWord(Html(Virama));
            var ssaWord = await LayoutWord(Html(Ssa));
            var vowelSignEWord = await LayoutWord(Html(VowelSignE));
            var unligatedSum = kaWord.Width + viramaWord.Width + ssaWord.Width + vowelSignEWord.Width;

            Assert.NotNull(conjunctWord.EffectiveUseCategories);
            Assert.Equal(4, conjunctWord.EffectiveUseCategories!.Length);
            Assert.True(conjunctWord.Width < unligatedSum * 0.9,
                $"conjunct width={conjunctWord.Width}pt is not meaningfully narrower than the sum of its 4 " +
                $"individually-measured codepoints ({unligatedSum}pt) - GSUB cjct/reorder may not have run");
        }

        private static string Html(string text) => $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'TamlTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Tamil)}') format('truetype'); }}
body {{ font-family: 'TamlTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{text}</p></body>
</html>";
    }
}

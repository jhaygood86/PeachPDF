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
    /// End-to-end wiring coverage for Bengali's Universal Shaping Engine syllable reordering (issue
    /// #533, Phase 5c) - drives the real HTML layout pipeline
    /// (<see cref="CssBidiParagraphResolver"/> → <see cref="CssBox.AppendWordsFromText"/> →
    /// <see cref="DerivedStyle.ResolveWordShapingFeatures"/> → <c>GsubShaper.Shape</c>'s USE stage),
    /// including the generalized <c>CssBidiParagraphResolver.UseShapedScripts</c> gate (Devanagari/
    /// Bengali/Gujarati/Tamil) that replaced the original Devanagari-only check - the same
    /// "prove it isn't a no-op" standard <c>DevanagariUseCharacterizationTests</c> already applies. The
    /// core algorithm itself is exhaustively verified against real HarfBuzz's own output in
    /// <c>BengaliUseShapingCharacterizationTests</c>; this file only proves the surrounding wiring
    /// reaches it for a second script.
    /// </summary>
    public class BengaliUseCharacterizationTests
    {
        private const string Ka = "ক";
        private const string Virama = "্";
        private const string Ssa = "ষ";
        private const string VowelSignI = "ি";

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
        public async Task EndToEndLayout_BengaliWord_ResolvesScriptTagAndUseCategories()
        {
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'BengTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Bengali)}') format('truetype'); }}
body {{ font-family: 'BengTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{Ka}{VowelSignI}</p></body>
</html>");

            Assert.Equal("beng", word.ScriptTag);
            Assert.NotNull(word.EffectiveUseCategories);
            Assert.Equal([UseCategory.B, UseCategory.VPre], word.EffectiveUseCategories);
        }

        [Fact]
        public async Task EndToEndLayout_ConjunctWithMatra_MeasuresNarrowerThanTheSumOfIndependentGlyphs()
        {
            // Regression proof that real GSUB conjunct ligation + reorder actually ran (not a no-op) -
            // mirrors DevanagariUseCharacterizationTests' own identical proof for a second script.
            // Unlike that test, the comparison here is against this font's own measured
            // one-codepoint-at-a-time widths rather than a hardcoded absolute pt threshold: Bengali's
            // own glyph advances in this bundled font are simply wider than Devanagari's own, so a
            // threshold tuned for that font doesn't transfer - a self-calibrating relative comparison
            // is robust to that per-font difference while still failing if ligation stops firing.
            var conjunctWord = await LayoutWord(Html($"{Ka}{Virama}{Ssa}{VowelSignI}"));
            var kaWord = await LayoutWord(Html(Ka));
            var viramaWord = await LayoutWord(Html(Virama));
            var ssaWord = await LayoutWord(Html(Ssa));
            var vowelSignIWord = await LayoutWord(Html(VowelSignI));
            var unligatedSum = kaWord.Width + viramaWord.Width + ssaWord.Width + vowelSignIWord.Width;

            Assert.NotNull(conjunctWord.EffectiveUseCategories);
            Assert.Equal(4, conjunctWord.EffectiveUseCategories!.Length);
            Assert.True(conjunctWord.Width < unligatedSum * 0.9,
                $"conjunct width={conjunctWord.Width}pt is not meaningfully narrower than the sum of its 4 " +
                $"individually-measured codepoints ({unligatedSum}pt) - GSUB cjct/reorder may not have run");
        }

        private static string Html(string text) => $@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'BengTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Bengali)}') format('truetype'); }}
body {{ font-family: 'BengTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{text}</p></body>
</html>";
    }
}

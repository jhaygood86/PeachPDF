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
    /// End-to-end wiring coverage for Gujarati's Universal Shaping Engine syllable reordering (issue
    /// #533, Phase 5c) - drives the real HTML layout pipeline the same way
    /// <see cref="DevanagariUseCharacterizationTests"/> does, proving the generalized
    /// <c>CssBidiParagraphResolver.UseShapedScripts</c> gate reaches a third script's real font. The
    /// core algorithm itself (including the nested-contextual-lookup GSUB fix this real font's own
    /// `abvs` feature needed - see this feature's own recent-fixes entry) is exhaustively verified
    /// against real HarfBuzz's own output in <c>GujaratiUseShapingCharacterizationTests</c>.
    /// </summary>
    public class GujaratiUseCharacterizationTests
    {
        private const string Ka = "ક";
        private const string Virama = "્";
        private const string Ssa = "ષ";
        private const string VowelSignI = "િ";

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
        public async Task EndToEndLayout_GujaratiWord_ResolvesScriptTagAndUseCategories()
        {
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'GujrTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Gujarati)}') format('truetype'); }}
body {{ font-family: 'GujrTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{Ka}{VowelSignI}</p></body>
</html>");

            Assert.Equal("gujr", word.ScriptTag);
            Assert.NotNull(word.EffectiveUseCategories);
            Assert.Equal([UseCategory.B, UseCategory.VPre], word.EffectiveUseCategories);
        }

        [Fact]
        public async Task EndToEndLayout_ConjunctWithMatra_MeasuresNarrowerThanTheSumOfIndependentGlyphs()
        {
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'GujrTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Gujarati)}') format('truetype'); }}
body {{ font-family: 'GujrTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{Ka}{Virama}{Ssa}{VowelSignI}</p></body>
</html>");

            Assert.NotNull(word.EffectiveUseCategories);
            Assert.Equal(4, word.EffectiveUseCategories!.Length);
            Assert.True(word.Width < 15, $"width={word.Width}pt is too wide for a ligated conjunct - GSUB cjct/reorder may not have run");
        }
    }
}

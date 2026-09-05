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
    /// Coverage for <c>CssBidiParagraphResolver</c>'s generalized <c>UseShapedScripts</c> gate (issue
    /// #533, Phase 5c) - the set replacing the original Devanagari-only string check
    /// (<c>resolvedScripts[c] != "Devanagari"</c>) so <see cref="CssBox.UseCategories"/> is allocated
    /// for a paragraph containing text in *any* of Devanagari/Bengali/Gujarati/Tamil, not just
    /// Devanagari. These tests specifically exercise a paragraph mixing *two different* USE-shaped
    /// scripts (not just one USE-shaped script plus Latin, which
    /// <see cref="DevanagariUseCharacterizationTests.EndToEndLayout_MixedDevanagariAndLatinParagraph_LatinWordStaysUnaffected"/>
    /// already covers) - a case a naive single-string-equality-to-HashSet-membership port could still
    /// get wrong (e.g. if the replacement set only checked the *first* matched script per paragraph).
    /// </summary>
    public class MixedUseShapedScriptsCharacterizationTests
    {
        private const string DevanagariKa = "क";
        private const string DevanagariVowelSignI = "ि";
        private const string BengaliKa = "ক";
        private const string BengaliVowelSignI = "ি";

        private static string B64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

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
        public async Task EndToEndLayout_DevanagariAndBengaliInTheSameParagraph_BothResolveTheirOwnScriptAndCategories()
        {
            // Both fonts declare @font-face for their own script's text only, but font-family lists
            // both - font fallback per-word picks whichever face actually covers that word's codepoints,
            // so each word's own script tag/USE categories still need to resolve independently and
            // correctly even though they share one paragraph (and therefore one CssBox.UseCategories
            // allocation, per-character-sliced across both words - see CssBox.UseCategories' own remarks).
            var words = await LayoutAllWords($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'DevaTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Devanagari)}') format('truetype'); }}
@font-face {{ font-family: 'BengTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Bengali)}') format('truetype'); }}
body {{ font-family: 'DevaTest', 'BengTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{DevanagariKa}{DevanagariVowelSignI} {BengaliKa}{BengaliVowelSignI}</p></body>
</html>");

            var devanagariWord = words.First(w => w.Text == $"{DevanagariKa}{DevanagariVowelSignI}");
            var bengaliWord = words.First(w => w.Text == $"{BengaliKa}{BengaliVowelSignI}");

            Assert.Equal("deva", devanagariWord.ScriptTag);
            Assert.NotNull(devanagariWord.EffectiveUseCategories);

            Assert.Equal("beng", bengaliWord.ScriptTag);
            Assert.NotNull(bengaliWord.EffectiveUseCategories);
        }

        [Fact]
        public async Task EndToEndLayout_LatinWordSharingAParagraphWithBengaliText_StaysUnaffected()
        {
            // The same adversarial-review-found gap DevanagariUseCharacterizationTests' own identical
            // test guards, re-proven for Bengali specifically (a script the original Devanagari-only
            // string-equality check could never have allocated CssBox.UseCategories for at all, so this
            // exercises the new HashSet-membership check's own "only allocate/classify for an in-set
            // script" behavior, not just the pre-existing per-word null-vs-non-null slicing).
            var words = await LayoutAllWords($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'BengTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.Bengali)}') format('truetype'); }}
body {{ font-family: 'BengTest', sans-serif; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>Hello {BengaliKa}{BengaliVowelSignI}</p></body>
</html>");

            var latinWord = words.First(w => w.Text == "Hello");
            var bengaliWord = words.First(w => w.Text == $"{BengaliKa}{BengaliVowelSignI}");

            Assert.Null(latinWord.EffectiveUseCategories);
            Assert.NotEqual("beng", latinWord.ScriptTag);
            Assert.NotNull(bengaliWord.EffectiveUseCategories);
        }
    }
}

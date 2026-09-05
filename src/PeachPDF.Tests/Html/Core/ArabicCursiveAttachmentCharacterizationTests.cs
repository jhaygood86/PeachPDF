using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Real-font characterization for GPOS Lookup Type 3 (Cursive Attachment,
    /// <see cref="PeachPDF.Text.GposPositioner.ApplyCursiveAttachment"/>) against a font whose own Arabic
    /// joining actually relies on it - <see cref="BundledFonts.ArabicCursive"/> ("Aref Ruqaa"), unlike
    /// <see cref="BundledFonts.Arabic"/> ("Noto Sans Arabic", used by <see cref="ArabicJoiningCharacterizationTests"/>),
    /// which defines no `curs` GPOS feature at all.
    ///
    /// This is the regression surface for a real bug: the first implementation of cursive attachment
    /// computed its own formula directly from the OpenType spec's prose ("adjusts the x-coordinate so the
    /// two points coincide") rather than porting a real shaping engine's actual algorithm. That formula
    /// was internally self-consistent (its own synthetic tests passed) but wrong against this real font -
    /// it produced deeply negative advances that collapsed an entire connected word's measured width to
    /// roughly zero, found by rasterizing real output and seeing whole words render blank. Cross-checked
    /// against real HarfBuzz's own output for the same text+font (via `uharfbuzz`) before rewriting
    /// <c>GposPositioner.TryApplyCursivePair</c> to port HarfBuzz's actual RTL main-direction formula
    /// instead of re-deriving a second one from spec text - see this fix's own recent-fixes entry.
    /// </summary>
    public class ArabicCursiveAttachmentCharacterizationTests
    {
        // Same letters ArabicJoiningCharacterizationTests uses, for consistency across the Arabic-family
        // test fixtures - all present in the bundled Aref Ruqaa subset (see generate_aref_ruqaa_subset.py).
        private const string Beh = "ب";
        private const string Teh = "ت";

        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.ArabicCursive)).Fontface;
            return new OpenTypeDescriptor("aref-ruqaa-test", "aref-ruqaa-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Theory]
        [InlineData("تب")]
        [InlineData("بت")]
        public async Task EndToEndLayout_CursivelyConnectedWord_MeasuresAPlausiblePositiveWidth(string text)
        {
            // The pre-fix bug's own signature: a wrong cursive-attachment formula drove this exact
            // 2-letter word's measured width to ~0 (sometimes exactly 0, sometimes a fraction of a point) -
            // regardless of which of the two letters carried the exit vs. entry anchor, confirming this
            // wasn't one unlucky glyph pair but a structural formula error. 300 design units is a
            // generous floor - real single Arabic letters in this font measure several hundred design
            // units wide alone, so two connected ones must clear it by a wide margin if the fix holds.
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'CursiveTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.ArabicCursive)}') format('truetype'); }}
body {{ font-family: 'CursiveTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{text}</p></body>
</html>");

            var descriptor = Descriptor();
            var unitsPerEm = descriptor.UnitsPerEm;
            var minPlausibleWidthPt = 300.0 / unitsPerEm * 14.0;

            Assert.True(word.Width > minPlausibleWidthPt,
                $"width={word.Width}pt is implausibly small for a 2-letter cursively-connected word (floor {minPlausibleWidthPt}pt) - the pre-fix bug collapsed this to ~0");
        }

        [Fact]
        public async Task EndToEndLayout_TehBeh_MatchesRealHarfBuzzsOwnTotalAdvance()
        {
            // Pins the exact expected value, not just "positive": cross-checked directly against real
            // HarfBuzz (`uharfbuzz`, shaping this exact text through this exact font file) during
            // development, which reported a total x-advance of 805 design units (at this font's own 1000
            // unitsPerEm) for "تب" - matching the plain, uncorrected sum of the two letters' own nominal
            // widths (313 + 492), since this specific letter pair's cursive connection happens to leave
            // the run's total advance unchanged even though it visibly repositions the glyphs. If this
            // ever regresses, it means the ported formula (GposPositioner.TryApplyCursivePair) has
            // drifted from HarfBuzz's own real behavior again.
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'CursiveTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.ArabicCursive)}') format('truetype'); }}
body {{ font-family: 'CursiveTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>{Teh}{Beh}</p></body>
</html>");

            var descriptor = Descriptor();
            var expectedWidthPt = 805.0 / descriptor.UnitsPerEm * 14.0;

            Assert.Equal(expectedWidthPt, word.Width, precision: 2);
        }

        [Fact]
        public async Task EndToEndLayout_LatinWord_NoCursiveCorrectionApplied()
        {
            // Regression: this whole lookup type must be a complete no-op for ordinary (non-Arabic-family)
            // text - GposPositioner only requests "curs" when a run carries resolved joining forms, so
            // Latin content (which never does) must measure exactly as if cursive attachment didn't exist.
            var word = await LayoutWord($@"<!DOCTYPE html>
<html><head><style>
@font-face {{ font-family: 'CursiveTest'; src: url('data:font/truetype;base64,{B64(BundledFonts.ArabicCursive)}') format('truetype'); }}
body {{ font-family: 'CursiveTest'; font-size: 14pt; }}
p {{ width: 400px; }}
</style></head>
<body><p>Hello</p></body>
</html>");

            Assert.Null(word.EffectiveJoiningForms);
            Assert.True(word.Width > 0);
        }

        private static string B64(string path) => Convert.ToBase64String(File.ReadAllBytes(path));

        // Routed through the shared LayoutHarness (see its own remarks: "prefer this over hand-rolling
        // another per-file BuildAndLayout copy") rather than this file's own HtmlContainerInt/
        // PdfSharpAdapter/GraphicsAdapter wiring - margin: 0 matches this file's own prior no-margin setup,
        // since these tests assert word-level shaping properties, not page-margin-relative coordinates.
        private static async Task<CssRectWord> LayoutWord(string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html, pageWidth: 595, pageHeight: 842, margin: 0);
            var p = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name.Equals("p", StringComparison.OrdinalIgnoreCase) == true);
            return WordsOf(p).First(w => w.Text != "\n");
        }

        private static List<CssRectWord> WordsOf(CssBox p)
        {
            var words = new List<CssRectWord>();
            Collect(p, words);
            return words;
        }

        private static void Collect(CssBox box, List<CssRectWord> words)
        {
            words.AddRange(box.Words.OfType<CssRectWord>());
            foreach (var child in box.Boxes)
                Collect(child, words);
        }
    }
}

using PeachPDF.Text;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for the real-GSUB side of <c>font-variant-caps</c>: when the resolved font
    /// actually has <c>smcp</c>/<c>c2sc</c>/<c>titl</c> GSUB data (confirmed via direct byte inspection
    /// of the bundled Source Sans 3), small-caps/all-small-caps/titling-caps must render via real glyph
    /// substitution rather than the synthetic uppercase+shrink approximation
    /// <see cref="SmallCapsIntegrationTests"/> covers for a font that lacks the feature (Source Code
    /// Pro, confirmed to carry zero caps-family GSUB tags). Petite-caps/all-petite-caps are unsupported
    /// by both of those bundled fonts (still covered below as "unsupported -&gt; no visual effect, but
    /// correct cascade"), but real substitution for them is proven end to end against
    /// <see cref="BundledFonts.GsubTestLookup3"/> - the only publicly available font found with real
    /// petite-caps data, which happens to implement it via GSUB <b>Alternate Substitution</b> (Lookup
    /// Type 3) rather than the Single Substitution (Lookup Type 1) Source Sans 3 uses, so this also
    /// exercises that lookup type end to end. Unicase has no known real-font candidate at all - its
    /// "real substitution applied" branch stays covered only at the low-level
    /// <see cref="GsubSingleSubstitutionSyntheticTests"/> layer.
    /// </summary>
    public class FontVariantCapsIntegrationTests
    {
        [Theory]
        [InlineData("small-caps", "SmallCaps")]
        [InlineData("all-small-caps", "AllSmallCaps")]
        [InlineData("titling-caps", "TitlingCaps")]
        public async Task RealGsubSupportedKeyword_ResolvesToRealFeature_AndWordStaysWhole(string keyword, string expectedFeatureName)
        {
            var container = await LayoutHtml($"<b id=\"w\" style=\"font-variant-caps:{keyword}\">Hello</b>", BundledFonts.Ttf, "truetype", "SS3Caps");
            var box = FindWordsBox(container.Root!, "w");

            Assert.Equal(expectedFeatureName, box.ActualFontVariantCaps.ToString());
            // No synthesis: the word is kept whole, at its original case and full size - real GSUB
            // substitution (not exercised at this layer) does the visual work transparently.
            Assert.Single(box.Words);
            Assert.Equal("Hello", box.Words[0].Text);
            Assert.Equal(1.0, box.Words[0].FontSizeScale);
        }

        [Theory]
        [InlineData("petite-caps")]
        [InlineData("all-petite-caps")]
        [InlineData("unicase")]
        public async Task UnsupportedByEitherBundledFont_RendersAsNormal_ButCascadesTheRealKeyword(string keyword)
        {
            // Neither bundled font has pcap/c2pc/unic - these three keywords never synthesize (unlike
            // small-caps/all-small-caps), so an unsupported font means a silent no-op at render time.
            var container = await LayoutHtml($"<b id=\"w\" style=\"font-variant-caps:{keyword}\">Hello</b>", BundledFonts.Ttf, "truetype", "SS3Caps");
            var box = FindWordsBox(container.Root!, "w");

            Assert.Equal(keyword, box.FontVariantCaps);
            Assert.Equal(FontVariantCapsFeature.None, box.ActualFontVariantCaps);
            Assert.Single(box.Words);
            Assert.Equal("Hello", box.Words[0].Text);
            Assert.Equal(1.0, box.Words[0].FontSizeScale);
        }

        [Fact]
        public async Task StandaloneFontFeatureSettings_ActivatesGsub_IndependentlyOfFontVariantCaps()
        {
            var container = await LayoutHtml(
                "<b id=\"w\" style='font-feature-settings:\"smcp\" 1'>Hello</b>",
                BundledFonts.Ttf, "truetype", "SS3Caps");
            var box = FindWordsBox(container.Root!, "w");

            Assert.Equal("normal", box.FontVariantCaps);
            Assert.Contains(("smcp", 1), box.ActualFontFeatureSettings);
            // font-feature-settings has no synthesis fallback at all - it's a real-GSUB-or-nothing
            // channel, so this composes with the (font-variant-caps-driven) small-caps synthesis gate
            // not at all: the word stays whole regardless of whether "smcp" actually fires.
            Assert.Single(box.Words);
        }

        // ─── Real Alternate Substitution (Lookup Type 3): gsubtest-lookup3.otf ─────
        //
        // Every feature in this font maps a "default" control codepoint plus one "altN" codepoint per
        // alternate-glyph index (see BundledFonts.GsubTestLookup3) - each altN codepoint's glyph
        // substitutes to the literal word "PASS" only when the feature actually selects alternate
        // index N-1, and to "FAIL" at every other index (confirmed via direct fontTools introspection
        // of the bundled font). Comparing against these fixed, well-known glyph indices is unambiguous
        // proof of which alternate a request actually selected - the same "assert glyph indices, not
        // measured width" discipline FontVariantNumericIntegrationTests already uses. Each feature has
        // its own distinct PASS/FAIL glyph pair, so a substitution can't be mistaken for a different
        // feature's.
        private const int PcapPassGlyph = 1054;
        private const int C2pcPassGlyph = 82;
        private const int SaltPassGlyph = 1156;
        private const int SaltFailGlyph = 1157;
        private const string PcapAlt1 = ""; // pcap.alt1: PASS only when pcap selects index 0
        private const string C2pcAlt1 = ""; // c2pc.alt1: PASS only when c2pc selects index 0
        private const string SaltAlt1 = ""; // salt.alt1: PASS only when salt selects index 0
        private const string SaltAlt2 = ""; // salt.alt2: PASS only when salt selects index 1

        [Fact]
        public async Task PetiteCaps_OnAFontWithRealAlternateSubstitutionData_SelectsTheFirstAlternate()
        {
            // A plain font-variant-caps activation (no explicit integer) is a boolean "on" request,
            // which CSS Fonts Level 3 maps to the first glyph alternate (index 0) for a feature
            // implemented via Alternate Substitution - so pcap.alt1 (designed to PASS exactly there)
            // must come back as the PASS glyph.
            var box = await FindWordsBoxWithGsubFont($"<b id=\"w\" style=\"font-variant-caps:petite-caps\">{PcapAlt1}</b>");

            Assert.Equal(FontVariantCapsFeature.PetiteCaps, box.ActualFontVariantCaps);
            Assert.Equal(PcapPassGlyph, ShapeSingleGlyph(box, PcapAlt1));
        }

        [Fact]
        public async Task AllPetiteCaps_RequiresBothPcapAndC2pc_BothApplyIndependently()
        {
            // all-petite-caps activates both pcap and c2pc together (see GsubShaper.GetFeatureTags) -
            // one word carrying one alt1 codepoint from each feature (each with its own, independent
            // PASS glyph) proves both tags are actually active in the same combined shaping pass, not
            // just one of them.
            var text = PcapAlt1 + C2pcAlt1;
            var box = await FindWordsBoxWithGsubFont($"<b id=\"w\" style=\"font-variant-caps:all-petite-caps\">{text}</b>");

            Assert.Equal(FontVariantCapsFeature.AllPetiteCaps, box.ActualFontVariantCaps);

            var descriptor = ((PeachPDF.Adapters.FontAdapter)box.ActualFont).Font.Descriptor;
            var shaped = descriptor.Shape(text, box.ActualTextShapingFeatures);
            Assert.Equal(PcapPassGlyph, shaped[0].GlyphIndex);
            Assert.Equal(C2pcPassGlyph, shaped[1].GlyphIndex);
        }

        [Fact]
        public async Task FontFeatureSettingsExplicitValue_SelectsTheMatchingAlternateIndex_NotJustAnyAlternate()
        {
            // "salt" (not a tag any font-variant-* longhand reserves) "2" must select alternate index 1
            // (value - 1) - salt.alt2's own PASS glyph sits at exactly that index, proving the explicit
            // CSS integer actually reaches the right alternate, not merely that the feature turns on.
            var box = await FindWordsBoxWithGsubFont(
                $"<b id=\"w\" style='font-feature-settings:\"salt\" 2'>{SaltAlt2}</b>");

            Assert.Equal(SaltPassGlyph, ShapeSingleGlyph(box, SaltAlt2));
        }

        [Fact]
        public async Task FontFeatureSettingsExplicitValue_WrongIndexStillFails_ProvingSelectionIsExact()
        {
            // Same codepoint as above (salt.alt2), but requesting index 0 (value 1) instead of the
            // index it actually needs (1) - must come back FAIL, not PASS, or the "index selection" in
            // the prior test would be unfalsifiable (any nonzero index could look like it "worked").
            var box = await FindWordsBoxWithGsubFont(
                $"<b id=\"w\" style='font-feature-settings:\"salt\" 1'>{SaltAlt2}</b>");

            Assert.Equal(SaltFailGlyph, ShapeSingleGlyph(box, SaltAlt2));
        }

        [Fact]
        public async Task FontFeatureSettingsValueZero_NeverSubstitutes()
        {
            var box = await FindWordsBoxWithGsubFont(
                $"<b id=\"w\" style='font-feature-settings:\"salt\" 0'>{SaltAlt1}</b>");

            var descriptor = ((PeachPDF.Adapters.FontAdapter)box.ActualFont).Font.Descriptor;
            var unshaped = descriptor.CharCodeToGlyphIndex(new System.Text.Rune(0xE301));
            Assert.Equal(unshaped, ShapeSingleGlyph(box, SaltAlt1));
            Assert.NotEqual(SaltPassGlyph, unshaped);
            Assert.NotEqual(SaltFailGlyph, unshaped);
        }

        [Fact]
        public async Task FontFeatureSettings_ReservedCapsTag_IsIgnored_FontVariantCapsAlwaysWinsForThatTag()
        {
            // "pcap" is reserved to font-variant-caps (see GsubShaper.ReservedTags) per CSS Fonts'
            // precedence rule - an explicit font-feature-settings value for it must never activate
            // anything on its own, even a well-formed, in-range alternate index.
            var box = await FindWordsBoxWithGsubFont(
                $"<b id=\"w\" style='font-feature-settings:\"pcap\" 1'>{PcapAlt1}</b>");

            var descriptor = ((PeachPDF.Adapters.FontAdapter)box.ActualFont).Font.Descriptor;
            var unshaped = descriptor.CharCodeToGlyphIndex(new System.Text.Rune(0xE2BD));
            Assert.Equal(unshaped, ShapeSingleGlyph(box, PcapAlt1));
        }

        private static int ShapeSingleGlyph(CssBox box, string text)
        {
            var descriptor = ((PeachPDF.Adapters.FontAdapter)box.ActualFont).Font.Descriptor;
            return descriptor.Shape(text, box.ActualTextShapingFeatures)[0].GlyphIndex;
        }

        private static Task<CssBox> FindWordsBoxWithGsubFont(string body) =>
            FindWordsBoxWithFont(body, BundledFonts.GsubTestLookup3, "opentype", "GSubTest");

        private static async Task<CssBox> FindWordsBoxWithFont(string body, string fontPath, string format, string family)
        {
            var container = await LayoutHtml(body, fontPath, format, family);
            return FindWordsBox(container.Root!, "w");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body, string fontBase64, string format, string family) =>
            $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: '{family}'; src: url('data:font/{format};base64,{fontBase64}') format('{format}'); }}
body {{ font-family: '{family}'; width: 400px; }}
</style></head><body>{body}</body></html>";

        private static async Task<HtmlContainerInt> LayoutHtml(string body, string fontPath, string format, string family)
        {
            var fontBase64 = Convert.ToBase64String(File.ReadAllBytes(fontPath));
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(Wrap(body, fontBase64, format, family), null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }

        private static CssBox FindWordsBox(CssBox root, string id)
        {
            var element = FindById(root, id)!;
            if (element.Words.Count > 0) return element;

            var wordsChild = element.Boxes.FirstOrDefault(b => b.Words.Count > 0);
            Assert.NotNull(wordsChild);
            return wordsChild!;
        }
    }
}

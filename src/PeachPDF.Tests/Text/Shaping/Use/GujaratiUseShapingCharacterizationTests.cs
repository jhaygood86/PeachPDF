using System.IO;
using System.Linq;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using PeachPDF.Text.Shaping.Use;
using Xunit;

namespace PeachPDF.Tests.Text.Shaping.Use
{
    /// <summary>
    /// Real-font characterization for Gujarati's Universal Shaping Engine syllable reordering (issue
    /// #533, Phase 5c) - drives PeachPDF's actual OpenType reader/shaper
    /// (<see cref="OpenTypeDescriptor.Shape"/>) against a real font (a "Noto Sans Gujarati" subset -
    /// see <see cref="BundledFonts.Gujarati"/>). Gujarati needs no new
    /// <see cref="UseCategory"/>/classifier/scanner code beyond what Devanagari already exercises
    /// (verified by enumerating every codepoint in the Gujarati block against the real UCD data - see
    /// this feature's own recent-fixes entry), so these tests are what actually prove the existing
    /// pipeline shapes a second script's real font correctly, not just that it happens to compile.
    /// Every expected glyph ID/order below was independently cross-checked against real HarfBuzz's own
    /// output for this exact font file (via <c>uharfbuzz</c>) during development - not merely
    /// reverse-engineered from this implementation.
    /// </summary>
    public class GujaratiUseShapingCharacterizationTests
    {
        private const int Ka = 0x0A95;
        private const int Ssa = 0x0AB7;
        private const int Ra = 0x0AB0;
        private const int Virama = 0x0ACD;
        private const int VowelSignI = 0x0ABF;
        private const int VowelSignAa = 0x0ABE;
        private const int Nukta = 0x0ABC;
        private const int Anusvara = 0x0A82;
        private const int Visarga = 0x0A83;

        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Gujarati)).Fontface;
            return new OpenTypeDescriptor("gujarati-test", "gujarati-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        private static int[] ShapeGlyphIds(OpenTypeDescriptor descriptor, params int[] codepoints)
        {
            var text = string.Concat(codepoints.Select(cp => new System.Text.Rune(cp).ToString()));
            var categories = codepoints.Select(cp => UseCategoryClassifier.Classify(cp)).ToList();
            return descriptor.Shape(text, new TextShapingFeatures(ScriptTag: "gujr", UseCategories: categories))
                .Select(g => g.GlyphIndex).ToArray();
        }

        [Fact]
        public void SimplePreBaseMatra_MovesBeforeTheBase()
        {
            // KA + VOWEL SIGN I - real HarfBuzz output for this exact font: [ivowelsign1gujr (glyph
            // 73), kagujr (glyph 5)].
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignI);

            Assert.Equal([73, 5], glyphs);
        }

        [Fact]
        public void ConjunctWithMatra_LigatesTheWholeConjunctAndMovesTheMatraBefore()
        {
            // KA + VIRAMA + SSA + VOWEL SIGN I - the whole conjunct ligates into one glyph (cjct), and
            // the matra moves before it - real HarfBuzz output: [ivowelsign4gujr (glyph 76), kassagujr
            // (glyph 21, the fused ligature) - a different contextual matra variant (4 vs. 1) than the
            // bare pre-base-matra case above, since this font's own `pres` feature picks the matra's
            // glyph form contextually against what follows it, not something this port's own reorder
            // logic controls.
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Virama, Ssa, VowelSignI);

            Assert.Equal([76, 21], glyphs);
        }

        [Fact]
        public void RephWithFollowingMatra_FusesIntoOneCombinedPresentationGlyph()
        {
            // RA + VIRAMA + KA + VOWEL SIGN I - RA+VIRAMA forms a repha (rphf); pass 1 moves it
            // forward past KA to just before the (post-base-flagged) matra glyph; pass 2 then moves
            // that matra glyph back to the syllable start, landing [matra, base, repha] exactly like
            // Devanagari's own headline case - and, exactly like the Devanagari bundled font, this
            // font's own `pres`/`abvs` features then fuse the repha and matra into one combined
            // presentation glyph (skip-aware ligature matching across the intervening base, reusing
            // the same pre-existing mechanism with zero new code - see this feature's own
            // recent-fixes entry) while leaving a trailing GDEF mark placeholder behind: real HarfBuzz
            // output for this exact font: [ivowelsignreph1gujr (glyph 85, the fused matra+repha
            // glyph), kagujr (glyph 5), dummymarkgujr (glyph 97, the ligature's own trailing
            // artifact)].
            var glyphs = ShapeGlyphIds(Descriptor(), Ra, Virama, Ka, VowelSignI);

            Assert.Equal([85, 5, 97], glyphs);
        }

        [Fact]
        public void PostBaseMatra_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignAa);

            Assert.Equal([5, 12], glyphs);
        }

        [Fact]
        public void Nukta_LigatesWithTheBase()
        {
            // The font's own `nukt` feature fuses the nukta into the base consonant's own glyph,
            // exactly like the Devanagari bundled font's QA (KA+NUKTA) ligature.
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Nukta);

            Assert.Equal([15], glyphs);
        }

        [Fact]
        public void Anusvara_IsNotReordered()
        {
            // The font's own contextual substitution picks a "left"-positioned anusvara variant after
            // this particular consonant - a presentation detail the font's own lookups decide, not
            // something this port's reorder logic controls; the glyph still stays in logical order
            // (no reordering at all) either way.
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Anusvara);

            Assert.Equal([5, 98], glyphs);
        }

        [Fact]
        public void Visarga_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Visarga);

            Assert.Equal([5, 3], glyphs);
        }
    }
}

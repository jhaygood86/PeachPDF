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
    /// Real-font characterization for Devanagari's Universal Shaping Engine syllable reordering
    /// (issue #533, Phase 5b) - drives PeachPDF's actual OpenType reader/shaper
    /// (<see cref="OpenTypeDescriptor.Shape"/>) against a real font (a "Noto Sans Devanagari" subset -
    /// see <see cref="BundledFonts.Devanagari"/>), so a genuinely broken wiring anywhere in the
    /// classify/scan/rphf/basic-features/reorder/general-pass pipeline would show up as real glyph IDs
    /// never matching, not just that synthetic byte-blob GSUB tables dispatch correctly (the same
    /// "prove it isn't a no-op" standard <c>ArabicJoiningCharacterizationTests</c> already applies).
    /// Every expected glyph ID/order below was independently cross-checked against real HarfBuzz's own
    /// output for this exact font file (via <c>uharfbuzz</c>) during development - not merely
    /// reverse-engineered from this implementation - see this feature's own recent-fixes entry.
    /// </summary>
    public class DevanagariUseShapingCharacterizationTests
    {
        private const int Ka = 0x0915;
        private const int Ta = 0x0924;
        private const int Ra = 0x0930;
        private const int Ssa = 0x0937;
        private const int Virama = 0x094D;
        private const int VowelSignI = 0x093F;
        private const int VowelSignAa = 0x093E;
        private const int Anusvara = 0x0902;
        private const int Visarga = 0x0903;

        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Devanagari)).Fontface;
            return new OpenTypeDescriptor("devanagari-test", "devanagari-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        private static int[] ShapeGlyphIds(OpenTypeDescriptor descriptor, params int[] codepoints)
        {
            var text = string.Concat(codepoints.Select(cp => new System.Text.Rune(cp).ToString()));
            var categories = codepoints.Select(cp => UseCategoryClassifier.Classify(cp)).ToList();
            return descriptor.Shape(text, new TextShapingFeatures(ScriptTag: "deva", UseCategories: categories))
                .Select(g => g.GlyphIndex).ToArray();
        }

        [Fact]
        public void SimplePreBaseMatra_MovesBeforeTheBase()
        {
            // KA + VOWEL SIGN I - real HarfBuzz output for this exact font: [uni093F.04 (glyph 63),
            // uni0915 (glyph 6)] - the matra's own contextual glyph variant, then the base.
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignI);

            Assert.Equal([63, 6], glyphs);
        }

        [Fact]
        public void ConjunctWithMatra_LigatesTheWholeConjunctAndMovesTheMatraBefore()
        {
            // KA + VIRAMA + SSA + VOWEL SIGN I (क्षि) - the whole 3-letter conjunct ligates into one
            // glyph (cjct), and the matra moves before it - real HarfBuzz output: [uni093F.10 (glyph
            // 67), uni0915094D0937 (glyph 12, the fused क्ष ligature)].
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Virama, Ssa, VowelSignI);

            Assert.Equal([67, 12], glyphs);
        }

        [Fact]
        public void BareConjunct_LigatesWithoutAnyMatra()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Virama, Ssa);

            Assert.Equal([12], glyphs);
        }

        [Fact]
        public void RephWithFollowingMatra_FormsAndCombinesWithTheMatra()
        {
            // RA + VIRAMA + KA + VOWEL SIGN I (र्कि) - RA+VIRAMA forms a repha (rphf), which the
            // reorder pass moves forward past KA to sit adjacent to the (already pre-base-moved)
            // matra; the font's own `pres`/`abvs` feature then fuses the repha and matra into one
            // combined presentation glyph, skipping over KA (a GDEF base glyph) while matching -
            // real HarfBuzz output: [uni093F0930094D.04 (glyph 87, the fused matra+repha glyph),
            // uni0915 (glyph 6, KA - left in place by the ligature's own skip-and-reinsert
            // convention), NullMark (glyph 113 - the font's own trailing artifact of this same
            // ligature rule, reproduced identically by real HarfBuzz for this exact sequence).
            var glyphs = ShapeGlyphIds(Descriptor(), Ra, Virama, Ka, VowelSignI);

            Assert.Equal([87, 6, 113], glyphs);
        }

        [Fact]
        public void PostBaseMatra_IsNotReordered()
        {
            // KA + VOWEL SIGN AA (का) - a post-base (right-side) matra stays in logical order.
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignAa);

            Assert.Equal([6, 3], glyphs);
        }

        [Fact]
        public void Anusvara_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Anusvara);

            Assert.Equal([6, 13], glyphs);
        }

        [Fact]
        public void Visarga_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Visarga);

            Assert.Equal([6, 14], glyphs);
        }
    }
}

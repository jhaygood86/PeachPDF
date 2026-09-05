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
    /// Real-font characterization for Bengali's Universal Shaping Engine syllable reordering (issue
    /// #533, Phase 5c) - drives PeachPDF's actual OpenType reader/shaper
    /// (<see cref="OpenTypeDescriptor.Shape"/>) against a real font (a "Noto Sans Bengali" subset -
    /// see <see cref="BundledFonts.Bengali"/>), the same "prove it isn't a no-op" standard
    /// <see cref="DevanagariUseShapingCharacterizationTests"/> already applies - including Bengali's
    /// own two USE categories Devanagari never reaches (<see cref="UseCategory.GB"/>,
    /// <see cref="UseCategory.FMAbv"/>). Every expected glyph ID/order below was independently
    /// cross-checked against real HarfBuzz's own output for this exact font file (via
    /// <c>uharfbuzz</c>) during development - not merely reverse-engineered from this implementation -
    /// see this feature's own recent-fixes entry.
    /// </summary>
    public class BengaliUseShapingCharacterizationTests
    {
        private const int Ka = 0x0995;
        private const int Ssa = 0x09B7;
        private const int Ra = 0x09B0;
        private const int Virama = 0x09CD;
        private const int VowelSignI = 0x09BF;
        private const int VowelSignAa = 0x09BE;
        private const int Candrabindu = 0x0981;
        private const int Visarga = 0x0983;
        private const int Anji = 0x0980; // GB - Consonant Placeholder
        private const int SandhiMark = 0x09FE; // FMAbv - Syllable Modifier

        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Bengali)).Fontface;
            return new OpenTypeDescriptor("bengali-test", "bengali-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        private static int[] ShapeGlyphIds(OpenTypeDescriptor descriptor, params int[] codepoints)
        {
            var text = string.Concat(codepoints.Select(cp => new System.Text.Rune(cp).ToString()));
            var categories = codepoints.Select(cp => UseCategoryClassifier.Classify(cp)).ToList();
            return descriptor.Shape(text, new TextShapingFeatures(ScriptTag: "beng", UseCategories: categories))
                .Select(g => g.GlyphIndex).ToArray();
        }

        [Fact]
        public void SimplePreBaseMatra_MovesBeforeTheBase()
        {
            // KA + VOWEL SIGN I - real HarfBuzz output for this exact font: [ivowelsignbeng (glyph
            // 15), kabeng (glyph 6)].
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignI);

            Assert.Equal([15, 6], glyphs);
        }

        [Fact]
        public void ConjunctWithMatra_LigatesTheWholeConjunctAndMovesTheMatraBefore()
        {
            // KA + VIRAMA + SSA + VOWEL SIGN I - the whole conjunct ligates into one glyph (cjct), and
            // the matra moves before it - real HarfBuzz output: [ivowelsign1beng (glyph 55),
            // kassabeng (glyph 23, the fused ligature)].
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Virama, Ssa, VowelSignI);

            Assert.Equal([55, 23], glyphs);
        }

        [Fact]
        public void BareConjunct_LigatesWithoutAnyMatra()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Virama, Ssa);

            Assert.Equal([23], glyphs);
        }

        [Fact]
        public void RephWithFollowingMatra_MovesForwardThenTheMatraMovesBeforeBoth()
        {
            // RA + VIRAMA + KA + VOWEL SIGN I - RA+VIRAMA forms a repha (rphf); pass 1 moves it
            // forward past KA to just before the (post-base-flagged) matra glyph; pass 2 then moves
            // that same matra glyph all the way back to the syllable start - unlike the Devanagari
            // bundled font, this font's own pres/abvs features don't fuse the repha and matra into one
            // combined glyph, so the final order is the "plain" matra/base/repha triple real HarfBuzz
            // also produces: [ivowelsignbeng (glyph 15), kabeng (glyph 6), rephbeng (glyph 24)].
            var glyphs = ShapeGlyphIds(Descriptor(), Ra, Virama, Ka, VowelSignI);

            Assert.Equal([15, 6, 24], glyphs);
        }

        [Fact]
        public void PostBaseMatra_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignAa);

            Assert.Equal([6, 14], glyphs);
        }

        [Fact]
        public void Candrabindu_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Candrabindu);

            Assert.Equal([6, 3], glyphs);
        }

        [Fact]
        public void Visarga_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Visarga);

            Assert.Equal([6, 4], glyphs);
        }

        [Fact]
        public void ConsonantPlaceholder_ShapesAsItsOwnGlyph()
        {
            // U+0980 BENGALI ANJI (GB - Consonant Placeholder), the category Devanagari's own
            // classifier scope never reaches - forms its own single-glyph standard_cluster, exactly
            // like a plain base consonant.
            var glyphs = ShapeGlyphIds(Descriptor(), Anji);

            Assert.Equal([2], glyphs);
        }

        [Fact]
        public void ConsonantPlaceholderFollowedByPostBaseMatra_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Anji, VowelSignAa);

            Assert.Equal([2, 14], glyphs);
        }

        [Fact]
        public void SandhiMark_IsConsumedByTheSameSyllableAndNotReordered()
        {
            // U+09FE BENGALI SANDHI MARK (FMAbv - Syllable Modifier), the other category Devanagari's
            // own classifier scope never reaches - consumed as the base's own final_modifiers, staying
            // in logical order (only its GPOS mark anchoring, not glyph reordering, places it visually).
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, SandhiMark);

            Assert.Equal([6, 17], glyphs);
        }
    }
}

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
    /// Real-font characterization for Tamil's Universal Shaping Engine syllable reordering (issue
    /// #533, Phase 5c) - drives PeachPDF's actual OpenType reader/shaper
    /// (<see cref="OpenTypeDescriptor.Shape"/>) against a real font (a "Noto Sans Tamil" subset - see
    /// <see cref="BundledFonts.Tamil"/>). Tamil needs no new
    /// <see cref="UseCategory"/>/classifier/scanner code beyond what Devanagari already exercises
    /// (verified by enumerating every codepoint in the Tamil block against the real UCD data - see
    /// this feature's own recent-fixes entry), so these tests are what actually prove the existing
    /// pipeline shapes a third script's real font correctly, not just that it happens to compile.
    /// Every expected glyph ID/order below was independently cross-checked against real HarfBuzz's own
    /// output for this exact font file (via <c>uharfbuzz</c>) during development - not merely
    /// reverse-engineered from this implementation.
    /// </summary>
    public class TamilUseShapingCharacterizationTests
    {
        private const int Ka = 0x0B95;
        private const int Ssa = 0x0BB7;
        private const int Virama = 0x0BCD; // pulli
        private const int VowelSignE = 0x0BC6; // pre-base matra
        private const int VowelSignAa = 0x0BBE; // post-base matra
        private const int Anusvara = 0x0B82;
        private const int Aaytham = 0x0B83; // Modifying_Letter - USE category O

        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Tamil)).Fontface;
            return new OpenTypeDescriptor("tamil-test", "tamil-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        private static int[] ShapeGlyphIds(OpenTypeDescriptor descriptor, params int[] codepoints)
        {
            var text = string.Concat(codepoints.Select(cp => new System.Text.Rune(cp).ToString()));
            var categories = codepoints.Select(cp => UseCategoryClassifier.Classify(cp)).ToList();
            return descriptor.Shape(text, new TextShapingFeatures(ScriptTag: "taml", UseCategories: categories))
                .Select(g => g.GlyphIndex).ToArray();
        }

        [Fact]
        public void SimplePreBaseMatra_MovesBeforeTheBase()
        {
            // KA + VOWEL SIGN E - real HarfBuzz output for this exact font: [evowelsigntamil (glyph
            // 11), katamil (glyph 5)].
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignE);

            Assert.Equal([11, 5], glyphs);
        }

        [Fact]
        public void ConjunctWithMatra_LigatesTheGranthaConjunctAndMovesTheMatraBefore()
        {
            // KA + VIRAMA + SSA + VOWEL SIGN E - SSA is a Grantha-origin letter Tamil borrows for
            // Sanskrit loanwords; this font's own `cjct`/`half` features still ligate KA+VIRAMA+SSA
            // into one glyph exactly like a native Devanagari/Bengali/Gujarati conjunct, and the
            // pre-base matra still moves before it - real HarfBuzz output: [evowelsigntamil (glyph
            // 11), tchatamil (glyph 13, the fused ligature)].
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Virama, Ssa, VowelSignE);

            Assert.Equal([11, 13], glyphs);
        }

        [Fact]
        public void BareConjunct_LigatesWithoutAnyMatra()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Virama, Ssa);

            Assert.Equal([13], glyphs);
        }

        [Fact]
        public void PostBaseMatra_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignAa);

            Assert.Equal([5, 10], glyphs);
        }

        [Fact]
        public void Anusvara_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Anusvara);

            Assert.Equal([5, 2], glyphs);
        }

        [Fact]
        public void Aaytham_IsItsOwnSingleGlyphSyllable()
        {
            // U+0B83 TAMIL SIGN AAYTHAM - Indic_Syllabic_Category=Modifying_Letter (unlike
            // Devanagari's/Bengali's/Gujarati's own combining-mark Visarga), classifying as USE
            // category O per UseCategoryClassifierTests.TamilVisarga_FallsBackToOther - forms its own
            // inert single-glyph syllable, never reordered or attached to anything.
            var glyphs = ShapeGlyphIds(Descriptor(), Aaytham);

            Assert.Equal([3], glyphs);
        }

        [Fact]
        public void WordFinalVirama_LigatesToAHalfForm()
        {
            // KA + VIRAMA (pulli) with nothing following - the font's own `half` feature still forms
            // a half-consonant presentation glyph for a bare word-final halant, exactly like
            // Devanagari's own dependent_vowels "| H" grammar alternative.
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Virama);

            Assert.Equal([14], glyphs);
        }
    }
}

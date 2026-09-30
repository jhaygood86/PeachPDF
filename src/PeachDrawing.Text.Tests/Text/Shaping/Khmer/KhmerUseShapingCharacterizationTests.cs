using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Internal.Fonts;
using System.IO;
using System.Linq;
using PeachDrawing.Text.Internal.Fonts.OpenType;
using PeachPDF.Tests.TestSupport;
using PeachDrawing.Text.Internal.Text;
using PeachDrawing.Text.Internal.Text.Shaping.Khmer;
using Xunit;

namespace PeachDrawing.Text.Tests.Text.Shaping.Khmer
{
    /// <summary>
    /// Real-font characterization for Khmer's coeng/subjoined-consonant shaping (issue #1493) - drives
    /// PeachPDF's actual OpenType reader/shaper (<see cref="OpenTypeDescriptor.Shape"/>) against a real
    /// font (a "Noto Sans Khmer" subset - see <see cref="BundledFonts.Khmer"/>), so a genuinely broken
    /// wiring anywhere in the classify/scan/reorder/locl-ccmp/basic-features/general-pass pipeline would
    /// show up as real glyph IDs never matching, not just that synthetic byte-blob GSUB tables dispatch
    /// correctly (the same "prove it isn't a no-op" standard <c>DevanagariUseShapingCharacterizationTests</c>
    /// already applies). Every expected glyph ID/order below was independently cross-checked against real
    /// HarfBuzz's own output for this exact font file (via <c>uharfbuzz</c>) during development - not
    /// merely reverse-engineered from this implementation - see this feature's own recent-fixes entry.
    /// <para>
    /// Named to match this repo's established per-script pair
    /// (<c>&lt;Script&gt;UseShapingCharacterizationTests</c>/<c>&lt;Script&gt;UseCharacterizationTests</c>)
    /// even though Khmer is shaped by HarfBuzz's own separate, older "Indic" shaper rather than the
    /// Universal Shaping Engine those other pairs exercise - see <see cref="KhmerCategory"/>'s own
    /// remarks on why.
    /// </para>
    /// </summary>
    public class KhmerUseShapingCharacterizationTests
    {
        private const int Ka = 0x1780;
        private const int Ro = 0x179A;
        private const int Sa = 0x179F;
        private const int No = 0x1793;
        private const int Coeng = 0x17D2;
        private const int VowelSignEPre = 0x17C1;
        private const int VowelSignIAbove = 0x17B7;
        private const int VowelSignUBelow = 0x17BB;
        private const int VowelSignAaPost = 0x17B6;
        private const int Robat = 0x17CC;

        private static OpenTypeDescriptor Descriptor()
        {
            var face = FontFileData.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Khmer)).Fontface;
            return new OpenTypeDescriptor("khmer-test", "khmer-test", face);
        }

        private static int[] ShapeGlyphIds(OpenTypeDescriptor descriptor, params int[] codepoints)
        {
            var text = string.Concat(codepoints.Select(cp => new System.Text.Rune(cp).ToString()));
            var categories = codepoints.Select(cp => KhmerCategoryClassifier.Classify(cp)).ToList();
            return descriptor.Shape(text, new ShapeSettings(ScriptTag: "khmr", KhmerCategories: categories))
                .Select(g => g.GlyphIndex).ToArray();
        }

        [Fact]
        public void BareConsonant_IsUnchanged()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka);

            Assert.Equal([22], glyphs);
        }

        [Fact]
        public void CoengRo_ReordersBeforeTheBaseAndFormsAPrefLigature()
        {
            // KA + COENG + RO (ក្រ) - real HarfBuzz output for this exact font: [67 (the `pref`
            // presentation ligature of the coeng+RO pair), 22 (KA)] - the pair moves before the base
            // and forms one presentation glyph.
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Coeng, Ro);

            Assert.Equal([67, 22], glyphs);
        }

        [Fact]
        public void CoengNonRoSubjoinedConsonant_StaysInPlaceAndFormsABlwfLigature()
        {
            // KA + COENG + SA (ក្ស) - the pair stays in logical order and forms one `blwf`
            // (below-base) presentation glyph, unlike CoengRo above.
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Coeng, Sa);

            Assert.Equal([22, 69], glyphs);
        }

        [Fact]
        public void PreBaseVowelSign_MovesBeforeTheBase()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignEPre);

            Assert.Equal([41, 22], glyphs);
        }

        [Fact]
        public void AboveBaseVowelSign_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignIAbove);

            Assert.Equal([22, 35], glyphs);
        }

        [Fact]
        public void BelowBaseVowelSign_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignUBelow);

            Assert.Equal([22, 39], glyphs);
        }

        [Fact]
        public void PostBaseVowelSign_FormsAPresLigatureWithoutReordering()
        {
            // KA + VOWEL SIGN AA - fuses into one `pres` presentation ligature (no reorder needed - a
            // post-base vowel is already in its final logical/visual position).
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, VowelSignAaPost);

            Assert.Equal([73], glyphs);
        }

        [Fact]
        public void CoengRoAndPreBaseVowel_BothReorderBeforeTheBaseWithTheVowelFirst()
        {
            // KA + COENG + RO + VOWEL SIGN E - real HarfBuzz output: [41 (the vowel), 67 (the
            // `pref`-ligated coeng+Ro pair), 22 (KA)]. This is the single left-to-right reorder loop's
            // own signature result (see KhmerReorderer's own remarks) - two independent passes ordered
            // the other way (repha-style "move forward" before "move back") would put the vowel
            // *after* the already-front-moved pair instead.
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Coeng, Ro, VowelSignEPre);

            Assert.Equal([41, 67, 22], glyphs);
        }

        [Fact]
        public void Robat_IsNotReordered()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Ka, Robat);

            Assert.Equal([22, 48], glyphs);
        }

        [Fact]
        public void TwoBareConsonants_ShapeAsTwoIndependentSyllables()
        {
            var glyphs = ShapeGlyphIds(Descriptor(), Sa, Ka);

            Assert.Equal([31, 22], glyphs);
        }
    }
}

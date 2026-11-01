using PeachDrawing.Text.Unicode;
using PeachDrawing.Text.Shaping;
using System.Collections.Generic;
using System.Linq;
using PeachDrawing.Text.Internal.Text.Shaping.Khmer;
using Xunit;

namespace PeachDrawing.Text.Tests.Text.Shaping.Khmer
{
    /// <summary>
    /// Coverage for <see cref="KhmerReorderer"/>, the ported <c>reorder_consonant_syllable</c> single
    /// left-to-right glyph-array reorder. Glyph indices below are arbitrary but distinct, so asserting
    /// the resulting <see cref="PlacedGlyph.GlyphIndex"/> order directly proves which glyph physically
    /// moved where - not just that reordering "did something". Every case here is also cross-checked
    /// against real HarfBuzz's own output for a real font in
    /// <c>KhmerUseShapingCharacterizationTests</c>.
    /// </summary>
    public class KhmerReordererTests
    {
        private static PlacedGlyph G(int glyphIndex) => new(glyphIndex, ClusterStart: glyphIndex, ClusterLength: 1);

        [Fact]
        public void CoengRo_MovesBeforeTheBase()
        {
            // KA + COENG + RO (ក្រ) - C H Ra.
            var glyphs = new List<PlacedGlyph> { G(100), G(101), G(102) };
            var categories = new[] { KhmerCategory.C, KhmerCategory.H, KhmerCategory.Ra };

            KhmerReorderer.ReorderSyllable(glyphs, categories, 0, 3);

            Assert.Equal([101, 102, 100], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void CoengNonRoSubjoinedConsonant_IsNotMoved()
        {
            // KA + COENG + SA (ក្ស) - C H C - only a coeng+Ra pair is reordered; any other subjoined
            // consonant stays in place (it only gets a below-base glyph form via `blwf`).
            var glyphs = new List<PlacedGlyph> { G(100), G(101), G(102) };
            var categories = new[] { KhmerCategory.C, KhmerCategory.H, KhmerCategory.C };

            KhmerReorderer.ReorderSyllable(glyphs, categories, 0, 3);

            Assert.Equal([100, 101, 102], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void PreBaseVowel_MovesBeforeTheBase()
        {
            var glyphs = new List<PlacedGlyph> { G(100), G(101) };
            var categories = new[] { KhmerCategory.C, KhmerCategory.VPre };

            KhmerReorderer.ReorderSyllable(glyphs, categories, 0, 2);

            Assert.Equal([101, 100], glyphs.Select(g => g.GlyphIndex));
        }

        [Theory]
        [InlineData(KhmerCategory.VAbv)]
        [InlineData(KhmerCategory.VBlw)]
        [InlineData(KhmerCategory.VPst)]
        public void NonPreBaseVowel_IsNotMoved(KhmerCategory vowel)
        {
            var glyphs = new List<PlacedGlyph> { G(100), G(101) };
            var categories = new[] { KhmerCategory.C, vowel };

            KhmerReorderer.ReorderSyllable(glyphs, categories, 0, 2);

            Assert.Equal([100, 101], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void CoengRoAndPreBaseVowel_BothMoveWithTheVowelEndingUpFirst()
        {
            // KA + COENG + RO + VOWEL SIGN E - C H Ra VPre. Verified byte-for-byte against real
            // HarfBuzz's own output for a real font in
            // KhmerUseShapingCharacterizationTests.CoengRoAndPreBaseVowel_BothReorderBeforeTheBase -
            // this is a single left-to-right loop (not two independent passes, see KhmerReorderer's
            // own remarks), so the vowel (reached second by the loop) lands ahead of the
            // already-front-moved coeng+Ro pair rather than behind it.
            var glyphs = new List<PlacedGlyph> { G(100), G(101), G(102), G(103) };
            var categories = new[] { KhmerCategory.C, KhmerCategory.H, KhmerCategory.Ra, KhmerCategory.VPre };

            KhmerReorderer.ReorderSyllable(glyphs, categories, 0, 4);

            Assert.Equal([103, 101, 102, 100], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void Robat_IsNotMoved()
        {
            var glyphs = new List<PlacedGlyph> { G(100), G(101) };
            var categories = new[] { KhmerCategory.C, KhmerCategory.Robatic };

            KhmerReorderer.ReorderSyllable(glyphs, categories, 0, 2);

            Assert.Equal([100, 101], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void ReorderAll_OnlyTouchesReorderableSyllables()
        {
            // Two syllables: KA+COENG+RO (reordered) then a bare non-Khmer character (untouched).
            var glyphs = new List<PlacedGlyph> { G(100), G(101), G(102), G(200) };
            var categories = new[] { KhmerCategory.C, KhmerCategory.H, KhmerCategory.Ra, KhmerCategory.Other };
            var syllables = new List<KhmerSyllable>
            {
                new(0, 3, KhmerSyllableType.ConsonantSyllable),
                new(3, 1, KhmerSyllableType.NonKhmerCluster),
            };

            KhmerReorderer.ReorderAll(glyphs, categories, syllables);

            Assert.Equal([101, 102, 100, 200], glyphs.Select(g => g.GlyphIndex));
        }
    }
}

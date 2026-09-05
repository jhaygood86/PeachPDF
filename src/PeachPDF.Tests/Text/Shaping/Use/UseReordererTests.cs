using System.Collections.Generic;
using System.Linq;
using PeachPDF.Text;
using PeachPDF.Text.Shaping.Use;
using Xunit;

namespace PeachPDF.Tests.Text.Shaping.Use
{
    /// <summary>
    /// Coverage for <see cref="UseReorderer"/>, the ported <c>reorder_syllable_use</c> two-pass
    /// glyph-array reorder (issue #533, Phase 5b). Glyph indices below are arbitrary but distinct, so
    /// asserting the resulting <see cref="ShapedGlyph.GlyphIndex"/> order directly proves which glyph
    /// physically moved where - not just that reordering "did something".
    /// </summary>
    public class UseReordererTests
    {
        private static ShapedGlyph G(int glyphIndex) => new(glyphIndex, ClusterStart: glyphIndex, ClusterLength: 1);

        [Fact]
        public void PreBaseVowel_MovesBeforeItsBase()
        {
            // KA + VOWEL SIGN I ("कि") - B VPre. No halant precedes the vowel, so it moves all the
            // way back to the syllable start.
            var glyphs = new List<ShapedGlyph> { G(100), G(101) };
            var categories = new[] { UseCategory.B, UseCategory.VPre };

            UseReorderer.ReorderSyllable(glyphs, categories, 0, 2);

            Assert.Equal([101, 100], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void PostBaseVowel_IsNotMoved()
        {
            var glyphs = new List<ShapedGlyph> { G(100), G(101) };
            var categories = new[] { UseCategory.B, UseCategory.VPst };

            UseReorderer.ReorderSyllable(glyphs, categories, 0, 2);

            Assert.Equal([100, 101], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void PreBaseVowelInAConjunct_MovesToAfterTheLastHalantSeen()
        {
            // KA + VIRAMA + SSA + VOWEL SIGN I ("क्षि") with no ligation having merged KA+VIRAMA into
            // a half-form glyph (the font-doesn't-implement-'half' case) - the vowel moves to
            // immediately after the halant, per the algorithm's own "j resets at the last halant"
            // rule, landing between the conjunct's two consonants rather than before the whole
            // conjunct (see UseReorderer's own remarks on why a font that DOES ligate the conjunct
            // first, via 'half'/'cjct' before this pass ever runs, gets the more familiar
            // matra-before-the-whole-conjunct result instead - GsubShaper's USE stage runs those
            // features before this reorder pass for exactly that reason).
            var glyphs = new List<ShapedGlyph> { G(100), G(101), G(102), G(103) };
            var categories = new[] { UseCategory.B, UseCategory.H, UseCategory.B, UseCategory.VPre };

            UseReorderer.ReorderSyllable(glyphs, categories, 0, 4);

            Assert.Equal([100, 101, 103, 102], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void Repha_MovesForwardToBeforeTheFirstPostBaseGlyph()
        {
            // Repha + KA + VOWEL SIGN AA ("र्का", conceptually) - R B VPst. Repha moves past the base
            // consonant but stops right before the post-base vowel.
            var glyphs = new List<ShapedGlyph> { G(100), G(101), G(102) };
            var categories = new[] { UseCategory.R, UseCategory.B, UseCategory.VPst };

            UseReorderer.ReorderSyllable(glyphs, categories, 0, 3);

            Assert.Equal([101, 100, 102], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void RephaWithNoPostBaseGlyph_MovesToTheSyllablesEnd()
        {
            var glyphs = new List<ShapedGlyph> { G(100), G(101) };
            var categories = new[] { UseCategory.R, UseCategory.B };

            UseReorderer.ReorderSyllable(glyphs, categories, 0, 2);

            Assert.Equal([101, 100], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void RephaAndPreBaseVowel_BothMove_MatchingTheResearchedWalkthrough()
        {
            // Repha + KA + VOWEL SIGN I - R B VPre. Both passes fire: pass 1 moves the repha forward
            // past the base to just before the (post-base-flagged) VPre glyph; pass 2 then moves that
            // same VPre glyph all the way back to the syllable start, landing in front of both the
            // base and the just-relocated repha. Final order: matra, base, repha - independently
            // hand-derived against real HarfBuzz's own documented algorithm during this feature's own
            // research (see its recent-fixes entry), not merely reverse-engineered from this
            // implementation.
            var glyphs = new List<ShapedGlyph> { G(100), G(101), G(102) };
            var categories = new[] { UseCategory.R, UseCategory.B, UseCategory.VPre };

            UseReorderer.ReorderSyllable(glyphs, categories, 0, 3);

            Assert.Equal([102, 101, 100], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void NuktaBetweenRephaAndItsTarget_DoesNotStopTheForwardSearch()
        {
            // Repha + KA + NUKTA + VOWEL SIGN AA - R B CMBlw VPst. CMBlw is deliberately excluded from
            // HarfBuzz's own POST_BASE_FLAGS64 (see UseReorderer's own remarks), so the forward walk
            // must skip past it and stop at the VPst glyph instead.
            var glyphs = new List<ShapedGlyph> { G(100), G(101), G(102), G(103) };
            var categories = new[] { UseCategory.R, UseCategory.B, UseCategory.CMBlw, UseCategory.VPst };

            UseReorderer.ReorderSyllable(glyphs, categories, 0, 4);

            Assert.Equal([101, 102, 100, 103], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void SyllableOfLengthOne_IsNeverTouched()
        {
            var glyphs = new List<ShapedGlyph> { G(100) };
            var categories = new[] { UseCategory.B };

            UseReorderer.ReorderSyllable(glyphs, categories, 0, 1);

            Assert.Equal([100], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void ReorderAll_OnlyTouchesReorderableSyllableTypes_LeavingNonClustersAlone()
        {
            // A pre-base vowel classified (unusually) inside a NonCluster span must not be reordered -
            // ReorderAll gates on syllable type before ever calling ReorderSyllable.
            var glyphs = new List<ShapedGlyph> { G(100), G(101) };
            var categories = new[] { UseCategory.B, UseCategory.VPre };
            var syllables = new[] { new UseSyllable(0, 2, UseSyllableType.NonCluster) };

            UseReorderer.ReorderAll(glyphs, categories, syllables);

            Assert.Equal([100, 101], glyphs.Select(g => g.GlyphIndex));
        }

        [Fact]
        public void ReorderAll_ReordersEachSyllableIndependently()
        {
            var glyphs = new List<ShapedGlyph> { G(100), G(101), G(102), G(103) };
            var categories = new[] { UseCategory.B, UseCategory.VPre, UseCategory.B, UseCategory.VPre };
            var syllables = new[]
            {
                new UseSyllable(0, 2, UseSyllableType.StandardCluster),
                new UseSyllable(2, 2, UseSyllableType.StandardCluster),
            };

            UseReorderer.ReorderAll(glyphs, categories, syllables);

            Assert.Equal([101, 100, 103, 102], glyphs.Select(g => g.GlyphIndex));
        }
    }
}

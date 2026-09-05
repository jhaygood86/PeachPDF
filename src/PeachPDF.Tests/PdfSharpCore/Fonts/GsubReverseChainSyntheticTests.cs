using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for <see cref="GsubTable"/>'s Lookup Type 8 (Reverse Chaining Context Single
    /// Substitution) reader and <see cref="GsubShaper.ApplyReverseChainSingleSubstitutionLookup"/> -
    /// the one GSUB lookup type specified to process end-to-start rather than start-to-end, exercised
    /// directly against the parsed lookup (see <see cref="GsubMultipleAndContextualSyntheticTests"/>'s
    /// own internal-visibility rationale for why this is possible without a real font's cmap).
    ///
    /// Layout: no ScriptList features are exercised here. Lookup 0: coverage {50} -&gt; substitute 55,
    /// backtrack=[Coverage{40}], lookahead=[Coverage{60}] (single-subtable, used for the basic
    /// match/mismatch tests). Lookup 1: two subtables proving genuine end-to-start order sensitivity -
    /// subtable A (coverage {80} -&gt; 85, lookahead=[Coverage{86}] - requires the *already-substituted*
    /// form of the following glyph) and subtable B (coverage {81} -&gt; 86, no lookahead).
    /// </summary>
    public class GsubReverseChainSyntheticTests
    {
        private static byte[] BuildSyntheticGsub()
        {
            var b = new SfntByteBuilder();

            b.U16(1); b.U16(0);
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();

            int scriptListStart = b.Position;
            b.PatchU16(scriptListOffsetAt, scriptListStart);
            b.U16(0); // scriptCount

            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(0); // featureCount

            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(2); // lookupCount
            int lookup0At = b.PlaceholderU16();
            int lookup1At = b.PlaceholderU16();

            // Lookup 0: Type 8, single subtable - coverage {50} -> 55, backtrack=[40], lookahead=[60].
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(8); b.U16(0); b.U16(1); // lookupType=8, lookupFlag=0, subtableCount=1
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format 1
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // backtrackGlyphCount
                int backtrackOffsetAt = b.PlaceholderU16();
                b.U16(1); // lookaheadGlyphCount
                int lookaheadOffsetAt = b.PlaceholderU16();
                b.U16(1); // glyphCount
                b.U16(55); // substituteGlyphIDs[0] (for coverage index 0, glyph 50)
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(50);
                int backtrackStart = b.Position;
                b.PatchU16(backtrackOffsetAt, backtrackStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(40);
                int lookaheadStart = b.Position;
                b.PatchU16(lookaheadOffsetAt, lookaheadStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(60);
            }

            // Lookup 1: Type 8, two subtables proving end-to-start order sensitivity.
            int lookup1Start = b.Position;
            b.PatchU16(lookup1At, lookup1Start - lookupListStart);
            b.U16(8); b.U16(0); b.U16(2); // lookupType=8, lookupFlag=0, subtableCount=2
            int lookup1SubAAt = b.PlaceholderU16();
            int lookup1SubBAt = b.PlaceholderU16();

            // Subtable A: coverage {80} -> 85, no backtrack, lookahead=[Coverage{86}].
            int subAStart = b.Position;
            b.PatchU16(lookup1SubAAt, subAStart - lookup1Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format 1
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0); // backtrackGlyphCount
                b.U16(1); // lookaheadGlyphCount
                int lookaheadOffsetAt = b.PlaceholderU16();
                b.U16(1); // glyphCount
                b.U16(85); // substitute for glyph 80
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(80);
                int lookaheadStart = b.Position;
                b.PatchU16(lookaheadOffsetAt, lookaheadStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(86); // requires the ALREADY-SUBSTITUTED form of glyph 81
            }

            // Subtable B: coverage {81} -> 86, no backtrack, no lookahead.
            int subBStart = b.Position;
            b.PatchU16(lookup1SubBAt, subBStart - lookup1Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format 1
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0); // backtrackGlyphCount
                b.U16(0); // lookaheadGlyphCount
                b.U16(1); // glyphCount
                b.U16(86); // substitute for glyph 81
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(81);
            }

            return b.ToArray();
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var combined = new byte[a.Length + b.Length];
            a.CopyTo(combined, 0);
            b.CopyTo(combined, a.Length);
            return combined;
        }

        private static (OpenTypeFontface Face, int TableStart) BuildFaceWithSyntheticGsub()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, BuildSyntheticGsub());
            return (XFontSource.GetOrCreateFrom(combined).Fontface, tableStart);
        }

        [Fact]
        public void MatchedBacktrackInputLookahead_SubstitutesTheCoveredGlyph()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetReverseChainSingleSubstLookup(0);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(40, 0, 1), new(50, 1, 1), new(60, 2, 1) };
            GsubShaper.ApplyReverseChainSingleSubstitutionLookup(lookup, glyphs, gdef: null, markFilteringSet: null);

            Assert.Equal([40, 55, 60], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void UncoveredGlyph_LeftUnchanged()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetReverseChainSingleSubstLookup(0);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(40, 0, 1), new(999, 1, 1), new(60, 2, 1) };
            GsubShaper.ApplyReverseChainSingleSubstitutionLookup(lookup, glyphs, gdef: null, markFilteringSet: null);

            Assert.Equal([40, 999, 60], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void MismatchedBacktrack_LeavesGlyphUnchanged()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetReverseChainSingleSubstLookup(0);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(999, 0, 1), new(50, 1, 1), new(60, 2, 1) };
            GsubShaper.ApplyReverseChainSingleSubstitutionLookup(lookup, glyphs, gdef: null, markFilteringSet: null);

            Assert.Equal([999, 50, 60], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void MismatchedLookahead_LeavesGlyphUnchanged()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetReverseChainSingleSubstLookup(0);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(40, 0, 1), new(50, 1, 1), new(999, 2, 1) };
            GsubShaper.ApplyReverseChainSingleSubstitutionLookup(lookup, glyphs, gdef: null, markFilteringSet: null);

            Assert.Equal([40, 50, 999], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ProcessesEndToStart_LaterPositionsSubstituteBeforeEarlierOnesAreChecked()
        {
            // Subtable A's own lookahead (Coverage{86}) can only ever be satisfied by glyph 81 having
            // ALREADY been substituted to 86 - which only happens if position 1 (glyph 81) is
            // processed, and substituted via subtable B, BEFORE position 0 (glyph 80) is checked. A
            // start-to-end walk would see glyph 81 still in its original (unsubstituted) form when
            // checking position 0's lookahead, fail to match subtable A there, and leave glyph 80
            // unchanged - producing [80, 86] instead of the spec-correct [85, 86].
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetReverseChainSingleSubstLookup(1);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(80, 0, 1), new(81, 1, 1) };
            GsubShaper.ApplyReverseChainSingleSubstitutionLookup(lookup, glyphs, gdef: null, markFilteringSet: null);

            Assert.Equal([85, 86], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void UnrecognizedSubtableFormat_LookupHasNoSubtables_AccessorReturnsNull()
        {
            var b = new SfntByteBuilder();
            b.U16(1); b.U16(0);
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();
            int scriptListStart = b.Position;
            b.PatchU16(scriptListOffsetAt, scriptListStart);
            b.U16(0);
            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(0);
            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(1); // lookupCount
            int lookup0At = b.PlaceholderU16();
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(8); b.U16(0); b.U16(1); // lookupType=8, lookupFlag=0, subtableCount=1
            int subOffsetAt = b.PlaceholderU16();
            int subStart = b.Position;
            b.PatchU16(subOffsetAt, subStart - lookup0Start);
            b.U16(2); // format 2 - unrecognized (only format 1 is defined for this lookup type)

            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, b.ToArray());
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;
            var gsub = new GsubTable(face, tableStart);

            // The one subtable fails to parse, leaving the lookup with zero subtables - which the
            // accessor treats the same as "not this lookup type at all", per every other lookup-type
            // reader's own convention (see e.g. ReadLigatureLookup).
            Assert.Null(gsub.GetReverseChainSingleSubstLookup(0));
        }

        [Fact]
        public void MismatchedLookupTypeAccessors_ReturnNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetLigatureLookup(0)); // lookup 0 is Type 8
            Assert.Null(gsub.GetSingleSubstitutionLookup(1)); // lookup 1 is Type 8
        }

        [Fact]
        public void OutOfRangeLookupListIndex_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetReverseChainSingleSubstLookup(-1));
            Assert.Null(gsub.GetReverseChainSingleSubstLookup(999));
        }

        [Fact]
        public void GetResolvedLookupType_ReportsType8()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Equal(8, gsub.GetResolvedLookupType(0));
            Assert.Equal(8, gsub.GetResolvedLookupType(1));
        }

        /// <summary>
        /// Same rationale as <see cref="GsubTableSyntheticTests.ConcurrentAccess_FromManyThreads_ProducesConsistentResults"/>
        /// (issue #543) - GsubTable instances are cached and shared process-wide.
        /// </summary>
        [Fact]
        public void ConcurrentAccess_FromManyThreads_ProducesConsistentResults()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var actions = new Action[]
            {
                () => Assert.NotNull(gsub.GetReverseChainSingleSubstLookup(0)),
                () => Assert.NotNull(gsub.GetReverseChainSingleSubstLookup(1)),
                () => Assert.Equal(8, gsub.GetResolvedLookupType(0)),
                () => Assert.Null(gsub.GetLigatureLookup(0)),
            };

            const int repeatsPerAction = 40;
            var work = Enumerable.Range(0, actions.Length * repeatsPerAction)
                .Select(i => actions[i % actions.Length]);

            Parallel.ForEach(work, action => action());
        }
    }
}

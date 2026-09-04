using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for <see cref="GsubTable"/>'s Lookup Type 3 (Alternate Substitution) reader - added
    /// once byte-inspecting several real fonts for a real <c>font-variant-caps: petite-caps</c>
    /// candidate turned up only the web-platform-tests <c>gsubtest-lookup3.otf</c> conformance font
    /// (see <see cref="BundledFonts.GsubTestLookup3"/>), which implements every feature via this
    /// lookup type rather than Lookup Type 1 - exposing that <see cref="GsubTable.SupportsAllFeatureTags"/>
    /// previously reported "supported" based on tag presence alone, without checking the lookup was one
    /// this reader could actually apply. A fresh, independent synthetic GSUB table (rather than
    /// extending <see cref="GsubTableSyntheticTests"/>'s or <see cref="GsubSingleSubstitutionSyntheticTests"/>'s
    /// shared ones) avoids renumbering their existing <c>PatchU16</c> offsets.
    ///
    /// Layout: one script "aaaa" (DefaultLangSys, no required feature) referencing features "salt"
    /// (-&gt; lookup 0, Type 3 format 1), "swsh" (-&gt; lookup 1, Type 7 wrapping Type 3), "ss01"
    /// (-&gt; lookup 2, Type 7 wrapping Type 1 - proves the alternate reader correctly declines a
    /// wrapped type it doesn't handle) and "ss02" (-&gt; lookup 3, Type 2 multiple substitution -
    /// unsupported by the single/alternate readers <see cref="GsubTable.SupportsAllFeatureTags"/>
    /// checks (font-variant-caps/-numeric/-east-asian gating only ever routes through those two),
    /// proving that method actually reports unsupported instead of trusting tag presence alone).
    /// </summary>
    public class GsubAlternateSubstitutionSyntheticTests
    {
        private static void WriteAlternateSubtable(SfntByteBuilder b, ushort[] coverageGlyphs, ushort[][] alternateSets)
        {
            int subtableStart = b.Position;
            b.U16(1); // substFormat
            int coverageOffsetAt = b.PlaceholderU16();
            b.U16(alternateSets.Length); // alternateSetCount
            var altSetOffsetAts = new int[alternateSets.Length];
            for (int i = 0; i < alternateSets.Length; i++)
                altSetOffsetAts[i] = b.PlaceholderU16();

            for (int i = 0; i < alternateSets.Length; i++)
            {
                int altSetStart = b.Position;
                b.PatchU16(altSetOffsetAts[i], altSetStart - subtableStart);
                b.U16(alternateSets[i].Length);
                foreach (ushort g in alternateSets[i]) b.U16(g);
            }

            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); // coverage format 1
            b.U16(coverageGlyphs.Length);
            foreach (ushort g in coverageGlyphs) b.U16(g);
        }

        private static void WriteType1Format1Subtable(SfntByteBuilder b, ushort firstGlyph, short delta)
        {
            int subtableStart = b.Position;
            b.U16(1); // substFormat
            int coverageOffsetAt = b.PlaceholderU16();
            b.U16(unchecked((ushort)delta));

            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); // coverage format 1
            b.U16(1); // glyphCount
            b.U16(firstGlyph);
        }

        private static byte[] BuildSyntheticGsub()
        {
            var b = new SfntByteBuilder();

            // ---- Header ----
            b.U16(1); b.U16(0);
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();

            // ---- ScriptList ----
            int scriptListStart = b.Position;
            b.PatchU16(scriptListOffsetAt, scriptListStart);
            b.U16(1); // scriptCount
            b.Tag("aaaa"); int aaaaOffsetAt = b.PlaceholderU16();

            int aaaaStart = b.Position;
            b.PatchU16(aaaaOffsetAt, aaaaStart - scriptListStart);
            int aaaaLangSysOffsetAt = b.PlaceholderU16();
            b.U16(0); // langSysCount
            int aaaaLangSysStart = b.Position;
            b.PatchU16(aaaaLangSysOffsetAt, aaaaLangSysStart - aaaaStart);
            b.U16(0); // lookupOrder
            b.U16(0xFFFF); // requiredFeatureIndex - none
            b.U16(4); // featureIndexCount
            b.U16(0); // -> feature 0 ("salt")
            b.U16(1); // -> feature 1 ("swsh")
            b.U16(2); // -> feature 2 ("ss01")
            b.U16(3); // -> feature 3 ("ss02")

            // ---- FeatureList ----
            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(4); // featureCount
            b.Tag("salt"); int feat0OffsetAt = b.PlaceholderU16();
            b.Tag("swsh"); int feat1OffsetAt = b.PlaceholderU16();
            b.Tag("ss01"); int feat2OffsetAt = b.PlaceholderU16();
            b.Tag("ss02"); int feat3OffsetAt = b.PlaceholderU16();

            int feat0Start = b.Position;
            b.PatchU16(feat0OffsetAt, feat0Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(0); // -> lookup 0

            int feat1Start = b.Position;
            b.PatchU16(feat1OffsetAt, feat1Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(1); // -> lookup 1

            int feat2Start = b.Position;
            b.PatchU16(feat2OffsetAt, feat2Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(2); // -> lookup 2

            int feat3Start = b.Position;
            b.PatchU16(feat3OffsetAt, feat3Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(3); // -> lookup 3

            // ---- LookupList ----
            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(5); // lookupCount
            int lookup0OffsetAt = b.PlaceholderU16();
            int lookup1OffsetAt = b.PlaceholderU16();
            int lookup2OffsetAt = b.PlaceholderU16();
            int lookup3OffsetAt = b.PlaceholderU16();
            int lookup4OffsetAt = b.PlaceholderU16();

            // Lookup 0: Type 3 format 1 - glyph 10 -> alternates {15, 16, 17}, glyph 11 -> {25, 26}.
            int lookup0Start = b.Position;
            b.PatchU16(lookup0OffsetAt, lookup0Start - lookupListStart);
            b.U16(3); b.U16(0); b.U16(1);
            int lookup0SubOffsetAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubOffsetAt, lookup0SubStart - lookup0Start);
            WriteAlternateSubtable(b, [10, 11], [[15, 16, 17], [25, 26]]);

            // Lookup 1: Type 7 (Extension) wrapping a valid Type 3 - glyph 50 -> alternates {55, 56}.
            int lookup1Start = b.Position;
            b.PatchU16(lookup1OffsetAt, lookup1Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(1);
            int lookup1SubOffsetAt = b.PlaceholderU16();
            int lookup1SubStart = b.Position;
            b.PatchU16(lookup1SubOffsetAt, lookup1SubStart - lookup1Start);
            b.U16(1); // extension substFormat
            b.U16(3); // extensionLookupType = 3 (valid)
            int lookup1ExtOffsetAt = b.Position;
            b.U32(0); // placeholder for extensionOffset (u32) - patched below
            int lookup1TargetStart = b.Position;
            b.PatchU32(lookup1ExtOffsetAt, (uint)(lookup1TargetStart - lookup1SubStart));
            WriteAlternateSubtable(b, [50], [[55, 56]]);

            // Lookup 2: Type 7 (Extension) wrapping a *non-3* type (1) - must be skipped for
            // alternate-substitution, even though it's a real, applicable lookup for single-substitution.
            int lookup2Start = b.Position;
            b.PatchU16(lookup2OffsetAt, lookup2Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(1);
            int lookup2SubOffsetAt = b.PlaceholderU16();
            int lookup2SubStart = b.Position;
            b.PatchU16(lookup2SubOffsetAt, lookup2SubStart - lookup2Start);
            b.U16(1); // extension substFormat
            b.U16(1); // extensionLookupType = 1 (NOT 3)
            int lookup2ExtOffsetAt = b.Position;
            b.U32(0);
            int lookup2TargetStart = b.Position;
            b.PatchU32(lookup2ExtOffsetAt, (uint)(lookup2TargetStart - lookup2SubStart));
            WriteType1Format1Subtable(b, firstGlyph: 70, delta: 5);

            // Lookup 3: unsupported lookup type (2 = multiple substitution) - neither reader applies.
            int lookup3Start = b.Position;
            b.PatchU16(lookup3OffsetAt, lookup3Start - lookupListStart);
            b.U16(2); b.U16(0); b.U16(0);

            // Lookup 4: Type 3 with an invalid substFormat (2 - only format 1 exists in the spec for
            // Alternate Substitution) - must fail closed rather than misread the bytes as format 1.
            int lookup4Start = b.Position;
            b.PatchU16(lookup4OffsetAt, lookup4Start - lookupListStart);
            b.U16(3); b.U16(0); b.U16(1);
            int lookup4SubOffsetAt = b.PlaceholderU16();
            int lookup4SubStart = b.Position;
            b.PatchU16(lookup4SubOffsetAt, lookup4SubStart - lookup4Start);
            b.U16(2); // substFormat = 2 (invalid)

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
        public void AlternateSubstitutionFormat1_SelectsRequestedIndexPerCoverageGlyph()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var lookup = gsub.GetAlternateSubstitutionLookup(0);

            Assert.NotNull(lookup);
            var subtable = Assert.Single(lookup.Subtables);

            Assert.True(subtable.TryGetAlternate(10, 0, out var g10alt0));
            Assert.Equal(15, g10alt0);
            Assert.True(subtable.TryGetAlternate(10, 2, out var g10alt2));
            Assert.Equal(17, g10alt2);
            Assert.True(subtable.TryGetAlternate(11, 1, out var g11alt1));
            Assert.Equal(26, g11alt1);
        }

        [Fact]
        public void AlternateSubstitution_UncoveredGlyph_ReturnsFalse()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var subtable = Assert.Single(gsub.GetAlternateSubstitutionLookup(0)!.Subtables);

            Assert.False(subtable.TryGetAlternate(999, 0, out var substitute));
            Assert.Equal(0, substitute);
        }

        [Fact]
        public void AlternateSubstitution_IndexOutOfRangeForThatGlyphsAlternateSet_ReturnsFalse()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var subtable = Assert.Single(gsub.GetAlternateSubstitutionLookup(0)!.Subtables);

            // Glyph 11's alternate set only has 2 entries (indices 0-1).
            Assert.False(subtable.TryGetAlternate(11, 2, out var substitute));
            Assert.Equal(0, substitute);
        }

        [Fact]
        public void ExtensionSubstitution_WrappingType3_Unwraps()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var lookup = gsub.GetAlternateSubstitutionLookup(1);

            Assert.NotNull(lookup);
            var subtable = Assert.Single(lookup.Subtables);
            Assert.True(subtable.TryGetAlternate(50, 1, out var substitute));
            Assert.Equal(56, substitute);
        }

        [Fact]
        public void ExtensionSubstitution_WrappingNonType3_YieldsNoSubtablesForAlternateReader()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            // Lookup 2 is a real, applicable Type 1 lookup (wrapped in Type 7) - the alternate reader
            // must not mis-apply it, even though the single-substitution reader can.
            Assert.Null(gsub.GetAlternateSubstitutionLookup(2));
            Assert.NotNull(gsub.GetSingleSubstitutionLookup(2));
        }

        [Fact]
        public void UnsupportedLookupType_ReturnsNullFromAlternateSubstitutionReader()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetAlternateSubstitutionLookup(3));
        }

        [Fact]
        public void InvalidSubstFormat_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetAlternateSubstitutionLookup(4));
        }

        [Fact]
        public void OutOfRangeLookupListIndex_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetAlternateSubstitutionLookup(-1));
            Assert.Null(gsub.GetAlternateSubstitutionLookup(999));
        }

        [Theory]
        [InlineData(0, 3)]
        [InlineData(1, 3)] // Type 7 wrapping Type 3 resolves to the wrapped type
        [InlineData(2, 1)] // Type 7 wrapping Type 1 resolves to 1, not 3
        [InlineData(3, 2)]
        public void GetResolvedLookupType_ReturnsRealType(int lookupIndex, int expectedType)
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Equal(expectedType, gsub.GetResolvedLookupType(lookupIndex));
        }

        [Fact]
        public void SupportsAllFeatureTags_AcceptsAnAlternateSubstitutionBackedTag()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            // "salt" (lookup 0) and "swsh" (lookup 1, Type 7 wrapping Type 3) are both real,
            // applicable Alternate Substitution lookups.
            Assert.True(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "salt" }));
            Assert.True(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "swsh" }));
        }

        [Fact]
        public void SupportsAllFeatureTags_RejectsATagBackedOnlyByAnUnsupportedLookupType()
        {
            // Regression test for the bug found while investigating a real petite-caps candidate font:
            // "ss02" (lookup 3) has an active lookup in the FeatureList, but that lookup is Type 2
            // (multiple substitution), which neither GSUB reader implements. Before this fix,
            // SupportsAllFeatureTags only checked tag presence/active-lookup-count, so it wrongly
            // reported "supported" here - which for font-variant-caps specifically means the caps
            // synthesis fallback gets skipped for a feature that then silently substitutes nothing at
            // all, a worse outcome than not claiming support.
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.False(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "ss02" }));
        }

        [Fact]
        public void SupportsAllFeatureTags_RejectsAnEntirelyAbsentTag()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.False(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "calt" }));
        }

        /// <summary>
        /// Same rationale as <see cref="GsubTableSyntheticTests.ConcurrentAccess_FromManyThreads_ProducesConsistentResults"/>
        /// (issue #543) - the new alternate-substitution cache shares the same process-wide-instance,
        /// unsynchronized-cursor hazard as the ligature/single-substitution readers.
        /// </summary>
        [Fact]
        public void ConcurrentAccess_FromManyThreads_ProducesConsistentResults()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var actions = new Action[]
            {
                () =>
                {
                    var lookup = gsub.GetAlternateSubstitutionLookup(0);
                    Assert.NotNull(lookup);
                    Assert.True(lookup.Subtables[0].TryGetAlternate(10, 0, out var s));
                    Assert.Equal(15, s);
                },
                () =>
                {
                    var lookup = gsub.GetAlternateSubstitutionLookup(1);
                    Assert.NotNull(lookup);
                    Assert.True(lookup.Subtables[0].TryGetAlternate(50, 1, out var s));
                    Assert.Equal(56, s);
                },
                () => Assert.Equal(3, gsub.GetResolvedLookupType(0)),
                () => Assert.Equal(1, gsub.GetResolvedLookupType(2)),
                () => Assert.Null(gsub.GetAlternateSubstitutionLookup(2)),
                () => Assert.True(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "salt" })),
                () => Assert.False(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "ss02" })),
            };

            const int repeatsPerAction = 40;
            var work = Enumerable.Range(0, actions.Length * repeatsPerAction)
                .Select(i => actions[i % actions.Length]);

            Parallel.ForEach(work, action => action());
        }
    }
}

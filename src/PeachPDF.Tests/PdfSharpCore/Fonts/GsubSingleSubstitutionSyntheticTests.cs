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
    /// Coverage for <see cref="GsubTable"/>'s Lookup Type 1 (Single Substitution) reader - the
    /// generic reader added to support <c>font-variant-caps</c>/<c>-numeric</c>/<c>-east-asian</c> and
    /// explicit <c>font-feature-settings</c> tags, alongside the pre-existing Type 4 (ligature) reader.
    /// A fresh, independent synthetic GSUB table (rather than extending
    /// <see cref="GsubTableSyntheticTests"/>'s shared one) avoids renumbering every existing
    /// <c>PatchU16</c> offset there.
    ///
    /// Layout: one script "aaaa" (DefaultLangSys, no required feature) referencing features "smcp"
    /// (-> lookup 0) and "onum" (-> lookup 1) - deliberately no "c2sc" feature anywhere in this font,
    /// so <see cref="GsubTable.SupportsAllFeatureTags"/> can prove the "both tags required" case.
    /// Lookups: 0 = Type 1 format 1 (delta); 1 = Type 1 format 2 (explicit array); 2 = Type 9 wrapping
    /// a valid Type 1; 3 = Type 9 wrapping a non-1 type (Type 4); 4 = plain Type 4 (ligature, for
    /// GetResolvedLookupType's cross-type dispatch); 5 = unsupported type (2, multiple substitution);
    /// 6 = Type 1 with an invalid substFormat (3); 7 = Type 1 format 2 with a coverage table listing
    /// more glyphs than the substitute array has entries (malformed, but must fail closed rather than
    /// index out of range); 8 = Type 9 (Extension) with zero subtables (malformed - resolves to the
    /// wrapper type 9 itself rather than reading past the end of the lookup).
    /// </summary>
    public class GsubSingleSubstitutionSyntheticTests
    {
        private sealed class Builder
        {
            private readonly List<byte> _bytes = [];
            public int Position => _bytes.Count;

            public void U16(int v)
            {
                _bytes.Add((byte)(v >> 8));
                _bytes.Add((byte)v);
            }

            public void U32(uint v)
            {
                _bytes.Add((byte)(v >> 24));
                _bytes.Add((byte)(v >> 16));
                _bytes.Add((byte)(v >> 8));
                _bytes.Add((byte)v);
            }

            public void PatchU32(int at, uint value)
            {
                _bytes[at] = (byte)(value >> 24);
                _bytes[at + 1] = (byte)(value >> 16);
                _bytes[at + 2] = (byte)(value >> 8);
                _bytes[at + 3] = (byte)value;
            }

            public void Tag(string fourChars)
            {
                foreach (char c in fourChars)
                    _bytes.Add((byte)c);
            }

            public int PlaceholderU16()
            {
                int at = _bytes.Count;
                U16(0);
                return at;
            }

            public void PatchU16(int at, int value)
            {
                _bytes[at] = (byte)(value >> 8);
                _bytes[at + 1] = (byte)value;
            }

            public byte[] ToArray() => _bytes.ToArray();
        }

        private static void WriteType1Format1Subtable(Builder b, ushort firstGlyph, short delta)
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

        private static void WriteType1Format2Subtable(Builder b, ushort[] coverageGlyphs, ushort[] substitutes)
        {
            int subtableStart = b.Position;
            b.U16(2); // substFormat
            int coverageOffsetAt = b.PlaceholderU16();
            b.U16(substitutes.Length);
            foreach (var s in substitutes) b.U16(s);

            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); // coverage format 1
            b.U16(coverageGlyphs.Length);
            foreach (var g in coverageGlyphs) b.U16(g);
        }

        private static byte[] BuildSyntheticGsub()
        {
            var b = new Builder();

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
            b.U16(2); // featureIndexCount
            b.U16(0); // -> feature 0 ("smcp")
            b.U16(1); // -> feature 1 ("onum")

            // ---- FeatureList ----
            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(2); // featureCount
            b.Tag("smcp"); int feat0OffsetAt = b.PlaceholderU16();
            b.Tag("onum"); int feat1OffsetAt = b.PlaceholderU16();

            int feat0Start = b.Position;
            b.PatchU16(feat0OffsetAt, feat0Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(0); // -> lookup 0

            int feat1Start = b.Position;
            b.PatchU16(feat1OffsetAt, feat1Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(1); // -> lookup 1

            // ---- LookupList ----
            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(9); // lookupCount
            int lookup0OffsetAt = b.PlaceholderU16();
            int lookup1OffsetAt = b.PlaceholderU16();
            int lookup2OffsetAt = b.PlaceholderU16();
            int lookup3OffsetAt = b.PlaceholderU16();
            int lookup4OffsetAt = b.PlaceholderU16();
            int lookup5OffsetAt = b.PlaceholderU16();
            int lookup6OffsetAt = b.PlaceholderU16();
            int lookup7OffsetAt = b.PlaceholderU16();
            int lookup8OffsetAt = b.PlaceholderU16();

            // Lookup 0: Type 1 format 1 (delta) - glyph 30 -> 35 (delta +5).
            int lookup0Start = b.Position;
            b.PatchU16(lookup0OffsetAt, lookup0Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup0SubOffsetAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubOffsetAt, lookup0SubStart - lookup0Start);
            WriteType1Format1Subtable(b, firstGlyph: 30, delta: 5);

            // Lookup 1: Type 1 format 2 (explicit array) - {40,41} -> [45,46].
            int lookup1Start = b.Position;
            b.PatchU16(lookup1OffsetAt, lookup1Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup1SubOffsetAt = b.PlaceholderU16();
            int lookup1SubStart = b.Position;
            b.PatchU16(lookup1SubOffsetAt, lookup1SubStart - lookup1Start);
            WriteType1Format2Subtable(b, [40, 41], [45, 46]);

            // Lookup 2: Type 9 (Extension) wrapping a valid Type 1 - glyph 50 -> 55.
            int lookup2Start = b.Position;
            b.PatchU16(lookup2OffsetAt, lookup2Start - lookupListStart);
            b.U16(9); b.U16(0); b.U16(1);
            int lookup2SubOffsetAt = b.PlaceholderU16();
            int lookup2SubStart = b.Position;
            b.PatchU16(lookup2SubOffsetAt, lookup2SubStart - lookup2Start);
            b.U16(1); // extension substFormat
            b.U16(1); // extensionLookupType = 1 (valid)
            int lookup2ExtOffsetAt = b.Position;
            b.U32(0); // placeholder for extensionOffset (u32) - patched below
            int lookup2TargetStart = b.Position;
            b.PatchU32(lookup2ExtOffsetAt, (uint)(lookup2TargetStart - lookup2SubStart));
            WriteType1Format1Subtable(b, firstGlyph: 50, delta: 5);

            // Lookup 3: Type 9 (Extension) wrapping a *non-1* type (4) - must be skipped for single-sub.
            int lookup3Start = b.Position;
            b.PatchU16(lookup3OffsetAt, lookup3Start - lookupListStart);
            b.U16(9); b.U16(0); b.U16(1);
            int lookup3SubOffsetAt = b.PlaceholderU16();
            int lookup3SubStart = b.Position;
            b.PatchU16(lookup3SubOffsetAt, lookup3SubStart - lookup3Start);
            b.U16(1); // extension substFormat
            b.U16(4); // extensionLookupType = 4 (NOT 1)
            b.U16(0); b.U16(0); // extensionOffset - never followed since type != 1

            // Lookup 4: plain Type 4 (ligature) - for GetResolvedLookupType's cross-type dispatch.
            int lookup4Start = b.Position;
            b.PatchU16(lookup4OffsetAt, lookup4Start - lookupListStart);
            b.U16(4); b.U16(0); b.U16(0); // subtableCount=0 - content irrelevant, only the type is tested

            // Lookup 5: unsupported lookup type (2 = multiple substitution).
            int lookup5Start = b.Position;
            b.PatchU16(lookup5OffsetAt, lookup5Start - lookupListStart);
            b.U16(2); b.U16(0); b.U16(0);

            // Lookup 6: Type 1 with an invalid substFormat (3).
            int lookup6Start = b.Position;
            b.PatchU16(lookup6OffsetAt, lookup6Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup6SubOffsetAt = b.PlaceholderU16();
            int lookup6SubStart = b.Position;
            b.PatchU16(lookup6SubOffsetAt, lookup6SubStart - lookup6Start);
            b.U16(3); // substFormat = 3 (invalid)

            // Lookup 7: Type 1 format 2 - coverage lists glyphs {70,71} but only one substitute is
            // present, so the second coverage hit resolves to a coverageIndex the substitutes array
            // doesn't cover.
            int lookup7Start = b.Position;
            b.PatchU16(lookup7OffsetAt, lookup7Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup7SubOffsetAt = b.PlaceholderU16();
            int lookup7SubStart = b.Position;
            b.PatchU16(lookup7SubOffsetAt, lookup7SubStart - lookup7Start);
            WriteType1Format2Subtable(b, coverageGlyphs: [70, 71], substitutes: [75]);

            // Lookup 8: Type 9 (Extension) with zero subtables - malformed, but GetResolvedLookupType
            // must return the wrapper type itself rather than reading a subtable offset that isn't there.
            int lookup8Start = b.Position;
            b.PatchU16(lookup8OffsetAt, lookup8Start - lookupListStart);
            b.U16(9); b.U16(0); b.U16(0); // subtableCount = 0

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
        public void SingleSubstitutionFormat1_Delta_SubstitutesCoveredGlyph()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var lookup = gsub.GetSingleSubstitutionLookup(0);

            Assert.NotNull(lookup);
            var subtable = Assert.Single(lookup.Subtables);
            Assert.True(subtable.TryGetSubstitute(30, out var substitute));
            Assert.Equal(35, substitute);
            Assert.False(subtable.TryGetSubstitute(31, out _));
        }

        [Fact]
        public void SingleSubstitutionFormat2_ExplicitArray_SubstitutesPerCoverageIndex()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var lookup = gsub.GetSingleSubstitutionLookup(1);

            Assert.NotNull(lookup);
            var subtable = Assert.Single(lookup.Subtables);
            Assert.True(subtable.TryGetSubstitute(40, out var s40));
            Assert.Equal(45, s40);
            Assert.True(subtable.TryGetSubstitute(41, out var s41));
            Assert.Equal(46, s41);
        }

        [Fact]
        public void ExtensionSubstitution_WrappingType1_Unwraps()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var lookup = gsub.GetSingleSubstitutionLookup(2);

            Assert.NotNull(lookup);
            var subtable = Assert.Single(lookup.Subtables);
            Assert.True(subtable.TryGetSubstitute(50, out var substitute));
            Assert.Equal(55, substitute);
        }

        [Fact]
        public void ExtensionSubstitution_WrappingNonType1_YieldsNoSubtables()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetSingleSubstitutionLookup(3));
        }

        [Fact]
        public void UnsupportedLookupType_ReturnsNullFromSingleSubstitutionReader()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            // Lookup 4 is a real (if empty) Type 4 lookup - the single-substitution reader must not
            // mis-apply it.
            Assert.Null(gsub.GetSingleSubstitutionLookup(4));
            Assert.Null(gsub.GetSingleSubstitutionLookup(5));
        }

        [Fact]
        public void InvalidSubstFormat_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetSingleSubstitutionLookup(6));
        }

        [Fact]
        public void SingleSubstitutionFormat2_CoverageIndexBeyondSubstitutesArray_FailsClosed()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var lookup = gsub.GetSingleSubstitutionLookup(7);

            Assert.NotNull(lookup);
            var subtable = Assert.Single(lookup.Subtables);
            // Glyph 70 -> coverageIndex 0, within the single-entry substitutes array.
            Assert.True(subtable.TryGetSubstitute(70, out var s70));
            Assert.Equal(75, s70);
            // Glyph 71 -> coverageIndex 1, but the substitutes array only has one entry - must fail
            // closed (no substitute) rather than throw an index-out-of-range exception.
            Assert.False(subtable.TryGetSubstitute(71, out var s71));
            Assert.Equal(0, s71);
        }

        [Fact]
        public void OutOfRangeLookupListIndex_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetSingleSubstitutionLookup(-1));
            Assert.Null(gsub.GetSingleSubstitutionLookup(999));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(2, 1)] // Type 9 wrapping Type 1 resolves to the wrapped type
        [InlineData(3, 4)] // Type 9 wrapping Type 4 resolves to 4
        [InlineData(4, 4)]
        [InlineData(5, 2)]
        [InlineData(8, 9)] // Type 9 with zero subtables - falls back to the wrapper type itself
        [InlineData(-1, -1)]
        [InlineData(999, -1)]
        public void GetResolvedLookupType_ReturnsRealType(int lookupIndex, int expectedType)
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Equal(expectedType, gsub.GetResolvedLookupType(lookupIndex));
        }

        [Fact]
        public void SupportsAllFeatureTags_RequiresEveryTagIndependently()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            // "smcp" alone is present (feature 0) - supported.
            Assert.True(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "smcp" }));

            // "smcp" + "c2sc": this font has no "c2sc" feature at all - a naive unioned lookup query
            // would still find "smcp"'s lookup and wrongly report supported; checking each tag
            // independently correctly reports unsupported.
            Assert.False(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "smcp", "c2sc" }));

            // A tag absent entirely.
            Assert.False(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "c2sc" }));
        }

        /// <summary>
        /// Same rationale as <see cref="GsubTableSyntheticTests.ConcurrentAccess_FromManyThreads_ProducesConsistentResults"/>
        /// (issue #543) - the new single-substitution/resolved-type caches share the same
        /// process-wide-instance, unsynchronized-cursor hazard as the ligature reader.
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
                    var lookup = gsub.GetSingleSubstitutionLookup(0);
                    Assert.NotNull(lookup);
                    Assert.True(lookup.Subtables[0].TryGetSubstitute(30, out var s));
                    Assert.Equal(35, s);
                },
                () =>
                {
                    var lookup = gsub.GetSingleSubstitutionLookup(1);
                    Assert.NotNull(lookup);
                    Assert.True(lookup.Subtables[0].TryGetSubstitute(41, out var s));
                    Assert.Equal(46, s);
                },
                () => Assert.Equal(1, gsub.GetResolvedLookupType(0)),
                () => Assert.Equal(4, gsub.GetResolvedLookupType(4)),
                () => Assert.Null(gsub.GetSingleSubstitutionLookup(3)),
                () => Assert.True(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "smcp" })),
                () => Assert.False(gsub.SupportsAllFeatureTags(["aaaa"], new HashSet<string> { "smcp", "c2sc" })),
            };

            const int repeatsPerAction = 40;
            var work = Enumerable.Range(0, actions.Length * repeatsPerAction)
                .Select(i => actions[i % actions.Length]);

            Parallel.ForEach(work, action => action());
        }
    }
}

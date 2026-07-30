using System.Collections.Generic;
using System.IO;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for <see cref="GsubTable"/> branches no bundled font exercises: Source Sans 3's real
    /// GSUB data has no required feature, no Extension Substitution (type 9) lookup, no out-of-range
    /// feature/lookup index, and always resolves its script on the first try. Rather than author a
    /// whole new font (COLR/CPAL's fixture-font precedent), this hand-crafts a minimal GSUB table's
    /// bytes directly and appends them past the end of a real, already-valid font file - SFNT table
    /// extents are absolute offsets, not end-of-file-relative, so a normal parse of the real tables
    /// never looks past them, and <see cref="GsubTable"/>'s own reads are all offset-based (not
    /// sequential-from-file-start), so it can be pointed at the appended region directly.
    ///
    /// Layout (one shared table covering every scenario, to build it only once):
    /// - Scripts: "aaaa" (DefaultLangSys, no required feature, references feature 0 "liga" -> lookup 0,
    ///   an ordinary 2-glyph ligature), "bbbb" (DefaultLangSys with a *valid* required feature index
    ///   1 "extr" -> lookup 1, an Extension Substitution wrapping a valid Type 4 subtable - AND an
    ///   *out-of-range* feature index in its regular feature list, to hit both branches from one
    ///   script), "cccc" (no DefaultLangSys at all).
    /// - Lookups: 0 = plain Type 4 (glyph 10 + component 11 -> ligature 12); 1 = Type 9 wrapping a
    ///   valid Type 4 (glyph 20 + component 21 -> ligature 22); 2 = Type 9 wrapping a *non-4* type
    ///   (skipped, yields no subtables); 3 = an unsupported lookup type (1, single substitution);
    ///   4 = Type 4 whose subtable has an invalid substFormat (2).
    /// </summary>
    public class GsubTableSyntheticTests
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

            public void Tag(string fourChars)
            {
                foreach (char c in fourChars)
                    _bytes.Add((byte)c);
            }

            /// <summary>Writes a placeholder u16 and returns its byte index, for <see cref="PatchU16"/>.</summary>
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

            public void PatchU32(int at, uint value)
            {
                _bytes[at] = (byte)(value >> 24);
                _bytes[at + 1] = (byte)(value >> 16);
                _bytes[at + 2] = (byte)(value >> 8);
                _bytes[at + 3] = (byte)value;
            }

            public byte[] ToArray() => _bytes.ToArray();
        }

        /// <summary>Builds one Type 4 (Ligature Substitution, format 1) subtable at the builder's current
        /// position: a single-glyph coverage over <paramref name="firstGlyph"/>, one ligature set with one
        /// ligature merging it with <paramref name="componentGlyph"/> into <paramref name="ligatureGlyph"/>.</summary>
        private static void WriteType4Subtable(Builder b, ushort firstGlyph, ushort componentGlyph, ushort ligatureGlyph)
        {
            int subtableStart = b.Position;
            b.U16(1); // substFormat
            int coverageOffsetAt = b.PlaceholderU16();
            b.U16(1); // ligatureSetCount
            int ligSetOffsetAt = b.PlaceholderU16();

            int ligSetStart = b.Position;
            b.PatchU16(ligSetOffsetAt, ligSetStart - subtableStart);
            b.U16(1); // ligatureCount
            int ligOffsetAt = b.PlaceholderU16();

            int ligStart = b.Position;
            b.PatchU16(ligOffsetAt, ligStart - ligSetStart);
            b.U16(ligatureGlyph);
            b.U16(2); // componentCount (first glyph + 1 component)
            b.U16(componentGlyph);

            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); // coverage format 1
            b.U16(1); // glyphCount
            b.U16(firstGlyph);
        }

        private static byte[] BuildSyntheticGsub()
        {
            var b = new Builder();

            // ---- Header ----
            b.U16(1); b.U16(0); // majorVersion, minorVersion
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();

            // ---- ScriptList ----
            int scriptListStart = b.Position;
            b.PatchU16(scriptListOffsetAt, scriptListStart);
            b.U16(3); // scriptCount
            b.Tag("aaaa"); int aaaaOffsetAt = b.PlaceholderU16();
            b.Tag("bbbb"); int bbbbOffsetAt = b.PlaceholderU16();
            b.Tag("cccc"); int ccccOffsetAt = b.PlaceholderU16();

            // Script "aaaa": DefaultLangSys, no required feature, feature index 0 only.
            int aaaaStart = b.Position;
            b.PatchU16(aaaaOffsetAt, aaaaStart - scriptListStart);
            int aaaaLangSysOffsetAt = b.PlaceholderU16();
            b.U16(0); // langSysCount
            int aaaaLangSysStart = b.Position;
            b.PatchU16(aaaaLangSysOffsetAt, aaaaLangSysStart - aaaaStart);
            b.U16(0); // lookupOrder (reserved)
            b.U16(0xFFFF); // requiredFeatureIndex - none
            b.U16(1); // featureIndexCount
            b.U16(0); // -> feature 0

            // Script "bbbb": DefaultLangSys, required feature index 1 (valid), plus one out-of-range
            // feature index (999) in its regular feature list.
            int bbbbStart = b.Position;
            b.PatchU16(bbbbOffsetAt, bbbbStart - scriptListStart);
            int bbbbLangSysOffsetAt = b.PlaceholderU16();
            b.U16(0); // langSysCount
            int bbbbLangSysStart = b.Position;
            b.PatchU16(bbbbLangSysOffsetAt, bbbbLangSysStart - bbbbStart);
            b.U16(0); // lookupOrder
            b.U16(1); // requiredFeatureIndex -> feature 1 (valid)
            b.U16(1); // featureIndexCount
            b.U16(999); // out-of-range feature index

            // Script "cccc": no DefaultLangSys at all.
            int ccccStart = b.Position;
            b.PatchU16(ccccOffsetAt, ccccStart - scriptListStart);
            b.U16(0); // defaultLangSysOffset = 0 (none)
            b.U16(0); // langSysCount

            // ---- FeatureList ----
            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(5); // featureCount
            b.Tag("liga"); int feat0OffsetAt = b.PlaceholderU16();
            b.Tag("extr"); int feat1OffsetAt = b.PlaceholderU16();
            b.Tag("bad2"); int feat2OffsetAt = b.PlaceholderU16();
            b.Tag("bad3"); int feat3OffsetAt = b.PlaceholderU16();
            b.Tag("bad4"); int feat4OffsetAt = b.PlaceholderU16();

            int feat0Start = b.Position;
            b.PatchU16(feat0OffsetAt, feat0Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(0); // featureParams, lookupIndexCount=1, lookup 0

            int feat1Start = b.Position;
            b.PatchU16(feat1OffsetAt, feat1Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(1); // -> lookup 1

            int feat2Start = b.Position;
            b.PatchU16(feat2OffsetAt, feat2Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(2); // -> lookup 2 (extension wrapping a non-4 type)

            int feat3Start = b.Position;
            b.PatchU16(feat3OffsetAt, feat3Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(3); // -> lookup 3 (unsupported type)

            int feat4Start = b.Position;
            b.PatchU16(feat4OffsetAt, feat4Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(4); // -> lookup 4 (invalid substFormat)

            // ---- LookupList ----
            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(5); // lookupCount
            int lookup0OffsetAt = b.PlaceholderU16();
            int lookup1OffsetAt = b.PlaceholderU16();
            int lookup2OffsetAt = b.PlaceholderU16();
            int lookup3OffsetAt = b.PlaceholderU16();
            int lookup4OffsetAt = b.PlaceholderU16();

            // Lookup 0: plain Type 4.
            int lookup0Start = b.Position;
            b.PatchU16(lookup0OffsetAt, lookup0Start - lookupListStart);
            b.U16(4); b.U16(0); b.U16(1); // lookupType, lookupFlag, subtableCount=1
            int lookup0SubOffsetAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubOffsetAt, lookup0SubStart - lookup0Start);
            WriteType4Subtable(b, firstGlyph: 10, componentGlyph: 11, ligatureGlyph: 12);

            // Lookup 1: Type 9 (Extension) wrapping a valid Type 4.
            int lookup1Start = b.Position;
            b.PatchU16(lookup1OffsetAt, lookup1Start - lookupListStart);
            b.U16(9); b.U16(0); b.U16(1);
            int lookup1SubOffsetAt = b.PlaceholderU16();
            int lookup1SubStart = b.Position;
            b.PatchU16(lookup1SubOffsetAt, lookup1SubStart - lookup1Start);
            b.U16(1); // extension substFormat
            b.U16(4); // extensionLookupType = 4 (valid)
            int lookup1ExtOffsetAt = b.Position;
            b.U32(0); // placeholder for extensionOffset (u32) - patched below
            int lookup1TargetStart = b.Position;
            b.PatchU32(lookup1ExtOffsetAt, (uint)(lookup1TargetStart - lookup1SubStart));
            WriteType4Subtable(b, firstGlyph: 20, componentGlyph: 21, ligatureGlyph: 22);

            // Lookup 2: Type 9 (Extension) wrapping a *non-4* type - must be skipped, not mis-applied.
            int lookup2Start = b.Position;
            b.PatchU16(lookup2OffsetAt, lookup2Start - lookupListStart);
            b.U16(9); b.U16(0); b.U16(1);
            int lookup2SubOffsetAt = b.PlaceholderU16();
            int lookup2SubStart = b.Position;
            b.PatchU16(lookup2SubOffsetAt, lookup2SubStart - lookup2Start);
            b.U16(1); // extension substFormat
            b.U16(1); // extensionLookupType = 1 (NOT 4 - must be skipped)
            b.U32(0); // extensionOffset - never followed since type != 4

            // Lookup 3: unsupported lookup type (1 = single substitution), no real subtable content needed.
            int lookup3Start = b.Position;
            b.PatchU16(lookup3OffsetAt, lookup3Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(0); // lookupType=1, lookupFlag, subtableCount=0

            // Lookup 4: Type 4 with an invalid substFormat (2).
            int lookup4Start = b.Position;
            b.PatchU16(lookup4OffsetAt, lookup4Start - lookupListStart);
            b.U16(4); b.U16(0); b.U16(1);
            int lookup4SubOffsetAt = b.PlaceholderU16();
            int lookup4SubStart = b.Position;
            b.PatchU16(lookup4SubOffsetAt, lookup4SubStart - lookup4Start);
            b.U16(2); // substFormat = 2 (invalid - only format 1 is Ligature Substitution)

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
        public void RequiredFeature_Valid_And_OutOfRangeFeatureIndex_BothHandled()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            // Script "bbbb": required feature 1 ("extr", valid -> lookup 1) collected; the regular
            // feature list's out-of-range index (999) is silently skipped, not a crash.
            var indices = gsub.GetActiveLookupIndices(["bbbb"], new HashSet<string> { "extr" });

            Assert.Equal([1], indices);
        }

        [Fact]
        public void NoDefaultLangSys_ReturnsEmpty()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var indices = gsub.GetActiveLookupIndices(["cccc"], new HashSet<string> { "liga" });

            Assert.Empty(indices);
        }

        [Fact]
        public void ScriptPreference_FallsBackToFirstRecord_WhenNothingMatches()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            // Neither "zzzz" nor "yyyy" exist - falls back to the first ScriptRecord ("aaaa"), whose
            // "liga" feature is lookup 0.
            var indices = gsub.GetActiveLookupIndices(["zzzz", "yyyy"], new HashSet<string> { "liga" });

            Assert.Equal([0], indices);
        }

        [Fact]
        public void ScriptPreference_SkipsNonMatchingPreference_BeforeMatching()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            // "yyyy" doesn't exist; the search must move on to "bbbb" rather than stopping.
            var indices = gsub.GetActiveLookupIndices(["yyyy", "bbbb"], new HashSet<string> { "extr" });

            Assert.Equal([1], indices);
        }

        [Fact]
        public void ExtensionSubstitution_WrappingType4_Unwraps()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            var lookup = gsub.GetLigatureLookup(1);

            Assert.NotNull(lookup);
            var subtable = Assert.Single(lookup.Subtables);
            Assert.Equal(0, subtable.Coverage.IndexOfGlyph(20));
            Assert.Equal(22, subtable.LigatureSets[0][0].LigatureGlyph);
        }

        [Fact]
        public void ExtensionSubstitution_WrappingNonType4_YieldsNoSubtables()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetLigatureLookup(2));
        }

        [Fact]
        public void UnsupportedLookupType_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetLigatureLookup(3));
        }

        [Fact]
        public void InvalidSubstFormat_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetLigatureLookup(4));
        }

        [Fact]
        public void OutOfRangeLookupListIndex_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Null(gsub.GetLigatureLookup(-1));
            Assert.Null(gsub.GetLigatureLookup(999));
        }
    }
}

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
    /// Coverage for <see cref="GdefTable"/> (and, transitively, <see cref="ClassDefTable"/>) - no
    /// bundled font's GDEF table is guaranteed to exercise every branch (format 1 vs. 2 ClassDef,
    /// MarkGlyphSetsDef presence, GDEF header versions), so this hand-crafts a minimal GDEF table's
    /// bytes and appends them past a real font's own tables, mirroring <see cref="GsubTableSyntheticTests"/>'s
    /// established approach.
    /// </summary>
    public class GdefTableSyntheticTests
    {
        /// <summary>Builds a GDEF 1.2 table: GlyphClassDef (format 2 ranges: glyphs 10-12 -> class 1
        /// "Base", 20-22 -> class 3 "Mark"), MarkAttachClassDef (format 1: glyph 20 -> class 1, glyph
        /// 21 -> class 2), MarkGlyphSetsDef (one set containing glyph 20 only).</summary>
        private static byte[] BuildSyntheticGdef()
        {
            var b = new SfntByteBuilder();

            b.U16(1); b.U16(2); // majorVersion, minorVersion (1.2 - has MarkGlyphSetsDef)
            int glyphClassDefOffsetAt = b.PlaceholderU16();
            int attachListOffsetAt = b.PlaceholderU16();
            int ligCaretListOffsetAt = b.PlaceholderU16();
            int markAttachClassDefOffsetAt = b.PlaceholderU16();
            int markGlyphSetsDefOffsetAt = b.PlaceholderU16();

            b.PatchU16(attachListOffsetAt, 0); // not present
            b.PatchU16(ligCaretListOffsetAt, 0); // not present

            // GlyphClassDef: ClassDef format 2, two ranges.
            int glyphClassDefStart = b.Position;
            b.PatchU16(glyphClassDefOffsetAt, glyphClassDefStart);
            b.U16(2); // format
            b.U16(2); // classRangeCount
            b.U16(10); b.U16(12); b.U16(1); // glyphs 10-12 -> class 1 (Base)
            b.U16(20); b.U16(22); b.U16(3); // glyphs 20-22 -> class 3 (Mark)

            // MarkAttachClassDef: ClassDef format 1.
            int markAttachClassDefStart = b.Position;
            b.PatchU16(markAttachClassDefOffsetAt, markAttachClassDefStart);
            b.U16(1); // format
            b.U16(20); // startGlyphID
            b.U16(2); // glyphCount
            b.U16(1); // glyph 20 -> class 1
            b.U16(2); // glyph 21 -> class 2

            // MarkGlyphSetsDef: one mark-filtering set, Coverage format 1 over glyph 20 only.
            int markGlyphSetsDefStart = b.Position;
            b.PatchU16(markGlyphSetsDefOffsetAt, markGlyphSetsDefStart);
            b.U16(1); // markSetTableFormat
            b.U16(1); // markSetCount
            int setOffsetAt = b.Position;
            b.U32(0); // placeholder Offset32 - patched below
            int coverageStart = b.Position;
            b.PatchU32(setOffsetAt, (uint)(coverageStart - markGlyphSetsDefStart));
            b.U16(1); // coverage format 1
            b.U16(1); // glyphCount
            b.U16(20);

            return b.ToArray();
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var combined = new byte[a.Length + b.Length];
            a.CopyTo(combined, 0);
            b.CopyTo(combined, a.Length);
            return combined;
        }

        private static (OpenTypeFontface Face, int TableStart) BuildFaceWithSyntheticGdef()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, BuildSyntheticGdef());
            return (XFontSource.GetOrCreateFrom(combined).Fontface, tableStart);
        }

        [Fact]
        public void GlyphClassDef_Format2_ClassifiesGlyphsInRange()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGdef();
            var gdef = new GdefTable(face, tableStart);

            Assert.Equal(1, gdef.GetGlyphClass(10));
            Assert.Equal(1, gdef.GetGlyphClass(12));
            Assert.Equal(3, gdef.GetGlyphClass(20));
            Assert.Equal(3, gdef.GetGlyphClass(22));
            // Outside every declared range: unassigned (class 0), not a thrown exception.
            Assert.Equal(0, gdef.GetGlyphClass(999));
        }

        [Fact]
        public void MarkAttachClassDef_Format1_ClassifiesGlyphsFromStart()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGdef();
            var gdef = new GdefTable(face, tableStart);

            Assert.Equal(1, gdef.GetMarkAttachClass(20));
            Assert.Equal(2, gdef.GetMarkAttachClass(21));
            Assert.Equal(0, gdef.GetMarkAttachClass(22)); // past the declared glyphCount
            Assert.Equal(0, gdef.GetMarkAttachClass(5)); // before startGlyphID
        }

        [Fact]
        public void MarkGlyphSetsDef_Offset32Array_ResolvesToCoverageTable()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGdef();
            var gdef = new GdefTable(face, tableStart);

            var set = gdef.GetMarkGlyphSet(0);
            Assert.NotNull(set);
            Assert.True(set.IndexOfGlyph(20) >= 0);
            Assert.True(set.IndexOfGlyph(21) < 0);

            Assert.Null(gdef.GetMarkGlyphSet(1)); // out of range
            Assert.Null(gdef.GetMarkGlyphSet(-1));
        }

        [Fact]
        public void MissingGdefTable_EverythingResolvesToUnclassified()
        {
            // A GDEF 1.0 header (no MarkGlyphSetsDef field at all) with every offset null - the common
            // real-world case (most fonts have no mark-filtering-set data even when they do have GDEF).
            var b = new SfntByteBuilder();
            b.U16(1); b.U16(0); // 1.0
            b.U16(0); b.U16(0); b.U16(0); b.U16(0); // all four offsets null

            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, b.ToArray());
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;

            var gdef = new GdefTable(face, tableStart);

            Assert.Equal(0, gdef.GetGlyphClass(10));
            Assert.Equal(0, gdef.GetMarkAttachClass(10));
            Assert.Null(gdef.GetMarkGlyphSet(0));
        }

        /// <summary>Builds a Type 4 (Ligature Substitution) lookup with `lookupFlag`'s IGNORE_MARKS
        /// bit (0x0008) set: coverage {10}, one ligature (component 11 -&gt; ligature glyph 99).
        /// Combined with <see cref="BuildSyntheticGdef"/>'s own GlyphClassDef (glyphs 10-12 -&gt; Base,
        /// 20-22 -&gt; Mark), this lets a test prove <see cref="GsubShaper.ApplyLigatureLookup"/>
        /// actually skips an intervening mark glyph when matching components.</summary>
        private static byte[] BuildLigatureWithIgnoreMarksFlag()
        {
            var b = new SfntByteBuilder();

            b.U16(1); b.U16(0); // majorVersion, minorVersion
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
            b.U16(1); // lookupCount
            int lookup0At = b.PlaceholderU16();

            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            const ushort ignoreMarks = 0x0008;
            b.U16(4); b.U16(ignoreMarks); b.U16(1); // lookupType=4, lookupFlag=IGNORE_MARKS, subtableCount=1
            int subOffsetAt = b.PlaceholderU16();
            int subStart = b.Position;
            b.PatchU16(subOffsetAt, subStart - lookup0Start);

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
            b.U16(99); // ligatureGlyph
            b.U16(2); // componentCount (first glyph + 1 component)
            b.U16(11); // component glyph
            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); b.U16(1); b.U16(10); // coverage format 1, glyph 10

            return b.ToArray();
        }

        [Fact]
        public void ApplyLigatureLookup_IgnoreMarksFlag_SkipsInterveningGdefMarkGlyph()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            byte[] gdefBytes = BuildSyntheticGdef();
            byte[] gsubBytes = BuildLigatureWithIgnoreMarksFlag();

            int gdefStart = fontBytes.Length;
            int gsubStart = gdefStart + gdefBytes.Length;
            byte[] combined = Concat(Concat(fontBytes, gdefBytes), gsubBytes);
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;

            var gdef = new GdefTable(face, gdefStart);
            var gsub = new GsubTable(face, gsubStart);
            var lookup = gsub.GetLigatureLookup(0);
            Assert.NotNull(lookup);

            // Glyph 20 is classified Mark (class 3) by BuildSyntheticGdef's own GlyphClassDef -
            // IGNORE_MARKS must skip it while matching the ligature's component (glyph 11), merging
            // the base (10) and component (11) into the ligature glyph (99) while leaving the mark
            // (20) in the stream, moved to immediately after the new ligature glyph.
            var glyphs = new List<ShapedGlyph> { new(10, 0, 1), new(20, 1, 1), new(11, 2, 1) };
            GsubShaper.ApplyLigatureLookup(lookup, glyphs, gdef);

            Assert.Equal([99, 20], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ApplyLigatureLookup_WithoutIgnoreMarksFlag_MarkBreaksTheMatch()
        {
            // Same font/glyph setup as ApplyLigatureLookup_IgnoreMarksFlag_SkipsInterveningGdefMarkGlyph,
            // but this lookup's own lookupFlag has no mark-skipping bits set - the intervening mark
            // must NOT be skipped, so the component (glyph 11) is never found adjacent and the
            // ligature does not form at all.
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            byte[] gdefBytes = BuildSyntheticGdef();

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
            b.U16(1);
            int lookup0At = b.PlaceholderU16();
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(4); b.U16(0); b.U16(1); // lookupType=4, lookupFlag=0 (no mark filtering)
            int subOffsetAt = b.PlaceholderU16();
            int subStart = b.Position;
            b.PatchU16(subOffsetAt, subStart - lookup0Start);
            int subtableStart = b.Position;
            b.U16(1);
            int coverageOffsetAt = b.PlaceholderU16();
            b.U16(1);
            int ligSetOffsetAt = b.PlaceholderU16();
            int ligSetStart = b.Position;
            b.PatchU16(ligSetOffsetAt, ligSetStart - subtableStart);
            b.U16(1);
            int ligOffsetAt = b.PlaceholderU16();
            int ligStart = b.Position;
            b.PatchU16(ligOffsetAt, ligStart - ligSetStart);
            b.U16(99);
            b.U16(2);
            b.U16(11);
            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); b.U16(1); b.U16(10);
            byte[] gsubBytes = b.ToArray();

            int gdefStart = fontBytes.Length;
            int gsubStart = gdefStart + gdefBytes.Length;
            byte[] combined = Concat(Concat(fontBytes, gdefBytes), gsubBytes);
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;

            var gdef = new GdefTable(face, gdefStart);
            var gsub = new GsubTable(face, gsubStart);
            var lookup = gsub.GetLigatureLookup(0);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(10, 0, 1), new(20, 1, 1), new(11, 2, 1) };
            GsubShaper.ApplyLigatureLookup(lookup, glyphs, gdef);

            Assert.Equal([10, 20, 11], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        /// <summary>
        /// Same rationale as <see cref="GsubTableSyntheticTests.ConcurrentAccess_FromManyThreads_ProducesConsistentResults"/>
        /// (issue #543) - GdefTable instances are cached and shared process-wide identically to
        /// GsubTable/GposTable.
        /// </summary>
        [Fact]
        public void ConcurrentAccess_FromManyThreads_ProducesConsistentResults()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGdef();
            var gdef = new GdefTable(face, tableStart);

            var actions = new Action[]
            {
                () => Assert.Equal(1, gdef.GetGlyphClass(10)),
                () => Assert.Equal(3, gdef.GetGlyphClass(20)),
                () => Assert.Equal(1, gdef.GetMarkAttachClass(20)),
                () => Assert.NotNull(gdef.GetMarkGlyphSet(0)),
            };

            const int repeatsPerAction = 40;
            var work = Enumerable.Range(0, actions.Length * repeatsPerAction)
                .Select(i => actions[i % actions.Length]);

            Parallel.ForEach(work, action => action());
        }
    }
}

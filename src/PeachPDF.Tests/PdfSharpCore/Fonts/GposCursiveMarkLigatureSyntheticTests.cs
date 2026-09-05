using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for <see cref="GposTable"/>'s Lookup Type 3 (Cursive Attachment) reader and
    /// <see cref="GposPositioner.ApplyCursiveAttachment"/> - exercised directly against the parsed
    /// lookup, mirroring <see cref="GposTableSyntheticTests"/>'s own approach.
    ///
    /// Layout: no ScriptList features are exercised here. Lookup 0: Type 3, LTR - coverage {300,301};
    /// glyph 300 exit=(100,10), glyph 301 entry=(20,5). Lookup 1: Type 3, RTL (lookupFlag RIGHT_TO_LEFT
    /// bit set) - coverage {310,311}; glyph 310 exit=(100,10), glyph 311 entry=(20,5) (same anchors as
    /// lookup 0, different glyph ids, to prove the RTL bit alone changes which glyph gets the Y
    /// correction). Lookup 2: Type 3, LTR, 3-glyph cascade - coverage {320,321,322}; glyph 320
    /// exit=(50,30); glyph 321 entry=(10,-5)/exit=(60,40); glyph 322 entry=(15,8). Lookup 3: Type 3,
    /// LTR - coverage {330,331}, neither glyph has any anchor at all.
    /// </summary>
    public class GposCursiveMarkLigatureSyntheticTests
    {
        private static byte[] BuildSyntheticGpos()
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
            b.U16(6); // lookupCount
            int lookup0At = b.PlaceholderU16();
            int lookup1At = b.PlaceholderU16();
            int lookup2At = b.PlaceholderU16();
            int lookup3At = b.PlaceholderU16();
            int lookup4At = b.PlaceholderU16();
            int lookup5At = b.PlaceholderU16();

            // Lookup 0: Type 3, LTR - coverage {300,301}; 300 exit=(100,10); 301 entry=(20,5).
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(3); b.U16(0); b.U16(1); // lookupType=3, lookupFlag=0 (LTR), subtableCount=1
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            WriteCursiveAttachmentSubtable(b, [300, 301],
                [(null, (100, 10)), ((20, 5), null)]);

            // Lookup 1: Type 3, RTL - coverage {310,311}; 310 exit=(100,10); 311 entry=(20,5).
            const ushort rightToLeft = 0x0001;
            int lookup1Start = b.Position;
            b.PatchU16(lookup1At, lookup1Start - lookupListStart);
            b.U16(3); b.U16(rightToLeft); b.U16(1);
            int lookup1SubAt = b.PlaceholderU16();
            int lookup1SubStart = b.Position;
            b.PatchU16(lookup1SubAt, lookup1SubStart - lookup1Start);
            WriteCursiveAttachmentSubtable(b, [310, 311],
                [(null, (100, 10)), ((20, 5), null)]);

            // Lookup 2: Type 3, LTR, 3-glyph cascade - coverage {320,321,322}.
            int lookup2Start = b.Position;
            b.PatchU16(lookup2At, lookup2Start - lookupListStart);
            b.U16(3); b.U16(0); b.U16(1);
            int lookup2SubAt = b.PlaceholderU16();
            int lookup2SubStart = b.Position;
            b.PatchU16(lookup2SubAt, lookup2SubStart - lookup2Start);
            WriteCursiveAttachmentSubtable(b, [320, 321, 322],
                [(null, (50, 30)), ((10, -5), (60, 40)), ((15, 8), null)]);

            // Lookup 3: Type 3, LTR - coverage {330,331}, neither glyph has any anchor.
            int lookup3Start = b.Position;
            b.PatchU16(lookup3At, lookup3Start - lookupListStart);
            b.U16(3); b.U16(0); b.U16(1);
            int lookup3SubAt = b.PlaceholderU16();
            int lookup3SubStart = b.Position;
            b.PatchU16(lookup3SubAt, lookup3SubStart - lookup3Start);
            WriteCursiveAttachmentSubtable(b, [330, 331],
                [(null, null), (null, null)]);

            // Lookup 4: Type 3, unrecognized subtable format (only format 1 is defined).
            int lookup4Start = b.Position;
            b.PatchU16(lookup4At, lookup4Start - lookupListStart);
            b.U16(3); b.U16(0); b.U16(1);
            int lookup4SubAt = b.PlaceholderU16();
            int lookup4SubStart = b.Position;
            b.PatchU16(lookup4SubAt, lookup4SubStart - lookup4Start);
            b.U16(9); // format 9 - unrecognized

            // Lookup 5: Type 5, unrecognized subtable format (only format 1 is defined).
            int lookup5Start = b.Position;
            b.PatchU16(lookup5At, lookup5Start - lookupListStart);
            b.U16(5); b.U16(0); b.U16(1);
            int lookup5SubAt = b.PlaceholderU16();
            int lookup5SubStart = b.Position;
            b.PatchU16(lookup5SubAt, lookup5SubStart - lookup5Start);
            b.U16(9); // format 9 - unrecognized

            return b.ToArray();
        }

        /// <summary>Writes a `CursivePosFormat1` subtable: <paramref name="coverageGlyphs"/> in
        /// coverage order, each paired (by index) with its own (entry, exit) anchor pair - either may
        /// be null.</summary>
        private static void WriteCursiveAttachmentSubtable(SfntByteBuilder b, ushort[] coverageGlyphs,
            ((short X, short Y)? Entry, (short X, short Y)? Exit)[] records)
        {
            int subtableStart = b.Position;
            b.U16(1); // format
            int coverageOffsetAt = b.PlaceholderU16();
            b.U16((ushort)records.Length); // entryExitCount

            var entryOffsetAts = new int[records.Length];
            var exitOffsetAts = new int[records.Length];
            for (int i = 0; i < records.Length; i++)
            {
                entryOffsetAts[i] = b.PlaceholderU16();
                exitOffsetAts[i] = b.PlaceholderU16();
            }

            for (int i = 0; i < records.Length; i++)
            {
                if (records[i].Entry is { } entry)
                {
                    int entryStart = b.Position;
                    b.PatchU16(entryOffsetAts[i], entryStart - subtableStart);
                    b.U16(1); b.S16(entry.X); b.S16(entry.Y); // AnchorFormat1
                }
                else
                {
                    b.PatchU16(entryOffsetAts[i], 0);
                }

                if (records[i].Exit is { } exit)
                {
                    int exitStart = b.Position;
                    b.PatchU16(exitOffsetAts[i], exitStart - subtableStart);
                    b.U16(1); b.S16(exit.X); b.S16(exit.Y); // AnchorFormat1
                }
                else
                {
                    b.PatchU16(exitOffsetAts[i], 0);
                }
            }

            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); b.U16((ushort)coverageGlyphs.Length);
            foreach (ushort glyph in coverageGlyphs)
                b.U16(glyph);
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var combined = new byte[a.Length + b.Length];
            a.CopyTo(combined, 0);
            b.CopyTo(combined, a.Length);
            return combined;
        }

        private static (OpenTypeFontface Face, int TableStart) BuildFaceWithSyntheticGpos()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, BuildSyntheticGpos());
            return (XFontSource.GetOrCreateFrom(combined).Fontface, tableStart);
        }

        private static OpenTypeDescriptor RealDescriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf)).Fontface;
            return new OpenTypeDescriptor("gpos-cursive-test", "gpos-cursive-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Fact]
        public void CursiveAttachment_Ltr_ConnectsExitToEntry_AdjustsAdvanceAndEntryGlyphY()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetCursiveAttachmentLookup(0);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(300, 0, 1), new(301, 1, 1) };
            GposPositioner.ApplyCursiveAttachment(descriptor, lookup, glyphs, gdef: null);

            // Ported from real HarfBuzz's own CursivePosFormat1::apply (see GposPositioner.TryApplyCursivePair's
            // own remarks) - each glyph's own correction depends only on its own anchor and its own
            // prior XOffset, never on the other glyph's position or natural width:
            //   glyphs[0].XAdvanceDelta = glyphs[0].XOffset = -(exit.X + glyphs[0].XOffset_before) = -(100+0).
            Assert.Equal(-100, glyphs[0].XAdvanceDelta);
            Assert.Equal(-100, glyphs[0].XOffset);
            Assert.Equal(0, glyphs[0].YOffset); // LTR: exit glyph's Y is untouched.

            // glyphs[1].YOffset = exit.Y - entry.Y + glyphs[0].YOffset = 10 - 5 + 0.
            Assert.Equal(5, glyphs[1].YOffset);
            // glyphs[1].XAdvanceDelta replaces (not adjusts) its natural advance: entry.X + glyphs[1].XOffset(0) - naturalWidth(301).
            double expectedGlyph1XDelta = 20 - descriptor.GlyphIndexToWidth(301);
            Assert.Equal(expectedGlyph1XDelta, glyphs[1].XAdvanceDelta);
            Assert.Equal(0, glyphs[1].XOffset); // entry glyph's own XOffset is untouched, only its advance.
        }

        [Fact]
        public void CursiveAttachment_Rtl_AdjustsAdvanceAndExitGlyphY()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetCursiveAttachmentLookup(1);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(310, 0, 1), new(311, 1, 1) };
            GposPositioner.ApplyCursiveAttachment(descriptor, lookup, glyphs, gdef: null);

            // The main-direction (X) correction is hardcoded to HarfBuzz's own HB_DIRECTION_RTL branch
            // regardless of this lookup's own RIGHT_TO_LEFT flag (a separate concept - see
            // GposPositioner.ApplyCursiveAttachment's own remarks on why RTL-only is the correct scope
            // here), so it's identical in shape to the LTR-flagged lookup's own case above.
            Assert.Equal(-100, glyphs[0].XAdvanceDelta);
            Assert.Equal(-100, glyphs[0].XOffset);
            double expectedGlyph1XDelta = 20 - descriptor.GlyphIndexToWidth(311);
            Assert.Equal(expectedGlyph1XDelta, glyphs[1].XAdvanceDelta);

            // RTL: Y correction goes on the EXIT glyph (index 0) instead of the entry glyph.
            Assert.Equal(-5, glyphs[0].YOffset); // entry.Y - exit.Y + glyphs[1].YOffset = 5 - 10 + 0.
            Assert.Equal(0, glyphs[1].YOffset);
        }

        [Fact]
        public void CursiveAttachment_Ltr_ThreeGlyphChain_CascadesEarlierCorrectionIntoLaterPair()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetCursiveAttachmentLookup(2);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(320, 0, 1), new(321, 1, 1), new(322, 2, 1) };
            GposPositioner.ApplyCursiveAttachment(descriptor, lookup, glyphs, gdef: null);

            // Pair (0,1): glyph321.YOffset = exit320.Y(30) - entry321.Y(-5) + glyph320.YOffset(0) = 35.
            Assert.Equal(35, glyphs[1].YOffset);

            // Pair (1,2), processed after pair (0,1) in the LTR (ascending) walk, must use glyph321's
            // ALREADY-UPDATED YOffset (35) as its own reference point - proving the cascade actually
            // threads through, not just each pair computed in isolation off zeroed offsets.
            // glyph322.YOffset = exit321.Y(40) - entry322.Y(8) + glyph321.YOffset(35) = 67.
            Assert.Equal(67, glyphs[2].YOffset);
        }

        [Fact]
        public void CursiveAttachment_NeitherGlyphHasAnAnchor_NoAdjustment()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetCursiveAttachmentLookup(3);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(330, 0, 1), new(331, 1, 1) };
            GposPositioner.ApplyCursiveAttachment(descriptor, lookup, glyphs, gdef: null);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
            Assert.Equal(0, glyphs[0].YOffset);
            Assert.Equal(0, glyphs[1].XAdvanceDelta);
            Assert.Equal(0, glyphs[1].YOffset);
        }

        [Fact]
        public void MismatchedLookupTypeAccessors_ReturnNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            Assert.Null(gpos.GetSingleAdjustmentLookup(0)); // lookup 0 is Type 3
            Assert.Null(gpos.GetMarkToBaseLookup(1)); // lookup 1 is Type 3
            Assert.Null(gpos.GetMarkToLigatureLookup(0)); // lookup 0 is Type 3
        }

        [Fact]
        public void UnrecognizedSubtableFormat_CursiveAttachment_AccessorReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            Assert.Null(gpos.GetCursiveAttachmentLookup(4));
        }

        [Fact]
        public void UnrecognizedSubtableFormat_MarkToLigature_AccessorReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            Assert.Null(gpos.GetMarkToLigatureLookup(5));
        }

        [Fact]
        public void OutOfRangeLookupListIndex_MarkToLigature_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            Assert.Null(gpos.GetMarkToLigatureLookup(-1));
            Assert.Null(gpos.GetMarkToLigatureLookup(999));
        }

        // ---- Mark-to-Ligature (Type 5) end-to-end: GSUB ligature merge feeding GPOS component
        // resolution - see MarkToLigature_EndToEnd_EachMarkResolvesItsOwnLigatureComponent below.

        private static byte[] Concat3(byte[] a, byte[] b, byte[] c) => Concat(Concat(a, b), c);

        /// <summary>GDEF classifying glyphs 460/461 as Mark (class 3) - the two marks in the
        /// mark-to-ligature end-to-end test below.</summary>
        private static byte[] BuildMarkToLigatureGdef()
        {
            var b = new SfntByteBuilder();
            b.U16(1); b.U16(0); // 1.0
            int glyphClassDefOffsetAt = b.PlaceholderU16();
            b.U16(0); b.U16(0); b.U16(0); // attachList, ligCaretList, markAttachClassDef - all null
            int glyphClassDefStart = b.Position;
            b.PatchU16(glyphClassDefOffsetAt, glyphClassDefStart);
            b.U16(2); // ClassDef format 2
            b.U16(1); // classRangeCount
            b.U16(460); b.U16(461); b.U16(3); // glyphs 460-461 -> class 3 (Mark)
            return b.ToArray();
        }

        /// <summary>A single Type 4 (Ligature) lookup: coverage {400}, IGNORE_MARKS set, component
        /// glyph 401 -&gt; ligature glyph 450.</summary>
        private static byte[] BuildMarkToLigatureGsub()
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
            const ushort ignoreMarks = 0x0008;
            b.U16(4); b.U16(ignoreMarks); b.U16(1); // lookupType=4, IGNORE_MARKS, subtableCount=1
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
            b.U16(450); // ligatureGlyph
            b.U16(2); // componentCount (first glyph + 1 component)
            b.U16(401); // component glyph
            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); b.U16(1); b.U16(400);

            return b.ToArray();
        }

        /// <summary>A single Type 5 (MarkToLigature) lookup, IGNORE_MARKS set: mark coverage
        /// {460 -&gt; class 0, 461 -&gt; class 1}, ligature coverage {450}, ligature 450 has 2
        /// components - component 0 has a class-0 anchor (10,20) only, component 1 has a class-1
        /// anchor (30,40) only.</summary>
        private static byte[] BuildMarkToLigatureGpos()
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
            const ushort ignoreMarks = 0x0008;
            b.U16(5); b.U16(ignoreMarks); b.U16(1); // lookupType=5, IGNORE_MARKS, subtableCount=1
            int subOffsetAt = b.PlaceholderU16();
            int subStart = b.Position;
            b.PatchU16(subOffsetAt, subStart - lookup0Start);

            int subtableStart = b.Position;
            b.U16(1); // format
            int markCoverageOffsetAt = b.PlaceholderU16();
            int ligCoverageOffsetAt = b.PlaceholderU16();
            b.U16(2); // markClassCount
            int markArrayOffsetAt = b.PlaceholderU16();
            int ligArrayOffsetAt = b.PlaceholderU16();

            // MarkArray: mark 460 -> class 0, mark 461 -> class 1, each anchored at its own origin (0,0).
            int markArrayStart = b.Position;
            b.PatchU16(markArrayOffsetAt, markArrayStart - subtableStart);
            b.U16(2); // markCount
            b.U16(0); // mark[0].class
            int mark0AnchorOffsetAt = b.PlaceholderU16();
            b.U16(1); // mark[1].class
            int mark1AnchorOffsetAt = b.PlaceholderU16();
            int mark0AnchorStart = b.Position;
            b.PatchU16(mark0AnchorOffsetAt, mark0AnchorStart - markArrayStart);
            b.U16(1); b.S16(0); b.S16(0); // AnchorFormat1 (0,0)
            int mark1AnchorStart = b.Position;
            b.PatchU16(mark1AnchorOffsetAt, mark1AnchorStart - markArrayStart);
            b.U16(1); b.S16(0); b.S16(0); // AnchorFormat1 (0,0)

            // LigatureArray: one ligature (450), 2 components.
            int ligArrayStart = b.Position;
            b.PatchU16(ligArrayOffsetAt, ligArrayStart - subtableStart);
            b.U16(1); // ligatureCount
            int ligAttachOffsetAt = b.PlaceholderU16();
            int ligAttachStart = b.Position;
            b.PatchU16(ligAttachOffsetAt, ligAttachStart - ligArrayStart);
            b.U16(2); // componentCount
            int comp0Class0OffsetAt = b.PlaceholderU16();
            int comp0Class1OffsetAt = b.PlaceholderU16();
            int comp1Class0OffsetAt = b.PlaceholderU16();
            int comp1Class1OffsetAt = b.PlaceholderU16();
            int comp0Class0Start = b.Position;
            b.PatchU16(comp0Class0OffsetAt, comp0Class0Start - ligAttachStart);
            b.U16(1); b.S16(10); b.S16(20); // component 0, class 0 anchor (10,20)
            b.PatchU16(comp0Class1OffsetAt, 0); // component 0, class 1 - none
            int comp1Class1Start = b.Position;
            b.PatchU16(comp1Class1OffsetAt, comp1Class1Start - ligAttachStart);
            b.U16(1); b.S16(30); b.S16(40); // component 1, class 1 anchor (30,40)
            b.PatchU16(comp1Class0OffsetAt, 0); // component 1, class 0 - none

            int markCoverageStart = b.Position;
            b.PatchU16(markCoverageOffsetAt, markCoverageStart - subtableStart);
            b.U16(1); b.U16(2); b.U16(460); b.U16(461);

            int ligCoverageStart = b.Position;
            b.PatchU16(ligCoverageOffsetAt, ligCoverageStart - subtableStart);
            b.U16(1); b.U16(1); b.U16(450);

            return b.ToArray();
        }

        [Fact]
        public void MarkToLigature_EndToEnd_EachMarkResolvesItsOwnLigatureComponent()
        {
            var descriptor = RealDescriptor();
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            byte[] gdefBytes = BuildMarkToLigatureGdef();
            byte[] gsubBytes = BuildMarkToLigatureGsub();
            byte[] gposBytes = BuildMarkToLigatureGpos();

            int gdefStart = fontBytes.Length;
            int gsubStart = gdefStart + gdefBytes.Length;
            int gposStart = gsubStart + gsubBytes.Length;
            byte[] combined = Concat(Concat3(fontBytes, gdefBytes, gsubBytes), gposBytes);
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;

            var gdef = new GdefTable(face, gdefStart);
            var gsub = new GsubTable(face, gsubStart);
            var gpos = new GposTable(face, gposStart);
            var ligLookup = gsub.GetLigatureLookup(0);
            var markLookup = gpos.GetMarkToLigatureLookup(0);
            Assert.NotNull(ligLookup);
            Assert.NotNull(markLookup);

            // Logical order: comp0(400) mark0(460, belongs to comp0) comp1(401) mark1(461, belongs to comp1).
            var glyphs = new List<ShapedGlyph>
            {
                new(400, 0, 1),
                new(460, 1, 1),
                new(401, 2, 1),
                new(461, 3, 1),
            };
            GsubShaper.ApplyLigatureLookup(ligLookup, glyphs, gdef);

            // GSUB merges comp0+comp1 (skipping mark0 via IGNORE_MARKS) into one ligature glyph,
            // reinserting mark0 immediately after it; mark1 is untouched, already following the match.
            Assert.Equal([450, 460, 461], glyphs.ConvertAll(g => g.GlyphIndex));
            Assert.NotNull(glyphs[0].LigatureComponentClusterStarts);
            Assert.Equal([0, 2], glyphs[0].LigatureComponentClusterStarts!);

            GposPositioner.ApplyMarkToLigature(descriptor, markLookup, glyphs, gdef);

            // mark0 (class 0) must resolve to component 0's anchor (10,20), not component 1's.
            double ligWidth = descriptor.GlyphIndexToWidth(450);
            double expectedMark0X = 10 - 0 - ligWidth; // baseAnchor.X - markAnchor.X - intermediateAdvance
            Assert.Equal(expectedMark0X, glyphs[1].XOffset);
            Assert.Equal(20, glyphs[1].YOffset);

            // mark1 (class 1) must resolve to component 1's anchor (30,40), not component 0's -
            // proving ResolveLigatureComponent actually distinguishes the two marks by cluster start
            // rather than both collapsing onto the same (e.g. first) component.
            double mark0Width = descriptor.GlyphIndexToWidth(460);
            double expectedMark1X = 30 - 0 - (ligWidth + mark0Width + glyphs[1].XAdvanceDelta);
            Assert.Equal(expectedMark1X, glyphs[2].XOffset);
            Assert.Equal(40, glyphs[2].YOffset);
        }

        [Fact]
        public void OutOfRangeLookupListIndex_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            Assert.Null(gpos.GetCursiveAttachmentLookup(-1));
            Assert.Null(gpos.GetCursiveAttachmentLookup(999));
        }

        /// <summary>
        /// Same rationale as <see cref="GsubTableSyntheticTests.ConcurrentAccess_FromManyThreads_ProducesConsistentResults"/>
        /// (issue #543) - GposTable instances are cached and shared process-wide.
        /// </summary>
        [Fact]
        public void ConcurrentAccess_FromManyThreads_ProducesConsistentResults()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            var actions = new Action[]
            {
                () => Assert.NotNull(gpos.GetCursiveAttachmentLookup(0)),
                () => Assert.NotNull(gpos.GetCursiveAttachmentLookup(1)),
                () => Assert.NotNull(gpos.GetCursiveAttachmentLookup(2)),
                () => Assert.Null(gpos.GetSingleAdjustmentLookup(0)),
            };

            const int repeatsPerAction = 40;
            var work = Enumerable.Range(0, actions.Length * repeatsPerAction)
                .Select(i => actions[i % actions.Length]);

            Parallel.ForEach(work, action => action());
        }
    }
}

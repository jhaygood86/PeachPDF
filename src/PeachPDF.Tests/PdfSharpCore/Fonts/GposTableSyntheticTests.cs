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
    /// Coverage for <see cref="GposTable"/> (Lookup Types 1/2/4/6, Type 9 Extension unwrap) and
    /// <see cref="GposPositioner"/>'s corresponding application logic (exercised directly against the
    /// parsed lookups, mirroring <see cref="GsubMultipleAndContextualSyntheticTests"/>'s approach -
    /// see <see cref="GposPositioner.ApplySingleAdjustment"/>'s own internal-visibility rationale).
    ///
    /// Layout: no ScriptList features are exercised here (every test reaches a lookup by index
    /// directly). Lookups: 0 = Type 1 format 1 (glyph 10, XAdvance +100); 1 = Type 1 format 2 (glyphs
    /// 20/21, XPlacement 5/7); 2 = Type 2 format 1 (pair 30+31, XAdvance -20 on the first glyph);
    /// 3 = Type 2 format 2 (class pair: glyph 40 class 1 x glyph 50 class 1, XAdvance -15);
    /// 4 = Type 4 MarkToBase (mark 60 anchored to base 61); 5 = Type 6 MarkToMark (mark 70 anchored to
    /// mark2 71); 6 = Type 9 (Extension) wrapping a valid Type 1 (glyph 80, XAdvance +33).
    /// </summary>
    public class GposTableSyntheticTests
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
            b.U16(7); // lookupCount
            int lookup0At = b.PlaceholderU16();
            int lookup1At = b.PlaceholderU16();
            int lookup2At = b.PlaceholderU16();
            int lookup3At = b.PlaceholderU16();
            int lookup4At = b.PlaceholderU16();
            int lookup5At = b.PlaceholderU16();
            int lookup6At = b.PlaceholderU16();

            // Lookup 0: Type 1 format 1 - glyph 10, XAdvance +100.
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); // valueFormat: XAdvance
                b.S16(100);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(10);
            }

            // Lookup 1: Type 1 format 2 - glyphs 20/21, XPlacement 5/7.
            int lookup1Start = b.Position;
            b.PatchU16(lookup1At, lookup1Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup1SubAt = b.PlaceholderU16();
            int lookup1SubStart = b.Position;
            b.PatchU16(lookup1SubAt, lookup1SubStart - lookup1Start);
            {
                int subtableStart = b.Position;
                b.U16(2); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0001); // valueFormat: XPlacement
                b.U16(2); // valueCount
                b.S16(5);
                b.S16(7);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(2); b.U16(20); b.U16(21);
            }

            // Lookup 2: Type 2 format 1 - pair (30, 31), XAdvance -20 on the first glyph.
            int lookup2Start = b.Position;
            b.PatchU16(lookup2At, lookup2Start - lookupListStart);
            b.U16(2); b.U16(0); b.U16(1);
            int lookup2SubAt = b.PlaceholderU16();
            int lookup2SubStart = b.Position;
            b.PatchU16(lookup2SubAt, lookup2SubStart - lookup2Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); // valueFormat1: XAdvance
                b.U16(0); // valueFormat2: none
                b.U16(1); // pairSetCount
                int pairSetOffsetAt = b.PlaceholderU16();
                int pairSetStart = b.Position;
                b.PatchU16(pairSetOffsetAt, pairSetStart - subtableStart);
                b.U16(1); // pairValueCount
                b.U16(31); // secondGlyph
                b.S16(-20); // valueRecord1 (XAdvance)
                // valueRecord2 is empty (valueFormat2 = 0, zero fields).
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(30);
            }

            // Lookup 3: Type 2 format 2 - glyph 40 (class 1) x glyph 50 (class 1) -> XAdvance -15.
            int lookup3Start = b.Position;
            b.PatchU16(lookup3At, lookup3Start - lookupListStart);
            b.U16(2); b.U16(0); b.U16(1);
            int lookup3SubAt = b.PlaceholderU16();
            int lookup3SubStart = b.Position;
            b.PatchU16(lookup3SubAt, lookup3SubStart - lookup3Start);
            {
                int subtableStart = b.Position;
                b.U16(2); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); // valueFormat1: XAdvance
                b.U16(0); // valueFormat2: none
                int classDef1OffsetAt = b.PlaceholderU16();
                int classDef2OffsetAt = b.PlaceholderU16();
                b.U16(2); // class1Count (0, 1)
                b.U16(2); // class2Count (0, 1)
                // Flat class1Count x class2Count array: (0,0) (0,1) (1,0) (1,1).
                b.S16(0); // (0,0)
                b.S16(0); // (0,1)
                b.S16(0); // (1,0)
                b.S16(-15); // (1,1) - our (class1=1, class2=1) entry
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(40);
                int classDef1Start = b.Position;
                b.PatchU16(classDef1OffsetAt, classDef1Start - subtableStart);
                b.U16(1); b.U16(40); b.U16(1); b.U16(1); // format1: glyph 40 -> class 1
                int classDef2Start = b.Position;
                b.PatchU16(classDef2OffsetAt, classDef2Start - subtableStart);
                b.U16(1); b.U16(50); b.U16(1); b.U16(1); // format1: glyph 50 -> class 1
            }

            // Lookup 4: Type 4 MarkToBase - mark 60 (anchor 50,200) attaches to base 61 (anchor 60,0).
            int lookup4Start = b.Position;
            b.PatchU16(lookup4At, lookup4Start - lookupListStart);
            b.U16(4); b.U16(0); b.U16(1);
            int lookup4SubAt = b.PlaceholderU16();
            int lookup4SubStart = b.Position;
            b.PatchU16(lookup4SubAt, lookup4SubStart - lookup4Start);
            WriteMarkAttachmentSubtable(b,
                markCoverageGlyph: 60, baseCoverageGlyph: 61,
                markAnchorX: 50, markAnchorY: 200,
                baseAnchorX: 60, baseAnchorY: 0);

            // Lookup 5: Type 6 MarkToMark - mark 70 (anchor 10,100) attaches to mark2 71 (anchor 15,50).
            int lookup5Start = b.Position;
            b.PatchU16(lookup5At, lookup5Start - lookupListStart);
            b.U16(6); b.U16(0); b.U16(1);
            int lookup5SubAt = b.PlaceholderU16();
            int lookup5SubStart = b.Position;
            b.PatchU16(lookup5SubAt, lookup5SubStart - lookup5Start);
            WriteMarkAttachmentSubtable(b,
                markCoverageGlyph: 70, baseCoverageGlyph: 71,
                markAnchorX: 10, markAnchorY: 100,
                baseAnchorX: 15, baseAnchorY: 50);

            // Lookup 6: Type 9 (Extension) wrapping a valid Type 1 - glyph 80, XAdvance +33.
            int lookup6Start = b.Position;
            b.PatchU16(lookup6At, lookup6Start - lookupListStart);
            b.U16(9); b.U16(0); b.U16(1);
            int lookup6SubAt = b.PlaceholderU16();
            int lookup6SubStart = b.Position;
            b.PatchU16(lookup6SubAt, lookup6SubStart - lookup6Start);
            b.U16(1); // posFormat (extension, always 1)
            b.U16(1); // extensionLookupType = 1
            int extOffsetAt = b.Position;
            b.U32(0);
            int extTargetStart = b.Position;
            b.PatchU32(extOffsetAt, (uint)(extTargetStart - lookup6SubStart));
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); // valueFormat: XAdvance
                b.S16(33);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(80);
            }

            return b.ToArray();
        }

        private static void WriteMarkAttachmentSubtable(SfntByteBuilder b, ushort markCoverageGlyph, ushort baseCoverageGlyph,
            short markAnchorX, short markAnchorY, short baseAnchorX, short baseAnchorY)
        {
            int subtableStart = b.Position;
            b.U16(1); // format
            int markCoverageOffsetAt = b.PlaceholderU16();
            int baseCoverageOffsetAt = b.PlaceholderU16();
            b.U16(1); // markClassCount
            int markArrayOffsetAt = b.PlaceholderU16();
            int baseArrayOffsetAt = b.PlaceholderU16();

            int markArrayStart = b.Position;
            b.PatchU16(markArrayOffsetAt, markArrayStart - subtableStart);
            b.U16(1); // markCount
            b.U16(0); // markClass
            int markAnchorOffsetAt = b.PlaceholderU16();
            int markAnchorStart = b.Position;
            b.PatchU16(markAnchorOffsetAt, markAnchorStart - markArrayStart);
            b.U16(1); b.S16(markAnchorX); b.S16(markAnchorY); // AnchorFormat1

            int baseArrayStart = b.Position;
            b.PatchU16(baseArrayOffsetAt, baseArrayStart - subtableStart);
            b.U16(1); // baseCount
            int baseAnchorOffsetAt = b.PlaceholderU16(); // anchorOffsets[markClassCount=1]
            int baseAnchorStart = b.Position;
            b.PatchU16(baseAnchorOffsetAt, baseAnchorStart - baseArrayStart);
            b.U16(1); b.S16(baseAnchorX); b.S16(baseAnchorY); // AnchorFormat1

            int markCoverageStart = b.Position;
            b.PatchU16(markCoverageOffsetAt, markCoverageStart - subtableStart);
            b.U16(1); b.U16(1); b.U16(markCoverageGlyph);

            int baseCoverageStart = b.Position;
            b.PatchU16(baseCoverageOffsetAt, baseCoverageStart - subtableStart);
            b.U16(1); b.U16(1); b.U16(baseCoverageGlyph);
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
            return new OpenTypeDescriptor("gpos-test", "gpos-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Fact]
        public void SingleAdjustmentFormat1_AppliesSharedValueToEveryCoveredGlyph()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetSingleAdjustmentLookup(0);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(10, 0, 1) };
            GposPositioner.ApplySingleAdjustment(lookup, glyphs);

            Assert.Equal(100, glyphs[0].XAdvanceDelta);
        }

        [Fact]
        public void SingleAdjustmentFormat2_AppliesPerGlyphValue()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetSingleAdjustmentLookup(1);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(20, 0, 1), new(21, 1, 1) };
            GposPositioner.ApplySingleAdjustment(lookup, glyphs);

            Assert.Equal(5, glyphs[0].XOffset);
            Assert.Equal(7, glyphs[1].XOffset);
        }

        [Fact]
        public void PairAdjustmentFormat1_ExactPair_AppliesKerning()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetPairAdjustmentLookup(2);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(30, 0, 1), new(31, 1, 1) };
            GposPositioner.ApplyPairAdjustment(lookup, glyphs);

            Assert.Equal(-20, glyphs[0].XAdvanceDelta);
            Assert.Equal(0, glyphs[1].XAdvanceDelta);
        }

        [Fact]
        public void PairAdjustmentFormat1_NonMatchingSecondGlyph_NoAdjustment()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetPairAdjustmentLookup(2);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(30, 0, 1), new(999, 1, 1) };
            GposPositioner.ApplyPairAdjustment(lookup, glyphs);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
        }

        [Fact]
        public void PairAdjustmentFormat2_ClassPair_AppliesKerning()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetPairAdjustmentLookup(3);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(40, 0, 1), new(50, 1, 1) };
            GposPositioner.ApplyPairAdjustment(lookup, glyphs);

            Assert.Equal(-15, glyphs[0].XAdvanceDelta);
        }

        [Fact]
        public void MarkToBase_PositionsMarkRelativeToBaseAnchor()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetMarkToBaseLookup(4);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(61, 0, 1), new(60, 1, 1) }; // base, then mark
            GposPositioner.ApplyMarkToBase(descriptor, lookup, glyphs, gdef: null);

            // mark.XOffset = baseAnchor.X - markAnchor.X - intermediateAdvance(base's own natural
            // width + delta) + base.XOffset = 60 - 50 - descriptor.GlyphIndexToWidth(61) + 0.
            double expectedX = 60 - 50 - descriptor.GlyphIndexToWidth(61);
            Assert.Equal(expectedX, glyphs[1].XOffset);
            Assert.Equal(0 - 200, glyphs[1].YOffset); // baseAnchor.Y(0) - markAnchor.Y(200)
            // The base glyph itself is never repositioned by mark attachment.
            Assert.Equal(0, glyphs[0].XOffset);
            Assert.Equal(0, glyphs[0].YOffset);
        }

        [Fact]
        public void MarkToBase_UncoveredMark_NotRepositioned()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetMarkToBaseLookup(4);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(61, 0, 1), new(999, 1, 1) };
            GposPositioner.ApplyMarkToBase(descriptor, lookup, glyphs, gdef: null);

            Assert.Equal(0, glyphs[1].XOffset);
            Assert.Equal(0, glyphs[1].YOffset);
        }

        [Fact]
        public void MarkToMark_PositionsMarkRelativeToPrecedingMarkAnchor()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetMarkToMarkLookup(5);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(71, 0, 1), new(70, 1, 1) }; // mark2, then mark
            GposPositioner.ApplyMarkToMark(descriptor, lookup, glyphs);

            double expectedX = 15 - 10 - descriptor.GlyphIndexToWidth(71);
            Assert.Equal(expectedX, glyphs[1].XOffset);
            Assert.Equal(50 - 100, glyphs[1].YOffset);
        }

        [Fact]
        public void GetResolvedLookupType_UnwrapsType9Extension()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            Assert.Equal(1, gpos.GetResolvedLookupType(6));
        }

        [Fact]
        public void ExtensionPositioning_WrappingType1_Unwraps()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetSingleAdjustmentLookup(6);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(80, 0, 1) };
            GposPositioner.ApplySingleAdjustment(lookup, glyphs);

            Assert.Equal(33, glyphs[0].XAdvanceDelta);
        }

        [Fact]
        public void MismatchedLookupTypeAccessors_ReturnNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            Assert.Null(gpos.GetPairAdjustmentLookup(0)); // lookup 0 is Type 1
            Assert.Null(gpos.GetSingleAdjustmentLookup(2)); // lookup 2 is Type 2
            Assert.Null(gpos.GetMarkToMarkLookup(4)); // lookup 4 is Type 4
            Assert.Null(gpos.GetMarkToBaseLookup(5)); // lookup 5 is Type 6
        }

        [Fact]
        public void OutOfRangeLookupListIndex_ReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            Assert.Null(gpos.GetSingleAdjustmentLookup(-1));
            Assert.Null(gpos.GetSingleAdjustmentLookup(999));
            Assert.Equal(-1, gpos.GetResolvedLookupType(999));
        }

        /// <summary>
        /// Same rationale as <see cref="GsubTableSyntheticTests.ConcurrentAccess_FromManyThreads_ProducesConsistentResults"/>
        /// (issue #543) - GposTable instances are cached and shared process-wide identically to
        /// GsubTable, and lock on the same shared <see cref="OpenTypeFontface"/> instance.
        /// </summary>
        [Fact]
        public void ConcurrentAccess_FromManyThreads_ProducesConsistentResults()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            var actions = new Action[]
            {
                () => Assert.NotNull(gpos.GetSingleAdjustmentLookup(0)),
                () => Assert.NotNull(gpos.GetPairAdjustmentLookup(2)),
                () => Assert.NotNull(gpos.GetMarkToBaseLookup(4)),
                () => Assert.NotNull(gpos.GetMarkToMarkLookup(5)),
                () => Assert.Equal(1, gpos.GetResolvedLookupType(6)),
                () => Assert.Null(gpos.GetPairAdjustmentLookup(0)),
            };

            const int repeatsPerAction = 40;
            var work = Enumerable.Range(0, actions.Length * repeatsPerAction)
                .Select(i => actions[i % actions.Length]);

            Parallel.ForEach(work, action => action());
        }
    }
}

using System.Collections.Generic;
using System.IO;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for <see cref="GposPositioner.Apply"/>'s own lookup-type dispatch switch (cases 3, 5,
    /// 7, 8) - every other synthetic test in this project calls the underlying <c>ApplyXxx</c> method
    /// directly, bypassing <c>Apply</c>'s own feature-tag-driven activation entirely. This is the one
    /// test that builds a real (if minimal) ScriptList/FeatureList so <c>Apply</c> itself resolves and
    /// dispatches each of these lookup types via <see cref="GposTable.GetActiveLookupIndices"/>,
    /// exactly like a real shaping call would.
    ///
    /// Layout: script "latn", one LangSys with two features - "mark" -&gt; lookup 0 (Type 5), "kern" -&gt;
    /// lookups 1 (Type 3), 2 (Type 7), 4 (Type 8) (tagging cursive/contextual lookups under "kern" is
    /// a test-only convenience - GposPositioner.Apply doesn't validate tag-vs-lookup-type correspondence,
    /// it only dispatches by each lookup's own resolved type). Lookups 3 and 5 are plain Type 1 nested
    /// targets for 2 and 4 respectively, never activated by a feature tag directly.
    /// </summary>
    public class GposApplyDispatchSyntheticTests
    {
        private static byte[] BuildSyntheticGpos()
        {
            var b = new SfntByteBuilder();

            b.U16(1); b.U16(0);
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();

            // ---- ScriptList: one script "latn", one (default) LangSys activating both features. ----
            int scriptListStart = b.Position;
            b.PatchU16(scriptListOffsetAt, scriptListStart);
            b.U16(1); // scriptCount
            b.Tag("latn");
            int scriptOffsetAt = b.PlaceholderU16();

            int scriptStart = b.Position;
            b.PatchU16(scriptOffsetAt, scriptStart - scriptListStart);
            int defaultLangSysOffsetAt = b.PlaceholderU16();

            int langSysStart = b.Position;
            b.PatchU16(defaultLangSysOffsetAt, langSysStart - scriptStart);
            b.U16(0); // lookupOrder (reserved)
            b.U16(0xFFFF); // requiredFeatureIndex - none
            b.U16(2); // featureIndexCount
            b.U16(0); b.U16(1); // featureIndices [0 ("mark"), 1 ("kern")]

            // ---- FeatureList ----
            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(2); // featureCount
            b.Tag("mark");
            int feature0OffsetAt = b.PlaceholderU16();
            b.Tag("kern");
            int feature1OffsetAt = b.PlaceholderU16();

            int feature0Start = b.Position;
            b.PatchU16(feature0OffsetAt, feature0Start - featureListStart);
            b.U16(0); // featureParams - none
            b.U16(1); // lookupIndexCount
            b.U16(0); // lookup 0 (Type 5)

            int feature1Start = b.Position;
            b.PatchU16(feature1OffsetAt, feature1Start - featureListStart);
            b.U16(0); // featureParams - none
            b.U16(3); // lookupIndexCount
            b.U16(1); b.U16(2); b.U16(4); // lookups 1 (Type 3), 2 (Type 7), 4 (Type 8)

            // ---- LookupList ----
            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(6); // lookupCount
            int lookup0At = b.PlaceholderU16();
            int lookup1At = b.PlaceholderU16();
            int lookup2At = b.PlaceholderU16();
            int lookup3At = b.PlaceholderU16();
            int lookup4At = b.PlaceholderU16();
            int lookup5At = b.PlaceholderU16();

            // Lookup 0: Type 5 (MarkToLigature) - mark {50, class 0}; ligature {40}, 1 component,
            // class-0 anchor (5,5); mark's own anchor (0,0).
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(5); b.U16(0); b.U16(1);
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int markCoverageOffsetAt = b.PlaceholderU16();
                int ligCoverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // markClassCount
                int markArrayOffsetAt = b.PlaceholderU16();
                int ligArrayOffsetAt = b.PlaceholderU16();

                int markArrayStart = b.Position;
                b.PatchU16(markArrayOffsetAt, markArrayStart - subtableStart);
                b.U16(1); // markCount
                b.U16(0); // mark[0].class
                int markAnchorOffsetAt = b.PlaceholderU16();
                int markAnchorStart = b.Position;
                b.PatchU16(markAnchorOffsetAt, markAnchorStart - markArrayStart);
                b.U16(1); b.S16(0); b.S16(0);

                int ligArrayStart = b.Position;
                b.PatchU16(ligArrayOffsetAt, ligArrayStart - subtableStart);
                b.U16(1); // ligatureCount
                int ligAttachOffsetAt = b.PlaceholderU16();
                int ligAttachStart = b.Position;
                b.PatchU16(ligAttachOffsetAt, ligAttachStart - ligArrayStart);
                b.U16(1); // componentCount
                int comp0AnchorOffsetAt = b.PlaceholderU16();
                int comp0AnchorStart = b.Position;
                b.PatchU16(comp0AnchorOffsetAt, comp0AnchorStart - ligAttachStart);
                b.U16(1); b.S16(5); b.S16(5);

                int markCoverageStart = b.Position;
                b.PatchU16(markCoverageOffsetAt, markCoverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(50);
                int ligCoverageStart = b.Position;
                b.PatchU16(ligCoverageOffsetAt, ligCoverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(40);
            }

            // Lookup 1: Type 3 (Cursive) - 60 exit=(10,0); 61 entry=(2,0).
            int lookup1Start = b.Position;
            b.PatchU16(lookup1At, lookup1Start - lookupListStart);
            b.U16(3); b.U16(0); b.U16(1);
            int lookup1SubAt = b.PlaceholderU16();
            int lookup1SubStart = b.Position;
            b.PatchU16(lookup1SubAt, lookup1SubStart - lookup1Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(2); // entryExitCount
                int entry0At = b.PlaceholderU16();
                int exit0At = b.PlaceholderU16();
                int entry1At = b.PlaceholderU16();
                int exit1At = b.PlaceholderU16();
                b.PatchU16(entry0At, 0); // glyph 60: no entry
                int exit0Start = b.Position;
                b.PatchU16(exit0At, exit0Start - subtableStart);
                b.U16(1); b.S16(10); b.S16(0);
                int entry1Start = b.Position;
                b.PatchU16(entry1At, entry1Start - subtableStart);
                b.U16(1); b.S16(2); b.S16(0);
                b.PatchU16(exit1At, 0); // glyph 61: no exit
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(2); b.U16(60); b.U16(61);
            }

            // Lookup 2: Type 7 format 3 - input=[Coverage{70}, Coverage{71}], seqLookupRecords=[(1,3)].
            int lookup2Start = b.Position;
            b.PatchU16(lookup2At, lookup2Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(1);
            int lookup2SubAt = b.PlaceholderU16();
            int lookup2SubStart = b.Position;
            b.PatchU16(lookup2SubAt, lookup2SubStart - lookup2Start);
            {
                int subtableStart = b.Position;
                b.U16(3); // format 3
                b.U16(2); // glyphCount
                b.U16(1); // seqLookupCount
                int cov0At = b.PlaceholderU16();
                int cov1At = b.PlaceholderU16();
                b.U16(1); b.U16(3); // sequenceIndex=1, lookupListIndex=3
                int cov0Start = b.Position;
                b.PatchU16(cov0At, cov0Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(70);
                int cov1Start = b.Position;
                b.PatchU16(cov1At, cov1Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(71);
            }

            // Lookup 3: Type 1 - glyph 71, XAdvance +9. Nested target for lookup 2.
            int lookup3Start = b.Position;
            b.PatchU16(lookup3At, lookup3Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup3SubAt = b.PlaceholderU16();
            int lookup3SubStart = b.Position;
            b.PatchU16(lookup3SubAt, lookup3SubStart - lookup3Start);
            {
                int subtableStart = b.Position;
                b.U16(1);
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); b.S16(9);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(71);
            }

            // Lookup 4: Type 8 format 1 - coverage {81}; rule backtrack=[80], lookahead=[82],
            // seqLookupRecords=[(0,5)].
            int lookup4Start = b.Position;
            b.PatchU16(lookup4At, lookup4Start - lookupListStart);
            b.U16(8); b.U16(0); b.U16(1);
            int lookup4SubAt = b.PlaceholderU16();
            int lookup4SubStart = b.Position;
            b.PatchU16(lookup4SubAt, lookup4SubStart - lookup4Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format 1
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // ruleSetCount
                int ruleSetOffsetAt = b.PlaceholderU16();
                int ruleSetStart = b.Position;
                b.PatchU16(ruleSetOffsetAt, ruleSetStart - subtableStart);
                b.U16(1); // ruleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSetStart);
                b.U16(1); b.U16(80); // backtrackGlyphCount=1, backtrack=[80]
                b.U16(1); // inputGlyphCount=1 (position 0 only)
                b.U16(1); b.U16(82); // lookaheadGlyphCount=1, lookahead=[82]
                b.U16(1); // seqLookupCount
                b.U16(0); b.U16(5); // sequenceIndex=0, lookupListIndex=5
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(81);
            }

            // Lookup 5: Type 1 - glyph 81, XAdvance +13. Nested target for lookup 4.
            int lookup5Start = b.Position;
            b.PatchU16(lookup5At, lookup5Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup5SubAt = b.PlaceholderU16();
            int lookup5SubStart = b.Position;
            b.PatchU16(lookup5SubAt, lookup5SubStart - lookup5Start);
            {
                int subtableStart = b.Position;
                b.U16(1);
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); b.S16(13);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(81);
            }

            return b.ToArray();
        }

        /// <summary>Removes any existing directory entry for <paramref name="tag"/> (its own table
        /// data is left as harmless unreferenced dead space, not physically removed - every other
        /// table's own byte offsets stay valid since nothing moves) - needed because
        /// <see cref="SyntheticFontTables.InsertTableDirectoryEntry"/> uses <c>Dictionary.Add</c>
        /// while parsing the directory (<c>OpenTypeFontface.Read</c>), which throws on a duplicate tag
        /// rather than letting the later entry win.</summary>
        private static byte[] RemoveTableDirectoryEntry(byte[] fontBytes, string tag)
        {
            int numTables = (fontBytes[4] << 8) | fontBytes[5];
            int directoryEnd = 12 + numTables * 16;

            var result = new SfntByteBuilder();
            for (int i = 0; i < 4; i++)
                result.Byte(fontBytes[i]);

            int keptCount = 0;
            for (int i = 0; i < numTables; i++)
            {
                int entryStart = 12 + i * 16;
                var entryTag = new string([
                    (char)fontBytes[entryStart], (char)fontBytes[entryStart + 1],
                    (char)fontBytes[entryStart + 2], (char)fontBytes[entryStart + 3],
                ]);
                if (entryTag != tag)
                    keptCount++;
            }
            result.U16(keptCount);
            for (int i = 6; i < 12; i++)
                result.Byte(fontBytes[i]);

            for (int i = 0; i < numTables; i++)
            {
                int entryStart = 12 + i * 16;
                var entryTag = new string([
                    (char)fontBytes[entryStart], (char)fontBytes[entryStart + 1],
                    (char)fontBytes[entryStart + 2], (char)fontBytes[entryStart + 3],
                ]);
                if (entryTag == tag)
                    continue;

                for (int j = 0; j < 8; j++) // tag + checksum, unchanged
                    result.Byte(fontBytes[entryStart + j]);

                // The directory shrinks by 16 bytes (one removed entry), so every table's data - which
                // isn't itself moving - now starts 16 bytes earlier relative to the file start; every
                // surviving entry's own offset must shift down by 16 to match (the exact inverse of
                // SyntheticFontTables.InsertTableDirectoryEntry's own +16 adjustment when it grows the
                // directory).
                int offset = (fontBytes[entryStart + 8] << 24) | (fontBytes[entryStart + 9] << 16) |
                             (fontBytes[entryStart + 10] << 8) | fontBytes[entryStart + 11];
                result.U32((uint)(offset - 16));

                for (int j = 12; j < 16; j++) // length, unchanged
                    result.Byte(fontBytes[entryStart + j]);
            }

            for (int i = directoryEnd; i < fontBytes.Length; i++)
                result.Byte(fontBytes[i]);

            return result.ToArray();
        }

        private static OpenTypeDescriptor BuildDescriptorWithSyntheticGpos()
        {
            // Unlike every other synthetic test in this project (which appends bytes past EOF and
            // constructs GposTable directly against that offset, bypassing table lookup entirely),
            // this one drives GposPositioner.Apply through OpenTypeDescriptor/OpenTypeFontface's own
            // real "GPOS" table-directory resolution - so it needs a genuine directory entry, not just
            // appended bytes. The bundled font already has its own real "GPOS" table (kerning), so
            // that entry is removed first (OpenTypeFontface.Read throws on a duplicate tag rather than
            // letting a later entry win), then SyntheticFontTables.InsertTableDirectoryEntry splices in
            // this test's own.
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            byte[] withoutRealGpos = RemoveTableDirectoryEntry(fontBytes, "GPOS");
            byte[] combined = SyntheticFontTables.InsertTableDirectoryEntry(withoutRealGpos, "GPOS", BuildSyntheticGpos());
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;
            return new OpenTypeDescriptor("gpos-apply-dispatch-test", "gpos-apply-dispatch-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Fact]
        public void Apply_ActivatesAndDispatchesCursiveMarkToLigatureContextualAndChainingLookups()
        {
            var descriptor = BuildDescriptorWithSyntheticGpos();

            var glyphs = new List<ShapedGlyph>
            {
                new(40, 0, 1), new(50, 1, 1), // Type 5: ligature + mark
                new(60, 2, 1), new(61, 3, 1), // Type 3: cursive pair
                new(70, 4, 1), new(71, 5, 1), // Type 7: contextual, nests Type 1 at position 1
                new(80, 6, 1), new(81, 7, 1), new(82, 8, 1), // Type 8: chaining, nests Type 1
            };

            // features.Kerning defaults to true, so "kern" (lookups 1/2/4) activates alongside the
            // unconditionally-requested "mark" (lookup 0).
            GposPositioner.Apply(descriptor, glyphs, TextShapingFeatures.Default);

            // Type 5 (MarkToLigature): mark's XOffset/YOffset reflect the (5,5) anchor.
            Assert.NotEqual(0, glyphs[1].XOffset);
            Assert.Equal(5, glyphs[1].YOffset);

            // Type 3 (Cursive): the exit glyph's advance is corrected, connecting to the entry glyph.
            Assert.NotEqual(0, glyphs[2].XAdvanceDelta);

            // Type 7 (Contextual): the nested Type 1 lookup applied XAdvance +9 to glyph 71.
            Assert.Equal(9, glyphs[5].XAdvanceDelta);

            // Type 8 (Chaining): the nested Type 1 lookup applied XAdvance +13 to glyph 81.
            Assert.Equal(13, glyphs[7].XAdvanceDelta);
        }
    }
}

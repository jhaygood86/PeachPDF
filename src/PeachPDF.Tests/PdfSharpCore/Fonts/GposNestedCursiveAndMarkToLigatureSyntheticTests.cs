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
    /// Coverage for <see cref="GposPositioner"/>'s nested-lookup dispatch (the private
    /// <c>ApplyNestedLookup</c> switch inside <c>ApplyMatchedLookups</c>) gaining Type 3 (Cursive
    /// Attachment) and Type 5 (MarkToLigature Attachment) cases - previously only nested Types 1/2/4/6
    /// were dispatched, so a font that nests a cursive or mark-to-ligature correction inside a
    /// contextual/chaining-context lookup (rather than requesting it directly at the top level) would
    /// have silently no-opped for exactly those two lookup types.
    ///
    /// Exercises <see cref="GposPositioner.ApplySequenceContextLookup"/> directly (bypassing ScriptList/
    /// FeatureList activation, same technique <c>GposCursiveMarkLigatureSyntheticTests</c> uses) against
    /// a hand-built Type 7 (Contextual, format 3/Coverage) lookup whose two <c>SeqLookupRecords</c> nest
    /// lookup 1 (Type 3 cursive) at sequenceIndex 0 and lookup 2 (Type 5 mark-to-ligature) at
    /// sequenceIndex 1 - both nested lookups fire off the same single contextual match.
    ///
    /// Layout: input Coverage[0]={200}, Coverage[1]={201}. Lookup 1 (Type 3): coverage {200,201}; glyph
    /// 200 exit=(40,0), no entry; glyph 201 entry=(6,0), no exit. Lookup 2 (Type 5): mark coverage {201}
    /// (class 0, anchor (0,0)); ligature coverage {200}, 1 component, class-0 anchor (7,3).
    /// </summary>
    public class GposNestedCursiveAndMarkToLigatureSyntheticTests
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
            b.U16(0); // scriptCount - unused, this test drives ApplySequenceContextLookup directly

            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(0); // featureCount

            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(3); // lookupCount
            int lookup0At = b.PlaceholderU16();
            int lookup1At = b.PlaceholderU16();
            int lookup2At = b.PlaceholderU16();

            // Lookup 0: Type 7 format 3 (Coverage) - input=[Coverage{200}, Coverage{201}],
            // seqLookupRecords=[(0,1), (1,2)] - nests lookup 1 (cursive) at position 0, lookup 2
            // (mark-to-ligature) at position 1.
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(1);
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            {
                int subtableStart = b.Position;
                b.U16(3); // format 3
                b.U16(2); // glyphCount
                b.U16(2); // seqLookupCount
                int cov0At = b.PlaceholderU16();
                int cov1At = b.PlaceholderU16();
                b.U16(0); b.U16(1); // sequenceIndex=0, lookupListIndex=1 (cursive)
                b.U16(1); b.U16(2); // sequenceIndex=1, lookupListIndex=2 (mark-to-ligature)
                int cov0Start = b.Position;
                b.PatchU16(cov0At, cov0Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(200);
                int cov1Start = b.Position;
                b.PatchU16(cov1At, cov1Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(201);
            }

            // Lookup 1: Type 3 (Cursive) - coverage {200,201}; 200 exit=(40,0); 201 entry=(6,0).
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
                b.PatchU16(entry0At, 0); // glyph 200: no entry
                int exit0Start = b.Position;
                b.PatchU16(exit0At, exit0Start - subtableStart);
                b.U16(1); b.S16(40); b.S16(0);
                int entry1Start = b.Position;
                b.PatchU16(entry1At, entry1Start - subtableStart);
                b.U16(1); b.S16(6); b.S16(0);
                b.PatchU16(exit1At, 0); // glyph 201: no exit
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(2); b.U16(200); b.U16(201);
            }

            // Lookup 2: Type 5 (MarkToLigature) - mark {201, class 0, anchor (0,0)};
            // ligature {200}, 1 component, class-0 anchor (7,3).
            int lookup2Start = b.Position;
            b.PatchU16(lookup2At, lookup2Start - lookupListStart);
            b.U16(5); b.U16(0); b.U16(1);
            int lookup2SubAt = b.PlaceholderU16();
            int lookup2SubStart = b.Position;
            b.PatchU16(lookup2SubAt, lookup2SubStart - lookup2Start);
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
                b.U16(1); b.S16(7); b.S16(3);

                int markCoverageStart = b.Position;
                b.PatchU16(markCoverageOffsetAt, markCoverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(201);
                int ligCoverageStart = b.Position;
                b.PatchU16(ligCoverageOffsetAt, ligCoverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(200);
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
            return new OpenTypeDescriptor("gpos-nested-cursive-markliga-test", "gpos-nested-cursive-markliga-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Fact]
        public void ContextualLookup_NestsCursiveAndMarkToLigature_BothApply()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var contextual = gpos.GetContextualLookup(0);
            Assert.NotNull(contextual);

            var glyphs = new List<ShapedGlyph> { new(200, 0, 1), new(201, 1, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, contextual.Subtables, glyphs, gdef: null, contextual.LookupFlag, markFilteringSet: null);

            // Nested Type 3 (cursive): glyph 200's exit(40,0) is pulled back to the run's end - same
            // formula as GposCursiveMarkLigatureSyntheticTests' own top-level cursive tests (see
            // GposPositioner.TryApplyCursivePair's own remarks).
            Assert.Equal(-40, glyphs[0].XAdvanceDelta);
            Assert.Equal(-40, glyphs[0].XOffset);

            // Nested Type 5 (mark-to-ligature): glyph 201 (the mark) aligns its own (0,0) anchor with
            // glyph 200's (ligature) class-0 anchor (7,3), offset by the pen distance between them - see
            // GposPositioner.ApplyMarkAnchor's own remarks. This must reflect glyph 200's own
            // already-cursive-corrected XOffset/XAdvanceDelta (both nested lookups apply against the
            // *same* glyph list, in the order their SeqLookupRecords are given), proving
            // ApplyNestedLookup dispatches both rather than only the first nested type it recognizes.
            double expectedGlyph1XOffset = 7 - descriptor.GlyphIndexToWidth(200);
            Assert.Equal(expectedGlyph1XOffset, glyphs[1].XOffset);
            Assert.Equal(3, glyphs[1].YOffset);
        }
    }
}

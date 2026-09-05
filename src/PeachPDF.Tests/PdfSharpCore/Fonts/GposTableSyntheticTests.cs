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
    /// Coverage for <see cref="GposTable"/> (Lookup Types 1/2/4/6/7/8, Type 9 Extension unwrap) and
    /// <see cref="GposPositioner"/>'s corresponding application logic (exercised directly against the
    /// parsed lookups, mirroring <see cref="GsubMultipleAndContextualSyntheticTests"/>'s approach -
    /// see <see cref="GposPositioner.ApplySingleAdjustment"/>'s own internal-visibility rationale).
    ///
    /// Layout: no ScriptList features are exercised here (every test reaches a lookup by index
    /// directly). Lookups: 0 = Type 1 format 1 (glyph 10, XAdvance +100); 1 = Type 1 format 2 (glyphs
    /// 20/21, XPlacement 5/7); 2 = Type 2 format 1 (pair 30+31, XAdvance -20 on the first glyph);
    /// 3 = Type 2 format 2 (class pair: glyph 40 class 1 x glyph 50 class 1, XAdvance -15);
    /// 4 = Type 4 MarkToBase (mark 60 anchored to base 61); 5 = Type 6 MarkToMark (mark 70 anchored to
    /// mark2 71); 6 = Type 9 (Extension) wrapping a valid Type 1 (glyph 80, XAdvance +33); 7 = Type 1
    /// format 1 (glyph 201, XAdvance +50 - nested target for lookup 8); 8 = Type 7 format 3 (Coverage
    /// {200}, Coverage{201}; seqLookupRecords=[(1,7)] - nests lookup 7 at input position 1);
    /// 9 = Type 1 format 1 (glyph 211, XAdvance +77 - nested target for lookup 10); 10 = Type 8
    /// format 1 (Glyph, chaining - coverage {211}; rule: backtrack=[210], glyphCount=1 (no further
    /// input), lookahead=[212]; seqLookupRecords=[(0,9)]); 11 = Type 7 format 1 (Glyph - coverage
    /// {200}; rule: input=[201], seqLookupRecords=[(1,7)] - same nested target/expectation as lookup
    /// 8, exercising the RuleSets/ReadSequenceRule path instead of format 3's flat Coverage array);
    /// 12 = Type 7 format 2 (Class - coverage {200}, ClassDef 200-&gt;1/201-&gt;2; ruleSets[1]: rule
    /// input=[class 2], seqLookupRecords=[(1,7)] - same nested target/expectation again); 16 = Type 1
    /// format 1 (glyph 602, XAdvance +50 - nested target for lookup 17); 17 = Type 7 format 3, TWO
    /// subtables - subtable A: input=[Coverage{600},Coverage{601},Coverage{602}], seqLookupRecords=
    /// [(2,16)]; subtable B: input=[Coverage{602}], seqLookupRecords=[(0,16)] - proves the outer walk
    /// resumes past a matched span rather than re-trying position 2 as a fresh anchor once subtable A
    /// has already matched/nested lookup 16 onto it (which subtable B, tried second, would otherwise
    /// also match independently, double-applying lookup 16's +50 to the same glyph). 13 = Type 8
    /// format 2 (Class, chaining - coverage {211}, backtrack/input/lookahead ClassDefs each
    /// classifying 210/211/212 -&gt; class 1; ruleSets[1]: rule backtrack=[1], glyphCount=1,
    /// lookahead=[1], seqLookupRecords=[(0,9)] - same nested target/expectation as lookup 10).
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
            b.U16(18); // lookupCount
            int lookup0At = b.PlaceholderU16();
            int lookup1At = b.PlaceholderU16();
            int lookup2At = b.PlaceholderU16();
            int lookup3At = b.PlaceholderU16();
            int lookup4At = b.PlaceholderU16();
            int lookup5At = b.PlaceholderU16();
            int lookup6At = b.PlaceholderU16();
            int lookup7At = b.PlaceholderU16();
            int lookup8At = b.PlaceholderU16();
            int lookup9At = b.PlaceholderU16();
            int lookup10At = b.PlaceholderU16();
            int lookup11At = b.PlaceholderU16();
            int lookup12At = b.PlaceholderU16();
            int lookup13At = b.PlaceholderU16();
            int lookup14At = b.PlaceholderU16();
            int lookup15At = b.PlaceholderU16();
            int lookup16At = b.PlaceholderU16();
            int lookup17At = b.PlaceholderU16();

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

            // Lookup 7: Type 1 format 1 - glyph 201, XAdvance +50. Nested target for lookup 8.
            int lookup7Start = b.Position;
            b.PatchU16(lookup7At, lookup7Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup7SubAt = b.PlaceholderU16();
            int lookup7SubStart = b.Position;
            b.PatchU16(lookup7SubAt, lookup7SubStart - lookup7Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); // valueFormat: XAdvance
                b.S16(50);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(201);
            }

            // Lookup 8: Type 7 format 3 - input=[Coverage{200}, Coverage{201}], seqLookupRecords=[(1,7)].
            int lookup8Start = b.Position;
            b.PatchU16(lookup8At, lookup8Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(1);
            int lookup8SubAt = b.PlaceholderU16();
            int lookup8SubStart = b.Position;
            b.PatchU16(lookup8SubAt, lookup8SubStart - lookup8Start);
            {
                int subtableStart = b.Position;
                b.U16(3); // format 3
                b.U16(2); // glyphCount
                b.U16(1); // seqLookupCount
                int cov0OffsetAt = b.PlaceholderU16();
                int cov1OffsetAt = b.PlaceholderU16();
                b.U16(1); // sequenceIndex (second input position)
                b.U16(7); // lookupListIndex (lookup 7)
                int cov0Start = b.Position;
                b.PatchU16(cov0OffsetAt, cov0Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(200);
                int cov1Start = b.Position;
                b.PatchU16(cov1OffsetAt, cov1Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(201);
            }

            // Lookup 9: Type 1 format 1 - glyph 211, XAdvance +77. Nested target for lookup 10.
            int lookup9Start = b.Position;
            b.PatchU16(lookup9At, lookup9Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup9SubAt = b.PlaceholderU16();
            int lookup9SubStart = b.Position;
            b.PatchU16(lookup9SubAt, lookup9SubStart - lookup9Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); // valueFormat: XAdvance
                b.S16(77);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(211);
            }

            // Lookup 10: Type 8 format 1 (Glyph, chaining) - coverage {211}; rule: backtrack=[210],
            // glyphCount=1 (no further input), lookahead=[212]; seqLookupRecords=[(0,9)].
            int lookup10Start = b.Position;
            b.PatchU16(lookup10At, lookup10Start - lookupListStart);
            b.U16(8); b.U16(0); b.U16(1);
            int lookup10SubAt = b.PlaceholderU16();
            int lookup10SubStart = b.Position;
            b.PatchU16(lookup10SubAt, lookup10SubStart - lookup10Start);
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
                b.U16(1); b.U16(210); // backtrackGlyphCount=1, backtrack=[210]
                b.U16(1); // inputGlyphCount=1 (position 0 only - already matched via Coverage)
                b.U16(1); b.U16(212); // lookaheadGlyphCount=1, lookahead=[212]
                b.U16(1); // seqLookupCount
                b.U16(0); b.U16(9); // sequenceIndex=0, lookupListIndex=9
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(211);
            }

            // Lookup 11: Type 7 format 1 (Glyph) - coverage {200}; rule: input=[201],
            // seqLookupRecords=[(1,7)]. Same expectation as lookup 8, via RuleSets/ReadSequenceRule.
            int lookup11Start = b.Position;
            b.PatchU16(lookup11At, lookup11Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(1);
            int lookup11SubAt = b.PlaceholderU16();
            int lookup11SubStart = b.Position;
            b.PatchU16(lookup11SubAt, lookup11SubStart - lookup11Start);
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
                b.U16(2); // glyphCount (position 0 + 1 more)
                b.U16(1); // seqLookupCount
                b.U16(201); // input[0]
                b.U16(1); b.U16(7); // sequenceIndex=1, lookupListIndex=7
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(200);
            }

            // Lookup 12: Type 7 format 2 (Class) - coverage {200}, ClassDef 200->1/201->2;
            // ruleSets[1]: rule input=[class 2], seqLookupRecords=[(1,7)]. Same expectation again.
            int lookup12Start = b.Position;
            b.PatchU16(lookup12At, lookup12Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(1);
            int lookup12SubAt = b.PlaceholderU16();
            int lookup12SubStart = b.Position;
            b.PatchU16(lookup12SubAt, lookup12SubStart - lookup12Start);
            {
                int subtableStart = b.Position;
                b.U16(2); // format 2
                int coverageOffsetAt = b.PlaceholderU16();
                int classDefOffsetAt = b.PlaceholderU16();
                b.U16(2); // ruleSetCount
                b.U16(0); // ruleSets[0] - empty
                int ruleSet1OffsetAt = b.PlaceholderU16();
                int ruleSet1Start = b.Position;
                b.PatchU16(ruleSet1OffsetAt, ruleSet1Start - subtableStart);
                b.U16(1); // ruleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSet1Start);
                b.U16(2); // glyphCount (position 0 + 1 more)
                b.U16(1); // seqLookupCount
                b.U16(2); // input[0] = class 2
                b.U16(1); b.U16(7); // sequenceIndex=1, lookupListIndex=7
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(200);
                int classDefStart = b.Position;
                b.PatchU16(classDefOffsetAt, classDefStart - subtableStart);
                b.U16(1); b.U16(200); b.U16(2); b.U16(1); b.U16(2); // format1: 200->1, 201->2
            }

            // Lookup 13: Type 8 format 2 (Class, chaining) - coverage {211}, backtrack/input/lookahead
            // ClassDefs each classifying 210/211/212 -> class 1; ruleSets[1]: rule backtrack=[1],
            // glyphCount=1, lookahead=[1], seqLookupRecords=[(0,9)]. Same expectation as lookup 10.
            int lookup13Start = b.Position;
            b.PatchU16(lookup13At, lookup13Start - lookupListStart);
            b.U16(8); b.U16(0); b.U16(1);
            int lookup13SubAt = b.PlaceholderU16();
            int lookup13SubStart = b.Position;
            b.PatchU16(lookup13SubAt, lookup13SubStart - lookup13Start);
            {
                int subtableStart = b.Position;
                b.U16(2); // format 2
                int coverageOffsetAt = b.PlaceholderU16();
                int backtrackClassDefOffsetAt = b.PlaceholderU16();
                int inputClassDefOffsetAt = b.PlaceholderU16();
                int lookaheadClassDefOffsetAt = b.PlaceholderU16();
                b.U16(2); // ruleSetCount
                b.U16(0); // ruleSets[0] - empty
                int ruleSet1OffsetAt = b.PlaceholderU16();
                int ruleSet1Start = b.Position;
                b.PatchU16(ruleSet1OffsetAt, ruleSet1Start - subtableStart);
                b.U16(1); // ruleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSet1Start);
                b.U16(1); b.U16(1); // backtrackGlyphCount=1, backtrack=[class 1]
                b.U16(1); // inputGlyphCount=1 (position 0 only)
                b.U16(1); b.U16(1); // lookaheadGlyphCount=1, lookahead=[class 1]
                b.U16(1); // seqLookupCount
                b.U16(0); b.U16(9); // sequenceIndex=0, lookupListIndex=9
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(211);
                int backtrackClassDefStart = b.Position;
                b.PatchU16(backtrackClassDefOffsetAt, backtrackClassDefStart - subtableStart);
                b.U16(1); b.U16(210); b.U16(1); b.U16(1); // format1: 210->1
                int inputClassDefStart = b.Position;
                b.PatchU16(inputClassDefOffsetAt, inputClassDefStart - subtableStart);
                b.U16(1); b.U16(211); b.U16(1); b.U16(1); // format1: 211->1
                int lookaheadClassDefStart = b.Position;
                b.PatchU16(lookaheadClassDefOffsetAt, lookaheadClassDefStart - subtableStart);
                b.U16(1); b.U16(212); b.U16(1); b.U16(1); // format1: 212->1
            }

            // Lookup 14: Type 8 format 3 (Coverage, chaining) - backtrack=[Coverage{210}],
            // input=[Coverage{211}], lookahead=[Coverage{212}], seqLookupRecords=[(0,9)]. Same
            // expectation as lookups 10/13, via the flat per-position Coverage array instead of
            // RuleSets/ClassDefs.
            int lookup14Start = b.Position;
            b.PatchU16(lookup14At, lookup14Start - lookupListStart);
            b.U16(8); b.U16(0); b.U16(1);
            int lookup14SubAt = b.PlaceholderU16();
            int lookup14SubStart = b.Position;
            b.PatchU16(lookup14SubAt, lookup14SubStart - lookup14Start);
            {
                int subtableStart = b.Position;
                b.U16(3); // format 3
                b.U16(1); // backtrackGlyphCount
                int backtrackOffsetAt = b.PlaceholderU16();
                b.U16(1); // inputGlyphCount
                int inputOffsetAt = b.PlaceholderU16();
                b.U16(1); // lookaheadGlyphCount
                int lookaheadOffsetAt = b.PlaceholderU16();
                b.U16(1); // seqLookupCount
                b.U16(0); b.U16(9); // sequenceIndex=0, lookupListIndex=9
                int backtrackStart = b.Position;
                b.PatchU16(backtrackOffsetAt, backtrackStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(210);
                int inputStart = b.Position;
                b.PatchU16(inputOffsetAt, inputStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(211);
                int lookaheadStart = b.Position;
                b.PatchU16(lookaheadOffsetAt, lookaheadStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(212);
            }

            // Lookup 15: Type 7, unrecognized subtable format (only formats 1/2/3 are defined).
            int lookup15Start = b.Position;
            b.PatchU16(lookup15At, lookup15Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(1);
            int lookup15SubAt = b.PlaceholderU16();
            int lookup15SubStart = b.Position;
            b.PatchU16(lookup15SubAt, lookup15SubStart - lookup15Start);
            b.U16(9); // format 9 - unrecognized

            // Lookup 16: Type 1 format 1 - glyph 602, XAdvance +50. Nested target for lookup 17.
            int lookup16Start = b.Position;
            b.PatchU16(lookup16At, lookup16Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup16SubAt = b.PlaceholderU16();
            int lookup16SubStart = b.Position;
            b.PatchU16(lookup16SubAt, lookup16SubStart - lookup16Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); // valueFormat: XAdvance
                b.S16(50);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(602);
            }

            // Lookup 17: Type 7 format 3, TWO subtables - A: input=[600,601,602], seqLookupRecords=
            // [(2,16)]; B: input=[602], seqLookupRecords=[(0,16)]. Proves the outer walk resumes past
            // a matched span (see this class's own doc comment).
            int lookup17Start = b.Position;
            b.PatchU16(lookup17At, lookup17Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(2); // lookupType=7, lookupFlag=0, subtableCount=2
            int lookup17SubAAt = b.PlaceholderU16();
            int lookup17SubBAt = b.PlaceholderU16();

            int subAStart = b.Position;
            b.PatchU16(lookup17SubAAt, subAStart - lookup17Start);
            {
                int subtableStart = b.Position;
                b.U16(3); // format 3
                b.U16(3); // glyphCount
                b.U16(1); // seqLookupCount
                int cov0At = b.PlaceholderU16();
                int cov1At = b.PlaceholderU16();
                int cov2At = b.PlaceholderU16();
                b.U16(2); b.U16(16); // sequenceIndex=2, lookupListIndex=16
                int cov0Start = b.Position;
                b.PatchU16(cov0At, cov0Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(600);
                int cov1Start = b.Position;
                b.PatchU16(cov1At, cov1Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(601);
                int cov2Start = b.Position;
                b.PatchU16(cov2At, cov2Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(602);
            }

            int subBStart = b.Position;
            b.PatchU16(lookup17SubBAt, subBStart - lookup17Start);
            {
                int subtableStart = b.Position;
                b.U16(3); // format 3
                b.U16(1); // glyphCount
                b.U16(1); // seqLookupCount
                int cov0At = b.PlaceholderU16();
                b.U16(0); b.U16(16); // sequenceIndex=0, lookupListIndex=16
                int cov0Start = b.Position;
                b.PatchU16(cov0At, cov0Start - subtableStart);
                b.U16(1); b.U16(1); b.U16(602);
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
        public void ContextualType7Format3_MatchedInput_AppliesNestedSingleAdjustmentAtSecondPosition()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetContextualLookup(8);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(200, 0, 1), new(201, 1, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, lookup.Subtables, glyphs, gdef: null, lookup.LookupFlag, markFilteringSet: null);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
            Assert.Equal(50, glyphs[1].XAdvanceDelta);
        }

        [Fact]
        public void ContextualType7Format3_UnmatchedInput_LeavesGlyphsUnadjusted()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetContextualLookup(8);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(200, 0, 1), new(999, 1, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, lookup.Subtables, glyphs, gdef: null, lookup.LookupFlag, markFilteringSet: null);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
            Assert.Equal(0, glyphs[1].XAdvanceDelta);
        }

        [Fact]
        public void ChainingContextType8Format1_MatchedBacktrackInputLookahead_AppliesNestedSingleAdjustment()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetChainingContextLookup(10);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(210, 0, 1), new(211, 1, 1), new(212, 2, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, lookup.Subtables, glyphs, gdef: null, lookup.LookupFlag, markFilteringSet: null);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
            Assert.Equal(77, glyphs[1].XAdvanceDelta);
            Assert.Equal(0, glyphs[2].XAdvanceDelta);
        }

        [Fact]
        public void ChainingContextType8Format1_MissingBacktrack_LeavesGlyphsUnadjusted()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetChainingContextLookup(10);
            Assert.NotNull(lookup);

            // No backtrack glyph at all (211/212 start the run) - the chaining rule requires one before it.
            var glyphs = new List<ShapedGlyph> { new(211, 0, 1), new(212, 1, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, lookup.Subtables, glyphs, gdef: null, lookup.LookupFlag, markFilteringSet: null);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
            Assert.Equal(0, glyphs[1].XAdvanceDelta);
        }

        [Fact]
        public void ContextualType7Format1_MatchedInput_AppliesNestedSingleAdjustment()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetContextualLookup(11);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(200, 0, 1), new(201, 1, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, lookup.Subtables, glyphs, gdef: null, lookup.LookupFlag, markFilteringSet: null);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
            Assert.Equal(50, glyphs[1].XAdvanceDelta);
        }

        [Fact]
        public void ContextualType7Format2_ClassMatchedInput_AppliesNestedSingleAdjustment()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetContextualLookup(12);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(200, 0, 1), new(201, 1, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, lookup.Subtables, glyphs, gdef: null, lookup.LookupFlag, markFilteringSet: null);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
            Assert.Equal(50, glyphs[1].XAdvanceDelta);
        }

        [Fact]
        public void ChainingContextType8Format2_ClassMatchedBacktrackInputLookahead_AppliesNestedSingleAdjustment()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetChainingContextLookup(13);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(210, 0, 1), new(211, 1, 1), new(212, 2, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, lookup.Subtables, glyphs, gdef: null, lookup.LookupFlag, markFilteringSet: null);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
            Assert.Equal(77, glyphs[1].XAdvanceDelta);
            Assert.Equal(0, glyphs[2].XAdvanceDelta);
        }

        [Fact]
        public void ChainingContextType8Format3_CoverageMatchedBacktrackInputLookahead_AppliesNestedSingleAdjustment()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetChainingContextLookup(14);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(210, 0, 1), new(211, 1, 1), new(212, 2, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, lookup.Subtables, glyphs, gdef: null, lookup.LookupFlag, markFilteringSet: null);

            Assert.Equal(0, glyphs[0].XAdvanceDelta);
            Assert.Equal(77, glyphs[1].XAdvanceDelta);
            Assert.Equal(0, glyphs[2].XAdvanceDelta);
        }

        [Fact]
        public void ContextualType7_OuterWalkResumesPastMatchedSpan_DoesNotDoubleApplySameLookup()
        {
            var descriptor = RealDescriptor();
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetContextualLookup(17);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(600, 0, 1), new(601, 1, 1), new(602, 2, 1) };
            GposPositioner.ApplySequenceContextLookup(descriptor, gpos, lookup.Subtables, glyphs, gdef: null, lookup.LookupFlag, markFilteringSet: null);

            // Subtable A matches at position 0 (input [600,601,602]) and nests lookup 16 (+50) onto
            // position 2. The outer walk must then resume at position 3, never re-trying position 2 as
            // a fresh anchor - if it did, subtable B (input=[602]) would match there too and nest
            // lookup 16 a second time, landing on +100 instead of +50.
            Assert.Equal(50, glyphs[2].XAdvanceDelta);
        }

        [Fact]
        public void UnrecognizedSubtableFormat_Contextual_AccessorReturnsNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGpos();
            var gpos = new GposTable(face, tableStart);

            Assert.Null(gpos.GetContextualLookup(15));
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
            Assert.Null(gpos.GetContextualLookup(7)); // lookup 7 is Type 1
            Assert.Null(gpos.GetChainingContextLookup(8)); // lookup 8 is Type 7
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
                () => Assert.NotNull(gpos.GetContextualLookup(8)),
                () => Assert.NotNull(gpos.GetChainingContextLookup(10)),
            };

            const int repeatsPerAction = 40;
            var work = Enumerable.Range(0, actions.Length * repeatsPerAction)
                .Select(i => actions[i % actions.Length]);

            Parallel.ForEach(work, action => action());
        }

        /// <summary>A GPOS table with two scripts, "aaaa" (feature "kern" -&gt; lookup 0) listed first
        /// and "DFLT" (feature "kern" -&gt; lookup 1) listed second - mirrors
        /// <see cref="GsubTableSyntheticTests.BuildScriptListWithDfltGsub"/>'s own rationale: this
        /// file's main synthetic table above never defines a ScriptList at all (every other test here
        /// reaches a lookup by index directly), so it cannot exercise
        /// <see cref="GposTable.FindScript"/>'s "DFLT" fallback step distinctly from its older "true
        /// first record" one.</summary>
        private static byte[] BuildScriptListWithDfltGpos()
        {
            var b = new SfntByteBuilder();

            b.U16(1); b.U16(0);
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();

            int scriptListStart = b.Position;
            b.PatchU16(scriptListOffsetAt, scriptListStart);
            b.U16(2); // scriptCount
            b.Tag("aaaa"); int aaaaOffsetAt = b.PlaceholderU16();
            b.Tag("DFLT"); int dfltOffsetAt = b.PlaceholderU16();

            int aaaaStart = b.Position;
            b.PatchU16(aaaaOffsetAt, aaaaStart - scriptListStart);
            int aaaaLangSysOffsetAt = b.PlaceholderU16();
            b.U16(0); // langSysCount
            int aaaaLangSysStart = b.Position;
            b.PatchU16(aaaaLangSysOffsetAt, aaaaLangSysStart - aaaaStart);
            b.U16(0); b.U16(0xFFFF); b.U16(1); b.U16(0); // lookupOrder, no required feature, feature 0

            int dfltStart = b.Position;
            b.PatchU16(dfltOffsetAt, dfltStart - scriptListStart);
            int dfltLangSysOffsetAt = b.PlaceholderU16();
            b.U16(0); // langSysCount
            int dfltLangSysStart = b.Position;
            b.PatchU16(dfltLangSysOffsetAt, dfltLangSysStart - dfltStart);
            b.U16(0); b.U16(0xFFFF); b.U16(1); b.U16(1); // lookupOrder, no required feature, feature 1

            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(2); // featureCount
            b.Tag("kern"); int feature0OffsetAt = b.PlaceholderU16();
            b.Tag("kern"); int feature1OffsetAt = b.PlaceholderU16();

            int feature0Start = b.Position;
            b.PatchU16(feature0OffsetAt, feature0Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(0); // featureParams, lookupIndexCount, lookup 0

            int feature1Start = b.Position;
            b.PatchU16(feature1OffsetAt, feature1Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(1); // featureParams, lookupIndexCount, lookup 1

            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(2); // lookupCount
            int lookup0At = b.PlaceholderU16();
            int lookup1At = b.PlaceholderU16();

            // Lookup 0: Type 1 format 1 - glyph 900, XAdvance +11.
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            {
                int subtableStart = b.Position;
                b.U16(1);
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); b.S16(11);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(900);
            }

            // Lookup 1: Type 1 format 1 - glyph 901, XAdvance +22.
            int lookup1Start = b.Position;
            b.PatchU16(lookup1At, lookup1Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup1SubAt = b.PlaceholderU16();
            int lookup1SubStart = b.Position;
            b.PatchU16(lookup1SubAt, lookup1SubStart - lookup1Start);
            {
                int subtableStart = b.Position;
                b.U16(1);
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(0x0004); b.S16(22);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(901);
            }

            return b.ToArray();
        }

        [Fact]
        public void ScriptPreference_PrefersDflt_OverTrueFirstRecord_WhenNothingElseMatches()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, BuildScriptListWithDfltGpos());
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;
            var gpos = new GposTable(face, tableStart);

            // Neither "arab" nor "latn" exist - "DFLT" (the second-listed script) must win over "aaaa"
            // (the true first-listed record), unlike the pre-fix behavior that always fell straight to
            // ScriptList[0] regardless of a real "DFLT" entry existing elsewhere in the list.
            var indices = gpos.GetActiveLookupIndices(["arab", "latn"], new HashSet<string> { "kern" });

            Assert.Equal([1], indices);
        }
    }
}

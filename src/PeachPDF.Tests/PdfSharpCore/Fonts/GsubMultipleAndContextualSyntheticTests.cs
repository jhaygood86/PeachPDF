using System.Collections.Generic;
using System.IO;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for <see cref="GsubTable"/>'s Lookup Type 2 (Multiple Substitution) and Lookup Types
    /// 5/6 (Contextual/Chaining Context Substitution, formats 1/2/3) readers, plus
    /// <see cref="GsubShaper"/>'s corresponding application logic (exercised directly against the
    /// parsed lookups, bypassing cmap/real-text shaping - see <see cref="GsubShaper.ApplySequenceContextLookup"/>'s
    /// own internal-visibility rationale).
    ///
    /// Layout: no ScriptList features are exercised here (every test reaches a lookup by index
    /// directly), so the ScriptList is a single empty script - only the LookupList matters.
    /// Lookups: 0 = Type 2 (glyph 10 -&gt; sequence [11, 12, 13]); 1 = Type 1 (glyph 20 -&gt; 25, the
    /// nested target for lookup 2); 2 = Type 5 format 1 (coverage {20}, rule: input=[21],
    /// seqLookupRecords=[(0, 1)]); 3 = Type 1 (glyph 31 -&gt; 35, nested target for lookup 4);
    /// 4 = Type 6 format 3 (backtrack={30}, input={31}, lookahead={32}, seqLookupRecords=[(0, 3)]);
    /// 5 = Type 1 (glyph 40 -&gt; 45, nested target for lookup 6); 6 = Type 5 format 2 (coverage {40},
    /// ClassDef 40-&gt;class 1, rule: glyphCount=1 i.e. no further input, seqLookupRecords=[(0, 5)]);
    /// 7 = Type 1 (glyph 91 -&gt; 95, nested target for lookup 8); 8 = Type 6 format 1 (coverage {91},
    /// rule: backtrack=[90], input=[] i.e. glyphCount=1, lookahead=[92], seqLookupRecords=[(0, 7)]);
    /// 9 = Type 1 (glyph 100 -&gt; 105, nested target for lookup 10); 10 = Type 6 format 2 (coverage
    /// {100}, backtrack/input/lookahead ClassDefs each classifying glyph 99/100/102 -&gt; class 1,
    /// ruleSets[1] = rule: backtrack=[1], input=[] i.e. glyphCount=1, lookahead=[1],
    /// seqLookupRecords=[(0, 9)]); 11 = Type 2 (glyph 120 -&gt; sequence [124, 125], nested target for
    /// lookup 12); 12 = Type 5 format 1 (coverage {120}, rule: input=[121], seqLookupRecords=
    /// [(0, 11)]); 13 = Type 3 (glyph 130, alternates=[135, 136], nested target for lookup 14 - the
    /// nested dispatch always requests alternate index 0, i.e. 135); 14 = Type 5 format 1 (coverage
    /// {130}, rule: input=[131], seqLookupRecords=[(0, 13)]).
    /// </summary>
    public class GsubMultipleAndContextualSyntheticTests
    {
        private static byte[] BuildSyntheticGsub()
        {
            var b = new SfntByteBuilder();

            // ---- Header ----
            b.U16(1); b.U16(0);
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();

            // ---- ScriptList: empty (no tests here go through GetActiveLookupIndices) ----
            int scriptListStart = b.Position;
            b.PatchU16(scriptListOffsetAt, scriptListStart);
            b.U16(0); // scriptCount

            // ---- FeatureList: empty ----
            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(0); // featureCount

            // ---- LookupList ----
            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(15); // lookupCount
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

            // Lookup 0: Type 2, Multiple Substitution - glyph 10 -> [11, 12, 13].
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(2); b.U16(0); b.U16(1); // lookupType, lookupFlag, subtableCount
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // substFormat
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // sequenceCount
                int seq0OffsetAt = b.PlaceholderU16();
                int seq0Start = b.Position;
                b.PatchU16(seq0OffsetAt, seq0Start - subtableStart);
                b.U16(3); // glyphCount
                b.U16(11); b.U16(12); b.U16(13);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(10); // coverage format 1, 1 glyph, glyph 10
            }

            // Lookup 1: Type 1 format 1 - glyph 20 -> 25 (delta 5). Nested target for lookup 2.
            int lookup1Start = b.Position;
            b.PatchU16(lookup1At, lookup1Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup1SubAt = b.PlaceholderU16();
            int lookup1SubStart = b.Position;
            b.PatchU16(lookup1SubAt, lookup1SubStart - lookup1Start);
            WriteSingleSubFormat1(b, firstGlyph: 20, delta: 5);

            // Lookup 2: Type 5 format 1 - coverage {20}; rule: input=[21], records=[(0,1)].
            int lookup2Start = b.Position;
            b.PatchU16(lookup2At, lookup2Start - lookupListStart);
            b.U16(5); b.U16(0); b.U16(1);
            int lookup2SubAt = b.PlaceholderU16();
            int lookup2SubStart = b.Position;
            b.PatchU16(lookup2SubAt, lookup2SubStart - lookup2Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // seqRuleSetCount
                int ruleSetOffsetAt = b.PlaceholderU16();
                int ruleSetStart = b.Position;
                b.PatchU16(ruleSetOffsetAt, ruleSetStart - subtableStart);
                b.U16(1); // seqRuleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSetStart);
                b.U16(2); // glyphCount (first glyph + 1 more)
                b.U16(1); // seqLookupCount
                b.U16(21); // inputSequence[0]
                b.U16(0); b.U16(1); // seqLookupRecords[0]: sequenceIndex=0, lookupListIndex=1
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(20); // coverage format 1, glyph 20
            }

            // Lookup 3: Type 1 format 1 - glyph 31 -> 35 (delta 4). Nested target for lookup 4.
            int lookup3Start = b.Position;
            b.PatchU16(lookup3At, lookup3Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup3SubAt = b.PlaceholderU16();
            int lookup3SubStart = b.Position;
            b.PatchU16(lookup3SubAt, lookup3SubStart - lookup3Start);
            WriteSingleSubFormat1(b, firstGlyph: 31, delta: 4);

            // Lookup 4: Type 6 format 3 - backtrack={30}, input={31}, lookahead={32}, records=[(0,3)].
            int lookup4Start = b.Position;
            b.PatchU16(lookup4At, lookup4Start - lookupListStart);
            b.U16(6); b.U16(0); b.U16(1);
            int lookup4SubAt = b.PlaceholderU16();
            int lookup4SubStart = b.Position;
            b.PatchU16(lookup4SubAt, lookup4SubStart - lookup4Start);
            {
                int subtableStart = b.Position;
                b.U16(3); // format
                b.U16(1); // backtrackGlyphCount
                int backtrackCovAt = b.PlaceholderU16();
                b.U16(1); // inputGlyphCount
                int inputCovAt = b.PlaceholderU16();
                b.U16(1); // lookaheadGlyphCount
                int lookaheadCovAt = b.PlaceholderU16();
                b.U16(1); // seqLookupCount
                b.U16(0); b.U16(3); // seqLookupRecords[0]: sequenceIndex=0, lookupListIndex=3

                int backtrackCovStart = b.Position;
                b.PatchU16(backtrackCovAt, backtrackCovStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(30);

                int inputCovStart = b.Position;
                b.PatchU16(inputCovAt, inputCovStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(31);

                int lookaheadCovStart = b.Position;
                b.PatchU16(lookaheadCovAt, lookaheadCovStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(32);
            }

            // Lookup 5: Type 1 format 1 - glyph 40 -> 45 (delta 5). Nested target for lookup 6.
            int lookup5Start = b.Position;
            b.PatchU16(lookup5At, lookup5Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup5SubAt = b.PlaceholderU16();
            int lookup5SubStart = b.Position;
            b.PatchU16(lookup5SubAt, lookup5SubStart - lookup5Start);
            WriteSingleSubFormat1(b, firstGlyph: 40, delta: 5);

            // Lookup 6: Type 5 format 2 - coverage {40}, ClassDef (40 -> class 1), ruleSets indexed by
            // class (0 empty, 1 has one rule: glyphCount=1, no further input, records=[(0,5)]).
            int lookup6Start = b.Position;
            b.PatchU16(lookup6At, lookup6Start - lookupListStart);
            b.U16(5); b.U16(0); b.U16(1);
            int lookup6SubAt = b.PlaceholderU16();
            int lookup6SubStart = b.Position;
            b.PatchU16(lookup6SubAt, lookup6SubStart - lookup6Start);
            {
                int subtableStart = b.Position;
                b.U16(2); // format
                int coverageOffsetAt = b.PlaceholderU16();
                int classDefOffsetAt = b.PlaceholderU16();
                b.U16(2); // classSeqRuleSetCount (indices 0 and 1)
                int ruleSet0At = b.PlaceholderU16(); // class 0: none
                int ruleSet1At = b.PlaceholderU16(); // class 1: our rule
                b.PatchU16(ruleSet0At, 0);

                int ruleSet1Start = b.Position;
                b.PatchU16(ruleSet1At, ruleSet1Start - subtableStart);
                b.U16(1); // classSeqRuleCount
                int rule1OffsetAt = b.PlaceholderU16();
                int rule1Start = b.Position;
                b.PatchU16(rule1OffsetAt, rule1Start - ruleSet1Start);
                b.U16(1); // glyphCount (first position only, no further input)
                b.U16(1); // seqLookupCount
                // inputSequence has glyphCount-1 = 0 entries.
                b.U16(0); b.U16(5); // seqLookupRecords[0]: sequenceIndex=0, lookupListIndex=5

                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(40);

                int classDefStart = b.Position;
                b.PatchU16(classDefOffsetAt, classDefStart - subtableStart);
                b.U16(1); // ClassDef format 1
                b.U16(40); // startGlyphID
                b.U16(1); // glyphCount
                b.U16(1); // glyph 40 -> class 1
            }

            // Lookup 7: Type 1 format 1 - glyph 91 -> 95 (delta 4). Nested target for lookup 8.
            int lookup7Start = b.Position;
            b.PatchU16(lookup7At, lookup7Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup7SubAt = b.PlaceholderU16();
            int lookup7SubStart = b.Position;
            b.PatchU16(lookup7SubAt, lookup7SubStart - lookup7Start);
            WriteSingleSubFormat1(b, firstGlyph: 91, delta: 4);

            // Lookup 8: Type 6 format 1 - coverage {91}; rule: backtrack=[90], input=[] (glyphCount=1),
            // lookahead=[92], records=[(0,7)].
            int lookup8Start = b.Position;
            b.PatchU16(lookup8At, lookup8Start - lookupListStart);
            b.U16(6); b.U16(0); b.U16(1);
            int lookup8SubAt = b.PlaceholderU16();
            int lookup8SubStart = b.Position;
            b.PatchU16(lookup8SubAt, lookup8SubStart - lookup8Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // chainedSeqRuleSetCount
                int ruleSetOffsetAt = b.PlaceholderU16();
                int ruleSetStart = b.Position;
                b.PatchU16(ruleSetOffsetAt, ruleSetStart - subtableStart);
                b.U16(1); // chainedSeqRuleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSetStart);
                b.U16(1); b.U16(90); // backtrackGlyphCount, backtrackSequence[0]
                b.U16(1); // inputGlyphCount (glyphCount-1 = 0 further entries)
                b.U16(1); b.U16(92); // lookaheadGlyphCount, lookaheadSequence[0]
                b.U16(1); // seqLookupCount
                b.U16(0); b.U16(7); // seqLookupRecords[0]: sequenceIndex=0, lookupListIndex=7
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(91);
            }

            // Lookup 9: Type 1 format 1 - glyph 100 -> 105 (delta 5). Nested target for lookup 10.
            int lookup9Start = b.Position;
            b.PatchU16(lookup9At, lookup9Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup9SubAt = b.PlaceholderU16();
            int lookup9SubStart = b.Position;
            b.PatchU16(lookup9SubAt, lookup9SubStart - lookup9Start);
            WriteSingleSubFormat1(b, firstGlyph: 100, delta: 5);

            // Lookup 10: Type 6 format 2 - coverage {100}; backtrack/input/lookahead ClassDefs each
            // classifying glyph 99/100/102 -> class 1; ruleSets[1] = rule: backtrack=[1], input=[]
            // (glyphCount=1), lookahead=[1], records=[(0,9)].
            int lookup10Start = b.Position;
            b.PatchU16(lookup10At, lookup10Start - lookupListStart);
            b.U16(6); b.U16(0); b.U16(1);
            int lookup10SubAt = b.PlaceholderU16();
            int lookup10SubStart = b.Position;
            b.PatchU16(lookup10SubAt, lookup10SubStart - lookup10Start);
            {
                int subtableStart = b.Position;
                b.U16(2); // format
                int coverageOffsetAt = b.PlaceholderU16();
                int backtrackClassDefAt = b.PlaceholderU16();
                int inputClassDefAt = b.PlaceholderU16();
                int lookaheadClassDefAt = b.PlaceholderU16();
                b.U16(2); // chainedClassSeqRuleSetCount (indices 0 and 1)
                int ruleSet0At = b.PlaceholderU16(); // class 0: none
                int ruleSet1At = b.PlaceholderU16(); // class 1: our rule
                b.PatchU16(ruleSet0At, 0);

                int ruleSet1Start = b.Position;
                b.PatchU16(ruleSet1At, ruleSet1Start - subtableStart);
                b.U16(1); // chainedClassSeqRuleCount
                int rule1OffsetAt = b.PlaceholderU16();
                int rule1Start = b.Position;
                b.PatchU16(rule1OffsetAt, rule1Start - ruleSet1Start);
                b.U16(1); b.U16(1); // backtrackGlyphCount, backtrackSequence[0]=class 1
                b.U16(1); // inputGlyphCount (glyphCount-1 = 0 further entries)
                b.U16(1); b.U16(1); // lookaheadGlyphCount, lookaheadSequence[0]=class 1
                b.U16(1); // seqLookupCount
                b.U16(0); b.U16(9); // seqLookupRecords[0]: sequenceIndex=0, lookupListIndex=9

                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(100);

                int backtrackClassDefStart = b.Position;
                b.PatchU16(backtrackClassDefAt, backtrackClassDefStart - subtableStart);
                b.U16(1); b.U16(99); b.U16(1); b.U16(1); // ClassDef format1: glyph 99 -> class 1

                int inputClassDefStart = b.Position;
                b.PatchU16(inputClassDefAt, inputClassDefStart - subtableStart);
                b.U16(1); b.U16(100); b.U16(1); b.U16(1); // ClassDef format1: glyph 100 -> class 1

                int lookaheadClassDefStart = b.Position;
                b.PatchU16(lookaheadClassDefAt, lookaheadClassDefStart - subtableStart);
                b.U16(1); b.U16(102); b.U16(1); b.U16(1); // ClassDef format1: glyph 102 -> class 1
            }

            // Lookup 11: Type 2 - glyph 120 -> sequence [124, 125]. Nested target for lookup 12.
            int lookup11Start = b.Position;
            b.PatchU16(lookup11At, lookup11Start - lookupListStart);
            b.U16(2); b.U16(0); b.U16(1);
            int lookup11SubAt = b.PlaceholderU16();
            int lookup11SubStart = b.Position;
            b.PatchU16(lookup11SubAt, lookup11SubStart - lookup11Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // substFormat
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // sequenceCount
                int seq0OffsetAt = b.PlaceholderU16();
                int seq0Start = b.Position;
                b.PatchU16(seq0OffsetAt, seq0Start - subtableStart);
                b.U16(2); // glyphCount
                b.U16(124); b.U16(125);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(120);
            }

            // Lookup 12: Type 5 format 1 - coverage {120}; rule: input=[121], records=[(0,11)].
            int lookup12Start = b.Position;
            b.PatchU16(lookup12At, lookup12Start - lookupListStart);
            b.U16(5); b.U16(0); b.U16(1);
            int lookup12SubAt = b.PlaceholderU16();
            int lookup12SubStart = b.Position;
            b.PatchU16(lookup12SubAt, lookup12SubStart - lookup12Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // seqRuleSetCount
                int ruleSetOffsetAt = b.PlaceholderU16();
                int ruleSetStart = b.Position;
                b.PatchU16(ruleSetOffsetAt, ruleSetStart - subtableStart);
                b.U16(1); // seqRuleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSetStart);
                b.U16(2); // glyphCount (first glyph + 1 more)
                b.U16(1); // seqLookupCount
                b.U16(121); // inputSequence[0]
                b.U16(0); b.U16(11); // seqLookupRecords[0]: sequenceIndex=0, lookupListIndex=11
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(120);
            }

            // Lookup 13: Type 3 - glyph 130, alternates=[135, 136]. Nested target for lookup 14.
            int lookup13Start = b.Position;
            b.PatchU16(lookup13At, lookup13Start - lookupListStart);
            b.U16(3); b.U16(0); b.U16(1);
            int lookup13SubAt = b.PlaceholderU16();
            int lookup13SubStart = b.Position;
            b.PatchU16(lookup13SubAt, lookup13SubStart - lookup13Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // substFormat
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // alternateSetCount
                int altSetOffsetAt = b.PlaceholderU16();
                int altSetStart = b.Position;
                b.PatchU16(altSetOffsetAt, altSetStart - subtableStart);
                b.U16(2); // glyphCount
                b.U16(135); b.U16(136);
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(130);
            }

            // Lookup 14: Type 5 format 1 - coverage {130}; rule: input=[131], records=[(0,13)].
            int lookup14Start = b.Position;
            b.PatchU16(lookup14At, lookup14Start - lookupListStart);
            b.U16(5); b.U16(0); b.U16(1);
            int lookup14SubAt = b.PlaceholderU16();
            int lookup14SubStart = b.Position;
            b.PatchU16(lookup14SubAt, lookup14SubStart - lookup14Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // seqRuleSetCount
                int ruleSetOffsetAt = b.PlaceholderU16();
                int ruleSetStart = b.Position;
                b.PatchU16(ruleSetOffsetAt, ruleSetStart - subtableStart);
                b.U16(1); // seqRuleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSetStart);
                b.U16(2); // glyphCount (first glyph + 1 more)
                b.U16(1); // seqLookupCount
                b.U16(131); // inputSequence[0]
                b.U16(0); b.U16(13); // seqLookupRecords[0]: sequenceIndex=0, lookupListIndex=13
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(130);
            }

            return b.ToArray();
        }

        private static void WriteSingleSubFormat1(SfntByteBuilder b, ushort firstGlyph, short delta)
        {
            int subtableStart = b.Position;
            b.U16(1); // substFormat
            int coverageOffsetAt = b.PlaceholderU16();
            b.S16(delta);
            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); b.U16(1); b.U16(firstGlyph);
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
        public void MultipleSubstitution_ExpandsCoveredGlyphInPlace()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetMultipleSubstitutionLookup(0);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(10, 0, 1) };
            GsubShaper.ApplyMultipleSubstitutionLookup(lookup, glyphs);

            Assert.Equal(3, glyphs.Count);
            Assert.Equal([11, 12, 13], glyphs.ConvertAll(g => g.GlyphIndex));
            // The first output glyph keeps the original source span; the rest get a zero-length span
            // anchored at its end, so ToUnicode/text-extraction never over-copies the source character.
            Assert.Equal(0, glyphs[0].ClusterStart);
            Assert.Equal(1, glyphs[0].ClusterLength);
            Assert.Equal(1, glyphs[1].ClusterStart);
            Assert.Equal(0, glyphs[1].ClusterLength);
            Assert.Equal(1, glyphs[2].ClusterStart);
            Assert.Equal(0, glyphs[2].ClusterLength);
        }

        [Fact]
        public void MultipleSubstitution_UncoveredGlyph_Unchanged()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetMultipleSubstitutionLookup(0);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(999, 0, 1) };
            GsubShaper.ApplyMultipleSubstitutionLookup(lookup, glyphs);

            Assert.Equal([999], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ContextualFormat1_MatchedInput_AppliesNestedSingleSubstitution()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetContextualLookup(2);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(20, 0, 1), new(21, 1, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([25, 21], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ContextualFormat1_UnmatchedInput_LeavesGlyphsUnchanged()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetContextualLookup(2);
            Assert.NotNull(lookup);

            // Glyph 20 followed by something other than 21 - the rule shouldn't match.
            var glyphs = new List<ShapedGlyph> { new(20, 0, 1), new(999, 1, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([20, 999], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ChainingContextFormat3_MatchedBacktrackInputLookahead_AppliesNestedSingleSubstitution()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetChainingContextLookup(4);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(30, 0, 1), new(31, 1, 1), new(32, 2, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([30, 35, 32], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ChainingContextFormat3_MissingBacktrack_LeavesGlyphsUnchanged()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetChainingContextLookup(4);
            Assert.NotNull(lookup);

            // No backtrack glyph at all (31/32 start the run) - the chaining rule requires one before it.
            var glyphs = new List<ShapedGlyph> { new(31, 0, 1), new(32, 1, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([31, 32], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ContextualFormat2_ClassMatchedFirstPosition_AppliesNestedSingleSubstitution()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetContextualLookup(6);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(40, 0, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([45], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ContextualFormat2_UncoveredGlyph_LeavesGlyphsUnchanged()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetContextualLookup(6);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(41, 0, 1) }; // not in Coverage
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([41], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ChainingContextFormat1_MatchedBacktrackInputLookahead_AppliesNestedSingleSubstitution()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetChainingContextLookup(8);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(90, 0, 1), new(91, 1, 1), new(92, 2, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([90, 95, 92], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ChainingContextFormat1_MissingLookahead_LeavesGlyphsUnchanged()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetChainingContextLookup(8);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(90, 0, 1), new(91, 1, 1) }; // no lookahead glyph
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([90, 91], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ChainingContextFormat2_ClassMatchedBacktrackInputLookahead_AppliesNestedSingleSubstitution()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetChainingContextLookup(10);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(99, 0, 1), new(100, 1, 1), new(102, 2, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([99, 105, 102], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ChainingContextFormat2_UnclassifiedBacktrack_LeavesGlyphsUnchanged()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetChainingContextLookup(10);
            Assert.NotNull(lookup);

            // glyph 999 isn't classified into class 1 by the backtrack ClassDef - no match.
            var glyphs = new List<ShapedGlyph> { new(999, 0, 1), new(100, 1, 1), new(102, 2, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([999, 100, 102], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ContextualFormat1_NestedMultipleSubstitution_ExpandsMatchedPosition()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetContextualLookup(12);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(120, 0, 1), new(121, 1, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([124, 125, 121], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void ContextualFormat1_NestedAlternateSubstitution_UsesFirstAlternate()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetContextualLookup(14);
            Assert.NotNull(lookup);

            var glyphs = new List<ShapedGlyph> { new(130, 0, 1), new(131, 1, 1) };
            GsubShaper.ApplySequenceContextLookup(gsub, lookup.Subtables, glyphs, gdef: null, lookupFlag: 0, markFilteringSet: null);

            Assert.Equal([135, 131], glyphs.ConvertAll(g => g.GlyphIndex));
        }

        [Fact]
        public void GetResolvedLookupType_ReportsEachNewLookupTypeCorrectly()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            Assert.Equal(2, gsub.GetResolvedLookupType(0));
            Assert.Equal(5, gsub.GetResolvedLookupType(2));
            Assert.Equal(6, gsub.GetResolvedLookupType(4));
        }

        [Fact]
        public void MismatchedLookupTypeAccessors_ReturnNull()
        {
            var (face, tableStart) = BuildFaceWithSyntheticGsub();
            var gsub = new GsubTable(face, tableStart);

            // Lookup 0 is Type 2 - every other reader must decline it.
            Assert.Null(gsub.GetLigatureLookup(0));
            Assert.Null(gsub.GetSingleSubstitutionLookup(0));
            Assert.Null(gsub.GetAlternateSubstitutionLookup(0));
            Assert.Null(gsub.GetContextualLookup(0));
            Assert.Null(gsub.GetChainingContextLookup(0));
            // Lookup 2 is Type 5 - the chaining reader must decline it, and vice versa for lookup 4.
            Assert.Null(gsub.GetChainingContextLookup(2));
            Assert.Null(gsub.GetContextualLookup(4));
        }
    }
}

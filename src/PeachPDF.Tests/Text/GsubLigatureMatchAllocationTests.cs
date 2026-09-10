using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace PeachPDF.Tests.Text
{
    /// <summary>
    /// A ligature lookup walks the glyph run without allocating per glyph position, and reuses its
    /// match buffers across positions without carrying anything between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GsubShaper.ApplyLigatureLookup"/> tries a match at <b>every</b> position in the run,
    /// and every attempt used to allocate: an empty <c>skippedOffsets</c> list before it had even
    /// looked at a coverage table, a fresh pair of match lists per candidate ligature, and a boxed
    /// enumerator for the interface-typed <c>Subtables</c> walk. Almost every one of those attempts
    /// fails - a run of ordinary text matches no ligature at most positions - so the whole cost was
    /// paid to discard it.
    /// </para>
    /// <para>
    /// It became worth fixing when <c>ccmp</c>/<c>locl</c> started applying to every run rather than
    /// only to Arabic and USE text. Those features are Type 4 (ligature) lookups in real fonts, so
    /// text that previously ran no ligature lookup at all now runs one over every position.
    /// </para>
    /// <para>
    /// Asserted against the lookup rather than a rendered document because allocation over a document
    /// scales with whatever font a machine resolves. Here the walk is the only thing running, which
    /// makes the bound exact and identical on every platform.
    /// </para>
    /// </remarks>
    public class GsubLigatureMatchAllocationTests
    {
        [Fact]
        public void WalkingARunForLigatures_AllocatesNothingPerPosition()
        {
            // One subtable whose coverage matches nothing, so every position is tried and every
            // attempt fails - the only thing left that can allocate is the walk itself.
            //
            // NOT an empty subtable list: List<T> hands back a cached singleton enumerator when it is
            // empty, so the boxing under test disappears and the test passes against unfixed code.
            var subtable = new GsubLigatureSubtable
            {
                Coverage = MatchesNothing(),
                LigatureSets = [],
            };
            var lookup = new GsubLigatureLookup
            {
                Subtables = new List<GsubLigatureSubtable> { subtable },
            };

            var glyphs = new List<ShapedGlyph>();
            for (var i = 0; i < 200; i++) glyphs.Add(new ShapedGlyph(i, i, 1));

            // Warm: JIT the whole path before anything is counted.
            for (var i = 0; i < 3; i++) GsubShaper.ApplyLigatureLookup(lookup, glyphs, gdef: null);

            // Per THREAD, not process-wide: the suite runs collections in parallel, so
            // GC.GetTotalAllocatedBytes would count whatever every other test is allocating at the
            // same time. This body is synchronous and never awaits, so it stays on one thread.
            const int passes = 50;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < passes; i++) GsubShaper.ApplyLigatureLookup(lookup, glyphs, gdef: null);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // 200 positions x 50 passes is 10,000 match attempts. Unfixed that is an empty list plus a
            // boxed enumerator each, over half a megabyte; fixed it is nothing at all. A few hundred
            // bytes of slack rather than zero, so this states "not per position" without depending on
            // the runtime never allocating anything incidental.
            Assert.True(allocated < 4096,
                $"{passes * glyphs.Count:N0} ligature match attempts allocated {allocated:N0} bytes. They "
                + "should allocate nothing: a position that matches nothing has no result to build, and "
                + "the subtable list is interface-typed so a foreach over it boxes an enumerator.");
        }

        /// <summary>
        /// Two ligatures forming in one run, the first of them over a skipped mark. The match buffers
        /// are reused across positions now, so this is what would show it if anything survived from
        /// one match into the next: the second ligature would re-insert the first one's skipped glyph.
        /// </summary>
        [Fact]
        public void TwoLigaturesInOneRun_DoNotCarryTheFirstMatchsSkippedGlyph()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            byte[] gdefBytes = BuildGdefMarkingGlyph460();
            byte[] gsubBytes = BuildLigatureGsub();

            int gdefStart = fontBytes.Length;
            int gsubStart = gdefStart + gdefBytes.Length;
            var face = XFontSource.GetOrCreateFrom(Concat(fontBytes, gdefBytes, gsubBytes)).Fontface;

            var gdef = new GdefTable(face, gdefStart);
            var lookup = new GsubTable(face, gsubStart).GetLigatureLookup(0);
            Assert.NotNull(lookup);

            // 400 460 401 | 400 401 - the first pair straddles the mark, the second does not.
            var glyphs = new List<ShapedGlyph>
            {
                new(400, 0, 1),
                new(460, 1, 1),
                new(401, 2, 1),
                new(400, 3, 1),
                new(401, 4, 1),
            };

            GsubShaper.ApplyLigatureLookup(lookup, glyphs, gdef);

            // Both pairs merge to 450. The mark stays in the stream, immediately after the ligature it
            // was skipped inside - and appears exactly once. A skipped-offset list left uncleared
            // between matches puts it back a second time, after the second ligature as well.
            Assert.Equal([450, 460, 450], glyphs.ConvertAll(g => g.GlyphIndex));

            // Each ligature reports its own two component cluster starts, not the first match's.
            Assert.Equal([0, 2], glyphs[0].LigatureComponentClusterStarts!);
            Assert.Equal([3, 4], glyphs[2].LigatureComponentClusterStarts!);
        }

        /// <summary>
        /// A ligature applied as a <b>nested</b> lookup, invoked by a Type 5 contextual rule rather
        /// than by the run walk. That path reaches <c>ApplyLigatureAt</c> for a single position and so
        /// owns its own scratch, which starts empty and is filled only if a candidate is tried.
        /// </summary>
        [Fact]
        public void ALigatureInvokedFromAContextualRule_StillForms()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            byte[] gsubBytes = BuildContextualIntoLigatureGsub();

            int gsubStart = fontBytes.Length;
            var face = XFontSource.GetOrCreateFrom(Concat(fontBytes, gsubBytes)).Fontface;
            var gsub = new GsubTable(face, gsubStart);

            var contextual = gsub.GetContextualLookup(0);
            Assert.NotNull(contextual);

            var glyphs = new List<ShapedGlyph> { new(400, 0, 1), new(401, 1, 1) };

            GsubShaper.ApplySequenceContextLookup(gsub, contextual.Subtables, glyphs, gdef: null,
                contextual.LookupFlag, markFilteringSet: null);

            // The contextual rule matched 400 401 and ran lookup 1 - the ligature - at position 0.
            Assert.Equal([450], glyphs.ConvertAll(g => g.GlyphIndex));
            Assert.Equal([0, 1], glyphs[0].LigatureComponentClusterStarts!);
        }

        /// <summary>Two lookups: index 0 is a Type 5 (sequence context, format 1) whose only rule
        /// matches 400 401 and invokes lookup 1 at sequence index 0; index 1 is the Type 4 ligature
        /// 400 + 401 -&gt; 450.</summary>
        private static byte[] BuildContextualIntoLigatureGsub()
        {
            var b = new SfntByteBuilder();
            b.U16(1); b.U16(0);
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();
            b.PatchU16(scriptListOffsetAt, b.Position); b.U16(0);
            b.PatchU16(featureListOffsetAt, b.Position); b.U16(0);

            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(2); // lookupCount
            int lookup0At = b.PlaceholderU16();
            int lookup1At = b.PlaceholderU16();

            // ---- lookup 0: Type 5, format 1 ----
            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(5); b.U16(0); b.U16(1); // lookupType=5, no flags, subtableCount=1
            int sub0OffsetAt = b.PlaceholderU16();

            int sub0Start = b.Position;
            b.PatchU16(sub0OffsetAt, sub0Start - lookup0Start);
            b.U16(1); // format 1
            int ctxCoverageOffsetAt = b.PlaceholderU16();
            b.U16(1); // seqRuleSetCount
            int ruleSetOffsetAt = b.PlaceholderU16();

            int ruleSetStart = b.Position;
            b.PatchU16(ruleSetOffsetAt, ruleSetStart - sub0Start);
            b.U16(1); // seqRuleCount
            int ruleOffsetAt = b.PlaceholderU16();

            b.PatchU16(ruleOffsetAt, b.Position - ruleSetStart);
            b.U16(2);   // glyphCount, counting the coverage-matched first glyph
            b.U16(1);   // seqLookupCount
            b.U16(401); // the one further input glyph
            b.U16(0); b.U16(1); // record: at sequence index 0, run lookup 1

            b.PatchU16(ctxCoverageOffsetAt, b.Position - sub0Start);
            b.U16(1); b.U16(1); b.U16(400);

            // ---- lookup 1: Type 4 ligature ----
            int lookup1Start = b.Position;
            b.PatchU16(lookup1At, lookup1Start - lookupListStart);
            b.U16(4); b.U16(0); b.U16(1);
            int sub1OffsetAt = b.PlaceholderU16();

            int sub1Start = b.Position;
            b.PatchU16(sub1OffsetAt, sub1Start - lookup1Start);
            b.U16(1); // substFormat
            int ligCoverageOffsetAt = b.PlaceholderU16();
            b.U16(1); // ligatureSetCount
            int ligSetOffsetAt = b.PlaceholderU16();

            int ligSetStart = b.Position;
            b.PatchU16(ligSetOffsetAt, ligSetStart - sub1Start);
            b.U16(1); // ligatureCount
            int ligOffsetAt = b.PlaceholderU16();

            b.PatchU16(ligOffsetAt, b.Position - ligSetStart);
            b.U16(450); b.U16(2); b.U16(401);

            b.PatchU16(ligCoverageOffsetAt, b.Position - sub1Start);
            b.U16(1); b.U16(1); b.U16(400);

            return b.ToArray();
        }

        /// <summary>GDEF classifying glyph 460 as a Mark, which is what <c>IGNORE_MARKS</c> on the
        /// lookup below acts on.</summary>
        private static byte[] BuildGdefMarkingGlyph460()
        {
            var b = new SfntByteBuilder();
            b.U16(1); b.U16(0); // version 1.0
            int glyphClassDefOffsetAt = b.PlaceholderU16();
            b.U16(0); b.U16(0); b.U16(0); // attachList, ligCaretList, markAttachClassDef - all null
            int glyphClassDefStart = b.Position;
            b.PatchU16(glyphClassDefOffsetAt, glyphClassDefStart);
            b.U16(2); // ClassDef format 2
            b.U16(1); // classRangeCount
            b.U16(460); b.U16(460); b.U16(3); // glyph 460 -> class 3 (Mark)
            return b.ToArray();
        }

        /// <summary>A single Type 4 (Ligature) lookup with <c>IGNORE_MARKS</c>: coverage {400},
        /// component glyph 401, ligature glyph 450.</summary>
        private static byte[] BuildLigatureGsub()
        {
            var b = new SfntByteBuilder();
            b.U16(1); b.U16(0);
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();
            b.PatchU16(scriptListOffsetAt, b.Position); b.U16(0);
            b.PatchU16(featureListOffsetAt, b.Position); b.U16(0);

            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(1); // lookupCount
            int lookup0At = b.PlaceholderU16();

            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            const ushort ignoreMarks = 0x0008;
            b.U16(4); b.U16(ignoreMarks); b.U16(1); // lookupType=4, IGNORE_MARKS, subtableCount=1
            int subOffsetAt = b.PlaceholderU16();

            int subtableStart = b.Position;
            b.PatchU16(subOffsetAt, subtableStart - lookup0Start);
            b.U16(1); // substFormat
            int coverageOffsetAt = b.PlaceholderU16();
            b.U16(1); // ligatureSetCount
            int ligSetOffsetAt = b.PlaceholderU16();

            int ligSetStart = b.Position;
            b.PatchU16(ligSetOffsetAt, ligSetStart - subtableStart);
            b.U16(1); // ligatureCount
            int ligOffsetAt = b.PlaceholderU16();

            b.PatchU16(ligOffsetAt, b.Position - ligSetStart);
            b.U16(450); // ligatureGlyph
            b.U16(2);   // componentCount (the coverage-matched glyph plus one component)
            b.U16(401); // component glyph

            b.PatchU16(coverageOffsetAt, b.Position - subtableStart);
            b.U16(1); b.U16(1); b.U16(400); // coverage format 1, one glyph: 400

            return b.ToArray();
        }

        private static byte[] Concat(params byte[][] chunks)
        {
            var total = 0;
            foreach (var c in chunks) total += c.Length;
            var result = new byte[total];
            var pos = 0;
            foreach (var c in chunks) { c.CopyTo(result, pos); pos += c.Length; }
            return result;
        }

        /// <summary>
        /// A <see cref="CoverageTable"/> covering no glyph at all. Its constructor is private and its
        /// only route in reads a real font, so this leaves both backing arrays null - the state
        /// <see cref="CoverageTable.IndexOfGlyph"/> already answers -1 for.
        /// </summary>
        private static CoverageTable MatchesNothing() =>
            (CoverageTable)RuntimeHelpers.GetUninitializedObject(typeof(CoverageTable));
    }
}

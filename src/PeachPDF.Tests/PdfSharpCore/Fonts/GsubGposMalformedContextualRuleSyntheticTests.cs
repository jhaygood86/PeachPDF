using System.IO;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Regression coverage for a real crash risk in <see cref="GsubTable.ReadSequenceRule"/>/
    /// <see cref="GsubTable.ReadChainedSequenceRule"/> and their <see cref="GposTable"/> twins: per
    /// the OpenType spec a Contextual/Chained-Context Substitution/Positioning rule's own
    /// <c>glyphCount</c>/<c>inputGlyphCount</c> field always includes the first glyph (already matched
    /// via the subtable's own <c>Coverage</c> table), so the remaining <c>Input</c> array is sized
    /// <c>glyphCount - 1</c> - a spec-conformant font always has <c>glyphCount &gt;= 1</c>, but nothing
    /// stopped a malformed/corrupt font from claiming <c>glyphCount == 0</c>, which computed a negative
    /// array length and crashed the whole render with an <see cref="System.OverflowException"/> instead
    /// of degrading gracefully. Fixed by clamping to zero.
    /// </summary>
    public class GsubGposMalformedContextualRuleSyntheticTests
    {
        // Coverage {50}; one ruleSet with one rule: glyphCount=0 (malformed - spec requires >= 1),
        // seqLookupCount=0.
        private static byte[] BuildSyntheticGsubWithMalformedRule()
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
            b.U16(1); // lookupCount
            int lookup0At = b.PlaceholderU16();

            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(5); b.U16(0); b.U16(1); // lookupType 5 (Contextual), lookupFlag, subtableCount
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format 1
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // seqRuleSetCount
                int ruleSetOffsetAt = b.PlaceholderU16();
                int ruleSetStart = b.Position;
                b.PatchU16(ruleSetOffsetAt, ruleSetStart - subtableStart);
                b.U16(1); // seqRuleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSetStart);
                b.U16(0); // glyphCount - malformed: spec requires >= 1
                b.U16(0); // seqLookupCount
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(50); // coverage format 1, 1 glyph, glyph 50
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

        [Fact]
        public void Gsub_ContextualFormat1_MalformedZeroGlyphCountRule_DoesNotThrow()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, BuildSyntheticGsubWithMalformedRule());
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;

            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetContextualLookup(0);

            Assert.NotNull(lookup);
            var rule = Assert.Single(Assert.Single(lookup!.Subtables).RuleSets![0]);
            // A malformed glyphCount=0 degrades to an empty (never able to match beyond its own
            // coverage-matched first glyph) Input array rather than throwing.
            Assert.Empty(rule.Input);
        }

        // Coverage {50}; backtrack=[], one ruleSet with one rule: inputGlyphCount=0 (malformed),
        // lookahead=[], seqLookupCount=0.
        private static byte[] BuildSyntheticGsubWithMalformedChainedRule()
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
            b.U16(1);
            int lookup0At = b.PlaceholderU16();

            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(6); b.U16(0); b.U16(1); // lookupType 6 (Chaining Context)
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format 1
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // chainSeqRuleSetCount
                int ruleSetOffsetAt = b.PlaceholderU16();
                int ruleSetStart = b.Position;
                b.PatchU16(ruleSetOffsetAt, ruleSetStart - subtableStart);
                b.U16(1); // chainSeqRuleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSetStart);
                b.U16(0); // backtrackGlyphCount
                b.U16(0); // inputGlyphCount - malformed: spec requires >= 1
                b.U16(0); // lookaheadGlyphCount
                b.U16(0); // seqLookupCount
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(50); // coverage format 1, 1 glyph, glyph 50
            }

            return b.ToArray();
        }

        [Fact]
        public void Gsub_ChainingFormat1_MalformedZeroInputGlyphCountRule_DoesNotThrow()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, BuildSyntheticGsubWithMalformedChainedRule());
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;

            var gsub = new GsubTable(face, tableStart);
            var lookup = gsub.GetChainingContextLookup(0);

            Assert.NotNull(lookup);
            var rule = Assert.Single(Assert.Single(lookup!.Subtables).RuleSets![0]);
            Assert.Empty(rule.Input);
        }

        // The GPOS reader shares the identical rule format/bug - same fixture shape, GposTable instead.
        private static byte[] BuildSyntheticGposWithMalformedRule()
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
            b.U16(1);
            int lookup0At = b.PlaceholderU16();

            int lookup0Start = b.Position;
            b.PatchU16(lookup0At, lookup0Start - lookupListStart);
            b.U16(7); b.U16(0); b.U16(1); // lookupType 7 (Context Positioning)
            int lookup0SubAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubAt, lookup0SubStart - lookup0Start);
            {
                int subtableStart = b.Position;
                b.U16(1); // format 1
                int coverageOffsetAt = b.PlaceholderU16();
                b.U16(1); // seqRuleSetCount
                int ruleSetOffsetAt = b.PlaceholderU16();
                int ruleSetStart = b.Position;
                b.PatchU16(ruleSetOffsetAt, ruleSetStart - subtableStart);
                b.U16(1); // seqRuleCount
                int ruleOffsetAt = b.PlaceholderU16();
                int ruleStart = b.Position;
                b.PatchU16(ruleOffsetAt, ruleStart - ruleSetStart);
                b.U16(0); // glyphCount - malformed: spec requires >= 1
                b.U16(0); // seqLookupCount
                int coverageStart = b.Position;
                b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
                b.U16(1); b.U16(1); b.U16(50); // coverage format 1, 1 glyph, glyph 50
            }

            return b.ToArray();
        }

        [Fact]
        public void Gpos_ContextualFormat1_MalformedZeroGlyphCountRule_DoesNotThrow()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, BuildSyntheticGposWithMalformedRule());
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;

            var gpos = new GposTable(face, tableStart);
            var lookup = gpos.GetContextualLookup(0);

            Assert.NotNull(lookup);
            var rule = Assert.Single(Assert.Single(lookup!.Subtables).RuleSets![0]);
            Assert.Empty(rule.Input);
        }
    }
}

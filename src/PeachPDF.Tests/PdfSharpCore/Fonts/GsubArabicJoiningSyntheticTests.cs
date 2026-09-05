using System.Collections.Generic;
using System.IO;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using PeachPDF.Text.Shaping.Arabic;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Coverage for <see cref="GsubShaper.ApplyArabicJoiningFeatures"/> - the per-position joining-form
    /// GSUB application that consumes <see cref="ArabicJoiningShaper"/>'s resolved forms (issue #533).
    /// Layout: one script "arab" (DefaultLangSys) referencing features "init" (-&gt; lookup 0, glyph 10 ->
    /// 110), "medi" (-&gt; lookup 1, glyph 20 -&gt; 120), "fina" (-&gt; lookup 2, glyph 30 -&gt; 130), "isol"
    /// (-&gt; lookup 3, glyph 40 -&gt; 140) - deliberately no "fin2"/"fin3"/"med2" feature anywhere, so a
    /// form the font doesn't define can be proven to no-op rather than crash.
    /// </summary>
    public class GsubArabicJoiningSyntheticTests
    {
        private static void WriteType1Format1Subtable(SfntByteBuilder b, ushort firstGlyph, short delta)
        {
            int subtableStart = b.Position;
            b.U16(1); // substFormat
            int coverageOffsetAt = b.PlaceholderU16();
            b.U16(unchecked((ushort)delta));

            int coverageStart = b.Position;
            b.PatchU16(coverageOffsetAt, coverageStart - subtableStart);
            b.U16(1); // coverage format 1
            b.U16(1); // glyphCount
            b.U16(firstGlyph);
        }

        private static byte[] BuildSyntheticGsub()
        {
            var b = new SfntByteBuilder();

            // ---- Header ----
            b.U16(1); b.U16(0);
            int scriptListOffsetAt = b.PlaceholderU16();
            int featureListOffsetAt = b.PlaceholderU16();
            int lookupListOffsetAt = b.PlaceholderU16();

            // ---- ScriptList ----
            int scriptListStart = b.Position;
            b.PatchU16(scriptListOffsetAt, scriptListStart);
            b.U16(1); // scriptCount
            b.Tag("arab"); int arabOffsetAt = b.PlaceholderU16();

            int arabStart = b.Position;
            b.PatchU16(arabOffsetAt, arabStart - scriptListStart);
            int arabLangSysOffsetAt = b.PlaceholderU16();
            b.U16(0); // langSysCount
            int arabLangSysStart = b.Position;
            b.PatchU16(arabLangSysOffsetAt, arabLangSysStart - arabStart);
            b.U16(0); // lookupOrder
            b.U16(0xFFFF); // requiredFeatureIndex - none
            b.U16(4); // featureIndexCount
            b.U16(0); // -> feature 0 ("init")
            b.U16(1); // -> feature 1 ("medi")
            b.U16(2); // -> feature 2 ("fina")
            b.U16(3); // -> feature 3 ("isol")

            // ---- FeatureList ----
            int featureListStart = b.Position;
            b.PatchU16(featureListOffsetAt, featureListStart);
            b.U16(4); // featureCount
            b.Tag("init"); int feat0OffsetAt = b.PlaceholderU16();
            b.Tag("medi"); int feat1OffsetAt = b.PlaceholderU16();
            b.Tag("fina"); int feat2OffsetAt = b.PlaceholderU16();
            b.Tag("isol"); int feat3OffsetAt = b.PlaceholderU16();

            int feat0Start = b.Position;
            b.PatchU16(feat0OffsetAt, feat0Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(0); // -> lookup 0

            int feat1Start = b.Position;
            b.PatchU16(feat1OffsetAt, feat1Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(1); // -> lookup 1

            int feat2Start = b.Position;
            b.PatchU16(feat2OffsetAt, feat2Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(2); // -> lookup 2

            int feat3Start = b.Position;
            b.PatchU16(feat3OffsetAt, feat3Start - featureListStart);
            b.U16(0); b.U16(1); b.U16(3); // -> lookup 3

            // ---- LookupList ----
            int lookupListStart = b.Position;
            b.PatchU16(lookupListOffsetAt, lookupListStart);
            b.U16(4); // lookupCount
            int lookup0OffsetAt = b.PlaceholderU16();
            int lookup1OffsetAt = b.PlaceholderU16();
            int lookup2OffsetAt = b.PlaceholderU16();
            int lookup3OffsetAt = b.PlaceholderU16();

            // Lookup 0 ("init"): glyph 10 -> 110.
            int lookup0Start = b.Position;
            b.PatchU16(lookup0OffsetAt, lookup0Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup0SubOffsetAt = b.PlaceholderU16();
            int lookup0SubStart = b.Position;
            b.PatchU16(lookup0SubOffsetAt, lookup0SubStart - lookup0Start);
            WriteType1Format1Subtable(b, firstGlyph: 10, delta: 100);

            // Lookup 1 ("medi"): glyph 20 -> 120.
            int lookup1Start = b.Position;
            b.PatchU16(lookup1OffsetAt, lookup1Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup1SubOffsetAt = b.PlaceholderU16();
            int lookup1SubStart = b.Position;
            b.PatchU16(lookup1SubOffsetAt, lookup1SubStart - lookup1Start);
            WriteType1Format1Subtable(b, firstGlyph: 20, delta: 100);

            // Lookup 2 ("fina"): glyph 30 -> 130.
            int lookup2Start = b.Position;
            b.PatchU16(lookup2OffsetAt, lookup2Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup2SubOffsetAt = b.PlaceholderU16();
            int lookup2SubStart = b.Position;
            b.PatchU16(lookup2SubOffsetAt, lookup2SubStart - lookup2Start);
            WriteType1Format1Subtable(b, firstGlyph: 30, delta: 100);

            // Lookup 3 ("isol"): glyph 40 -> 140.
            int lookup3Start = b.Position;
            b.PatchU16(lookup3OffsetAt, lookup3Start - lookupListStart);
            b.U16(1); b.U16(0); b.U16(1);
            int lookup3SubOffsetAt = b.PlaceholderU16();
            int lookup3SubStart = b.Position;
            b.PatchU16(lookup3SubOffsetAt, lookup3SubStart - lookup3Start);
            WriteType1Format1Subtable(b, firstGlyph: 40, delta: 100);

            return b.ToArray();
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var combined = new byte[a.Length + b.Length];
            a.CopyTo(combined, 0);
            b.CopyTo(combined, a.Length);
            return combined;
        }

        private static GsubTable BuildGsub()
        {
            byte[] fontBytes = File.ReadAllBytes(BundledFonts.Ttf);
            int tableStart = fontBytes.Length;
            byte[] combined = Concat(fontBytes, BuildSyntheticGsub());
            var face = XFontSource.GetOrCreateFrom(combined).Fontface;
            return new GsubTable(face, tableStart);
        }

        [Fact]
        public void EachPosition_SubstitutesAccordingToItsOwnResolvedForm()
        {
            var gsub = BuildGsub();
            var glyphs = new List<ShapedGlyph>
            {
                new(10, 0, 1), // Init
                new(20, 1, 1), // Medi
                new(30, 2, 1), // Fina
                new(40, 3, 1), // Isol
            };
            var forms = new Dictionary<int, ArabicJoiningForm>
            {
                [0] = ArabicJoiningForm.Init,
                [1] = ArabicJoiningForm.Medi,
                [2] = ArabicJoiningForm.Fina,
                [3] = ArabicJoiningForm.Isol,
            };

            GsubShaper.ApplyArabicJoiningFeatures(gsub, glyphs, forms, languageTag: null, scriptPreference: ["arab"]);

            Assert.Equal(110, glyphs[0].GlyphIndex);
            Assert.Equal(120, glyphs[1].GlyphIndex);
            Assert.Equal(130, glyphs[2].GlyphIndex);
            Assert.Equal(140, glyphs[3].GlyphIndex);
        }

        [Fact]
        public void None_LeavesGlyphUnchanged()
        {
            var gsub = BuildGsub();
            var glyphs = new List<ShapedGlyph> { new(10, 0, 1) };
            var forms = new Dictionary<int, ArabicJoiningForm> { [0] = ArabicJoiningForm.None };

            GsubShaper.ApplyArabicJoiningFeatures(gsub, glyphs, forms, languageTag: null, scriptPreference: ["arab"]);

            Assert.Equal(10, glyphs[0].GlyphIndex);
        }

        [Fact]
        public void FormTheFontDoesNotDefine_NoOpsRatherThanThrowing()
        {
            // This synthetic font defines no "fin2"/"fin3"/"med2" feature at all - a real Arabic font
            // (Syriac-only forms) lacking one of these must silently leave the glyph as its nominal
            // (pre-joining) form, not throw.
            var gsub = BuildGsub();
            var glyphs = new List<ShapedGlyph> { new(10, 0, 1) };
            var forms = new Dictionary<int, ArabicJoiningForm> { [0] = ArabicJoiningForm.Fin2 };

            GsubShaper.ApplyArabicJoiningFeatures(gsub, glyphs, forms, languageTag: null, scriptPreference: ["arab"]);

            Assert.Equal(10, glyphs[0].GlyphIndex);
        }

        [Fact]
        public void EmptyGlyphsOrForms_DoesNotThrow()
        {
            var gsub = BuildGsub();
            var noForms = new Dictionary<int, ArabicJoiningForm>();

            GsubShaper.ApplyArabicJoiningFeatures(gsub, [], noForms, languageTag: null, scriptPreference: ["arab"]);
            GsubShaper.ApplyArabicJoiningFeatures(gsub, new List<ShapedGlyph> { new(10, 0, 1) }, noForms, languageTag: null, scriptPreference: ["arab"]);
        }

        [Fact]
        public void ClusterLengthZeroGlyph_SkippedRatherThanMisassigned()
        {
            // A trailing zero-length-cluster glyph (e.g. one an earlier decomposition stage inserted -
            // see GsubShaper.Shape's own ccmp/locl pre-stage remarks) must never itself be treated as a
            // joining position, even if formsByClusterStart happens to have an entry at its ClusterStart
            // (here, coincidentally the same offset the primary glyph before it already claimed).
            var gsub = BuildGsub();
            var glyphs = new List<ShapedGlyph>
            {
                new(10, 0, 1), // primary glyph at ClusterStart 0 - Init
                new(999, 1, 0), // trailing zero-length glyph anchored past the source span
            };
            var forms = new Dictionary<int, ArabicJoiningForm> { [0] = ArabicJoiningForm.Init, [1] = ArabicJoiningForm.Init };

            GsubShaper.ApplyArabicJoiningFeatures(gsub, glyphs, forms, languageTag: null, scriptPreference: ["arab"]);

            Assert.Equal(110, glyphs[0].GlyphIndex); // the real position substitutes normally
            Assert.Equal(999, glyphs[1].GlyphIndex); // the zero-length glyph is left untouched
        }

        [Fact]
        public void IsEmpty_JoiningFormsOnly_NoOtherFeatureRequested_IsNotEmpty()
        {
            // Regression: TextShapingFeatures.JoiningForms must participate in GsubShaper.IsEmpty's
            // early-return check - a run requesting JoiningForms but nothing else (every other field at
            // its "empty" value) must not skip GSUB substitution entirely in Shape.
            var forms = new[] { ArabicJoiningForm.Init };
            var withJoiningForms = new TextShapingFeatures(Ligatures: LigatureFeatures.None, ScriptTag: "arab", JoiningForms: forms);
            var trulyEmpty = new TextShapingFeatures(Ligatures: LigatureFeatures.None);

            Assert.False(GsubShaper.IsEmpty(withJoiningForms));
            Assert.True(GsubShaper.IsEmpty(trulyEmpty));
        }
    }
}

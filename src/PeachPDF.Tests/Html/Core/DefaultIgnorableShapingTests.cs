using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using PeachPDF.Text.Shaping.Arabic;
using System.IO;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Unicode <c>Default_Ignorable_Code_Point</c> handling in the shaping pipeline, and the GSUB
    /// <c>ccmp</c> application it sits next to. Driven against <see cref="BundledFonts.CcmpLigature"/>,
    /// a fixture built to have exactly the two properties a modern color-emoji font has and a Latin
    /// text font does not: its only GSUB feature is <c>ccmp</c>, and it has no glyph for U+FE0F.
    ///
    /// Every assertion here would fail as a plain no-op regression, per this repo's "prove it isn't a
    /// no-op" convention: a shaper that ignores <c>ccmp</c> produces two unligated glyphs, and one that
    /// lets an unmapped variation selector through produces an extra <c>.notdef</c>.
    /// </summary>
    public class DefaultIgnorableShapingTests
    {
        private const int NotdefGlyph = 0;

        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.CcmpLigature)).Fontface;
            return new OpenTypeDescriptor("ccmp-lig-test", "ccmp-lig-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        private static int[] Shape(string text) =>
            Descriptor().Shape(text, TextShapingFeatures.Default).Select(g => g.GlyphIndex).ToArray();

        [Fact]
        public void CcmpLigature_AppliesWithoutAnyLigatureFeatureRequested()
        {
            // The font declares no liga/rlig/clig whatsoever - if ccmp is treated as opt-in, "AB" stays
            // two glyphs. This is the exact shape of the real defect: Noto Color Emoji keeps every
            // emoji-sequence ligature in ccmp, so country-flag pairs rendered as bare letters.
            var glyphs = Shape("AB");

            Assert.Single(glyphs);
            Assert.NotEqual(NotdefGlyph, glyphs[0]);
            Assert.NotEqual(Shape("A")[0], glyphs[0]);
            Assert.NotEqual(Shape("B")[0], glyphs[0]);
        }

        [Fact]
        public void UnmappedVariationSelector_IsDroppedRatherThanDrawnAsNotdef()
        {
            // U+FE0F is Default_Ignorable_Code_Point and absent from this font's cmap. Rendering it as
            // .notdef is what put a visible tofu box after a heart emoji in a real COLR font.
            var glyphs = Shape("S️");

            Assert.Single(glyphs);
            Assert.Equal(Shape("S")[0], glyphs[0]);
            Assert.DoesNotContain(NotdefGlyph, glyphs);
        }

        [Fact]
        public void HiddenIgnorableBetweenComponents_DoesNotBlockTheLigature()
        {
            // The ligature's component list is [B] after coverage glyph A - it never mentions U+FE0F.
            // Matching therefore has to step over the hidden ignorable (HarfBuzz's SKIP_MAYBE) for this
            // to ligate at all, and the stepped-over glyph must not survive into the output.
            var withSelector = Shape("A️B");

            Assert.Equal(Shape("AB"), withSelector);
            Assert.DoesNotContain(NotdefGlyph, withSelector);
        }

        [Theory]
        // A representative codepoint from each contiguous run of the property, so a mistyped range
        // boundary in UnicodeDefaultIgnorables shows up as a concrete failure rather than a silent gap.
        [InlineData(0x00AD)] // SOFT HYPHEN
        [InlineData(0x034F)] // COMBINING GRAPHEME JOINER
        [InlineData(0x061C)] // ARABIC LETTER MARK
        [InlineData(0x115F)] // HANGUL CHOSEONG FILLER
        [InlineData(0x17B4)] // KHMER VOWEL INHERENT AQ
        [InlineData(0x180E)] // MONGOLIAN VOWEL SEPARATOR
        [InlineData(0x200D)] // ZERO WIDTH JOINER
        [InlineData(0x202E)] // RIGHT-TO-LEFT OVERRIDE
        [InlineData(0x2060)] // WORD JOINER
        [InlineData(0x2069)] // POP DIRECTIONAL ISOLATE
        [InlineData(0x3164)] // HANGUL FILLER
        [InlineData(0xFE0F)] // VARIATION SELECTOR-16
        [InlineData(0xFEFF)] // ZERO WIDTH NO-BREAK SPACE
        [InlineData(0xFFA0)] // HALFWIDTH HANGUL FILLER
        [InlineData(0xFFF0)] // reserved-for-future-ignorable
        [InlineData(0x1BCA0)] // SHORTHAND FORMAT LETTER OVERLAP
        [InlineData(0x1D173)] // MUSICAL SYMBOL BEGIN BEAM
        [InlineData(0xE0061)] // TAG LATIN SMALL LETTER A
        [InlineData(0xE0100)] // VARIATION SELECTOR-17
        public void EveryDefaultIgnorableRange_IsDroppedFromTheGlyphRun(int codepoint)
        {
            var glyphs = Shape("S" + char.ConvertFromUtf32(codepoint));

            Assert.Single(glyphs);
            Assert.Equal(Shape("S")[0], glyphs[0]);
        }

        [Fact]
        public void DroppingAnIgnorable_RemapsAMarkGlyphsAttachmentIndex()
        {
            // AttachedToIndex is a *glyph-list* index, so deleting a hidden ignorable has to rewrite it.
            // Needs a font that actually attaches marks: the Arabic subset's ccmp decomposes BEH into a
            // base plus a separate dot mark, and GPOS mark-to-base then records the base's index on the
            // mark. Appending an unmapped U+FE0F puts a dropped glyph in the same run, so a missing
            // remap would leave the mark anchored to a stale slot (or past the end of the list).
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Arabic)).Fontface;
            var descriptor = new OpenTypeDescriptor("arabic-ignorable-test", "arabic-ignorable-test",
                XFontStyle.Regular, face, new XPdfFontOptions(PdfFontEncoding.Unicode));

            const string beh = "ب";
            var forms = ArabicJoiningShaper.Resolve([beh[0]]);

            var plain = descriptor.Shape(beh, new TextShapingFeatures(ScriptTag: "arab", JoiningForms: forms));
            var withSelector = descriptor.Shape(beh + "️",
                new TextShapingFeatures(ScriptTag: "arab", JoiningForms: forms));

            // The selector contributes no glyph of its own, and changes nothing about the rest.
            Assert.Equal(plain.Select(g => g.GlyphIndex), withSelector.Select(g => g.GlyphIndex));
            Assert.DoesNotContain(NotdefGlyph, withSelector.Select(g => g.GlyphIndex));

            // Every surviving attachment must still point at a real slot in the *new* list.
            foreach (var glyph in withSelector)
            {
                if (glyph.AttachedToIndex is { } attachedTo)
                    Assert.InRange(attachedTo, 0, withSelector.Count - 1);
            }

            Assert.Equal(plain.Select(g => g.AttachedToIndex), withSelector.Select(g => g.AttachedToIndex));
        }

        [Theory]
        // Immediately outside each range above - these are ordinary characters and must still resolve
        // normally (to .notdef here, since this fixture's cmap covers only A/B/S), never be swallowed.
        [InlineData(0x00AC)]
        [InlineData(0x0350)]
        [InlineData(0x1161)]
        [InlineData(0x2010)]
        [InlineData(0x2070)]
        [InlineData(0xFDFF)]
        [InlineData(0xFFF9)]
        [InlineData(0x1D17B)]
        [InlineData(0xE1000)]
        public void NonIgnorableNeighbours_AreNotDropped(int codepoint)
        {
            var glyphs = Shape("S" + char.ConvertFromUtf32(codepoint));

            Assert.Equal(2, glyphs.Length);
            Assert.Equal(NotdefGlyph, glyphs[1]);
        }
    }
}

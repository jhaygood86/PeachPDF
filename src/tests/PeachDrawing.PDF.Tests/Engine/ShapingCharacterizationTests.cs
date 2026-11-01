
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.Text.Shaping;
using PeachDrawing.PDF;


using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.PDF.Engine;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PeachDrawing.Text;

namespace PeachPDF.Tests.PdfEngineTests
{
    /// <summary>
    /// PeachPDF applies GSUB substitution (ligature/single/multiple/alternate, plus contextual/
    /// chaining - Lookup Types 1-6, formats 1/2/3 - see the engine's GSUB shaper), GPOS positioning
    /// (kerning and mark-to-base/mark-to-mark attachment - Lookup Types 1/2/4/6, see
    /// the engine's GPOS positioner), and a real UAX#9 Unicode Bidi Algorithm (see
    /// <see cref="PeachDrawing.Text.Unicode.Bidi"/>). Still no Arabic/Indic complex-script joining,
    /// GSUB Lookup Type 8 (reverse chaining single substitution), or GPOS Types 3/5/7/8 (cursive
    /// attachment, mark-to-ligature, contextual positioning). See docs/html-css-support.md "Text
    /// shaping" for the reader-facing note and .claude/accepted-gaps/no-text-shaping.md for what's
    /// still out of scope.
    /// </summary>
    public class ShapingCharacterizationTests
    {
        private static Typeface Descriptor(string fontPath) => TypefaceFixtures.Shared(fontPath);

        [Fact]
        public void GsubLigatureSubstitution_FfCollectsLigatureGlyph()
        {
            // Source Sans 3's GSUB `liga` feature ligates "ff"/"ft"/"fft" from a single ('f')
            // coverage glyph (confirmed against the font directly via fontTools) - notably not
            // "fi"/"fl", so this deliberately doesn't use the precomposed U+FB01 "ﬁ" ligature the
            // old (pre-GSUB) version of this test referenced.
            var descriptor = Descriptor(BundledFonts.Ttf); // Source Sans 3
            var f = descriptor.GlyphOf(new Rune('f'));

            var cmap = new CharacterMapInfo(TypefaceFixtures.FromFile(BundledFonts.Ttf));
            cmap.AddShapedText("ff", new ShapeSettings(LigatureSet.Default));

            // The merged ligature glyph is not the plain 'f' glyph, and its source text ("ff") is
            // recorded for ToUnicode - a codepoint-keyed CharacterToGlyphIndex entry can't carry it.
            Assert.Single(cmap.LigatureGlyphToText);
            var (ligatureGlyph, text) = cmap.LigatureGlyphToText.Single();
            Assert.NotEqual(f, ligatureGlyph);
            Assert.Equal("ff", text);
            Assert.Contains(ligatureGlyph, cmap.GlyphIndices);
        }

        [Fact]
        public void GsubLigatureSubstitution_NoneDisablesIt()
        {
            // font-variant-ligatures: none (LigatureSet.None) must fully restore the pre-GSUB,
            // 1:1 codepoint-to-glyph behavior - this is what makes turning ligatures off actually work.
            var descriptor = Descriptor(BundledFonts.Ttf);
            var shaped = descriptor.ShapeGlyphs("ff", new ShapeSettings(LigatureSet.None));

            Assert.Equal(2, shaped.Count);
        }

        [Fact]
        public void GsubLigatureSubstitution_LongestMatchFirst()
        {
            // The font lists "fft" before "ff" in its LigatureSet for the shared 'f' coverage entry -
            // shaping must try ligatures in the font's own authored order (not merge greedily by
            // twos), so "fft" collects the single 3-glyph ligature, not "ff" + "t".
            var descriptor = Descriptor(BundledFonts.Ttf);
            var shaped = descriptor.ShapeGlyphs("fft", new ShapeSettings(LigatureSet.Default));

            Assert.Single(shaped);
            Assert.Equal(0, shaped[0].ClusterStart);
            Assert.Equal(3, shaped[0].ClusterLength);
        }

        [Fact]
        public void AddShapedText_NullText_IsANoOp()
        {
            var descriptor = Descriptor(BundledFonts.Ttf);
            var cmap = new CharacterMapInfo(TypefaceFixtures.FromFile(BundledFonts.Ttf));

            cmap.AddShapedText(null!, new ShapeSettings(LigatureSet.Default));

            Assert.Empty(cmap.GlyphIndices);
            Assert.Empty(cmap.LigatureGlyphToText);
        }

        [Fact]
        public void GlyphSelectionIsNeighborIndependent_NoContextualForms()
        {
            // No contextual/positional shaping (the Arabic isolated/initial/medial/final family, GSUB
            // lookup types 5-8, etc.): a codepoint not involved in any ligature resolves to the same
            // glyph regardless of the characters around it. ('a'/"cat" don't participate in any of
            // this font's ligatures, so this remains neighbor-independent even with GSUB wired up.)
            var descriptor = Descriptor(BundledFonts.Ttf);

            var alone = new CharacterMapInfo(TypefaceFixtures.FromFile(BundledFonts.Ttf));
            alone.AddChars("a");
            var inWord = new CharacterMapInfo(TypefaceFixtures.FromFile(BundledFonts.Ttf));
            inWord.AddChars("cat");

            Assert.Equal(alone.CharacterToGlyphIndex['a'], inWord.CharacterToGlyphIndex['a']);
        }
    }
}

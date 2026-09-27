using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF;
using PeachPDF.PdfSharpCore.Drawing;
using PeachDrawing.Text;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// End-to-end coverage of astral (supplementary-plane, codepoint &gt; U+FFFF) glyph resolution via the
    /// cmap <b>format-12</b> subtable - the path emoji take. Uses the bundled monochrome Noto Emoji font,
    /// which is a real TrueType font with <c>glyf</c> outlines and a format-12 subtable covering
    /// U+1F600 (😀) and friends. Before this work the pipeline returned the missing glyph for every
    /// codepoint above U+FFFF, so each of these assertions would fail.
    /// </summary>
    public class Format12CmapAstralTests
    {
        private const int Grin = 0x1F600;      // 😀 GRINNING FACE (astral), in Noto Emoji
        private const int Heart = 0x2764;      // ❤ HEAVY BLACK HEART (BMP), in Noto Emoji
        private const int AbsentAstral = 0x1F0A1; // 🂡 ACE OF SPADES (astral), NOT in the bundled subset

        private static Typeface Descriptor(byte[] font) => TypefaceFixtures.FromBytes(font);

        /// <summary>Whether a face of the family covers the codepoint - what per-character font matching asks of a family.</summary>
        private static bool Covers(TypefaceFamily family, int codepoint) =>
            family.TryMatch(new TypefaceQuery { MustCover = new Rune(codepoint) }, out _);

        [Fact]
        public void Format12_ResolvesAstralEmojiCodepoint_ToRealGlyph()
        {
            var descriptor = Descriptor(File.ReadAllBytes(BundledFonts.Emoji));

            // The astral emoji resolves to a real (non-missing) glyph through the format-12 subtable...
            Assert.NotEqual(0, descriptor.GlyphOf(new Rune(Grin)));
            // ...a BMP codepoint the font covers still resolves through format-4...
            Assert.NotEqual(0, descriptor.GlyphOf(new Rune(Heart)));
            // ...and an astral codepoint the font does not cover resolves to the missing glyph.
            Assert.Equal(0, descriptor.GlyphOf(new Rune(AbsentAstral)));
        }

        [Fact]
        public void Format12_Coverage_IncludesAstralEmoji_AndBmp()
        {
            var family = new FontSet().AddData(File.ReadAllBytes(BundledFonts.Emoji));

            Assert.True(Covers(family, Grin), "astral emoji should be covered");
            Assert.True(Covers(family, Heart), "BMP coverage should be included");
            Assert.False(Covers(family, AbsentAstral));
        }

        [Fact]
        public void BmpOnlyFont_AstralCodepoint_ResolvesToMissingGlyph_AndCoverageStaysBmp()
        {
            // A font with no format-12 subtable (Source Sans 3) has no astral mapping: an astral codepoint
            // resolves to the missing glyph and is not reported as covered, while BMP still works. This also
            // exercises the coverage extraction's fast path (no format-12 → BMP ranges only).
            var bytes = File.ReadAllBytes(BundledFonts.Ttf);
            var descriptor = Descriptor(bytes);

            Assert.Equal(0, descriptor.GlyphOf(new Rune(Grin)));
            Assert.False(descriptor.HasGlyph(new Rune(Grin)));
            Assert.NotEqual(0, descriptor.GlyphOf(new Rune('A')));

            var family = new FontSet().AddData(bytes);
            Assert.True(Covers(family, 'A'));
            Assert.False(Covers(family, Grin));
        }

        [Fact]
        public void CMapInfo_AddChars_AstralEmoji_IsSingleCodepointEntry()
        {
            // An astral emoji is a surrogate pair in UTF-16; the rune-based pipeline must record it as one
            // codepoint→glyph entry, not two surrogate entries.
            var cmap = new CMapInfo(TypefaceFixtures.FromBytes(File.ReadAllBytes(BundledFonts.Emoji)));
            cmap.AddChars(char.ConvertFromUtf32(Grin));

            Assert.Single(cmap.CharacterToGlyphIndex);
            Assert.True(cmap.CharacterToGlyphIndex.ContainsKey(Grin));
            Assert.NotEqual(0, cmap.CharacterToGlyphIndex[Grin]);
        }

        [Fact]
        public async Task Emoji_AstralCodepoint_RendersEmbedsAndRoundTripsToUnicode()
        {
            var b64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Emoji));

            var html = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'Emoji'; src: url('data:font/truetype;base64,{b64}') format('truetype'); }}
body {{ font-family: 'Emoji'; font-size: 40pt; }}
</style></head><body>{char.ConvertFromUtf32(Grin)}</body></html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            doc.PdfDocument.Options.CompressContentStreams = false; // keep the ToUnicode stream readable

            var ms = new MemoryStream();
            doc.Save(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray());

            // The emoji font subset embeds its outline...
            Assert.Contains("/FontFile2", pdf);
            // ...and the ToUnicode map carries the emoji back as its UTF-16 surrogate pair (D83D DE00 =
            // U+1F600), proving the pipeline handled the astral character as ONE codepoint end to end.
            Assert.Contains("D83DDE00", pdf);
        }
    }
}

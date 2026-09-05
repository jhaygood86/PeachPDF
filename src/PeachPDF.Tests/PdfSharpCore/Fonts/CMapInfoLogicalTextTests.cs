using System.IO;
using System.Text;
using PeachPDF.Fonts;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using PeachPDF.Text.Bidi;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Regression coverage for <see cref="CMapInfo.AddShapedText"/>'s <c>logicalText</c> remap - the
    /// fix for a real text-extraction defect confirmed against actual MuPDF/PDFium output: a word whose
    /// own <c>Text</c> was reversed/mirrored for RTL display (<c>CssLayoutEngine.MirrorWordTextIfNeeded</c>)
    /// used to have its ToUnicode CMap built directly from that already-mirrored string, so each glyph's
    /// recorded "source text" was whichever mirrored character happened to occupy its position in the
    /// *painted* string, not the true logical-order character it actually came from - most visibly wrong
    /// for a mirrored pair like parentheses, where the extracted character isn't just out of order but
    /// the wrong character entirely. <c>logicalText</c> itself must be positionally aligned with the
    /// visual string passed alongside it (see <see cref="CMapInfo.AddShapedText"/>'s own remarks) - these
    /// tests build it via <see cref="BidiMirrorResolver.ReverseRunes"/> from a stable source string, the
    /// same way a real caller (<c>FragmentPainter.Text.cs</c>, <c>MarginBoxRenderer</c>) does.
    /// </summary>
    public class CMapInfoLogicalTextTests
    {
        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf)).Fontface;
            return new OpenTypeDescriptor("logicaltext-test", "logicaltext-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        private static int GlyphFor(OpenTypeDescriptor descriptor, char c) =>
            descriptor.CharCodeToGlyphIndex(new Rune(c));

        [Fact]
        public void AddShapedText_WithDifferingLogicalText_RemapsEachGlyphToItsTrueLogicalSource()
        {
            var descriptor = Descriptor();
            var cmapInfo = new CMapInfo(descriptor);

            // "(AB)" is what the source document actually contains, in true logical reading order.
            // BidiMirrorResolver.ApplyMirroring's whole-string reversal + per-character mirroring (what
            // CssLayoutEngine.MirrorWordTextIfNeeded applies before painting an RTL word) turns this into
            // "(BA)" - the parentheses swap identity (each mirrors to the other), not just position.
            // logicalText is ReverseRunes(source) - positionally aligned with the visual string (position
            // only, no mirroring), exactly as a real caller builds it.
            const string source = "(AB)";
            const string visual = "(BA)";
            var logicalText = BidiMirrorResolver.ReverseRunes(source);

            cmapInfo.AddShapedText(visual, TextShapingFeatures.Default, logicalText);

            // The '(' glyph painted at the start of the visual string is standing in for the source
            // string's closing ')' - extracting it must recover ')', not the '(' it visually is.
            Assert.Equal(")", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, '(')]);
            // Likewise the trailing ')' glyph stands in for the source's opening '('.
            Assert.Equal("(", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, ')')]);
            // 'A'/'B' don't change identity under mirroring, only position - confirms the remap tracks
            // position correctly even when the character value itself is unaffected.
            Assert.Equal("A", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, 'A')]);
            Assert.Equal("B", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, 'B')]);
        }

        [Fact]
        public void AddShapedText_LigatureMergesTwoCharactersIntoOneGlyph_RemapsUsingBothCharactersLogicalPosition()
        {
            // Source Sans 3 (BundledFonts.Ttf) ligates "ff" into a single glyph (see
            // FontVariantLigaturesIntegrationTests) - a real GSUB Lookup Type 4 merge, so this glyph's
            // ClusterLength is 2, not 1, exercising the remap formula's general case rather than just the
            // 1-character-per-glyph case the other tests here use. "(off)" is built asymmetric around the
            // ligature (an 'o' on only one side) so mirroring doesn't happen to collapse back to the same
            // visual string, the way a plain symmetric "(ff)" would.
            const string source = "(off)";
            const string visual = "(ffo)"; // reverse("(off)") + mirror each char: ')f f o (' -> '( f f o )'
            var logicalText = BidiMirrorResolver.ReverseRunes(source);

            var descriptor = Descriptor();
            var cmapInfo = new CMapInfo(descriptor);
            cmapInfo.AddShapedText(visual, TextShapingFeatures.Default, logicalText);

            var shaped = descriptor.Shape(visual, TextShapingFeatures.Default);
            var ligatureGlyph = Assert.Single(shaped, g => g.ClusterLength > 1);
            Assert.Equal(2, ligatureGlyph.ClusterLength);

            // The merged "ff" glyph sits at visual positions 1-2 ("(**ff**o)") - unaffected by mirroring
            // itself (neither 'f' mirrors), but its recovered text must still come from the correct
            // logical-order position (source's own "ff", at positions 2-3 of "(o**ff**)"), not an
            // arbitrary or shifted substring.
            Assert.Equal("ff", cmapInfo.LigatureGlyphToText[ligatureGlyph.GlyphIndex]);
            // The asymmetric neighbors on each side of the ligature confirm the remap didn't just get the
            // ligature right by accident - '(' (visual position 0) stands in for source's closing ')',
            // and the trailing ')' (visual position 4) stands in for source's opening '('.
            Assert.Equal(")", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, '(')]);
            Assert.Equal("(", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, ')')]);
            Assert.Equal("o", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, 'o')]);
        }

        [Fact]
        public void AddShapedText_LogicalTextNull_RecordsEachGlyphsOwnSubstring()
        {
            // The overwhelming common case (LTR text, or any word never reversed/mirrored for display):
            // omitting logicalText must behave exactly as before this parameter existed.
            var descriptor = Descriptor();
            var cmapInfo = new CMapInfo(descriptor);

            cmapInfo.AddShapedText("AB", TextShapingFeatures.Default);

            Assert.Equal("A", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, 'A')]);
            Assert.Equal("B", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, 'B')]);
        }

        [Fact]
        public void AddShapedText_LogicalTextEqualToText_RecordsEachGlyphsOwnSubstring()
        {
            // A word that was never actually mirrored has a logical source identical to what's painted -
            // AddShapedText must recognize this as "nothing to remap" rather than running the remap
            // formula needlessly.
            var descriptor = Descriptor();
            var cmapInfo = new CMapInfo(descriptor);

            cmapInfo.AddShapedText("AB", TextShapingFeatures.Default, logicalText: "AB");

            Assert.Equal("A", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, 'A')]);
            Assert.Equal("B", cmapInfo.LigatureGlyphToText[GlyphFor(descriptor, 'B')]);
        }
    }
}

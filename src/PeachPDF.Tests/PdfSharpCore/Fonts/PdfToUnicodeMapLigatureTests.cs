using System.IO;
using System.Text;
using PeachPDF.Fonts;
using PeachPDF.Fonts.OpenType;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Tests.TestSupport;
using PeachPDF.Text;
using Xunit;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// Direct (non-end-to-end) coverage for <see cref="PdfToUnicodeMap.PrepareForSave"/> merging both
    /// of <see cref="CMapInfo"/>'s sources - the legacy codepoint-keyed <see cref="CMapInfo.CharacterToGlyphIndex"/>
    /// (still populated only by the direct-call <see cref="CMapInfo.AddChars"/> API the low-level
    /// cmap tests use) and the GSUB-ligature-aware <see cref="CMapInfo.LigatureGlyphToText"/> (populated
    /// by <see cref="CMapInfo.AddShapedText"/>, the pipeline's real entry point). The full pipeline
    /// round-trip (<c>FontVariantLigaturesIntegrationTests.Ligature_RendersEmbedsAndRoundTripsToUnicode</c>)
    /// only exercises the ligature source; this exercises both together at the ToUnicode-map layer directly.
    /// </summary>
    public class PdfToUnicodeMapLigatureTests
    {
        private static OpenTypeDescriptor Descriptor()
        {
            var face = XFontSource.GetOrCreateFrom(File.ReadAllBytes(BundledFonts.Ttf)).Fontface;
            return new OpenTypeDescriptor("tounicode-test", "tounicode-test", XFontStyle.Regular, face,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
        }

        [Fact]
        public void PrepareForSave_MergesCharacterAndLigatureSources()
        {
            var descriptor = Descriptor();
            var cmapInfo = new CMapInfo(descriptor);
            cmapInfo.AddChars("A"); // populates the legacy codepoint-keyed CharacterToGlyphIndex
            int aGlyph = cmapInfo.CharacterToGlyphIndex['A'];

            const int ligatureGlyph = 12345; // an arbitrary glyph id distinct from 'A's, standing in for a real merged ligature glyph
            cmapInfo.LigatureGlyphToText[ligatureGlyph] = "ff";

            var doc = new PdfDocument { Options = { CompressContentStreams = false } };
            var map = new PdfToUnicodeMap(doc, cmapInfo);
            map.PrepareForSave();

            var text = Encoding.Latin1.GetString(map.Stream.Value);

            Assert.Contains($"<{aGlyph:X4}><{aGlyph:X4}><0041>", text); // 'A' = U+0041
            Assert.Contains($"<{ligatureGlyph:X4}><{ligatureGlyph:X4}><00660066>", text); // "ff" = U+0066 U+0066
            Assert.Contains("2 beginbfrange", text);
        }
    }
}

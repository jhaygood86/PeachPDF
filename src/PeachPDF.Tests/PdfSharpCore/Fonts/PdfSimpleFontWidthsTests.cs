using PeachDrawing.Text;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests.Fonts
{
    /// <summary>
    /// The simple-font <c>/Widths</c> table, which sits beside the PDF font objects: each WinAnsi code's width, and a
    /// WinAnsi font written as a simple TrueType font with a 256-entry widths array. The engine's own infrastructure is
    /// tested in <c>FontEngineInfrastructureTests</c> of PeachDrawing.Text.Tests.
    /// </summary>
    public class PdfSimpleFontWidthsTests
    {
        [Fact]
        public void SimpleFontWidths_MapEachWinAnsiCodeToItsGlyphsPdfWidth()
        {
            var font = WinAnsiFont();

            int[] widths = PdfSimpleFontWidths.Compute(font.Typeface);

            Assert.Equal(256, widths.Length);
            Assert.True(widths['A'] > 0);
            Assert.Equal(PdfWidthOf(font.Typeface, new Rune('A')), widths['A']);
            // 0x80 is the euro sign in Windows-1252, not U+0080: the table goes through the WinAnsi mapping.
            Assert.Equal(PdfWidthOf(font.Typeface, new Rune(0x20AC)), widths[0x80]);
        }

        [Fact]
        public void WinAnsiFont_IsWrittenAsASimpleTrueTypeFontWithA256EntryWidthsArray()
        {
            var font = WinAnsiFont();
            var document = new PdfDocument();
            document.Options.NoCompression = true;
            var page = document.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
                gfx.DrawString("Hello", font, XBrushes.Black, new XPoint(20, 40));

            using var stream = new MemoryStream();
            document.Save(stream);
            string pdf = Encoding.Latin1.GetString(stream.ToArray());

            Assert.Contains("/Subtype /TrueType", pdf);
            Assert.Contains("/FirstChar 0", pdf);
            Assert.Contains("/LastChar 255", pdf);
        }

        /// <summary>A glyph's advance in the 1000 units per em of a PDF <c>/Widths</c> array, worked out from the public metrics.</summary>
        private static int PdfWidthOf(Typeface typeface, Rune rune)
        {
            typeface.TryMapRune(rune, out var glyph);
            return typeface.GetAdvance(glyph) * 1000 / typeface.Metrics.UnitsPerEm;
        }

        private static XFont WinAnsiFont()
        {
            var fontSet = new FontSet();
            string family = "WinAnsiFamily-" + Guid.NewGuid().ToString("N");
            using (var stream = File.OpenRead(BundledFonts.Ttf))
                fontSet.AddStream(stream, new AddOptions { FamilyName = family });

            return TestFonts.Create(family, 12, pdfOptions: new XPdfFontOptions(PdfFontEncoding.WinAnsi), fontSet: fontSet);
        }
    }
}

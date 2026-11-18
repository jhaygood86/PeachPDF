using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>The standard 14 fonts a file names without embedding or describing: Symbol's built-in encoding and Courier's fixed width.</summary>
    public class PdfStandardFontTests
    {
        private static PdfPageText Read(string baseFont, string shown)
        {
            var pdf = TestPdf.Build($"BT /F1 20 Tf 10 50 Td ({shown}) Tj ET", resources: "/Font << /F1 5 0 R >>", width: 200, height: 100,
                extraObjects: [$"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} >>"]);
            return PdfPageSource.Open(pdf).GetText(0);
        }

        [Fact]
        public void SymbolWithNoEncoding_ReadsItsGreekLetters()
        {
            Assert.Equal("αβχΑΩ", Read("Symbol", "abcAW").Text);
        }

        [Fact]
        public void SymbolArithmeticSigns_AreReadAsTheirCharacters()
        {
            Assert.Equal("a=b≠", Read("Symbol", @"a=b\271").Text.Replace("α", "a").Replace("β", "b"));
        }

        [Fact]
        public void CourierWithNoWidths_AdvancesSixHundredUnitsPerCharacter()
        {
            var text = Read("Courier", "WiW");

            Assert.Equal(12, text.GetBounds(1, 1).Single().X - text.GetBounds(0, 1).Single().X, 1);      // 0.6 em at size 20
            Assert.Equal(12, text.GetBounds(2, 1).Single().X - text.GetBounds(1, 1).Single().X, 1);
        }

        [Fact]
        public void SymbolNamedWithASubsetTag_IsStillSymbol()
        {
            Assert.Equal("α", Read("ABCDEF+Symbol", "a").Text);
        }
    }
}

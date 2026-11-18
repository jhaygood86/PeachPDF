using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Text in a Type 3 font is read from its glyph names or ToUnicode map, placed by its widths.</summary>
    public class PdfType3TextTests
    {
        private static PdfPageText Read(string content, string fontEntries, params string[] more)
        {
            var proc = TestPdf.Stream("", "500 0 0 0 500 500 d1 0 0 500 500 re f");
            var font = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 500 500] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /A 6 0 R /B 6 0 R /Q 6 0 R >> /FirstChar 65 /LastChar 67 /Widths [500 500 500] " + fontEntries + " >>";
            var pdf = TestPdf.Build(content, resources: "/Font << /F1 5 0 R >>", width: 200, height: 100, extraObjects: [font, proc, .. more]);
            return PdfPageSource.Open(pdf).GetText(0);
        }

        [Fact]
        public void GlyphNamesGiveTheText()
        {
            var text = Read("BT /F1 20 Tf 10 50 Td (ABA) Tj ET", "/Encoding << /Type /Encoding /Differences [65 /A /B] >>");

            Assert.Equal("ABA", text.Text);
        }

        [Fact]
        public void ToUnicodeTakesPrecedence_AndWidthsPlaceEachGlyph()
        {
            var cmap = TestPdf.Stream("", "1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <41> <005A> endbfchar");
            var text = Read("BT /F1 20 Tf 10 50 Td (AB) Tj ET", "/Encoding << /Type /Encoding /Differences [65 /A /B] >> /ToUnicode 7 0 R", cmap);

            Assert.Equal("ZB", text.Text);
            var first = text.GetBounds(0, 1).Single();
            var second = text.GetBounds(1, 1).Single();
            Assert.Equal(10, first.X, 0);
            Assert.Equal(10, second.X - first.X, 0);        // 500 units at size 20 is 10 points
        }

        [Fact]
        public void ACodeWithNoNameOrMapping_ContributesNoText()
        {
            var text = Read("BT /F1 20 Tf 10 50 Td (CAC) Tj ET", "/Encoding << /Type /Encoding /Differences [65 /A] >>");

            Assert.Equal("A", text.Text);
        }
    }
}

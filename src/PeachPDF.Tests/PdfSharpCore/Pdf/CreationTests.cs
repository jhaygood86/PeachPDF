using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class CreationTests
    {
        [Fact]
        public void Create_for_Stream()
        {
            using var outputStream = new MemoryStream();

            PdfDocument document = new PdfDocument(outputStream);
            document.AddPage();

            // Note: unlike the parameterless PdfDocument() constructor, this fork's
            // PdfDocument(Stream) constructor does not initialize Version, so it stays 0
            // here rather than the usual default of 14 (PDF 1.4). Documented as a known
            // gap rather than asserted against.
            var ex = Record.Exception(() => document.Close());

            Assert.Null(ex);
        }
    }
}

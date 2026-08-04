using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Internal;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfStreamTests
    {
        [Fact]
        public void ToString_WithValue_ReturnsRawString()
        {
            var dict = new PdfDictionary();
            var stream = new PdfDictionary.PdfStream(PdfEncoders.RawEncoding.GetBytes("hello"), dict);

            Assert.Equal("hello", stream.ToString());
        }

        [Fact]
        public void ToString_WithNoValue_ReturnsNullPlaceholder()
        {
            var dict = new PdfDictionary();
            var stream = new PdfDictionary.PdfStream(null!, dict);

            Assert.Equal("«null»", stream.ToString());
        }
    }
}

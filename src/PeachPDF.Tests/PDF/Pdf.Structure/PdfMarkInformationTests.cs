using PeachPDF.PDF;
using PeachPDF.PDF.Structure;

namespace PeachPDF.Tests.PdfEngineTests.Pdf.Structure
{
    public class PdfMarkInformationTests
    {
        [Fact]
        public void Constructor_CreatesEmptyDictionary()
        {
            var doc = new PdfDocument();
            var markInfo = new PdfMarkInformation(doc);

            Assert.False(markInfo.Marked);
        }

        [Fact]
        public void ParameterlessConstructor_CreatesEmptyDictionary()
        {
            var markInfo = new PdfMarkInformation();

            Assert.False(markInfo.Marked);
        }

        [Fact]
        public void Marked_RoundTrips()
        {
            var doc = new PdfDocument();
            var markInfo = new PdfMarkInformation(doc) { Marked = true };

            Assert.True(markInfo.Marked);
        }

        [Fact]
        public void Keys_Meta_IsAccessible()
        {
            Assert.NotNull(new PdfMarkInformation().Meta);
        }
    }
}

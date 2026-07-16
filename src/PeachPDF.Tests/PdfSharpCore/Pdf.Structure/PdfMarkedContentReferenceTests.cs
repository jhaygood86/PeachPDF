using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Structure;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Structure
{
    public class PdfMarkedContentReferenceTests
    {
        [Fact]
        public void Constructor_SetsTypeToMCR()
        {
            var doc = new PdfDocument();
            var mcr = new PdfMarkedContentReference(doc);

            Assert.Equal("/MCR", mcr.Elements.GetName(PdfMarkedContentReference.Keys.Type));
        }

        [Fact]
        public void ParameterlessConstructor_SetsTypeToMCR()
        {
            var mcr = new PdfMarkedContentReference();

            Assert.Equal("/MCR", mcr.Elements.GetName(PdfMarkedContentReference.Keys.Type));
        }

        [Fact]
        public void Page_RoundTrips_AsReference()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var mcr = new PdfMarkedContentReference(doc) { Page = page };
            doc.Internals.AddObject(mcr);

            Assert.Same(page, mcr.Page);
        }

        [Fact]
        public void Mcid_RoundTrips()
        {
            var doc = new PdfDocument();
            var mcr = new PdfMarkedContentReference(doc) { Mcid = 7 };

            Assert.Equal(7, mcr.Mcid);
        }

        [Fact]
        public void Keys_Meta_IsAccessible()
        {
            Assert.NotNull(new PdfMarkedContentReference().Meta);
        }
    }
}

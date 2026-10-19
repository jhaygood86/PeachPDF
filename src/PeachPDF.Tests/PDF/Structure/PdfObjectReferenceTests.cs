using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Structure;

namespace PeachPDF.Tests.PdfEngineTests.Structure
{
    public class PdfObjectReferenceTests
    {
        [Fact]
        public void Constructor_SetsTypeToOBJR()
        {
            var doc = new PdfDocument();
            var objRef = new PdfObjectReference(doc);

            Assert.Equal("/OBJR", objRef.Elements.GetName(PdfObjectReference.Keys.Type));
        }

        [Fact]
        public void ParameterlessConstructor_SetsTypeToOBJR()
        {
            var objRef = new PdfObjectReference();

            Assert.Equal("/OBJR", objRef.Elements.GetName(PdfObjectReference.Keys.Type));
        }

        [Fact]
        public void Page_RoundTrips_AsReference()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var objRef = new PdfObjectReference(doc) { Page = page };
            doc.Internals.AddObject(objRef);

            Assert.Same(page, objRef.Page);
        }

        [Fact]
        public void Object_RoundTrips_AsReference()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var annotation = page.AddWebLink(new PdfRectangle(new PeachDrawing.Core.Rect(0, 0, 10, 10)), "https://example.com");
            var objRef = new PdfObjectReference(doc) { Object = annotation };
            doc.Internals.AddObject(objRef);

            Assert.Same(annotation, objRef.Object);
        }

        [Fact]
        public void Keys_Meta_IsAccessible()
        {
            Assert.NotNull(new PdfObjectReference().Meta);
        }
    }
}

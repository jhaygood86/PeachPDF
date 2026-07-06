using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    // Adapted from upstream PDFsharp's Pdf.Objects/PdfCatalogTests.cs. Upstream also
    // asserts HasMetadata/GetOrCreateMetadata(), which don't exist on this fork's
    // PdfCatalog (no metadata/XMP support at all) -- only the Names entry is portable.
    public class PdfCatalogTests
    {
        [Fact]
        public void Test_Catalog_entries()
        {
            var doc = new PdfDocument();

            var catalog = doc.Catalog;

            var names = catalog.Names;
            Assert.NotNull(names);
            Assert.True(names.IsIndirect);
        }
    }
}

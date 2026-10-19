using PeachDrawing.PDF.Engine;
using System.IO;
using Xunit;

namespace PeachPDF.Tests.PdfEngineTests
{
    /// <summary>
    /// Regression tests for a bug introduced in commit 6183dc9 where
    /// PdfObject.SetObjectId accidentally nulled _document via the expression
    ///   _iref.Document = _document = null!
    /// instead of the original
    ///   _iref.Document = _document
    /// This caused AddPage() to throw NullReferenceException because the
    /// PdfCatalog's Owner was null after being added to the cross-reference table.
    /// </summary>
    public class PdfDocumentAddPageTests
    {
        [Fact]
        public void AddPage_OnNewDocument_DoesNotThrow()
        {
            var doc = new PdfDocument();
            var ex = Record.Exception(() => doc.AddPage());
            Assert.Null(ex);
        }

        [Fact]
        public void AddPage_OnNewDocument_ReturnsPage()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            Assert.NotNull(page);
        }

        [Fact]
        public void AddPage_MultipleTimes_YieldsCorrectPageCount()
        {
            var doc = new PdfDocument();
            doc.AddPage();
            doc.AddPage();
            doc.AddPage();
            Assert.Equal(3, doc.PageCount);
        }

        [Fact]
        public void AddPage_CatalogOwnerIsNotNullAfterAdd()
        {
            var doc = new PdfDocument();
            doc.AddPage();
            // Catalog.Owner being non-null is what prevents the NullReferenceException.
            // If the bug is present, even accessing PageCount would re-trigger it.
            Assert.True(doc.PageCount >= 1);
        }

        [Fact]
        public void NewDocument_CanBeSaved()
        {
            var doc = new PdfDocument();
            doc.AddPage();
            var ms = new MemoryStream();
            var ex = Record.Exception(() => doc.Save(ms));
            Assert.Null(ex);
            Assert.True(ms.Length > 0);
        }
    }
}

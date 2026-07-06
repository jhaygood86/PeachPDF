using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfInternalsTests
    {
        [Fact]
        public void Catalog_ReturnsDocumentCatalog()
        {
            var doc = new PdfDocument();

            Assert.Same(doc.Catalog, doc.Internals.Catalog);
        }

        [Fact]
        public void ExtGStateTable_IsNotNull()
        {
            var doc = new PdfDocument();

            Assert.NotNull(doc.Internals.ExtGStateTable);
        }

        [Fact]
        public void AddObject_MakesObjectIndirect()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();

            doc.Internals.AddObject(dict);

            Assert.True(dict.IsIndirect);
        }

        [Fact]
        public void AddObject_Null_Throws()
        {
            var doc = new PdfDocument();

            Assert.Throws<ArgumentNullException>(() => doc.Internals.AddObject(null!));
        }

        [Fact]
        public void AddObject_AlreadyOwnedByAnotherDocument_Throws()
        {
            var doc1 = new PdfDocument();
            var doc2 = new PdfDocument();
            var dict = new PdfDictionary(doc1);
            doc1.Internals.AddObject(dict);

            Assert.Throws<InvalidOperationException>(() => doc2.Internals.AddObject(dict));
        }

        [Fact]
        public void RemoveObject_RemovesIndirectObject()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            doc.Internals.RemoveObject(dict);

            var ex = Record.Exception(() => doc.Internals.AddObject(dict));
            Assert.Null(ex);
        }

        [Fact]
        public void RemoveObject_Null_Throws()
        {
            var doc = new PdfDocument();

            Assert.Throws<ArgumentNullException>(() => doc.Internals.RemoveObject(null!));
        }

        [Fact]
        public void RemoveObject_DirectObject_Throws()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();

            Assert.Throws<InvalidOperationException>(() => doc.Internals.RemoveObject(dict));
        }

        [Fact]
        public void GetObject_ReturnsPreviouslyAddedObject()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            var found = doc.Internals.GetObject(dict.ObjectID);

            Assert.Same(dict, found);
        }

        [Fact]
        public void GetAllObjects_IncludesAddedObject()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            var all = doc.Internals.GetAllObjects();

            Assert.Contains(dict, all);
        }

        [Fact]
        public void GetClosure_IncludesRootObjectFirst()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            var closure = doc.Internals.GetClosure(dict);

            Assert.Same(dict, closure[0]);
        }

        [Fact]
        public void GetClosure_WithDepth_IncludesRootObjectFirst()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            var closure = doc.Internals.GetClosure(dict, 1);

            Assert.Same(dict, closure[0]);
        }

        [Fact]
        public void WriteObject_WritesToStream()
        {
            var doc = new PdfDocument();
            using var stream = new MemoryStream();

            doc.Internals.WriteObject(stream, new PdfInteger(42));

            Assert.True(stream.Length > 0);
        }

        [Fact]
        public void FirstDocumentID_RoundTrips()
        {
            var doc = new PdfDocument();

            doc.Internals.FirstDocumentID = "0123456789ABCDEF";

            Assert.Equal("0123456789ABCDEF", doc.Internals.FirstDocumentID);
        }

        [Fact]
        public void SecondDocumentID_RoundTrips()
        {
            var doc = new PdfDocument();

            doc.Internals.SecondDocumentID = "FEDCBA9876543210";

            Assert.Equal("FEDCBA9876543210", doc.Internals.SecondDocumentID);
        }

        [Fact]
        public void FirstDocumentGuid_InvalidLength_ReturnsEmptyGuid()
        {
            // A freshly created document already has a valid 16-byte /ID set up automatically, so
            // FirstDocumentGuid is non-empty by default; the empty-Guid fallback only kicks in for an
            // ID string of the wrong length.
            var doc = new PdfDocument();
            doc.Internals.FirstDocumentID = "too-short";

            Assert.Equal(Guid.Empty, doc.Internals.FirstDocumentGuid);
        }

        [Fact]
        public void FirstDocumentGuid_ValidId_ProducesNonEmptyGuid()
        {
            var doc = new PdfDocument();
            doc.Internals.FirstDocumentID = "0123456789ABCDEF";

            Assert.NotEqual(Guid.Empty, doc.Internals.FirstDocumentGuid);
        }

        [Fact]
        public void StaticGetReference_ReturnsObjectsReference()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            Assert.Same(dict.Reference, PeachPDF.PdfSharpCore.Pdf.Advanced.PdfInternals.GetReference(dict));
        }

        [Fact]
        public void StaticGetReference_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PeachPDF.PdfSharpCore.Pdf.Advanced.PdfInternals.GetReference(null!));
        }

        [Fact]
        public void StaticGetObjectID_ReturnsObjectID()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            Assert.Equal(dict.ObjectID, PeachPDF.PdfSharpCore.Pdf.Advanced.PdfInternals.GetObjectID(dict));
        }

        [Fact]
        public void StaticGetObjectNumber_ReturnsObjectNumber()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            Assert.Equal(dict.ObjectNumber, PeachPDF.PdfSharpCore.Pdf.Advanced.PdfInternals.GetObjectNumber(dict));
        }

        [Fact]
        public void StaticGenerationNumber_ReturnsGenerationNumber()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            Assert.Equal(dict.GenerationNumber, PeachPDF.PdfSharpCore.Pdf.Advanced.PdfInternals.GenerationNumber(dict));
        }

        [Fact]
        public void CustomValueKey_IsNonEmpty()
        {
            var doc = new PdfDocument();

            Assert.NotEmpty(doc.Internals.CustomValueKey);
        }
    }
}

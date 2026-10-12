using PeachPDF.PDF;

namespace PeachPDF.Tests.PdfEngineTests.ObjectModel
{
    public class PdfObjectTests
    {
        [Fact]
        public void NewObject_IsNotIndirect_HasNoReference()
        {
            var dict = new PdfDictionary();

            Assert.False(dict.IsIndirect);
            Assert.Null(dict.Reference);
        }

        [Fact]
        public void AddedToDocument_BecomesIndirect()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();

            doc.Internals.AddObject(dict);

            Assert.True(dict.IsIndirect);
            Assert.NotNull(dict.Reference);
            Assert.Same(doc, dict.Owner);
        }

        [Fact]
        public void Internals_ObjectIdAndNumbers_ReflectIndirectReference()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            Assert.Equal(dict.ObjectId.ObjectNumber, dict.Internals.ObjectNumber);
            Assert.Equal(dict.ObjectId.GenerationNumber, dict.Internals.GenerationNumber);
            Assert.NotEmpty(dict.Internals.TypeId);
        }

        [Fact]
        public void ObjectId_ForDirectObject_IsEmpty()
        {
            var dict = new PdfDictionary();

            Assert.Equal(PdfObjectId.Empty, dict.ObjectId);
            Assert.Equal(0, dict.ObjectNumber);
            Assert.Equal(0, dict.GenerationNumber);
        }

        [Fact]
        public void Document_Setter_CannotBeChangedOnceSet()
        {
            var doc1 = new PdfDocument();
            var doc2 = new PdfDocument();
            var dict = new PdfDictionary(doc1);

            Assert.Throws<InvalidOperationException>(() => dict.Document = doc2);
        }

        [Fact]
        public void Document_Setter_SameDocumentAgain_DoesNotThrow()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary(doc);

            var ex = Record.Exception(() => dict.Document = doc);

            Assert.Null(ex);
        }
    }
}

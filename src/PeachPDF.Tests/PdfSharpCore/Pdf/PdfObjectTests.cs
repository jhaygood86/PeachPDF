using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
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
        public void Internals_ObjectIDAndNumbers_ReflectIndirectReference()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);

            Assert.Equal(dict.ObjectID.ObjectNumber, dict.Internals.ObjectNumber);
            Assert.Equal(dict.ObjectID.GenerationNumber, dict.Internals.GenerationNumber);
            Assert.NotEmpty(dict.Internals.TypeID);
        }

        [Fact]
        public void ObjectID_ForDirectObject_IsEmpty()
        {
            var dict = new PdfDictionary();

            Assert.Equal(PdfObjectID.Empty, dict.ObjectID);
            Assert.Equal(0, dict.ObjectNumber);
            Assert.Equal(0, dict.GenerationNumber);
        }

        [Fact]
        public void Clone_ProducesObjectWithoutOwnerOrReference()
        {
            var doc = new PdfDocument();
            var dict = new PdfDictionary();
            doc.Internals.AddObject(dict);
            dict.Elements.SetInteger("/Foo", 1);

            var clone = dict.Clone();

            Assert.Null(clone.Owner);
            Assert.Null(clone.Reference);
            Assert.False(clone.IsIndirect);
            Assert.Equal(1, clone.Elements.GetInteger("/Foo"));
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

using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.PdfSharpCore.Pdf.Structure;
using System.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfAssociatedFileTests
    {
        static PdfFileSpecification MakeFileSpec(PdfDocument document, string fileName, byte[] bytes, string mimeType, string relationship)
        {
            var embeddedFile = new PdfEmbeddedFile(document, bytes) { MimeType = mimeType };
            return new PdfFileSpecification(document, fileName, embeddedFile)
            {
                AssociatedFileRelationship = relationship,
            };
        }

        [Fact]
        public void PdfFileSpecification_RoundTripsFileNameAndEmbeddedFile()
        {
            var document = new PdfDocument();
            var bytes = Encoding.UTF8.GetBytes("<math><mi>x</mi></math>");
            var fileSpec = MakeFileSpec(document, "formula.mml", bytes, "application/mathml+xml", "/Supplement");

            Assert.Equal("formula.mml", fileSpec.FileName);
            Assert.Equal("/Supplement", fileSpec.AssociatedFileRelationship);
            // GetName always returns the value with its leading "/" (DictionaryElements.SetName adds
            // one automatically if missing) - the internal "/" in the MIME type is only escaped as
            // "#2F" when PdfWriter actually serializes the Name to PDF syntax, not in this in-memory
            // round trip, so the raw stored value legitimately still contains an unescaped "/".
            Assert.Equal("/application/mathml+xml", fileSpec.EmbeddedFile.MimeType);
            Assert.Equal(bytes, fileSpec.EmbeddedFile.Stream.Value);
            Assert.Equal("/Filespec", fileSpec.Elements.GetName("/Type"));
        }

        [Fact]
        public void PdfEmbeddedFile_CreateStreamAndSetProperties_SetsSizeAndBytes()
        {
            var document = new PdfDocument();
            var bytes = Encoding.UTF8.GetBytes("hello associated file");
            var embeddedFile = new PdfEmbeddedFile(document, bytes);

            Assert.Equal(bytes, embeddedFile.Stream.Value);
            Assert.Equal(bytes.Length, embeddedFile.Elements.GetDictionary("/Params")?.Elements.GetInteger("/Size"));
        }

        [Fact]
        public void PdfCatalog_AddAssociatedFile_CreatesAfArray()
        {
            var document = new PdfDocument();
            var fileSpec = MakeFileSpec(document, "a.mml", [1, 2, 3], "application/mathml+xml", "/Supplement");

            document.Catalog.AddAssociatedFile(fileSpec);

            var array = document.Catalog.Elements.GetArray("/AF");
            Assert.NotNull(array);
            Assert.Single(array!.Elements);
            Assert.Same(fileSpec, ((PdfReference)array.Elements[0]).Value);
        }

        [Fact]
        public void PdfCatalog_AddAssociatedFile_CalledTwice_AppendsToSameArray()
        {
            var document = new PdfDocument();
            var first = MakeFileSpec(document, "a.mml", [1], "application/mathml+xml", "/Supplement");
            var second = MakeFileSpec(document, "b.tex", [2], "text/x-tex", "/Source");

            document.Catalog.AddAssociatedFile(first);
            document.Catalog.AddAssociatedFile(second);

            var array = document.Catalog.Elements.GetArray("/AF");
            Assert.NotNull(array);
            Assert.Equal(2, array!.Elements.Count);
        }

        [Fact]
        public void PdfStructureElement_AppendAssociatedFile_CreatesAfArrayOnTheElement()
        {
            var document = new PdfDocument();
            var structureElement = new PdfStructureElement(document);
            var fileSpec = MakeFileSpec(document, "formula.mml", [1, 2, 3], "application/mathml+xml", "/Supplement");

            structureElement.AppendAssociatedFile(fileSpec);

            var array = structureElement.Elements.GetArray("/AF");
            Assert.NotNull(array);
            Assert.Single(array!.Elements);
            Assert.Same(fileSpec, ((PdfReference)array.Elements[0]).Value);

            // Does not also land on the document-level catalog array - the two indexes are independent.
            Assert.False(document.Catalog.Elements.ContainsKey("/AF"));
        }

        [Fact]
        public void PdfStructureElement_AppendAssociatedFile_DoesNotDoubleRegisterAnAlreadyIndirectFileSpec()
        {
            var document = new PdfDocument();
            var fileSpec = MakeFileSpec(document, "formula.mml", [1, 2, 3], "application/mathml+xml", "/Supplement");
            document.Internals.AddObject(fileSpec);
            Assert.True(fileSpec.IsIndirect);

            var structureElement = new PdfStructureElement(document);
            structureElement.AppendAssociatedFile(fileSpec);

            var array = structureElement.Elements.GetArray("/AF");
            Assert.Single(array!.Elements);
        }
    }
}

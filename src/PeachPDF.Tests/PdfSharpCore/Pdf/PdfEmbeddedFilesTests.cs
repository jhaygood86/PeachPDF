using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfEmbeddedFilesTests
    {
        static PdfFileSpecification MakeFileSpec(PdfDocument document, string fileName = "a.txt")
        {
            var embeddedFile = new PdfEmbeddedFile(document, [1, 2, 3]) { MimeType = "text/plain" };
            return new PdfFileSpecification(document, fileName, embeddedFile);
        }

        [Fact]
        public void FileSpecification_EmbeddedFile_NamesTheStreamUnderBothFAndUf()
        {
            var document = new PdfDocument();
            var fileSpec = MakeFileSpec(document);

            var ef = fileSpec.Elements.GetDictionary("/EF");
            Assert.NotNull(ef);
            var f = ef!.Elements.GetReference("/F");
            var uf = ef.Elements.GetReference("/UF");

            Assert.NotNull(f);
            Assert.Same(f!.Value, uf!.Value);
            Assert.Same(fileSpec.EmbeddedFile, f.Value);
        }

        [Fact]
        public void FileSpecification_SettingEmbeddedFileToNull_RemovesBothKeys()
        {
            var document = new PdfDocument();
            var fileSpec = MakeFileSpec(document);

            fileSpec.EmbeddedFile = null!;

            var ef = fileSpec.Elements.GetDictionary("/EF");
            Assert.False(ef!.Elements.ContainsKey("/F"));
            Assert.False(ef.Elements.ContainsKey("/UF"));
            Assert.Null(fileSpec.EmbeddedFile);
        }

        [Fact]
        public void FileSpecification_UnicodeFileName_RoundTrips()
        {
            var fileSpec = MakeFileSpec(new PdfDocument());

            fileSpec.UnicodeFileName = "café.txt";

            Assert.Equal("café.txt", fileSpec.UnicodeFileName);
            Assert.Equal("a.txt", fileSpec.FileName);
        }

        [Fact]
        public void FileSpecification_Description_RoundTrips_ForAsciiAndUnicode()
        {
            var fileSpec = MakeFileSpec(new PdfDocument());

            fileSpec.Description = "plain";
            Assert.Equal("plain", fileSpec.Description);

            fileSpec.Description = "café";
            Assert.Equal("café", fileSpec.Description);
        }

        [Fact]
        public void EmbeddedFile_ModificationDate_RoundTripsIntoParams_AndNullRemovesIt()
        {
            var document = new PdfDocument();
            var embeddedFile = new PdfEmbeddedFile(document, [1]);
            Assert.Null(embeddedFile.ModificationDate);

            var date = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Local);
            embeddedFile.ModificationDate = date;

            Assert.Equal(date, embeddedFile.ModificationDate);
            Assert.True(embeddedFile.Elements.GetDictionary("/Params")!.Elements.ContainsKey("/ModDate"));

            embeddedFile.ModificationDate = null;

            Assert.Null(embeddedFile.ModificationDate);
            Assert.False(embeddedFile.Elements.GetDictionary("/Params")!.Elements.ContainsKey("/ModDate"));
        }

        [Fact]
        public void EmbeddedFile_IsNotCompressedUnlessAskedTo()
        {
            var document = new PdfDocument();
            var embeddedFile = new PdfEmbeddedFile(document, [1, 2, 3]);

            Assert.False(embeddedFile.CompressOnWrite);
        }

        [Fact]
        public void KeysMeta_IsCreatedOnce_AndNeverNull()
        {
            Assert.NotNull(PdfFileSpecification.Keys.Meta);
            Assert.Same(PdfFileSpecification.Keys.Meta, PdfFileSpecification.Keys.Meta);

            Assert.NotNull(PdfEmbeddedFile.Keys.Meta);
            Assert.Same(PdfEmbeddedFile.Keys.Meta, PdfEmbeddedFile.Keys.Meta);

            Assert.Same(PdfFileSpecification.Keys.Meta, MakeFileSpec(new PdfDocument()).Meta);
            Assert.Same(PdfEmbeddedFile.Keys.Meta, new PdfEmbeddedFile(new PdfDocument(), [1]).Meta);
        }

        [Fact]
        public void NameDictionary_AddEmbeddedFile_CreatesAFlatTreeLazily()
        {
            var document = new PdfDocument();
            var names = document.Catalog.Names;
            Assert.Null(names.EmbeddedFilesTree);
            Assert.False(names.Elements.ContainsKey("/EmbeddedFiles"));

            var fileSpec = MakeFileSpec(document);
            names.AddEmbeddedFile("a.txt", fileSpec);

            var tree = names.EmbeddedFilesTree;
            Assert.NotNull(tree);
            Assert.True(names.Elements.ContainsKey("/EmbeddedFiles"));
            Assert.Equal(0, tree!.KidsCount);
            Assert.Same(fileSpec, tree.GetValue("a.txt"));
            Assert.True(fileSpec.IsIndirect);
        }

        [Fact]
        public void NameDictionary_AddEmbeddedFile_KeepsKeysSorted_AndReusesTheTree()
        {
            var document = new PdfDocument();
            var names = document.Catalog.Names;

            names.AddEmbeddedFile("b.txt", MakeFileSpec(document, "b.txt"));
            var tree = names.EmbeddedFilesTree;
            names.AddEmbeddedFile("a.txt", MakeFileSpec(document, "a.txt"));

            Assert.Same(tree, names.EmbeddedFilesTree);
            Assert.Equal(["a.txt", "b.txt"], tree!.GetNames());
        }

        [Fact]
        public void NameDictionary_WrappingAnExistingDictionary_FindsItsEmbeddedFilesTree()
        {
            var document = new PdfDocument();
            var names = document.Catalog.Names;
            names.AddEmbeddedFile("a.txt", MakeFileSpec(document));

            var wrapped = new PdfNameDictionary(names);

            Assert.NotNull(wrapped.EmbeddedFilesTree);
            Assert.Equal(["a.txt"], wrapped.EmbeddedFilesTree!.GetNames());
        }

        [Fact]
        public void Catalog_AddAttachment_IndexesTheSameFileSpecInAfAndTheNameTree()
        {
            var document = new PdfDocument();
            var fileSpec = MakeFileSpec(document);

            document.Catalog.AddAttachment("a.txt", fileSpec);

            var afEntry = (PdfReference)document.Catalog.Elements.GetArray("/AF")!.Elements.Single();
            Assert.Same(fileSpec, afEntry.Value);
            Assert.Same(fileSpec, document.Catalog.Names.EmbeddedFilesTree!.GetValue("a.txt"));
        }
    }
}

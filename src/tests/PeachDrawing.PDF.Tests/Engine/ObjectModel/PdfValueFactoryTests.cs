using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.AcroForms;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Annotations;
using PeachDrawing.PDF.Engine.Structure;
using Xunit;

namespace PeachPDF.Tests.PdfEngineTests.ObjectModel
{
    /// <summary>
    /// The real classes that <c>GetOrCreate</c> builds: each can be created when its key is missing and can take over a plain dictionary
    /// (or array) already stored under the key, keeping what the plain object held.
    /// </summary>
    public class PdfValueFactoryTests
    {
        private const string Key = "/Value";
        private const string Marker = "/Marker";

        private static void AssertCreatesAndWraps<T>() where T : PdfDictionary, IPdfDictionaryValue<T>
        {
            var document = new PdfDocument();
            var owner = new PdfDictionary(document);
            document.Internals.AddObject(owner);

            // Missing: created, stored directly.
            var created = owner.Elements.GetOrCreate<T>(Key, ValueCreationFlags.Create);
            Assert.Same(created, owner.Elements[Key]);

            // A plain dictionary stored directly: wrapped, what it held is kept.
            var plain = new PdfDictionary(document);
            plain.Elements.SetInteger(Marker, 7);
            owner.Elements[Key] = plain;
            var wrapped = owner.Elements.GetOrCreate<T>(Key, ValueCreationFlags.Create);
            Assert.IsType<T>(wrapped);
            Assert.Equal(7, wrapped.Elements.GetInteger(Marker));

            // A plain dictionary stored indirectly: followed, then wrapped.
            var indirect = new PdfDictionary(document);
            indirect.Elements.SetInteger(Marker, 9);
            document.Internals.AddObject(indirect);
            owner.Elements.SetReference(Key, indirect);
            var wrappedIndirect = owner.Elements.GetOrCreate<T>(Key, ValueCreationFlags.Create);
            Assert.IsType<T>(wrappedIndirect);
            Assert.Equal(9, wrappedIndirect.Elements.GetInteger(Marker));
        }

        [Fact]
        public void PdfPages_CreatesAndWraps() => AssertCreatesAndWraps<PdfPages>();

        [Fact]
        public void PdfOutline_CreatesAndWraps() => AssertCreatesAndWraps<PdfOutline>();

        [Fact]
        public void PdfAcroForm_CreatesAndWraps() => AssertCreatesAndWraps<PdfAcroForm>();

        [Fact]
        public void PdfMarkInformation_CreatesAndWraps() => AssertCreatesAndWraps<PdfMarkInformation>();

        [Fact]
        public void PdfStructureTreeRoot_CreatesAndWraps() => AssertCreatesAndWraps<PdfStructureTreeRoot>();

        [Fact]
        public void PdfViewerPreferences_CreatesAndWraps() => AssertCreatesAndWraps<PdfViewerPreferences>();

        [Fact]
        public void PdfResources_CreatesAndWraps() => AssertCreatesAndWraps<PdfResources>();

        [Fact]
        public void PdfResourceMap_CreatesAndWraps() => AssertCreatesAndWraps<PdfResourceMap>();

        [Fact]
        public void PdfDocumentInformation_CreatesAndWraps() => AssertCreatesAndWraps<PdfDocumentInformation>();

        [Fact]
        public void PdfCatalog_CreatesAndWraps() => AssertCreatesAndWraps<PdfCatalog>();

        [Fact]
        public void PdfNumberTreeNode_CreatesAndWraps() => AssertCreatesAndWraps<PdfNumberTreeNode>();

        [Fact]
        public void PdfAnnotations_CreatesAndWrapsAPlainArray()
        {
            var document = new PdfDocument();
            var owner = new PdfDictionary(document);
            document.Internals.AddObject(owner);

            var created = owner.Elements.GetOrCreateArray<PdfAnnotations>(Key, ValueCreationFlags.Create);
            Assert.Same(created, owner.Elements[Key]);

            var plain = new PdfArray(document);
            plain.Elements.Add(new PdfInteger(5));
            owner.Elements[Key] = plain;
            var wrapped = owner.Elements.GetExistingArray<PdfAnnotations>(Key);
            Assert.NotNull(wrapped);
            Assert.Single(wrapped!.Elements);
        }

        [Fact]
        public void PdfArray_AsAGeneralRequest_ReturnsASubclassUnchanged()
        {
            var document = new PdfDocument();
            var owner = new PdfDictionary(document);
            var annotations = new PdfAnnotations(new PdfArray(document));
            owner.Elements[Key] = annotations;

            Assert.Same(annotations, owner.Elements.GetOrCreateArray<PdfArray>(Key, ValueCreationFlags.Create));
        }
    }
}

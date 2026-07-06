using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfDocumentPropertiesTests
    {
        [Fact]
        public void Tag_RoundTrips()
        {
            var doc = new PdfDocument();

            doc.Tag = "my-tag";

            Assert.Equal("my-tag", doc.Tag);
        }

        [Fact]
        public void Guid_IsUniquePerDocument()
        {
            var doc1 = new PdfDocument();
            var doc2 = new PdfDocument();

            Assert.NotEqual(Guid.Empty, doc1.Guid);
            Assert.NotEqual(doc1.Guid, doc2.Guid);
        }

        [Fact]
        public void Version_DefaultsToPdf14ForInMemoryDocument()
        {
            var doc = new PdfDocument();

            Assert.Equal(14, doc.Version);
        }

        [Fact]
        public void HasVersion_MatchesCurrentOrHigherVersion()
        {
            var doc = new PdfDocument();

            Assert.True(doc.HasVersion("1.4"));
            Assert.False(doc.HasVersion("1.7"));
        }

        [Fact]
        public void PageCount_ReflectsAddedPages()
        {
            var doc = new PdfDocument();
            doc.AddPage();
            doc.AddPage();

            Assert.Equal(2, doc.PageCount);
        }

        [Fact]
        public void PageLayout_Setter_ThrowsOnReadBackDueToBuggyDebugAssert()
        {
            // PdfCatalog.PageLayout is backed by DictionaryElements.GetEnumFromName/SetEnumAsName.
            // SetEnumAsName stores the value as a PdfName, but GetEnumFromName's read path does
            // `Debug.Assert(obj is Enum)` before parsing it -- the stored object is always a
            // PdfName, never an Enum, so the assertion always fails for anything the setter wrote,
            // and a failed Debug.Assert aborts the test under a Debug build. Real, pre-existing bug
            // in GetEnumFromName; documented here rather than fixed.
            var doc = new PdfDocument();

            doc.PageLayout = PdfPageLayout.OneColumn;

            var ex = Record.Exception(() => doc.PageLayout);
            Assert.NotNull(ex);
        }

        [Fact]
        public void PageMode_Setter_ThrowsOnReadBackDueToBuggyDebugAssert()
        {
            // See the NOTE on PageLayout_Setter_ThrowsOnReadBackDueToBuggyDebugAssert above --
            // PdfCatalog.PageMode has the same GetEnumFromName/SetEnumAsName round-trip bug.
            var doc = new PdfDocument();

            doc.PageMode = PdfPageMode.FullScreen;

            var ex = Record.Exception(() => doc.PageMode);
            Assert.NotNull(ex);
        }

        [Fact]
        public void Language_RoundTrips()
        {
            var doc = new PdfDocument();

            doc.Language = "en-US";

            Assert.Equal("en-US", doc.Language);
        }

        [Fact]
        public void Outlines_IsNotNull()
        {
            var doc = new PdfDocument();

            Assert.NotNull(doc.Outlines);
        }

        [Fact]
        public void Info_IsNotNull()
        {
            var doc = new PdfDocument();

            Assert.NotNull(doc.Info);
        }

        [Fact]
        public void CustomValues_IsAccessible()
        {
            var doc = new PdfDocument();

            // No custom values set yet; accessing the property must not throw.
            var ex = Record.Exception(() => doc.CustomValues);

            Assert.Null(ex);
        }

        [Fact]
        public void Internals_IsNotNull()
        {
            var doc = new PdfDocument();

            Assert.NotNull(doc.Internals);
        }

        [Fact]
        public void ViewerPreferences_IsNotNull()
        {
            var doc = new PdfDocument();

            Assert.NotNull(doc.ViewerPreferences);
        }

        [Fact]
        public void Save_ToPath_WritesFile()
        {
            // Note: unlike the parameterless PdfDocument() constructor's FullPath handling
            // elsewhere, Save(string path) just opens a FileStream and delegates to
            // Save(Stream) -- it never assigns FullPath, so that property is not checked here.
            var doc = new PdfDocument();
            doc.AddPage();
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");

            try
            {
                doc.Save(path);

                Assert.True(File.Exists(path));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void CanSave_AlwaysReturnsTrue()
        {
            // CanSave is currently a stub ("return true;" unconditionally) regardless of page
            // count or any other document state -- this documents that as-is behavior rather
            // than the page-count validation its name might suggest.
            var doc = new PdfDocument();
            string message = "";

            Assert.True(doc.CanSave(ref message));

            doc.AddPage();

            Assert.True(doc.CanSave(ref message));
        }
    }
}

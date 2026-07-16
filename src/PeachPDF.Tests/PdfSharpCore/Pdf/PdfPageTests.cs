using PeachPDF;
using PeachPDF.PdfSharpCore.Pdf;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf
{
    public class PdfPageTests
    {
        [Fact]
        public void NewPage_DefaultsToPortraitOrientation()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            Assert.Equal(PageOrientation.Portrait, page.Orientation);
        }

        [Fact]
        public void Orientation_RoundTrips()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            page.Orientation = PageOrientation.Landscape;

            Assert.Equal(PageOrientation.Landscape, page.Orientation);
        }

        [Fact]
        public void Size_SetsMediaBoxDimensions()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            page.Size = PageSize.A5;

            Assert.Equal(PageSize.A5, page.Size);
            Assert.True(page.MediaBox.Width > 0);
            Assert.True(page.MediaBox.Height > 0);
        }

        [Fact]
        public void StructParents_DefaultsToZero()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            Assert.Equal(0, page.StructParents);
        }

        [Fact]
        public void StructParents_RoundTrips()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            page.StructParents = 4;

            Assert.Equal(4, page.StructParents);
        }

        [Fact]
        public void Size_InvalidValue_Throws()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => page.Size = (PageSize)9999);
        }

        [Fact]
        public void Height_PortraitAppliesToMediaBoxHeight()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            page.Height = 500;

            Assert.Equal(500, page.Height.Point, 3);
            Assert.Equal(PageSize.Undefined, page.Size);
        }

        [Fact]
        public void Width_PortraitAppliesToMediaBoxWidth()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            page.Width = 400;

            Assert.Equal(400, page.Width.Point, 3);
            Assert.Equal(PageSize.Undefined, page.Size);
        }

        [Fact]
        public void Height_Landscape_AppliesToMediaBoxWidth()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Orientation = PageOrientation.Landscape;

            page.Height = 500;

            Assert.Equal(500, page.MediaBox.Width, 3);
        }

        [Fact]
        public void Width_Landscape_AppliesToMediaBoxHeight()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            page.Orientation = PageOrientation.Landscape;

            page.Width = 400;

            Assert.Equal(400, page.MediaBox.Height, 3);
        }

        [Fact]
        public void Rotate_MultipleOfNinety_RoundTrips()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            page.Rotate = 180;

            Assert.Equal(180, page.Rotate);
        }

        [Fact]
        public void Rotate_NotMultipleOfNinety_Throws()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            Assert.Throws<ArgumentException>(() => page.Rotate = 45);
        }

        [Fact]
        public void CropBox_ArtBox_BleedBox_TrimBox_RoundTrip()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var rect = new PdfRectangle(0, 0, 100, 200);

            page.CropBox = rect;
            page.ArtBox = rect;
            page.BleedBox = rect;
            page.TrimBox = rect;

            Assert.Equal(rect.Width, page.CropBox.Width);
            Assert.Equal(rect.Width, page.ArtBox.Width);
            Assert.Equal(rect.Width, page.BleedBox.Width);
            Assert.Equal(rect.Width, page.TrimBox.Width);
        }

        [Fact]
        public void TrimMargins_All_SetsAllFourMargins()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            page.TrimMargins.All = 10;

            Assert.Equal(10, page.TrimMargins.Left.Point, 3);
            Assert.Equal(10, page.TrimMargins.Right.Point, 3);
            Assert.Equal(10, page.TrimMargins.Top.Point, 3);
            Assert.Equal(10, page.TrimMargins.Bottom.Point, 3);
        }

        [Fact]
        public void Tag_RoundTrips()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();
            var tag = new object();

            page.Tag = tag;

            Assert.Same(tag, page.Tag);
        }

        [Fact]
        public void Close_SetsIsClosed()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            Assert.False(page.IsClosed);

            page.Close();

            Assert.True(page.IsClosed);
        }

        [Fact]
        public void Contents_IsNotNullAndCached()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            var contents = page.Contents;

            Assert.NotNull(contents);
            Assert.Same(contents, page.Contents);
        }

        [Fact]
        public void HasAnnotations_WithNoAnnotsEntry_Throws()
        {
            // HasAnnotations's getter does `Elements.GetValue(Keys.Annots)` (no VCF.Create), which
            // returns null when the page has no /Annots entry yet, then immediately dereferences it
            // via `_annotations.Page = this` -- a real, pre-existing NullReferenceException bug for
            // any page that hasn't had an annotation added yet. Documented here, not fixed.
            var doc = new PdfDocument();
            var page = doc.AddPage();

            Assert.Throws<NullReferenceException>(() => page.HasAnnotations);
        }

        [Fact]
        public void HasAnnotations_TrueAfterAnnotationAdded()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            page.AddWebLink(new PdfRectangle(0, 0, 10, 10), "https://example.com");

            Assert.True(page.HasAnnotations);
        }

        [Fact]
        public void AddDocumentLink_ByPageNumber_AddsAnnotation()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            // Page numbers are one-based; 0 throws ArgumentException (see CreateDocumentLink).
            var annotation = page.AddDocumentLink(new PdfRectangle(0, 0, 10, 10), 1);

            Assert.NotNull(annotation);
            Assert.Equal(1, page.Annotations.Count);
        }

        [Fact]
        public void AddDocumentLink_ByName_AddsAnnotation()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            var annotation = page.AddDocumentLink(new PdfRectangle(0, 0, 10, 10), "destination");

            Assert.NotNull(annotation);
            Assert.Equal(1, page.Annotations.Count);
        }

        [Fact]
        public void AddFileLink_AddsAnnotation()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            var annotation = page.AddFileLink(new PdfRectangle(0, 0, 10, 10), "file.pdf");

            Assert.NotNull(annotation);
            Assert.Equal(1, page.Annotations.Count);
        }

        [Fact]
        public void CustomValues_DefaultsToNonNull()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            Assert.NotNull(page.CustomValues);
        }

        [Fact]
        public void CustomValues_SetNonNull_Throws()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            Assert.Throws<ArgumentException>(() => page.CustomValues = new PdfCustomValues());
        }

        [Fact]
        public void Resources_IsNotNull()
        {
            var doc = new PdfDocument();
            var page = doc.AddPage();

            Assert.NotNull(page.Resources);
        }
    }
}

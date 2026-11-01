using PeachDrawing.Core;
using PeachDrawing.PDF;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>What a viewer navigates by - links, outline, named destinations, document information, attachments - read back from a file the writer produced.</summary>
    public class PdfNavigationTests
    {
        private static byte[] Build(Action<PdfComposer> compose, Action<PdfComposerOptions>? configure = null)
        {
            var options = new PdfComposerOptions { CompressContentStreams = false };
            configure?.Invoke(options);
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            composer.AddPage(200, 300);
            composer.AddPage(200, 300);
            composer.AddPage(200, 300);
            compose(composer);
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        [Fact]
        public void UriLink_KeepsItsAddressAndRectangle()
        {
            var pdf = Build(c => c.Pages[0].AddLink(new Rect(10, 20, 50, 15), "https://example.com/a"));

            var link = Assert.Single(PdfPageSource.Open(pdf).GetLinks(0));

            Assert.Equal("https://example.com/a", link.Uri);
            Assert.Null(link.Destination);
            Assert.Equal(10, link.Bounds.X, 0.5);
            Assert.Equal(20, link.Bounds.Y, 0.5);
            Assert.Equal(50, link.Bounds.Width, 0.5);
            Assert.Equal(15, link.Bounds.Height, 0.5);
        }

        [Fact]
        public void DestinationLink_LeadsToThePageAndHeightItNames()
        {
            var pdf = Build(c =>
            {
                c.AddNamedDestination("target", 2, 40);
                c.Pages[0].AddDestinationLink(new Rect(5, 5, 20, 20), "target");
            });
            var source = PdfPageSource.Open(pdf);

            var link = Assert.Single(source.GetLinks(0));

            Assert.Null(link.Uri);
            Assert.Equal(2, link.Destination!.PageIndex);
            Assert.Equal(40, link.Destination.TopPoints, 0.5);
            Assert.Empty(source.GetLinks(1));
        }

        [Fact]
        public void NamedDestinations_AreListedWithTheirPlace()
        {
            var pdf = Build(c =>
            {
                c.AddNamedDestination("intro", 0, 0);
                c.AddNamedDestination("end", 2, 120);
            });

            var names = PdfPageSource.Open(pdf).GetNamedDestinations();

            Assert.Equal(2, names["end"].PageIndex);
            Assert.Equal(120, names["end"].TopPoints, 0.5);
            Assert.Equal(0, names["intro"].PageIndex);
        }

        [Fact]
        public void Outline_KeepsOrderNestingTitlesAndTargets()
        {
            var pdf = Build(c => c.AddBookmarks(
            [
                new PdfBookmark(1, "Chapter", true) { PageIndex = 0, TopPoints = 10 },
                new PdfBookmark(2, "Section é世", false) { PageIndex = 1, TopPoints = 50 },
                new PdfBookmark(1, "Site", false) { Uri = "https://example.com" },
            ]));

            var outline = PdfPageSource.Open(pdf).GetOutline();

            Assert.Equal(["Chapter", "Section é世", "Site"], outline.Select(o => o.Title));
            Assert.Equal([1, 2, 1], outline.Select(o => o.Level));
            Assert.Equal(1, outline[1].PageIndex);
            Assert.Equal(50, outline[1].TopPoints, 0.5);
            Assert.Equal("https://example.com", outline[2].Uri);
            Assert.True(outline[0].IsOpen);
        }

        [Fact]
        public void DocumentInformation_IsRead()
        {
            var pdf = Build(_ => { }, o => (o.Title, o.Author, o.Subject, o.Keywords) = ("A title é", "An author", "A subject", "k1, k2"));

            var info = PdfPageSource.Open(pdf).GetMetadata();

            Assert.Equal("A title é", info.Title);
            Assert.Equal("An author", info.Author);
            Assert.Equal("A subject", info.Subject);
            Assert.Equal("k1, k2", info.Keywords);
        }

        [Fact]
        public void AttachedFiles_AreReadWithTheirData()
        {
            var data = "id,name\n1,a\n"u8.ToArray();
            var pdf = Build(_ => { }, o => o.EmbeddedFiles.Add(new PdfEmbeddedFile { FileName = "table.csv", Data = data, MimeType = "text/csv", Description = "A table" }));

            var file = Assert.Single(PdfPageSource.Open(pdf).GetAttachments());

            Assert.Equal("table.csv", file.FileName);
            Assert.Equal("A table", file.Description);
            Assert.Equal("text/csv", file.MimeType);
            Assert.Equal(data, file.Data);
        }

        [Fact]
        public void FileWithoutNavigation_GivesEmptyResults()
        {
            var source = PdfPageSource.Open(TestPdf.Build("0 0 10 10 re f"));

            Assert.Empty(source.GetLinks(0));
            Assert.Empty(source.GetOutline());
            Assert.Empty(source.GetNamedDestinations());
            Assert.Empty(source.GetAttachments());
            Assert.Null(source.GetMetadata().Title);
        }

        [Fact]
        public void LinkOnARotatedPage_IsInTheTermsOfThePageAsShown()
        {
            // A 200x100 page turned 90 degrees clockwise is shown 100 wide and 200 tall: PDF (x, y) lands at (y, x).
            var pdf = TestPdf.Build("0 0 1 1 re f", pageExtras: "/Rotate 90 /Annots [5 0 R]", width: 200, height: 100,
                extraObjects: "<< /Type /Annot /Subtype /Link /Rect [10 20 30 40] /A << /S /URI /URI (https://example.com) >> >>");

            var link = Assert.Single(PdfPageSource.Open(pdf).GetLinks(0));

            Assert.Equal(20, link.Bounds.X, 0.01);
            Assert.Equal(10, link.Bounds.Y, 0.01);
            Assert.Equal(20, link.Bounds.Width, 0.01);
            Assert.Equal(20, link.Bounds.Height, 0.01);
        }

        [Fact]
        public void DestinationsInDifferentForms_AreAllUnderstood()
        {
            // A /Dest array on the link itself, a /GoTo action naming a destination through the old /Dests dictionary, and an unusable one.
            var pdf = TestPdf.Build("0 0 1 1 re f", pageExtras: "/Annots [5 0 R 6 0 R 7 0 R]",
                extraObjects:
                [
                    "<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Dest [3 0 R /XYZ 5 80 0] >>",
                    "<< /Type /Annot /Subtype /Link /Rect [20 0 30 10] /A << /S /GoTo /D /Named >> >>",
                    "<< /Type /Annot /Subtype /Link /Rect [40 0 50 10] /A << /S /Launch /F (x.exe) >> >>",
                ]);
            // The catalog needs /Dests for the named one; append it by rewriting the catalog line.
            var text = System.Text.Encoding.Latin1.GetString(pdf).Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /Dests << /Named [3 0 R /FitH 25] >>");
            var links = PdfPageSource.Open(System.Text.Encoding.Latin1.GetBytes(text)).GetLinks(0);

            Assert.Equal(2, links.Count);
            Assert.Equal(20, links[0].Destination!.TopPoints, 0.01);   // 100 - 80
            Assert.Equal(75, links[1].Destination!.TopPoints, 0.01);   // 100 - 25
        }
    }
}

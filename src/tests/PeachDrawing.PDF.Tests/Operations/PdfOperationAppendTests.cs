using PeachDrawing.Core;

namespace PeachDrawing.PDF.Tests.Operations
{
    /// <summary><see cref="PdfOperation.AppendTo"/>: the pages of an operation added to a document that is being composed.</summary>
    public class PdfOperationAppendTests
    {
        /// <summary>A document of <paramref name="pages"/> pages, page n a solid red of its own (20, 60, 100...).</summary>
        private static byte[] Make(int pages, Action<PdfComposer>? more = null)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            for (var i = 0; i < pages; i++)
            {
                var page = composer.AddPage(100, 100);
                page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, i * 40 + 20, 0, 0)), 0, 0, 100, 100);
            }

            more?.Invoke(composer);
            return Save(composer);
        }

        private static byte[] Save(PdfComposer composer)
        {
            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        private static int RedAt(byte[] pdf, int page)
        {
            var source = PdfPageSource.Open(pdf);
            var (w, h) = source.GetPageSize(page);
            using var canvas = new RasterRenderContext().CreateCanvas((int)w, (int)h);
            source.Paint(page, canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            return span[((int)h / 2 * (int)w + (int)w / 2) * 4];
        }

        private static void Near(int expected, int actual) => Assert.True(Math.Abs(expected - actual) <= 3, $"expected about {expected}, was {actual}");

        private static byte[] WithLink(int pages) => Make(pages, more: c =>
        {
            c.AddNamedDestination("end", pages - 1, 10);
            c.Pages[0].AddDestinationLink(new Rect(10, 10, 20, 20), "end");
            c.Pages[0].AddLink(new Rect(40, 40, 20, 20), "https://example.com/");
        });

        /// <summary>A document that already has two pages (the first two shades of green), with the operation's pages added after them.</summary>
        private static byte[] ComposeAfterTwoPages(Action<PdfComposer, PdfRenderContext> add)
        {
            var context = new PdfRenderContext();
            using var composer = new PdfComposer(context, new PdfComposerOptions { CompressContentStreams = false });
            for (var i = 0; i < 2; i++)
            {
                var page = composer.AddPage(100, 100);
                page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 0, i * 40 + 20, 0)), 0, 0, 100, 100);
            }

            add(composer, context);
            return Save(composer);
        }

        [Theory]
        [InlineData(PdfCopyMode.Redraw)]
        [InlineData(PdfCopyMode.ObjectCopy)]
        public void AppendTo_PutsThePagesAfterTheOnesTheDocumentHas(PdfCopyMode mode)
        {
            var added = Make(3);

            var result = ComposeAfterTwoPages((composer, context) => PdfOperation.LoadBytes(added).AppendTo(composer, context, mode));

            Assert.Equal(5, PdfPageSource.Open(result).PageCount);
            Near(0, RedAt(result, 1));
            Near(20, RedAt(result, 2));
            Near(60, RedAt(result, 3));
            Near(100, RedAt(result, 4));
        }

        [Fact]
        public void AppendTo_UsesTheOperationsOwnCopyModeUnlessGivenOne()
        {
            var added = Make(2);
            var operation = PdfOperation.LoadBytes(added).WithCopyMode(PdfCopyMode.ObjectCopy);

            var result = ComposeAfterTwoPages((composer, context) => operation.AppendTo(composer, context));

            Assert.Equal(4, PdfPageSource.Open(result).PageCount);
            Near(60, RedAt(result, 3));
        }

        [Fact]
        public void AppendTo_HonoursTakePagesAndMerges()
        {
            var result = ComposeAfterTwoPages((composer, context) =>
                PdfOperation.LoadBytes(Make(3)).TakePages("3,1").MergeBytes(Make(2), "2").AppendTo(composer, context));

            Assert.Equal(5, PdfPageSource.Open(result).PageCount);
            Near(100, RedAt(result, 2));
            Near(20, RedAt(result, 3));
            Near(60, RedAt(result, 4));
        }

        [Fact]
        public void AppendTo_PointsLinksAndTheOutlineAtThePagesTheyNowAreOn()
        {
            var outlined = Make(2, c => c.AddBookmarks([new PdfBookmark(1, "Added", true) { PageIndex = 1 }]));
            var linked = WithLink(2);

            var result = ComposeAfterTwoPages((composer, context) =>
            {
                PdfOperation.LoadBytes(linked).AppendTo(composer, context);
                PdfOperation.LoadBytes(outlined).AppendTo(composer, context);
            });

            var source = PdfPageSource.Open(result);
            Assert.Equal(6, source.PageCount);
            var links = source.GetLinks(2);
            Assert.Contains(links, l => l.Uri == "https://example.com/");
            Assert.Contains(links, l => l.Destination?.PageIndex == 3);   // the second page of the first file, after the two the document had
            Assert.Equal(5, Assert.Single(source.GetOutline()).PageIndex);   // the second page of the second file
        }

        [Fact]
        public void AppendTo_CalledTwice_KeepsEachFilesLinksAndNamesApart()
        {
            var file = WithLink(2);

            var result = ComposeAfterTwoPages((composer, context) =>
            {
                PdfOperation.LoadBytes(file).AppendTo(composer, context);
                PdfOperation.LoadBytes(file).AppendTo(composer, context);
            });

            var source = PdfPageSource.Open(result);
            Assert.Equal(6, source.PageCount);
            Assert.Contains(source.GetLinks(2), l => l.Destination?.PageIndex == 3);
            Assert.Contains(source.GetLinks(4), l => l.Destination?.PageIndex == 5);
            Assert.Equal(["end", "end_2"], source.GetNamedDestinations().Select(d => d.Key).Where(n => n.StartsWith("end", StringComparison.Ordinal)).Order());
            Assert.Equal(2, source.GetNamedDestinations().Count(d => d.Key.StartsWith("PeachPDFMerged", StringComparison.Ordinal)));
        }

        [Fact]
        public void AppendTo_RefusesTheStepsThatBelongToTheWholeFile()
        {
            var file = Make(1);
            var composer = new PdfComposer(new PdfRenderContext());
            var context = new PdfRenderContext();

            Assert.Throws<InvalidOperationException>(() => PdfOperation.LoadBytes(file).Linearize().AppendTo(composer, context));
            Assert.Throws<InvalidOperationException>(() => PdfOperation.LoadBytes(file).Encrypt(new PdfEncryptionOptions { UserPassword = "x" }).AppendTo(composer, context));
            Assert.Throws<InvalidOperationException>(() => PdfOperation.LoadBytes(file)
                .AddAttachment(new PdfFileAttachment { Data = [1], Key = "a.txt" }).AppendTo(composer, context));
            Assert.Throws<InvalidOperationException>(() => PdfOperation.LoadBytes(file).ExtendMetadata("<x:y xmlns:x=\"urn:x\">1</x:y>").AppendTo(composer, context));
            Assert.Equal(0, composer.PageCount);
        }

        [Fact]
        public void AppendTo_RefusesMissingArguments()
        {
            var operation = PdfOperation.LoadBytes(Make(1));

            Assert.Throws<ArgumentNullException>(() => operation.AppendTo(null!, new PdfRenderContext()));
            Assert.Throws<ArgumentNullException>(() => operation.AppendTo(new PdfComposer(new PdfRenderContext()), null!));
        }

        [Fact]
        public void AppendTo_RefusesAnOperationWithNoPages()
        {
            var composer = new PdfComposer(new PdfRenderContext());

            Assert.Throws<InvalidOperationException>(() => PdfOperation.LoadBytes(Make(1)).TakePages("x1").AppendTo(composer, new PdfRenderContext()));
        }
    }
}

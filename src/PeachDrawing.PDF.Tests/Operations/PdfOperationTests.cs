using PeachDrawing.Core;

namespace PeachDrawing.PDF.Tests.Operations
{
    public class PdfOperationTests
    {
        /// <summary>A document of <paramref name="pages"/> pages, page n a solid red of its own (20, 60, 100...) and the given size.</summary>
        private static byte[] Make(int pages, int width = 100, int height = 100, PdfComposerOptions? options = null, Action<PdfComposer>? more = null)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), options ?? new PdfComposerOptions { CompressContentStreams = false });
            for (var i = 0; i < pages; i++)
            {
                var page = composer.AddPage(width, height);
                page.Canvas.DrawRectangle(page.Canvas.GetSolidBrush(PaintColor.FromArgb(255, i * 40 + 20, 0, 0)), 0, 0, width, height);
            }

            more?.Invoke(composer);
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

        [Fact]
        public void LoadBytes_SavesAllItsPages()
        {
            var result = PdfOperation.LoadBytes(Make(3)).ToArray();
            Assert.Equal(3, PdfPageSource.Open(result).PageCount);
        }

        [Fact]
        public void TakePages_KeepsTheNamedPagesInTheNamedOrder()
        {
            var result = PdfOperation.LoadBytes(Make(4)).TakePages("3,1,1").ToArray();

            Assert.Equal(3, PdfPageSource.Open(result).PageCount);
            Near(100, RedAt(result, 0));
            Near(20, RedAt(result, 1));
            Near(20, RedAt(result, 2));
        }

        [Fact]
        public void MergeBytes_AppendsTheSelectedPagesOfTheOtherFile()
        {
            var result = PdfOperation.LoadBytes(Make(2)).MergeBytes(Make(5, 200, 300), "z,1").ToArray();

            var source = PdfPageSource.Open(result);
            Assert.Equal(4, source.PageCount);
            Assert.Equal((100.0, 100.0), source.GetPageSize(1));
            Assert.Equal((200.0, 300.0), source.GetPageSize(2));
            Near(180, RedAt(result, 2));   // the 5th page of the second file
            Near(20, RedAt(result, 3));
        }

        [Fact]
        public void Merge_StreamsAndFilesBehaveLikeBytes()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                File.WriteAllBytes(path, Make(2));
                using var stream = new MemoryStream(Make(1));
                var operation = PdfOperation.LoadFile(path).MergeFile(path, "1").MergeStream(stream);

                Assert.Equal(4, operation.PageCount);
                operation.Save(path + ".out");
                Assert.Equal(4, PdfPageSource.Open(File.ReadAllBytes(path + ".out")).PageCount);
            }
            finally
            {
                File.Delete(path);
                File.Delete(path + ".out");
            }
        }

        private static byte[] WithLink(int pages) => Make(pages, more: c =>
        {
            c.AddNamedDestination("end", pages - 1, 10);
            c.Pages[0].AddDestinationLink(new Rect(10, 10, 20, 20), "end");
            c.Pages[0].AddLink(new Rect(40, 40, 20, 20), "https://example.com/");
        });

        [Fact]
        public void Merge_CarriesLinksBetweenPagesToTheirNewPlaces()
        {
            var result = PdfOperation.LoadBytes(WithLink(2)).MergeBytes(WithLink(3)).ToArray();

            var source = PdfPageSource.Open(result);
            var first = source.GetLinks(0);
            Assert.Contains(first, l => l.Uri == "https://example.com/");
            Assert.Contains(first, l => l.Destination?.PageIndex == 1);
            Assert.Contains(source.GetLinks(2), l => l.Destination?.PageIndex == 4);   // the second file's own last page
            Assert.Equal(2, source.GetNamedDestinations().Count(d => d.Key.StartsWith("end", StringComparison.Ordinal)));
        }

        [Fact]
        public void TakePages_DropsWhatLeadsToAPageThatWasNotTaken()
        {
            var result = PdfOperation.LoadBytes(WithLink(3)).TakePages("1-2").ToArray();

            var source = PdfPageSource.Open(result);
            Assert.DoesNotContain(source.GetLinks(0), l => l.Destination is not null);
            Assert.Contains(source.GetLinks(0), l => l.Uri is not null);
            Assert.Empty(source.GetNamedDestinations());
        }

        [Fact]
        public void Merge_CarriesTheOutlineOfEachFile()
        {
            static byte[] WithOutline(string title) => Make(2, more: c => c.AddBookmarks([new PdfBookmark(1, title, true) { PageIndex = 1 }]));

            var result = PdfOperation.LoadBytes(WithOutline("A")).MergeBytes(WithOutline("B")).ToArray();

            var outline = PdfPageSource.Open(result).GetOutline();
            Assert.Equal(["A", "B"], outline.Select(o => o.Title));
            Assert.Equal([1, 3], outline.Select(o => o.PageIndex!.Value));
        }

        [Fact]
        public void OverlayFile_DrawsTheLayerOverTheTargetPages()
        {
            var result = PdfOperation.LoadBytes(Make(3))   // 20, 60, 100
                .OverlayFile(new PdfLayerConfiguration { Data = Make(2), TargetPages = "2-z", SourcePages = "2", RepeatSourcePages = "1" })
                .ToArray();

            Near(20, RedAt(result, 0));
            Near(60, RedAt(result, 1));    // the layer's second page (60) over page 2
            Near(20, RedAt(result, 2));    // then the repeated first page (20) over page 3
        }

        [Fact]
        public void UnderlayFile_IsHiddenByThePageItself()
        {
            var result = PdfOperation.LoadBytes(Make(1))
                .UnderlayFile(new PdfLayerConfiguration { Data = Make(2) })
                .ToArray();

            Assert.Equal(1, PdfPageSource.Open(result).PageCount);
            Near(20, RedAt(result, 0));
        }

        [Fact]
        public void Layers_FollowTheirPagesThroughALaterTakePages()
        {
            var result = PdfOperation.LoadBytes(Make(3))
                .OverlayFile(new PdfLayerConfiguration { Data = Make(2), SourcePages = "2", TargetPages = "3" })
                .TakePages("3")
                .ToArray();

            Near(60, RedAt(result, 0));
        }

        [Fact]
        public void Layer_NeedsExactlyOneSource()
        {
            var operation = PdfOperation.LoadBytes(Make(1));
            Assert.Throws<ArgumentException>(() => operation.OverlayFile(new PdfLayerConfiguration()));
            Assert.Throws<ArgumentException>(() => operation.OverlayFile(new PdfLayerConfiguration { Data = Make(1), FilePath = "x.pdf" }));
        }

        [Fact]
        public void Encrypt_ThenLoadWithEitherPassword()
        {
            var encrypted = PdfOperation.LoadBytes(Make(2))
                .Encrypt(new PdfEncryptionOptions { UserPassword = "user", OwnerPassword = "owner" })
                .ToArray();

            Assert.Throws<PdfPasswordException>(() => PdfOperation.LoadBytes(encrypted));
            var wrong = Assert.Throws<PdfPasswordException>(() => PdfOperation.LoadBytes(encrypted, "nope"));
            Assert.True(wrong.PasswordWasGiven);

            Assert.Equal(2, PdfOperation.LoadBytes(encrypted, "user").PageCount);
            Assert.Equal(2, PdfOperation.LoadBytes(encrypted, "owner").PageCount);
        }

        [Fact]
        public void Decrypt_WritesAnOpenFile()
        {
            var encrypted = PdfOperation.LoadBytes(Make(1)).Encrypt(new PdfEncryptionOptions { UserPassword = "u", OwnerPassword = "o" }).ToArray();

            var open = PdfOperation.LoadBytes(encrypted, "u").Decrypt().RemoveRestrictions().ToArray();

            Assert.Equal(1, PdfPageSource.Open(open).PageCount);   // no password needed
        }

        [Fact]
        public void AddAttachment_AttachesAndReplaces()
        {
            var attachment = new PdfFileAttachment { Data = [1, 2, 3], Key = "data.bin", MimeType = "application/x-test", Description = "d", Relationship = PdfEmbeddedFileRelationship.Data };
            var result = PdfOperation.LoadBytes(Make(1)).AddAttachment(attachment).ToArray();

            var file = Assert.Single(PdfPageSource.Open(result).GetAttachments());
            Assert.Equal("data.bin", file.FileName);
            Assert.Equal(new byte[] { 1, 2, 3 }, file.Data);

            // A second pass over the saved file: a clash is refused unless it asks to replace.
            var clash = PdfOperation.LoadBytes(result).AddAttachment(new PdfFileAttachment { Data = [9], Key = "data.bin" });
            Assert.Throws<InvalidOperationException>(() => clash.ToArray());
            var replaced = PdfOperation.LoadBytes(result).AddAttachment(new PdfFileAttachment { Data = [9], Key = "data.bin", Replace = true }).ToArray();
            Assert.Equal(new byte[] { 9 }, Assert.Single(PdfPageSource.Open(replaced).GetAttachments()).Data);
        }

        [Fact]
        public void AddAttachment_RefusesIncompleteInput()
        {
            var operation = PdfOperation.LoadBytes(Make(1));
            Assert.Throws<ArgumentException>(() => operation.AddAttachment(new PdfFileAttachment()));
            Assert.Throws<ArgumentException>(() => operation.AddAttachment(new PdfFileAttachment { Data = [1] }));
            Assert.Throws<FileNotFoundException>(() => operation.AddAttachment(new PdfFileAttachment { FilePath = "does-not-exist.bin" }));
        }

        [Fact]
        public void ExtendMetadata_WritesTheElementsIntoTheXmpPacket()
        {
            var result = PdfOperation.LoadBytes(Make(1))
                .ExtendMetadata("<custom:Value xmlns:custom='http://example.com/custom'>Example</custom:Value>")
                .ToArray();

            Assert.Contains("http://example.com/custom", System.Text.Encoding.Latin1.GetString(result));
            Assert.ThrowsAny<System.Xml.XmlException>(() => PdfOperation.LoadBytes(Make(1)).ExtendMetadata("<unclosed>"));
        }

        [Fact]
        public void DocumentInformation_IsKeptFromTheFirstFileThatGivesIt()
        {
            var first = Make(1, options: new PdfComposerOptions { Title = "First", CompressContentStreams = false });
            var second = Make(1, options: new PdfComposerOptions { Title = "Second", Author = "Someone", CompressContentStreams = false });

            var info = PdfPageSource.Open(PdfOperation.LoadBytes(first).MergeBytes(second).ToArray()).GetMetadata();

            Assert.Equal("First", info.Title);
            Assert.Equal("Someone", info.Author);
        }

        [Fact]
        public void TakePages_RefusesAPageTheFileLacks()
        {
            var operation = PdfOperation.LoadBytes(Make(1));
            Assert.Throws<FormatException>(() => operation.TakePages("2"));
        }

        [Fact]
        public void WrongArguments_AreRefused()
        {
            Assert.Throws<ArgumentNullException>(() => PdfOperation.LoadBytes(null!));
            Assert.Throws<ArgumentException>(() => PdfOperation.LoadFile(""));
            var operation = PdfOperation.LoadBytes(Make(1));
            Assert.Throws<ArgumentNullException>(() => operation.Encrypt(null!));
            Assert.Throws<ArgumentNullException>(() => operation.Save((Stream)null!));
        }
    }
}

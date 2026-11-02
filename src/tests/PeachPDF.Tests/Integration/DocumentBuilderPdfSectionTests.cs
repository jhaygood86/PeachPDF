using PeachDrawing;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Layout;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="IDocumentBuilder.Append(byte[], PdfCopyMode, string?)"/> and <see cref="IDocumentBuilder.Prepend(byte[], PdfCopyMode, string?)"/>:
    /// existing PDFs written into the document being generated, in the order the builder was called in.
    /// </summary>
    public class DocumentBuilderPdfSectionTests
    {
        /// <summary>An existing file of <paramref name="pages"/> pages of the given square size, built the way a caller's static file would be.</summary>
        private static async Task<byte[]> StaticFileAsync(int pages, double size, string label)
        {
            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                for (var i = 0; i < pages; i++)
                {
                    var n = i;
                    doc.Page(page =>
                    {
                        page.Size(size, size).Margin(10);
                        page.Content(c => c.Text($"{label} {n + 1}"));
                    });
                }
            });
            return await SaveAsync(document);
        }

        private static Action<IDocumentBuilder> Generated(string title, double size = 300) => doc =>
        {
            doc.Page(page =>
            {
                page.Size(size, size).Margin(20);
                page.Footer(f => f.Text(t =>
                {
                    t.CurrentPageNumber();
                    t.Span("/");
                    t.TotalPages();
                }));
                page.Content(c => c.Column(column => column.Item().Text(title)));
            });
        };

        private static async Task<byte[]> SaveAsync(PeachPdfDocument document)
        {
            using var ms = new MemoryStream();
            document.Save(ms);
            return ms.ToArray();
        }

        private static double[] Sizes(byte[] pdf)
        {
            var source = PdfPageSource.Open(pdf);
            return [.. Enumerable.Range(0, source.PageCount).Select(i => source.GetPageSize(i).Width)];
        }

        [Fact]
        public async Task Append_BeforeAndAfterPage_KeepsTheOrderOfTheCalls()
        {
            var before = await StaticFileAsync(2, 200, "front");
            var after = await StaticFileAsync(1, 250, "back");

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Append(before);
                Generated("body")(doc);
                doc.Append(after);
            });

            Assert.Equal(4, document.PageCount);
            var sizes = Sizes(await SaveAsync(document));
            Assert.Equal(new[] { 200d, 200d, 300d, 250d }, sizes);
        }

        [Fact]
        public async Task Prepend_PutsThePdfFirstWhereverItIsCalled_AndKeepsTheOrderOfSeveralCalls()
        {
            var one = await StaticFileAsync(1, 200, "one");
            var two = await StaticFileAsync(1, 220, "two");
            var last = await StaticFileAsync(1, 250, "last");

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                Generated("body")(doc);
                doc.Append(last);
                doc.Prepend(one);
                doc.Prepend(two);
            });

            var sizes = Sizes(await SaveAsync(document));
            Assert.Equal(new[] { 200d, 220d, 300d, 250d }, sizes);
        }

        [Fact]
        public async Task Append_AcceptsStreamsAndOperations_AndAnOperationsPagesCanBeChosen()
        {
            var file = await StaticFileAsync(3, 200, "file");
            using var stream = new MemoryStream(file);

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Prepend(stream);
                doc.Append(new MemoryStream(file));
                doc.Append(PdfOperation.LoadBytes(file).TakePages("2-3"));
                doc.Prepend(PdfOperation.LoadBytes(file).TakePages("1"), PdfCopyMode.ObjectCopy);
                Generated("body")(doc);
            });

            Assert.Equal(3 + 1 + 1 + 2 + 3, document.PageCount);
        }

        [Fact]
        public async Task ObjectCopy_KeepsTheContentOfTheAddedPagesAsTheyWere_AndRedrawWritesItAgain()
        {
            var file = await StaticFileAsync(1, 200, "kept");
            var original = PdfFile.Parse(file);

            var copied = PdfFile.Parse(await SaveAsync(await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Append(file, PdfCopyMode.ObjectCopy);
                Generated("body")(doc);
            })));
            var redrawn = PdfFile.Parse(await SaveAsync(await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Append(file);
                Generated("body")(doc);
            })));

            Assert.Equal(original.ContentText(original.Pages[0]), copied.ContentText(copied.Pages[0]));
            Assert.NotEqual(original.ContentText(original.Pages[0]), redrawn.ContentText(redrawn.Pages[0]));
        }

        [Fact]
        public async Task PagesAddedFirst_DoNotDisturbTheOutlineOfThePagesAfterThem()
        {
            var front = await StaticFileAsync(2, 200, "front");

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Append(front);
                Generated("one")(doc);
                doc.Page(page =>
                {
                    page.Size(300, 300).Margin(20);
                    page.Content(c => c.Bookmark("Second heading").Text("Second heading"));
                });
            });

            var source = PdfPageSource.Open(await SaveAsync(document));
            Assert.Equal(4, source.PageCount);
            var heading = Assert.Single(source.GetOutline(), o => o.Title == "Second heading");
            Assert.Equal(3, heading.PageIndex);
        }

        [Fact]
        public async Task TwoPageSections_PutTheOutlineOfTheSecondOnItsOwnPage()
        {
            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                Generated("one")(doc);
                doc.Page(page =>
                {
                    page.Size(300, 300).Margin(20);
                    page.Content(c => c.Bookmark("Second heading").Text("Second heading"));
                });
            });

            var heading = Assert.Single(PdfPageSource.Open(await SaveAsync(document)).GetOutline());
            Assert.Equal(1, heading.PageIndex);
        }

        [Fact]
        public async Task UseContinuousPageNumbers_RunsTheFooterOnAfterTheAddedPages_AsAMergeDoes()
        {
            var front = await StaticFileAsync(2, 200, "front");

            var plain = PdfFile.Parse(await SaveAsync(await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Append(front);
                Generated("A")(doc);
            })));
            var continuous = PdfFile.Parse(await SaveAsync(await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Append(front);
                Generated("A")(doc);
                doc.UseContinuousPageNumbers();
            })));
            var merged = PdfFile.Parse(await SaveAsync(await new PdfGenerator().CreateMergedDocument(
                DocumentMerge.Of(Generated("front", 200), Generated("front", 200), Generated("A")).UseContinuousPageNumbers())));

            Assert.NotEqual(plain.ContentText(plain.Pages[2]), continuous.ContentText(continuous.Pages[2]));

            // The third page of three, in a document of three pages: the footer reads "3/3" in both.
            Assert.Equal(3, continuous.PageCount);
            Assert.Equal(merged.ContentText(merged.Pages[2]), continuous.ContentText(continuous.Pages[2]));
        }

        [Fact]
        public async Task TheDocumentsConformanceEncryptionAndLinearizationCoverTheAddedPages()
        {
            var front = await StaticFileAsync(2, 200, "front");
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                DefaultLanguage = "en",
                Metadata = new PdfDocumentMetadata { Title = "Bundle", CreationDate = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero) },
                Encryption = null,
                Linearize = true,
            };

            var bytes = await SaveAsync(await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Prepend(front);
                Generated("body")(doc);
            }, config));

            var text = Encoding.Latin1.GetString(bytes);
            Assert.Contains("/Linearized", text);
            Assert.Contains("pdfaid:part", text);
            Assert.Equal(3, PdfPageSource.Open(bytes).PageCount);
        }

        [Fact]
        public async Task AnEncryptedDocument_EncryptsTheAddedPagesToo()
        {
            var front = await StaticFileAsync(1, 200, "front");
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                Encryption = new PdfEncryptionOptions { UserPassword = "user", OwnerPassword = "owner" },
            };

            var bytes = await SaveAsync(await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Append(front);
                Generated("body")(doc);
            }, config));

            Assert.ThrowsAny<Exception>(() => PdfPageSource.Open(bytes));
            Assert.Equal(2, PdfPageSource.Open(bytes, "user").PageCount);
        }

        [Fact]
        public async Task AnEncryptedFileCanBeAddedWithItsPassword()
        {
            var secret = (await new PdfGenerator().CreateDocument(Generated("secret"), new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                Encryption = new PdfEncryptionOptions { UserPassword = "pw", OwnerPassword = "pw2" },
            }));
            var locked = await SaveAsync(secret);

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Append(locked, password: "pw");
                Generated("body")(doc);
            });

            Assert.Equal(2, document.PageCount);
        }

        [Fact]
        public async Task ADocumentOfOnlyAddedPdfs_IsWritten()
        {
            var file = await StaticFileAsync(2, 200, "only");

            var document = await new PdfGenerator().CreateDocument(doc => doc.Append(file), new PdfGenerateConfig { Linearize = true });

            var bytes = await SaveAsync(document);
            Assert.Equal(2, PdfPageSource.Open(bytes).PageCount);
            Assert.Contains("/Linearized", Encoding.Latin1.GetString(bytes));
        }

        [Fact]
        public async Task AnOperationThatEncryptsOrLinearizes_IsRefused()
        {
            var file = await StaticFileAsync(1, 200, "x");

            await Assert.ThrowsAsync<InvalidOperationException>(() => new PdfGenerator().CreateDocument(doc => doc.Append(PdfOperation.LoadBytes(file).Linearize())));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PdfGenerator().CreateDocument(doc => doc.Append(PdfOperation.LoadBytes(file).Encrypt(new PeachDrawing.PDF.PdfEncryptionOptions { UserPassword = "a" }))));
        }

        [Fact]
        public async Task ALayerDrawnOnTheDocument_LandsOnTheGeneratedPageNotOnTheAddedOnes()
        {
            var front = await StaticFileAsync(2, 200, "front");
            var layer = LayerFile();

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Append(front);
                doc.Page(page =>
                {
                    page.Size(200, 200).Margin(0);
                    page.Content(c => c.Width(100).Height(100).Background(PdfColor.FromRgb(0, 0, 255)).Text(" "));
                });
                doc.OverlayFile(l => l.File(layer).TargetPages("3").SourcePages("1"));
            });

            var bytes = await SaveAsync(document);
            Assert.Equal(3, PdfPageSource.Open(bytes).PageCount);
            Assert.Equal(((byte)255, (byte)0, (byte)0), ColourAt(bytes, 2, 20, 20));
            Assert.Null(ColourAt(bytes, 0, 20, 20));
        }

        [Fact]
        public async Task ALayerFileConfigure_WithAPdfFirst_StillTakesTheDocumentInformationFromTheLayer()
        {
            var front = await StaticFileAsync(1, 200, "front");
            var layer = LayerFile();

            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Prepend(front);
                Generated("body", 200)(doc);
                doc.UnderlayFile(l => l.File(layer));
            });

            Assert.Equal(2, document.PageCount);
        }

        private static byte[] LayerFile()
        {
            using var composer = new PdfComposer(new PdfRenderContext());
            var first = composer.AddPage(200, 200).Canvas;
            first.DrawRectangle(first.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 50, 50);
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        private static (byte R, byte G, byte B)? ColourAt(byte[] pdf, int pageIndex, int x, int y)
        {
            var source = PdfPageSource.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(200, 200);
            source.Paint(pageIndex, canvas);
            var buffer = canvas.ToPixelBuffer();
            var i = (y * buffer.Width + x) * 4;
            var span = buffer.PremultipliedRgba.Span;
            return span[i + 3] == 0 ? null : (span[i], span[i + 1], span[i + 2]);
        }
    }
}

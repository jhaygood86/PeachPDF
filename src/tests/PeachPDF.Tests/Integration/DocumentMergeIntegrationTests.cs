using PeachDrawing.PDF;
using PeachPDF.Layout;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="DocumentMerge"/>: several declarative documents in one PDF, with their page numbers running on from one another or not.
    /// The number each footer shows is asserted on the laid-out fragment tree by the engine tests; here the whole pipeline is run.
    /// </summary>
    public class DocumentMergeIntegrationTests
    {
        private static Action<IDocumentBuilder> Report(string title, int pages, double pageSize = 300) => doc =>
        {
            doc.Page(page =>
            {
                page.Size(pageSize, pageSize).Margin(20);
                page.Footer(f => f.Text(t =>
                {
                    t.CurrentPageNumber();
                    t.Span("/");
                    t.TotalPages();
                }));
                page.Content(c => c.Column(column =>
                {
                    // A 300pt page with 20pt margins holds eight 30pt lines, so 8 * (pages - 1) + 4 of them fill exactly this many pages.
                    for (var i = 0; i < 8 * (pages - 1) + 4; i++)
                        column.Item().Height(30).Text(i == 0 ? title : $"Line {i}");
                }));
            });
        };

        private static async Task<byte[]> SaveAsync(PeachPdfDocument document)
        {
            using var ms = new MemoryStream();
            document.Save(ms);
            return ms.ToArray();
        }

        [Fact]
        public async Task CreateMergedDocument_WritesEveryPartsPagesInOrder()
        {
            var generator = new PdfGenerator();
            var merge = DocumentMerge.Of(Report("A", 2), Report("B", 3));

            var document = await generator.CreateMergedDocument(merge);

            Assert.Equal(2, merge.Count);
            Assert.Equal(MergePageNumbering.Original, merge.Numbering);
            Assert.Equal(5, document.PageCount);
            Assert.Equal(5, PdfPageSource.Open(await SaveAsync(document)).PageCount);
        }

        [Fact]
        public async Task ContinuousNumbering_ChangesTheFooterOfLaterPartsOnly()
        {
            var generator = new PdfGenerator();
            var original = PdfFile.Parse(await SaveAsync(await generator.CreateMergedDocument(DocumentMerge.Of(Report("A", 2), Report("B", 3)))));
            var continuous = PdfFile.Parse(await SaveAsync(await generator.CreateMergedDocument(
                DocumentMerge.Of(Report("A", 2), Report("B", 3)).UseContinuousPageNumbers())));

            Assert.Equal(5, continuous.PageCount);

            // The footer of a page is the one run of text that carries digits, so a page whose numbers changed has a different content stream
            // from the same page of the other run. Page 3 is the first of the second part: "1/3" against "3/5". The first page reads
            // "1/2" against "1/5": its total differs too.
            for (var i = 0; i < 5; i++)
            {
                Assert.NotEqual(
                    original.ContentText(original.Pages[i]),
                    continuous.ContentText(continuous.Pages[i]));
            }
        }

        [Fact]
        public async Task OriginalNumbering_GivesTheSameFooterToEqualParts()
        {
            var generator = new PdfGenerator();
            var file = PdfFile.Parse(await SaveAsync(await generator.CreateMergedDocument(DocumentMerge.Of(Report("A", 2), Report("A", 2)))));

            Assert.Equal(4, file.PageCount);
            Assert.Equal(file.ContentText(file.Pages[0]), file.ContentText(file.Pages[2]));
            Assert.Equal(file.ContentText(file.Pages[1]), file.ContentText(file.Pages[3]));
            Assert.NotEqual(file.ContentText(file.Pages[0]), file.ContentText(file.Pages[1]));
        }

        [Fact]
        public async Task AddMergedPages_AppendsToADocument()
        {
            var generator = new PdfGenerator();
            var document = await generator.CreateDocument(Report("first", 1));

            await generator.AddMergedPages(document, DocumentMerge.Of(Report("A", 1), Report("B", 2)));

            Assert.Equal(4, document.PageCount);
        }

        [Fact]
        public async Task SeveralPageSectionsInOnePart_NumberOnFromEachOther()
        {
            var generator = new PdfGenerator();
            static void TwoSections(IDocumentBuilder doc)
            {
                Report("s1", 2)(doc);
                Report("s2", 1)(doc);
            }

            var merged = PdfFile.Parse(await SaveAsync(await generator.CreateMergedDocument(DocumentMerge.Of(TwoSections))));
            var plain = PdfFile.Parse(await SaveAsync(await generator.CreateDocument(TwoSections)));

            // Without the merge the second section restarts at 1; merged, it reads 3/3.
            Assert.Equal(3, merged.PageCount);
            Assert.Equal(plain.ContentText(plain.Pages[0]).Length > 0, merged.ContentText(merged.Pages[0]).Length > 0);
            Assert.NotEqual(plain.ContentText(plain.Pages[2]), merged.ContentText(merged.Pages[2]));
        }

        [Fact]
        public void DocumentMerge_ChecksItsArguments()
        {
            Assert.Throws<ArgumentNullException>(() => DocumentMerge.Of((Action<IDocumentBuilder>[])null!));
            Assert.Throws<ArgumentNullException>(() => DocumentMerge.Of([null!]));
            Assert.Throws<ArgumentNullException>(() => DocumentMerge.Of(Report("a", 1)).Add(null!));
            Assert.Equal(1, DocumentMerge.Of((IEnumerable<Action<IDocumentBuilder>>)new[] { Report("a", 1) }).Count);
            Assert.Throws<ArgumentNullException>(() => DocumentMerge.Of((IEnumerable<Action<IDocumentBuilder>>)null!));
        }

        [Fact]
        public async Task CreateMergedDocument_RefusesANullMerge()
        {
            var generator = new PdfGenerator();
            await Assert.ThrowsAsync<ArgumentNullException>(() => generator.CreateMergedDocument(null!));
        }
    }
}

using PeachDrawing.PDF;
using PeachPDF.Layout;

namespace PeachPDF.Tests.Integration
{
    /// <summary>Page labels through the declarative builder: <c>IPageDescriptor.PageLabel</c> per section and <c>IDocumentBuilder.PageLabels</c> by position.</summary>
    public class DeclarativePageLabelTests
    {
        private static async Task<byte[]> SaveAsync(PeachPdfDocument document)
        {
            using var ms = new MemoryStream();
            document.Save(ms);
            return ms.ToArray();
        }

        private static async Task<byte[]> StaticFileAsync(int pages)
        {
            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                for (var i = 0; i < pages; i++)
                    doc.Page(page => { page.Size(200, 200).Margin(10); page.Content(c => c.Text("static")); });
            });
            return await SaveAsync(document);
        }

        private static void Section(IDocumentBuilder doc, Action<IPageDescriptor>? configure = null, string text = "text")
        {
            doc.Page(page =>
            {
                page.Size(200, 200).Margin(10);
                configure?.Invoke(page);
                page.Content(c => c.Text(text));
            });
        }

        private static async Task<IReadOnlyList<string>> LabelsAsync(Action<IDocumentBuilder> build)
        {
            var document = await new PdfGenerator().CreateDocument(build);
            return PdfPageSource.Open(await SaveAsync(document)).GetPageLabels();
        }

        [Fact]
        public async Task ASectionsLabel_StartsAtItsFirstPage_AndRunsOnUntilTheNextSectionSetsOne()
        {
            var labels = await LabelsAsync(doc =>
            {
                Section(doc, p => p.PageLabel(PdfPageLabelStyle.RomanLower));
                Section(doc, p => p.PageLabel(PdfPageLabelStyle.Decimal));
                Section(doc);                                                    // sets none: the decimal range runs on
                Section(doc, p => p.PageLabel(PdfPageLabelStyle.Decimal, "App-", 5));
            });

            Assert.Equal(["i", "1", "2", "App-5"], labels);
        }

        [Fact]
        public async Task ASectionThatPaginates_LabelsEachOfItsPagesInTurn()
        {
            var labels = await LabelsAsync(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(200, 100).Margin(10).PageLabel(PdfPageLabelStyle.RomanUpper);
                    page.Content(c => c.Column(column =>
                    {
                        for (var i = 0; i < 12; i++) column.Item().Height(40).Text("row " + i);
                    }));
                });
                Section(doc, p => p.PageLabel(PdfPageLabelStyle.Decimal));
            });

            Assert.True(labels.Count > 3, "the first section ran onto several pages");
            Assert.Equal("I", labels[0]);
            Assert.Equal("II", labels[1]);
            Assert.Equal("1", labels[^1]);
        }

        [Fact]
        public async Task ABeforeAndAnAfterSection_AroundAnAddedPdf_CountTheAddedPagesOnce()
        {
            var middle = await StaticFileAsync(2);

            var labels = await LabelsAsync(doc =>
            {
                Section(doc, p => p.PageLabel(PdfPageLabelStyle.RomanLower));
                doc.Append(middle);
                Section(doc, p => p.PageLabel(PdfPageLabelStyle.Decimal, "End "));
            });

            Assert.Equal(4, labels.Count);
            Assert.Equal("i", labels[0]);
            Assert.Equal("End 1", labels[3]);
        }

        [Fact]
        public async Task APrependedPdfsPages_ComeBeforeTheSectionsLabelRange()
        {
            var front = await StaticFileAsync(2);

            var labels = await LabelsAsync(doc =>
            {
                doc.Prepend(front);
                Section(doc, p => p.PageLabel(PdfPageLabelStyle.Decimal, "Body "));
            });

            Assert.Equal(3, labels.Count);
            Assert.Equal("Body 1", labels[2]);
            Assert.DoesNotContain("Body", labels[0]);
        }

        [Fact]
        public async Task LabelsSetByPosition_AreCountedFromTheDocumentsFirstPage()
        {
            var front = await StaticFileAsync(2);

            var labels = await LabelsAsync(doc =>
            {
                doc.Prepend(front);
                Section(doc);
                Section(doc);
                doc.PageLabels([new PdfPageLabelRange(0, PdfPageLabelStyle.RomanLower), new PdfPageLabelRange(2, PdfPageLabelStyle.Decimal)]);
            });

            Assert.Equal(["i", "ii", "1", "2"], labels);
        }

        [Fact]
        public async Task LabelsSetByPosition_ReplaceASectionsOwnAtTheSamePage()
        {
            var labels = await LabelsAsync(doc =>
            {
                Section(doc, p => p.PageLabel(PdfPageLabelStyle.RomanLower));
                Section(doc);
                doc.PageLabels([new PdfPageLabelRange(0, PdfPageLabelStyle.LettersUpper)]);
            });

            Assert.Equal(["A", "B"], labels);
        }

        [Fact]
        public async Task LabelsAddedWithAddPages_ArePositionedAfterThePagesAlreadyInTheDocument()
        {
            var generator = new PdfGenerator();
            var document = await generator.CreateDocument(doc => Section(doc));
            await generator.AddPages(document, doc =>
            {
                Section(doc);
                doc.PageLabels([new PdfPageLabelRange(0, PdfPageLabelStyle.RomanLower)]);
            });

            var labels = PdfPageSource.Open(await SaveAsync(document)).GetPageLabels();

            Assert.Equal(["1", "i"], labels);
        }

        [Fact]
        public async Task ARangeBeyondTheLastPage_IsAnErrorWhenTheDocumentIsBuilt()
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => LabelsAsync(doc =>
            {
                Section(doc);
                doc.PageLabels([new PdfPageLabelRange(5, PdfPageLabelStyle.Decimal)]);
            }));
        }

        [Fact]
        public async Task ADocumentWithNoLabelRequests_WritesNoLabelEntry()
        {
            var document = await new PdfGenerator().CreateDocument(doc => Section(doc));

            var bytes = await SaveAsync(document);

            Assert.DoesNotContain("/PageLabels", System.Text.Encoding.Latin1.GetString(bytes));
        }

        [Fact]
        public async Task MergedDocuments_EachKeepTheirOwnLabelsAtTheirOwnPositions()
        {
            var merge = DocumentMerge.Of(
                doc => Section(doc, p => p.PageLabel(PdfPageLabelStyle.RomanLower)),
                doc => Section(doc, p => p.PageLabel(PdfPageLabelStyle.Decimal, "B-")));

            var document = await new PdfGenerator().CreateMergedDocument(merge);

            Assert.Equal(["i", "B-1"], PdfPageSource.Open(await SaveAsync(document)).GetPageLabels());
        }
    }
}

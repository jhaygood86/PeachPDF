using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end tests for link-annotation page attribution in <c>PdfGenerator.HandleLinks</c>,
    /// asserting on the in-memory <c>PdfDocument</c> object model before save. Regression coverage
    /// for two pre-existing bugs fixed by the shifted-grid migration: (1) the page index was derived
    /// from <c>rect.Top / PageSize.Height</c> with no <c>MarginTop</c> shift, off-by-one for content
    /// near band boundaries; (2) the raw grid-slot index was used directly as a
    /// <c>document.Pages</c> index, so any content-empty slot skipped by
    /// <c>GetPaginationSlots</c> silently dropped (or misplaced) every annotation after the gap.
    /// </summary>
    public class HandleLinksPaginationTests
    {
        [Fact]
        public async Task Link_OnSecondPage_LandsOnSecondPagesAnnotations()
        {
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                </style></head><body>
                <p>page one content</p>
                <p style='page-break-before: always'><a href="https://example.com/">a link on page two</a></p>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(2, doc.PageCount);
            Assert.Equal(0, doc.Pages[0].Annotations.Count);
            Assert.Equal(1, doc.Pages[1].Annotations.Count);

            // The link is the first line of page 2's content, so its annotation top must sit within
            // the top margin-plus-a-couple-lines band of the sheet (PDF y counts from the bottom;
            // A4 is 842pt tall, config margins default to 10pt).
            var rect = doc.Pages[1].Annotations[0].Rectangle;
            var topFromSheetTop = 842 - rect.Y2;
            Assert.InRange(topFromSheetTop, 5, 60);
        }

        [Fact]
        public async Task Link_AfterSkippedEmptySlots_LandsOnMaterializedSecondPage()
        {
            // The 2500pt spacer has no text/background/border, so the grid slots it spans are
            // content-empty and never materialized (GetPaginationSlots) - the linked paragraph sits
            // in grid slot 3 but on materialized page 2 of 2. The historical code indexed
            // document.Pages by the raw slot number and silently dropped this annotation.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                </style></head><body>
                <p>page one content</p>
                <div style='height: 2500pt'></div>
                <p><a href="https://example.com/">a link after the gap</a></p>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var doc = await generator.GeneratePdf(html, config);

            Assert.Equal(2, doc.PageCount);
            Assert.Equal(0, doc.Pages[0].Annotations.Count);
            Assert.Equal(1, doc.Pages[1].Annotations.Count);
        }
    }
}

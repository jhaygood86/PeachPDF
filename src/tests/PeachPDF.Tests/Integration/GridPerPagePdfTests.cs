using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A grid container that continues across pages of different widths sizes its frame and each row to the page it lands on (css-break-3
    /// section 5.1). The layout rules are asserted on the fragment tree in <c>PeachBrackets.Engine.Tests</c> (<c>GridPerPageReflowIntegrationTests</c>);
    /// these read the same widths back from the fills a saved PDF paints, in points.
    /// </summary>
    public class GridPerPagePdfTests
    {
        // Every fixture: page 0 is 562pt wide (no left margin) and every later page 512pt.
        private const string Head = "<!DOCTYPE html><html><head><style>@page { size: 612pt 300pt; margin: 20pt 50pt } @page :first { margin-left: 0 } " +
            "body { margin: 0; font: 12pt Arial } #g { display: grid; background: #ecfdf5 } .i { background: #a7f3d0; height: 120pt; break-inside: avoid }";

        private static async Task<List<List<double>>> FillWidthsPerPageAsync(string body, string extraCss = "")
        {
            var html = Head + extraCss + "</style></head><body>" + body + "</body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect();

            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?\S+ \S+ (?<w>\S+) \S+ re\s+f", RegexOptions.CultureInvariant);
            var pages = new List<List<double>>();
            foreach (var page in file.Pages)
            {
                pages.Add([.. fill.Matches(file.ContentText(page))
                    .Select(m => double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture))]);
            }
            return pages;
        }

        private static bool Has(List<double> widths, double expected) => widths.Any(w => Math.Abs(w - expected) < 0.7);

        private static string Tiles(int count) =>
            string.Concat(Enumerable.Range(0, count).Select(n => $"<div class='i'>tile {n}</div>"));

        [Fact]
        public async Task TheContainerAndItsFrColumnsFollowEachPagesMeasure()
        {
            var pages = await FillWidthsPerPageAsync($"<div id='g' style='grid-template-columns:1fr 1fr 1fr'>{Tiles(12)}</div>");

            Assert.True(pages.Count >= 2, "the fixture must span at least two pages");
            Assert.True(Has(pages[0], 562) && Has(pages[0], 562.0 / 3), $"page 0: {string.Join(", ", pages[0])}");
            Assert.True(Has(pages[1], 512) && Has(pages[1], 512.0 / 3), $"page 1: {string.Join(", ", pages[1])}");
            Assert.False(Has(pages[1], 562.0 / 3), "page 1's tiles are not the first page's width");
        }

        [Fact]
        public async Task FixedColumnsKeepTheirWidthOnEveryPage()
        {
            var pages = await FillWidthsPerPageAsync($"<div id='g' style='grid-template-columns:100pt 100pt 100pt'>{Tiles(12)}</div>");

            Assert.True(pages.Count >= 2);
            Assert.True(Has(pages[0], 100) && Has(pages[1], 100));
        }

        [Fact]
        public async Task AContainerWhoseItemsStraddleAPageFollowsThePagesMeasureToo()
        {
            // Long text in each item: the row continues across several pages, so the grid is resumed on each of them.
            var text = string.Join(' ', Enumerable.Range(0, 500).Select(n => $"word{n}"));
            var pages = await FillWidthsPerPageAsync(
                $"<div id='g' style='grid-template-columns:1fr 1fr'><div class='i' style='height:auto'>{text}</div><div class='i' style='height:auto'>{text}</div></div>");

            Assert.True(pages.Count >= 3, "the row must continue across at least three pages");
            Assert.True(Has(pages[0], 562), $"page 0: {string.Join(", ", pages[0])}");
            Assert.True(Has(pages[1], 512), $"page 1: {string.Join(", ", pages[1])}");
            Assert.True(Has(pages[2], 512), $"page 2: {string.Join(", ", pages[2])}");
        }

        [Fact]
        public async Task AGridWithGapsAndAPercentageColumnSizesEachRowToItsPage()
        {
            var pages = await FillWidthsPerPageAsync(
                $"<div id='g' style='grid-template-columns:50% 1fr;column-gap:10pt'>{Tiles(8)}</div>");

            Assert.True(pages.Count >= 2);
            // Page 0: 50% of 562 = 281 and the 1fr takes the other 271; page 1: 256 and 246.
            Assert.True(Has(pages[0], 281) && Has(pages[0], 271), $"page 0: {string.Join(", ", pages[0])}");
            Assert.True(Has(pages[1], 256) && Has(pages[1], 246), $"page 1: {string.Join(", ", pages[1])}");
        }
    }
}

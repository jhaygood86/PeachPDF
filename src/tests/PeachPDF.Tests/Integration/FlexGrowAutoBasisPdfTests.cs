using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A flex item that grows and has an <c>auto</c> basis starts from its content size, which for a multi-column container is its columns
    /// side by side (the widths are those headless Chrome gives). The layout rules are asserted box by box in <c>PeachBrackets.Engine.Tests</c>
    /// (<c>FlexGrowAutoBasisTests</c>); these read the same widths back from the fills a saved PDF paints, in CSS pixels.
    /// </summary>
    public class FlexGrowAutoBasisPdfTests
    {
        private const string Red = "1 0.8 0.8";
        private const string Green = "0.8 1 0.8";

        private static async Task<Dictionary<string, double>> FillWidthsAsync(string body)
        {
            var generator = new PdfGenerator();
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0 }}</style></head><body>{body}</body></html>";
            var file = (await generator.GeneratePdf(html, PageSize.A4)).Inspect();

            var widths = new Dictionary<string, double>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?\S+ \S+ (?<w>\S+) \S+ re\s+f", RegexOptions.CultureInvariant);
            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
                widths.TryAdd(m.Groups["c"].Value, double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture) / 0.75);
            return widths;
        }

        private static string Row(string firstContent, string secondContent) =>
            "<div style='display:flex;width:500px'>" +
            $"<div style='flex:1 1 auto;background:#fcc'>{firstContent}</div>" +
            $"<div style='flex:1 1 auto;background:#cfc'>{secondContent}</div></div>";

        private static string Block(int width) => $"<div style='width:{width}px;height:10px'></div>";

        [Fact]
        public async Task TwoGrowingItemsShareTheFreeSpaceOnTopOfTheirContent()
        {
            var widths = await FillWidthsAsync(Row(Block(100), Block(40)));

            Assert.Equal(280, widths[Red], 0.5);
            Assert.Equal(220, widths[Green], 0.5);
        }

        [Fact]
        public async Task AGrowingItemHoldingAColumnWidthContainerStartsFromItsWidestChild()
        {
            // The container's max-content is the 150px blocks (wider than the 100px column-width): 150 + (500 - 190) / 2.
            var widths = await FillWidthsAsync(Row(
                "<div style='column-width:100px'>" + Block(150) + Block(150) + "</div>", Block(40)));

            Assert.Equal(305, widths[Red], 0.5);
            Assert.Equal(195, widths[Green], 0.5);
        }

        [Fact]
        public async Task AGrowingItemHoldingAColumnCountContainerStartsFromItsColumnsSideBySide()
        {
            var widths = await FillWidthsAsync(Row(
                "<div style='columns:2;column-gap:10px'>" + Block(100) + Block(100) + "</div>", Block(40)));

            Assert.Equal(335, widths[Red], 0.5);
            Assert.Equal(165, widths[Green], 0.5);
        }

        // A row that continues onto pages of another measure is sized again against each page's (css-break-3 section 5.1), by the same rules.

        private static async Task<List<Dictionary<string, double>>> FillWidthsPerPageAsync(string itemAStyle, string itemBStyle)
        {
            var lines = string.Concat(Enumerable.Repeat("a<br>", 80));
            var html = "<!DOCTYPE html><html><head><style>@page { size: 612pt 300pt; margin: 20pt 50pt } @page :first { margin-left: 0 } " +
                "body { margin: 0; font: 12pt Arial } #f { display: flex }</style></head><body><div id='f'>" +
                $"<div style='flex:1 1 auto;background:#fcc;{itemAStyle}'>{lines}</div>" +
                $"<div style='flex:1 1 auto;background:#cfc;{itemBStyle}'>{lines}</div></div></body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect();

            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?\S+ \S+ (?<w>\S+) \S+ re\s+f", RegexOptions.CultureInvariant);
            var pages = new List<Dictionary<string, double>>();
            foreach (var page in file.Pages)
            {
                var widths = new Dictionary<string, double>();
                foreach (Match m in fill.Matches(file.ContentText(page)))
                    widths.TryAdd(m.Groups["c"].Value, double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture));
                pages.Add(widths);
            }
            return pages;
        }

        [Fact]
        public async Task AContinuedRow_HoldsAGrowingItemToItsMaxWidthOnEveryPage()
        {
            var pages = await FillWidthsPerPageAsync("max-width:200pt", "");

            Assert.True(pages.Count >= 3, "the fixture must span at least three pages");
            // The first page is 562pt wide and every later one 512pt: the other item takes the rest.
            Assert.Equal(200, pages[0][Red], 0.6);
            Assert.Equal(362, pages[0][Green], 0.6);
            Assert.Equal(200, pages[1][Red], 0.6);
            Assert.Equal(312, pages[1][Green], 0.6);
        }

        [Fact]
        public async Task AContinuedRow_RaisesAGrowingItemToItsMinWidthWithoutRaisingItsBase()
        {
            var pages = await FillWidthsPerPageAsync("", "min-width:350pt");

            Assert.True(pages.Count >= 3, "the fixture must span at least three pages");
            Assert.Equal(212, pages[0][Red], 0.6);
            Assert.Equal(350, pages[0][Green], 0.6);
            Assert.Equal(162, pages[1][Red], 0.6);
            Assert.Equal(350, pages[1][Green], 0.6);
        }
    }
}

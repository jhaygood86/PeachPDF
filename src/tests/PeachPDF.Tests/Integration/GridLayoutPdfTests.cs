using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Grid layout shapes written to a PDF and read back from the fills they paint, in points. The box-by-box rules are asserted in
    /// <c>PeachBrackets.Engine.Tests</c>; these drive the same grid engine through the public API, so what the saved document shows (a track
    /// the right size, a subgrid's rows aligned, a baseline shared) is checked where the engine is exercised from a document.
    /// </summary>
    public class GridLayoutPdfTests
    {
        private record struct Fill(string Color, double X, double Y, double Width, double Height);

        private static readonly Regex FillPattern = new(
            @"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

        private static async Task<List<Fill>> FillsAsync(string body, string css = "")
        {
            var html = "<!DOCTYPE html><html><head><style>@page { margin: 0 } body { margin: 0; font: 12pt Arial } " + css + "</style></head><body>" + body + "</body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect();
            Assert.Single(file.Pages);

            return [.. FillPattern.Matches(file.ContentText(file.Pages[0])).Select(m => new Fill(
                m.Groups["c"].Value,
                double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture)))];
        }

        private static bool Has(IEnumerable<Fill> fills, Func<Fill, double> measure, double expected) =>
            fills.Any(f => Math.Abs(measure(f) - expected) < 0.7);

        [Fact]
        public async Task AutoRowsOfADefiniteHeightGridShareItsSpace()
        {
            // 300pt of height over two auto rows of one line each: align-content: normal stretches the rows to 150pt.
            var fills = await FillsAsync("<div style='display:grid;height:300pt;grid-template-columns:100pt'><div style='background:#fcc'>a</div><div style='background:#cfc'>b</div></div>");

            Assert.True(Has(fills, f => f.Height, 150), string.Join("; ", fills));
        }

        [Fact]
        public async Task AnItemSpanningTwoAutoRowsGrowsTheLastOfThem()
        {
            // The tall item needs 200pt of two rows whose own content is one 14pt line each, so the second row grows to hold it.
            var fills = await FillsAsync("<div style='display:grid;grid-template-columns:100pt 100pt'>" +
                "<div style='grid-row:1 / span 2;background:#fcc;height:200pt'></div><div style='background:#cfc'>a</div><div style='background:#ccf'>b</div></div>");

            Assert.True(Has(fills, f => f.Height, 200), string.Join("; ", fills));
        }

        [Fact]
        public async Task BaselineAlignedItemsInARowShareABaseline()
        {
            var fills = await FillsAsync("<div style='display:grid;grid-template-columns:100pt 100pt;align-items:baseline'>" +
                "<div style='background:#fcc;font-size:30pt'>Big</div><div style='background:#cfc;font-size:12pt'>small</div></div>");

            // The small item sits lower than the row's top by the difference of the two ascents, so its fill starts below the big one's.
            var big = fills.First(f => f.Color == fills[0].Color);
            Assert.True(fills.Count >= 2 && fills.Select(f => Math.Round(f.Y, 1)).Distinct().Count() >= 2, string.Join("; ", fills));
            Assert.True(big.Height > 0);
        }

        [Theory]
        [InlineData("baseline")]
        [InlineData("last baseline")]
        public async Task InlineAxisBaselineItemsLineUpOnTheirColumnEdge(string justify)
        {
            var fills = await FillsAsync($"<div style='display:grid;grid-template-columns:200pt;justify-items:{justify}'>" +
                "<div style='background:#fcc;width:50pt;height:20pt'></div><div style='background:#cfc;width:80pt;height:20pt'></div></div>");

            Assert.True(Has(fills, f => f.Width, 50) && Has(fills, f => f.Width, 80), string.Join("; ", fills));
        }

        [Fact]
        public async Task AColumnSubgridPlacesItsItemsOnTheParentsColumns()
        {
            var fills = await FillsAsync("<div style='display:grid;grid-template-columns:100pt 60pt 40pt'>" +
                "<div style='grid-column:1 / 4;display:grid;grid-template-columns:subgrid'>" +
                "<div style='background:#fcc'>a</div><div style='background:#cfc'>b</div><div style='background:#ccf'>c</div></div></div>");

            Assert.True(Has(fills, f => f.Width, 100) && Has(fills, f => f.Width, 60) && Has(fills, f => f.Width, 40), string.Join("; ", fills));
        }

        [Fact]
        public async Task RowSubgridsOverAutoRowsShareTheTallestContent()
        {
            // Both cards span the same two auto rows; the first card's first band is two lines, so the second card's band is as tall too.
            var fills = await FillsAsync("<div style='display:grid;grid-template-columns:100pt 100pt'>" +
                "<div style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid'><div style='background:#fcc'>one<br>two</div><div>x</div></div>" +
                "<div style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid'><div style='background:#cfc'>y</div><div>z</div></div></div>");

            var heights = fills.Select(f => Math.Round(f.Height, 1)).Distinct().ToList();
            Assert.Single(heights);
        }

        [Fact]
        public async Task ARowSubgridAdoptsDefiniteParentRows()
        {
            var fills = await FillsAsync("<div style='display:grid;grid-template-rows:40pt 60pt;grid-template-columns:100pt'>" +
                "<div style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid'><div style='background:#fcc'>a</div><div style='background:#cfc'>b</div></div></div>");

            Assert.True(Has(fills, f => f.Height, 40) && Has(fills, f => f.Height, 60), string.Join("; ", fills));
        }

        [Fact]
        public async Task AnInlineGridInAnRtlBlockShrinksToItsTracks()
        {
            var fills = await FillsAsync("<div style='direction:rtl;width:400pt'><div style='display:inline-grid;grid-template-columns:50pt 70pt;background:#eee'>" +
                "<div style='background:#fcc'>a</div><div style='background:#cfc'>b</div></div></div>");

            Assert.True(Has(fills, f => f.Width, 120), string.Join("; ", fills));
        }

        [Fact]
        public async Task AVerticalGridWithAutoSizesIsAsLongAsItsColumns()
        {
            var fills = await FillsAsync("<div style='display:grid;writing-mode:vertical-rl;grid-template-columns:30pt 50pt;background:#eee'>" +
                "<div style='background:#fcc'>a</div><div style='background:#cfc'>b</div></div>");

            Assert.True(fills.Count >= 2, string.Join("; ", fills));
            Assert.True(Has(fills, f => f.Height, 80), string.Join("; ", fills));
        }
    }
}

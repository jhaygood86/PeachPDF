using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Subgrids written to a PDF and read back from the fills they paint, in points (x from the left edge of a page with no margin). The box-by-box
    /// rules are asserted in <c>PeachBrackets.Engine.Tests</c>; these drive the same grid engine through the public API: a subgrid inherits the
    /// parent's line names, keeps its tracks on the parent's lines whatever edge it has, and its items size the parent's auto columns.
    /// </summary>
    public class GridSubgridPdfTests
    {
        private record struct Fill(double X, double Y, double Width, double Height);

        private static readonly Regex FillPattern = new(
            @"\S+ \S+ \S+ rg\s+(?:/\S+ gs\s+)?(?<x>\S+) (?<y>\S+) (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);

        private static async Task<List<Fill>> FillsAsync(string body)
        {
            var html = "<!DOCTYPE html><html><head><style>@page { margin: 0 } body { margin: 0; font: 12pt Arial } " +
                "div > div > div { height: 20pt }</style></head><body>" + body + "</body></html>";
            var file = (await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect();
            Assert.Single(file.Pages);

            return [.. FillPattern.Matches(file.ContentText(file.Pages[0])).Select(m => new Fill(
                double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups["w"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture)))];
        }

        private static bool AtX(IEnumerable<Fill> fills, double x) => fills.Any(f => Math.Abs(f.X - x) < 0.7);

        private static bool Wide(IEnumerable<Fill> fills, double width) => fills.Any(f => Math.Abs(f.Width - width) < 0.7);

        private static string Describe(IEnumerable<Fill> fills) => string.Join("; ", fills.Select(f => $"x={f.X:0.#} w={f.Width:0.#} h={f.Height:0.#}"));

        private const string Lines = "[a] 100pt [b] 100pt [c] 100pt [d]";

        private static string Sub(string template, string items, string style = "", string columns = Lines) =>
            $"<div style='display:grid;width:300pt;grid-template-columns:{columns}'>" +
            $"<div style='grid-column:2 / 4;display:grid;grid-template-columns:{template};{style}'>{items}</div></div>";

        [Fact]
        public async Task AnItemIsPlacedByTheParentsLineName()
        {
            var fills = await FillsAsync(Sub("subgrid", "<div style='grid-column:c / d;background:#fcc'></div>"));

            Assert.True(AtX(fills, 200), Describe(fills));
        }

        [Fact]
        public async Task TheSubgridsOwnNamesJoinTheInheritedOnes()
        {
            var fills = await FillsAsync(Sub("subgrid [own] [mid] [end]",
                "<div style='grid-column:mid / end;background:#fcc'></div><div style='grid-column:c / d;background:#cfc'></div>"));

            Assert.True(fills.Count(f => Math.Abs(f.X - 200) < 0.7) >= 2, Describe(fills));
        }

        [Fact]
        public async Task AreaNamesOfTheParentAreInherited()
        {
            var fills = await FillsAsync("<div style='display:grid;width:300pt;grid-template-columns:100pt 100pt 100pt;grid-template-areas:\"l m r\"'>" +
                "<div style='grid-column:2 / 4;display:grid;grid-template-columns:subgrid'><div style='grid-column:r;background:#fcc'></div></div></div>");

            Assert.True(AtX(fills, 200), Describe(fills));
        }

        [Fact]
        public async Task NamesAreInheritedThroughANestedSubgrid()
        {
            var fills = await FillsAsync(Sub("subgrid",
                "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid'><div style='grid-column:c / d;background:#fcc'></div></div>"));

            Assert.True(AtX(fills, 200), Describe(fills));
        }

        [Fact]
        public async Task RowNamesAreInheritedToo()
        {
            var fills = await FillsAsync("<div style='display:grid;width:100pt;grid-template-rows:[t] 30pt [m] 40pt [b]'>" +
                "<div style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid'><div style='grid-row:m / b;background:#fcc;height:auto'></div></div></div>");

            Assert.True(fills.Any(f => Math.Abs(f.Height - 40) < 0.7), Describe(fills));
        }

        [Fact]
        public async Task TheAreasFormDoesNotTakeSubgridForItsColumns()
        {
            // `/ subgrid` after the areas form is invalid, so the declaration is dropped and the columns stay what they were.
            var fills = await FillsAsync("<div style='display:grid;width:300pt;grid-template-columns:120pt 80pt;grid-template:\"a b\" 50pt / subgrid'>" +
                "<div style='background:#fcc'>x</div><div style='background:#cfc'>y</div></div>");

            Assert.True(Wide(fills, 120) && Wide(fills, 80), Describe(fills));
        }

        [Fact]
        public async Task PaddingInsetsTheEdgeItemsAndLeavesTheMiddleTrackOnItsLine()
        {
            var fills = await FillsAsync("<div style='display:grid;width:300pt;grid-template-columns:100pt 100pt 100pt'>" +
                "<div style='grid-column:1 / 4;display:grid;grid-template-columns:subgrid;padding:0 10pt 0 20pt'>" +
                "<div style='background:#fcc'></div><div style='background:#cfc'></div><div style='background:#ccf'></div></div></div>");

            Assert.True(AtX(fills, 20) && AtX(fills, 100) && AtX(fills, 200), Describe(fills));
            Assert.True(Wide(fills, 80) && Wide(fills, 100) && Wide(fills, 90), Describe(fills));
        }

        [Fact]
        public async Task RowPaddingInsetsTheFirstAndLastRowOfARowSubgrid()
        {
            var fills = await FillsAsync("<div style='display:grid;width:100pt;grid-template-rows:50pt 50pt'>" +
                "<div style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid;padding:8pt 0 4pt'>" +
                "<div style='background:#fcc;height:auto'></div><div style='background:#cfc;height:auto'></div></div></div>");

            Assert.True(Wide(fills, 100) && fills.Any(f => Math.Abs(f.Height - 42) < 0.7) && fills.Any(f => Math.Abs(f.Height - 46) < 0.7), Describe(fills));
        }

        [Fact]
        public async Task AnAutoParentRowGrowsToHoldTheSubgridsPadding()
        {
            var fills = await FillsAsync("<div style='display:grid;width:100pt;grid-template-rows:auto auto'>" +
                "<div style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid;padding:8pt 0 4pt;background:#eee'>" +
                "<div style='background:#fcc;height:10pt'></div><div style='background:#cfc;height:10pt'></div></div></div>");

            Assert.True(fills.Any(f => Math.Abs(f.Height - 32) < 0.7), Describe(fills));
        }

        [Fact]
        public async Task AutoColumnsSizeFromTheSubgridsItems()
        {
            var fills = await FillsAsync("<div style='display:grid;width:300pt;justify-content:start;grid-template-columns:auto auto'>" +
                "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid'>" +
                "<div style='background:#fcc;width:100pt'></div><div style='background:#cfc;width:50pt'></div></div></div>");

            Assert.True(AtX(fills, 100) && Wide(fills, 100) && Wide(fills, 50), Describe(fills));
        }

        [Fact]
        public async Task TheSubgridsEdgeIsAddedToTheFirstColumn()
        {
            var fills = await FillsAsync("<div style='display:grid;width:300pt;justify-content:start;grid-template-columns:auto auto'>" +
                "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid;padding:0 10pt 0 20pt'>" +
                "<div style='background:#fcc;width:100pt'></div><div style='background:#cfc;width:50pt'></div></div></div>");

            Assert.True(AtX(fills, 120), Describe(fills));
        }

        [Fact]
        public async Task AnItemSpanningTheSubgridsColumnsSharesWhatTheOtherColumnsDoNotCover()
        {
            var fills = await FillsAsync("<div style='display:grid;width:400pt;justify-content:start;grid-template-columns:auto 100pt'>" +
                "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid'>" +
                "<div style='background:#fcc;width:10pt'></div><div style='background:#cfc'></div>" +
                "<div style='grid-column:1 / 3;background:#ccf;width:250pt'></div></div></div>");

            Assert.True(AtX(fills, 150), Describe(fills));
        }

        [Fact]
        public async Task FlexibleColumnsTakeTheSubgridItemsAutomaticMinimum()
        {
            var fills = await FillsAsync("<div style='display:grid;width:300pt;grid-template-columns:1fr 1fr'>" +
                "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid'>" +
                "<div style='background:#fcc'>a</div><div style='background:#cfc'>b</div></div></div>");

            Assert.True(Wide(fills, 150), Describe(fills));
        }

        [Fact]
        public async Task ANestedSubgridContributesToTheOutermostColumns()
        {
            var fills = await FillsAsync("<div style='display:grid;width:300pt;justify-content:start;grid-template-columns:auto auto'>" +
                "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid'>" +
                "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid'>" +
                "<div style='background:#fcc;width:100pt'></div><div style='background:#cfc;width:50pt'></div></div></div></div>");

            Assert.True(AtX(fills, 100), Describe(fills));
        }

        [Fact]
        public async Task ASubgridWithItsOwnAreasStillLaysOut()
        {
            var fills = await FillsAsync("<div style='display:grid;width:300pt;justify-content:start;grid-template-columns:auto auto'>" +
                "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid;grid-template-areas:\"x y\"'>" +
                "<div style='background:#fcc;width:100pt'></div><div style='background:#cfc;width:50pt'></div></div></div>");

            // Its items cannot be placed without a layout pass, so it contributes nothing to the auto columns (as before): the point is that the document
            // is still generated as one page (FillsAsync), not that anything is drawn in zero-width tracks.
            Assert.NotNull(fills);
        }

        [Fact]
        public async Task AColumnFlowSubgridStillLaysOut()
        {
            var fills = await FillsAsync("<div style='display:grid;width:300pt;justify-content:start;grid-template-columns:auto auto'>" +
                "<div style='grid-column:1 / 3;display:grid;grid-template-columns:subgrid;grid-auto-flow:column'>" +
                "<div style='background:#ccf;width:60pt'></div><div style='background:#fcf;width:40pt'></div></div></div>");

            Assert.NotNull(fills);
        }

        [Fact]
        public async Task ARowOnlySubgridInAnAutoColumnGridStillLaysOut()
        {
            var fills = await FillsAsync("<div style='display:grid;width:300pt;grid-template-columns:auto auto'>" +
                "<div style='grid-row:1 / 3;display:grid;grid-template-rows:subgrid'><div style='background:#fcc;width:100pt'></div><div style='background:#cfc'></div></div>" +
                "<div style='background:#ccf'>x</div></div>");

            Assert.True(fills.Count >= 3, Describe(fills));
        }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A table cell has the browser's default padding of 1px, and a cell that holds a table is as wide as that table, its <c>border-spacing</c>
    /// included. The box-by-box rules are asserted in <c>PeachBrackets.Engine.Tests</c> (<c>NestedTableSpacingTests</c>,
    /// <c>TableLegacyAttributeTests</c>); these read the same sizes back from the rectangles a saved PDF fills, in points.
    /// </summary>
    public class TableDefaultPaddingPdfTests
    {
        private const string Red = "1 0.8 0.8";
        private const string Green = "0.8 1 0.8";

        private static async Task<Dictionary<string, (double X, double Width, double Height)>> FillsAsync(string body)
        {
            var html = $"<!DOCTYPE html><html><head><style>body {{ margin: 0 }}</style></head><body>{body}</body></html>";
            return ReadFills((await new PdfGenerator().GeneratePdf(html, PageSize.A4)).Inspect());
        }

        private static Dictionary<string, (double X, double Width, double Height)> ReadFills(PdfFile file)
        {
            var fills = new Dictionary<string, (double, double, double)>();
            var fill = new Regex(@"(?<c>\S+ \S+ \S+) rg\s+(?:/\S+ gs\s+)?(?<x>\S+) \S+ (?<w>\S+) (?<h>\S+) re\s+f", RegexOptions.CultureInvariant);
            foreach (Match m in fill.Matches(file.ContentText(file.Pages[0])))
            {
                double Number(string group) => double.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
                fills.TryAdd(m.Groups["c"].Value, (Number("x"), Math.Abs(Number("w")), Math.Abs(Number("h"))));
            }

            return fills;
        }

        private const string Content = "<div style='display:inline-block;width:40pt;height:10pt'></div>";

        [Fact]
        public async Task ACellHasADefaultPaddingOf1px()
        {
            var padded = await FillsAsync($"<table><tr><td style='background:#fcc'>{Content}</td></tr></table>");
            var bare = await FillsAsync($"<table><tr><td style='background:#fcc;padding:0'>{Content}</td></tr></table>");

            // 1px (0.75pt) on each side.
            Assert.Equal(bare[Red].Width + 1.5, padded[Red].Width, 0.05);
            Assert.Equal(bare[Red].Height + 1.5, padded[Red].Height, 0.05);
        }

        [Fact]
        public async Task ACellpaddingAttributeReplacesTheDefault()
        {
            var attribute = await FillsAsync($"<table cellpadding='4'><tr><td style='background:#fcc'>{Content}</td></tr></table>");
            var bare = await FillsAsync($"<table><tr><td style='background:#fcc;padding:0'>{Content}</td></tr></table>");

            // 4px (3pt) on each side, not 4px and 1px.
            Assert.Equal(bare[Red].Width + 6, attribute[Red].Width, 0.05);
        }

        [Fact]
        public async Task ACellHoldingATableIsAsWideAsTheTableAndItsSpacing()
        {
            // The inner table (green) is its 40pt of content with the table's default spacing (2px = 1.5pt) either side, and the outer cell (red)
            // holds it with its own default padding (1px = 0.75pt) either side.
            var fills = await FillsAsync(
                "<table><tr><td style='background:#fcc'><table style='background:#cfc'><tr><td style='padding:0'>" + Content + "</td></tr></table></td></tr></table>");

            Assert.Equal(40 + 2 * 1.5, fills[Green].Width, 0.05);
            Assert.Equal(fills[Green].Width + 2 * 0.75, fills[Red].Width, 0.05);
            Assert.True(fills[Green].X >= fills[Red].X, "the inner table starts inside the outer cell");
        }

        [Fact]
        public async Task ACellHoldingATableOfTwoColumnsCountsTheSpacingBetweenThem()
        {
            var fills = await FillsAsync(
                "<table style='border-spacing:0'><tr><td style='background:#fcc;padding:0'><table style='background:#cfc;border-spacing:6pt'>" +
                $"<tr><td style='padding:0'>{Content}</td><td style='padding:0'>{Content}</td></tr></table></td></tr></table>");

            // Three 6pt spacings and two 40pt columns.
            Assert.Equal(98, fills[Green].Width, 0.05);
            Assert.Equal(98, fills[Red].Width, 0.05);
        }

        [Fact]
        public async Task ACellHoldingATableOfManyRowsIsStillAsWideAsTheTable()
        {
            // The spacing is estimated from the first rows only, so a body of more than that many rows has to come out the same as a short one.
            var rows = string.Concat(Enumerable.Repeat($"<tr><td style='padding:0'>{Content}</td></tr>", 40));
            var fills = await FillsAsync(
                "<table style='border-spacing:0'><tr><td style='background:#fcc;padding:0'><table style='background:#cfc;border-spacing:6pt'>" +
                $"{rows}</table></td></tr></table>");

            Assert.Equal(52, fills[Green].Width, 0.05);
            Assert.Equal(52, fills[Red].Width, 0.05);
        }

        [Fact]
        public async Task ASpanningCellHasTheSpacingBetweenItsColumnsAlreadyInItsWidth()
        {
            const string block60 = "<div style='display:inline-block;width:60px;height:10px'></div>";
            const string block20 = "<div style='display:inline-block;width:20px;height:10px'></div>";

            // The cell is its 60px of content and 1px of padding either side; the columns it spans and the spacing between them make that up, and the
            // spacing is not added again (Chrome: 46.5pt, and a 16.5pt cell beside it).
            var fills = await FillsAsync(
                $"<table><tr><td rowspan='2' colspan='2' style='background:#fcc'>{block60}</td><td style='background:#cfc'>{block20}</td></tr><tr><td>{block20}</td></tr></table>");

            Assert.Equal(46.5, fills[Red].Width, 0.05);
            Assert.Equal(16.5, fills[Green].Width, 0.05);
        }

        [Theory]
        [InlineData("padding:20px")]
        [InlineData("padding:0")]
        [InlineData("padding:20px;box-sizing:border-box")]
        public async Task APercentageCellWidthIsTheCellsWholeShareOfTheTable(string cellStyle)
        {
            var fills = await FillsAsync(
                $"<table style='width:600px;border-spacing:0'><tr><td style='background:#fcc;width:50%;{cellStyle}'>x</td><td style='background:#cfc;padding:0'>y</td></tr></table>");

            Assert.Equal(225, fills[Red].Width, 0.05);
            Assert.Equal(225, fills[Green].Width, 0.05);
        }

        [Fact]
        public async Task APercentageCellWidthIsAShareOfTheGridWithoutItsSpacing()
        {
            var fills = await FillsAsync("<table style='width:600px'><tr><td style='background:#fcc;width:50%'>x</td><td style='background:#cfc'>y</td></tr></table>");

            Assert.Equal(222.75, fills[Red].Width, 0.05);
            Assert.Equal(222.75, fills[Green].Width, 0.05);
        }

        [Theory]
        [InlineData(20, 61.5)]
        [InlineData(120, 96)]
        public async Task ADeclaredWidthOfANestedTablesCellIsNotAMinimumForTheOuterCell(int contentPx, double outerCell)
        {
            // The outer cell asks for 80px. The nested table would like more, but only content that cannot shrink holds a column wider than it asked.
            var fills = await FillsAsync(
                $"<table><tr><td width='80' style='background:#fcc'><table><tr><td width='80'><div style='display:inline-block;width:{contentPx}px;height:10px'></div></td></tr></table></td></tr></table>");

            Assert.Equal(outerCell, fills[Red].Width, 0.05);
        }

        [Fact]
        public async Task ADeclarativeTableCellIsNotGivenTheDefaultPadding()
        {
            async Task<(double Width, double Height)> CellAsync(float? padding)
            {
                var document = await new PdfGenerator().CreateDocument(doc => doc.Page(page => page.Content(content => content.Table(table =>
                {
                    table.Columns(columns => columns.RelativeColumn());
                    table.Row(row =>
                    {
                        var cell = row.Cell().Background(PdfColor.FromRgb(255, 204, 204));
                        if (padding is { } value) cell = cell.Padding(value);
                        cell.Width(40).Height(10);
                    });
                }))), new PdfGenerateConfig { PageSize = PageSize.A4 });

                var cellFill = ReadFills(document.Inspect())[Red];
                return (cellFill.Width, cellFill.Height);
            }

            var withDefault = await CellAsync(null);
            var withNone = await CellAsync(0);

            // The browser's default is for the cells of an HTML table; a declarative cell is padded only by the Padding() call on it, as it always
            // was. The column is as wide as the page whatever the cell holds, so the height is what shows it.
            Assert.Equal(withNone.Height, withDefault.Height, 0.05);
        }
    }
}

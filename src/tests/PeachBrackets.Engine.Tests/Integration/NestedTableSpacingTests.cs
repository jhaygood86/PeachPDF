using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A cell that holds a table is as wide as that table, its <c>border-spacing</c> included (css-tables-3 section 3, the table's width is its grid plus
    /// the spacing around and between its columns). The spacing used to be left out of the outer cell's measure, so the cell came out narrower than the
    /// table inside it by two spacings, and the nested table painted outside it. Each figure is what headless Chrome gives for the same markup, in pt.
    /// </summary>
    public class NestedTableSpacingTests
    {
        // Inline-level, so the cells of a row add up on one line; the width of a row of block-level cell content is a separate matter.
        private const string Content = "<div style='display:inline-block;width:50pt;height:10pt'></div>";
        private const string Cell = "<td style='padding:0'>" + Content + "</td>";

        private static async Task<(double Cell, double Table)> WidthsAsync(string innerTable)
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<table style='border-spacing:0'><tr><td id='c' style='padding:0'>" + innerTable + "</td></tr></table>"));

            var cell = FindById(root, "c")!;
            var table = FindById(root, "a")!;

            return (cell.ActualRight - cell.Location.X, table.ActualRight - table.Location.X);
        }

        private static string Table(string rows, string style = "") =>
            $"<table id='a' style='border-spacing:6pt;{style}'>{rows}</table>";

        [Fact]
        public async Task ACellHoldsItsNestedTableWithTheTablesSpacingAroundAColumn()
        {
            // 50pt of content with a 6pt spacing on each side of the one column.
            var (cell, table) = await WidthsAsync(Table($"<tr>{Cell}</tr>"));

            Assert.Equal(62, table, 0.05);
            Assert.Equal(62, cell, 0.05);
        }

        [Fact]
        public async Task TheSpacingBetweenTwoColumnsCountsToo()
        {
            var (cell, table) = await WidthsAsync(Table($"<tr>{Cell}{Cell}</tr>"));

            Assert.Equal(118, table, 0.05);
            Assert.Equal(118, cell, 0.05);
        }

        [Fact]
        public async Task AColumnCountedFromAColspanRowAndARowBelowItIsNotCountedTwice()
        {
            var (cell, table) = await WidthsAsync(Table($"<tr><td colspan='2' style='padding:0'>{Content}</td></tr><tr>{Cell}{Cell}</tr>"));

            Assert.Equal(118, table, 0.05);
            Assert.Equal(118, cell, 0.05);
        }

        [Fact]
        public async Task ATableWithAHeadOrFootGroupIsCountedFromItsRowsLikeOneWithoutThem()
        {
            var (headCell, headTable) = await WidthsAsync(Table($"<thead><tr>{Cell}</tr></thead><tr>{Cell}</tr>"));
            var (footCell, footTable) = await WidthsAsync(Table($"<tr>{Cell}</tr><tfoot><tr>{Cell}</tr></tfoot>"));

            Assert.Equal(62, headTable, 0.05);
            Assert.Equal(62, headCell, 0.05);
            Assert.Equal(62, footTable, 0.05);
            Assert.Equal(62, footCell, 0.05);
        }

        [Fact]
        public async Task AnInlineTableCountsItsSpacingToo()
        {
            var (cell, table) = await WidthsAsync(Table($"<tr>{Cell}</tr>", "display:inline-table"));

            Assert.Equal(62, table, 0.05);
            Assert.Equal(62, cell, 0.05);
        }

        [Fact]
        public async Task AFloatedNestedTableIsSizedTheSameWay()
        {
            var (cell, table) = await WidthsAsync(Table($"<tr>{Cell}</tr>", "float:left"));

            Assert.Equal(62, table, 0.05);
            Assert.Equal(62, cell, 0.05);
        }

        [Fact]
        public async Task ADeclaredWidthIsTheWholeTableSoItsSpacingIsNotAddedToIt()
        {
            var (cell, table) = await WidthsAsync(Table($"<tr>{Cell}</tr>", "width:200pt"));

            Assert.Equal(200, table, 0.05);
            Assert.Equal(200, cell, 0.05);
        }

        [Fact]
        public async Task ACollapsedTableHasNoSpacingToCount()
        {
            var (cell, table) = await WidthsAsync(Table($"<tr>{Cell}</tr>", "border-collapse:collapse"));

            Assert.Equal(50, table, 0.05);
            Assert.Equal(50, cell, 0.05);
        }

        [Fact]
        public async Task ATableWithNoSpacingNeedsNothingAdded()
        {
            var (cell, table) = await WidthsAsync(Table($"<tr>{Cell}</tr>", "border-spacing:0"));

            Assert.Equal(50, table, 0.05);
            Assert.Equal(50, cell, 0.05);
        }
    }
}

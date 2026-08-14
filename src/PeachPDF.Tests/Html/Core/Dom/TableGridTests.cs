using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// <see cref="TableGrid.Build"/> tested against real, laid-out box trees (so rowspan placeholders -
    /// <see cref="CssSpacingBox"/> - already exist, exactly as <see cref="CssLayoutEngineTable"/> would
    /// hand them in) but with hand-rolled colspan/rowspan/last-row primitives standing in for
    /// <see cref="CssLayoutEngineTable"/>'s own (which are private) - simple sum-of-colspan arithmetic,
    /// not the visibility:collapse-aware row remapping <c>GetEffectiveEndRowIndex</c> does, which is
    /// exercised instead once this is wired into the real engine. Column placement itself is not an
    /// injected primitive - <see cref="TableGrid.Build"/> works it out internally from rowspan
    /// occupancy, so there is nothing to stand in for here.
    /// </summary>
    public class TableGridTests
    {
        private static int GetColSpan(CssBox b)
        {
            var att = b.GetAttribute("colspan", "1");
            return !int.TryParse(att, out var span) ? 1 : span;
        }

        private static int GetRowSpan(CssBox b)
        {
            var att = b.GetAttribute("rowspan", "1");
            return !int.TryParse(att, out var span) ? 1 : span;
        }

        private static int GetLastRow(int gridRow, CssBox cell, int rowSpan) => gridRow + rowSpan - 1;

        /// <summary>Every &lt;tr&gt; under <paramref name="table"/>, in thead/tbody-in-source-order/tfoot order.</summary>
        private static List<CssBox> AllRows(CssBox table)
        {
            var header = new List<CssBox>();
            var body = new List<CssBox>();
            var footer = new List<CssBox>();

            void CollectRows(CssBox groupOrRow, List<CssBox> into)
            {
                if (groupOrRow.DerivedStyle.ActualDisplay == Keywords.TableRow)
                {
                    into.Add(groupOrRow);
                    return;
                }
                foreach (var child in groupOrRow.Boxes) CollectRows(child, into);
            }

            foreach (var box in table.Boxes)
            {
                switch (box.DerivedStyle.ActualDisplay)
                {
                    case Keywords.TableHeaderGroup:
                        CollectRows(box, header);
                        break;
                    case Keywords.TableFooterGroup:
                        CollectRows(box, footer);
                        break;
                    case Keywords.TableRowGroup:
                    case Keywords.TableRow:
                        CollectRows(box, body);
                        break;
                }
            }

            return [.. header, .. body, .. footer];
        }

        /// <summary>One entry per column, repeated across a &lt;col span&gt;/&lt;colgroup span&gt; - mirrors <c>_columns</c>.</summary>
        private static List<CssBox?> AllColumns(CssBox table)
        {
            var columns = new List<CssBox?>();

            foreach (var box in table.Boxes)
            {
                if (box.DerivedStyle.ActualDisplay == Keywords.TableColumn)
                {
                    var span = box.GetAttribute("span", "1");
                    var count = int.TryParse(span, out var s) ? s : 1;
                    for (var i = 0; i < count; i++) columns.Add(box);
                }
                else if (box.DerivedStyle.ActualDisplay == Keywords.TableColumnGroup)
                {
                    var colChildren = box.Boxes.Where(c => c.DerivedStyle.ActualDisplay == Keywords.TableColumn).ToList();
                    if (colChildren.Count == 0)
                    {
                        var span = box.GetAttribute("span", "1");
                        var count = int.TryParse(span, out var s) ? s : 1;
                        for (var i = 0; i < count; i++) columns.Add(box);
                    }
                    else
                    {
                        foreach (var col in colChildren)
                        {
                            var span = col.GetAttribute("span", "1");
                            var count = int.TryParse(span, out var s) ? s : 1;
                            for (var i = 0; i < count; i++) columns.Add(col);
                        }
                    }
                }
            }

            return columns;
        }

        private static async Task<(CssBox Table, TableGrid Grid)> Build(string html)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var table = LayoutHarness.Descendants(root).First(b => b.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable);

            var grid = TableGrid.Build(
                AllRows(table), AllColumns(table),
                GetRowSpan, GetColSpan, GetLastRow);

            return (table, grid);
        }

        [Fact]
        public async Task PlainGrid_EachCellOccupiesItsOwnSlot()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <tr><td id='a'>a</td><td id='b'>b</td></tr>
                    <tr><td id='c'>c</td><td id='d'>d</td></tr>
                </table>"));

            Assert.Equal(2, grid.RowCount);
            Assert.Equal(2, grid.ColumnCount);
            Assert.NotNull(grid.CellAt(0, 0));
            Assert.NotSame(grid.CellAt(0, 0), grid.CellAt(0, 1));
            Assert.NotSame(grid.CellAt(0, 0), grid.CellAt(1, 0));
        }

        [Fact]
        public async Task Colspan_CellOccupiesEveryColumnItSpans()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <tr><td colspan='2' id='wide'>wide</td></tr>
                    <tr><td id='c'>c</td><td id='d'>d</td></tr>
                </table>"));

            Assert.Equal(2, grid.ColumnCount);
            var wide = grid.CellAt(0, 0);
            Assert.NotNull(wide);
            Assert.Same(wide, grid.CellAt(0, 1));
            Assert.True(grid.TryGetSpan(wide!, out var span));
            Assert.Equal(new CellSpan(0, 0, 1, 2), span);
        }

        [Fact]
        public async Task Rowspan_CellOccupiesEveryRowItSpans_ViaSpacingBoxPlaceholders()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <tr><td rowspan='2' id='tall'>tall</td><td id='b'>b</td></tr>
                    <tr><td id='c'>c</td></tr>
                </table>"));

            var tall = grid.CellAt(0, 0);
            Assert.NotNull(tall);
            Assert.Same(tall, grid.CellAt(1, 0));
            Assert.True(grid.TryGetSpan(tall!, out var span));
            Assert.Equal(new CellSpan(0, 0, 2, 1), span);
        }

        [Fact]
        public async Task RowspanAndColspan_Combine()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <tr><td rowspan='2' colspan='2' id='big'>big</td><td id='c'>c</td></tr>
                    <tr><td id='d'>d</td></tr>
                </table>"));

            var big = grid.CellAt(0, 0);
            Assert.NotNull(big);
            Assert.Same(big, grid.CellAt(0, 1));
            Assert.Same(big, grid.CellAt(1, 0));
            Assert.Same(big, grid.CellAt(1, 1));
            Assert.True(grid.TryGetSpan(big!, out var span));
            Assert.Equal(new CellSpan(0, 0, 2, 2), span);
        }

        [Fact]
        public async Task RaggedRows_ShorterRowLeavesTrailingSlotsEmpty()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <tr><td id='a'>a</td><td id='b'>b</td><td id='c'>c</td></tr>
                    <tr><td id='d'>d</td></tr>
                </table>"));

            Assert.Equal(3, grid.ColumnCount);
            Assert.NotNull(grid.CellAt(1, 0));
            Assert.Null(grid.CellAt(1, 1));
            Assert.Null(grid.CellAt(1, 2));
        }

        [Fact]
        public async Task RowspanIntoLastColumn_ResolvesCorrectly()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <tr><td id='a'>a</td><td id='b'>b</td><td rowspan='2' id='tall'>tall</td></tr>
                    <tr><td id='d'>d</td><td id='e'>e</td></tr>
                </table>"));

            Assert.Equal(3, grid.ColumnCount);
            var tall = grid.CellAt(0, 2);
            Assert.NotNull(tall);
            Assert.Same(tall, grid.CellAt(1, 2));
        }

        [Fact]
        public async Task TwoRowGroups_IdentifyFirstAndLastRowOfEach()
        {
            // <thead>/<tfoot> are detached from the table's own Boxes during layout (they repeat per
            // page via CssLayoutEngineTable's own _headerBox/_footerBox, not as ordinary children) -
            // exercised once TableGrid is wired into the real engine (Phase 2), which builds from
            // _allRows directly rather than re-walking the post-layout DOM the way this test's AllRows
            // helper does. Two plain <tbody> groups exercise the same row-group boundary logic without
            // that detachment.
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <tbody>
                        <tr><td id='a1'>a1</td></tr>
                        <tr><td id='a2'>a2</td></tr>
                    </tbody>
                    <tbody>
                        <tr><td id='b1'>b1</td></tr>
                    </tbody>
                </table>"));

            Assert.Equal(3, grid.RowCount);
            Assert.NotNull(grid.RowGroupAt(0));
            Assert.True(grid.IsFirstRowOfGroup(0));
            Assert.False(grid.IsLastRowOfGroup(0));
            Assert.False(grid.IsFirstRowOfGroup(1));
            Assert.True(grid.IsLastRowOfGroup(1));

            Assert.NotNull(grid.RowGroupAt(2));
            Assert.True(grid.IsFirstRowOfGroup(2));
            Assert.True(grid.IsLastRowOfGroup(2));
            Assert.NotSame(grid.RowGroupAt(1), grid.RowGroupAt(2));
        }

        [Fact]
        public async Task BareRow_HasNoRowGroup()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table><tr><td id='a'>a</td></tr></table>"));

            Assert.Null(grid.RowGroupAt(0));
        }

        [Fact]
        public async Task ColGroupWithSpan_NoColChildren_OneColumnBoxCoversTheWholeSpan()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <colgroup span='3'></colgroup>
                    <tr><td>a</td><td>b</td><td>c</td></tr>
                </table>"));

            Assert.Equal(3, grid.ColumnCount);
            Assert.True(grid.ColumnGroupStartsAt(0));
            Assert.False(grid.ColumnGroupStartsAt(1));
            Assert.False(grid.ColumnGroupStartsAt(2));
            Assert.True(grid.ColumnGroupEndsAt(2));
            Assert.Same(grid.ColumnGroupAt(0), grid.ColumnGroupAt(2));
            // No <col> children, so the group itself stands in as the column - no separate column box.
            Assert.Null(grid.ColumnAt(0));
        }

        [Fact]
        public async Task ColGroupWithColChildren_EachColIsItsOwnColumnBox()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <colgroup>
                        <col id='c1' />
                        <col id='c2' span='2' />
                    </colgroup>
                    <tr><td>a</td><td>b</td><td>c</td></tr>
                </table>"));

            Assert.Equal(3, grid.ColumnCount);
            Assert.True(grid.ColumnBoxStartsAt(0));
            Assert.True(grid.ColumnBoxStartsAt(1));
            Assert.False(grid.ColumnBoxStartsAt(2));
            Assert.True(grid.ColumnBoxEndsAt(0));
            Assert.True(grid.ColumnBoxEndsAt(2));
            Assert.NotSame(grid.ColumnAt(0), grid.ColumnAt(1));
            Assert.Same(grid.ColumnAt(1), grid.ColumnAt(2));
            // Every column here shares one enclosing <colgroup>.
            Assert.Same(grid.ColumnGroupAt(0), grid.ColumnGroupAt(2));
        }

        [Fact]
        public async Task BareColsWithNoColgroup_HaveNoColumnGroup()
        {
            var (_, grid) = await Build(LayoutHarness.Wrap(@"
                <table>
                    <col />
                    <col />
                    <tr><td>a</td><td>b</td></tr>
                </table>"));

            Assert.Equal(2, grid.ColumnCount);
            Assert.NotNull(grid.ColumnAt(0));
            Assert.Null(grid.ColumnGroupAt(0));
        }
    }
}

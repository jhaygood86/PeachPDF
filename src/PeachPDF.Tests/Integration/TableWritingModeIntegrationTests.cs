using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end layout tests for writing-mode-aware Table sizing and cell placement
    /// (<see cref="PeachPDF.Html.Core.Dom.CssLayoutEngineTable"/>'s axis-mapping fields), asserting actual
    /// post-layout <c>CssBox</c> geometry - not just that layout completes - per this repo's testing
    /// conventions for layout-engine changes. Scoped to simple tables (no <c>&lt;thead&gt;</c>/
    /// <c>&lt;tfoot&gt;</c>, no <c>colspan</c>/<c>rowspan</c>, no <c>border-collapse: collapse</c>, no
    /// <c>&lt;caption&gt;</c>) - see the axis-mapping fields' own remarks in <c>CssLayoutEngineTable.cs</c>
    /// and issue #762 for what remains out of scope.
    /// </summary>
    public class TableWritingModeIntegrationTests
    {
        [Fact]
        public async Task VerticalRl_Rows_StackRightToLeft_ColumnsStackTopToBottom()
        {
            // css-tables-3: rows always stack along the block axis (physical X for vertical-rl, growing
            // from the table's own right edge), columns always run along the inline axis (physical Y).
            // Fixture dimensions are given directly in pt (not px) per this repo's testing convention, so
            // expected values read literally without a px->pt conversion factor.
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-rl; border-spacing: 4pt">
                  <tr><td id="a1" style="height: 50pt; width: 30pt">A1</td><td id="b1" style="height: 50pt; width: 30pt">B1</td></tr>
                  <tr><td id="a2" style="height: 50pt; width: 30pt">A2</td><td id="b2" style="height: 50pt; width: 30pt">B2</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            var a1 = LayoutHarness.FindById(root, "a1");
            var b1 = LayoutHarness.FindById(root, "b1");
            var a2 = LayoutHarness.FindById(root, "a2");
            var b2 = LayoutHarness.FindById(root, "b2");
            Assert.NotNull(t);
            Assert.NotNull(a1);
            Assert.NotNull(b1);
            Assert.NotNull(a2);
            Assert.NotNull(b2);

            // Rows: row 1 (a1/b1) is flow-first, so it sits one border-spacing in from block-start - the
            // table's own right edge for vertical-rl - and row 2 (a2/b2) sits a further row-extent + one
            // border-spacing to the left of that.
            const double borderSpacing = 4;
            Assert.Equal(t!.ActualRight - borderSpacing, a1!.ActualRight, 1);
            Assert.Equal(t.ActualRight - borderSpacing, b1!.ActualRight, 1);
            Assert.True(a2!.Location.X < a1.Location.X, "row 2 should sit to the left of row 1 under vertical-rl");
            Assert.True(b2!.Location.X < b1.Location.X, "row 2 should sit to the left of row 1 under vertical-rl");
            Assert.Equal(a1.Location.X - borderSpacing, a2.ActualRight, 1);

            // Columns: within each row, column 1 (a1/a2) sits above column 2 (b1/b2) - top-to-bottom,
            // unaffected by the row-axis reflection above.
            Assert.True(a1.Location.Y < b1.Location.Y, "column 2 should sit below column 1 within a row");
            Assert.True(a2.Location.Y < b2.Location.Y, "column 2 should sit below column 1 within a row");

            // Both rows' cells share the same column-axis (Y) extent - columns don't move with the
            // row-axis reflection.
            Assert.Equal(a1.Location.Y, a2.Location.Y, 1);
            Assert.Equal(b1.Location.Y, b2.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalLr_Rows_StackLeftToRight_ColumnsStackTopToBottom()
        {
            // block-start is the table's own left edge for vertical-lr - the mirror image of vertical-rl,
            // with no reflection pass needed (rows already grow forward from block-start).
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-lr; border-spacing: 4pt">
                  <tr><td id="a1" style="height: 50pt; width: 30pt">A1</td><td id="b1" style="height: 50pt; width: 30pt">B1</td></tr>
                  <tr><td id="a2" style="height: 50pt; width: 30pt">A2</td><td id="b2" style="height: 50pt; width: 30pt">B2</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            var a1 = LayoutHarness.FindById(root, "a1");
            var a2 = LayoutHarness.FindById(root, "a2");
            var b1 = LayoutHarness.FindById(root, "b1");
            var b2 = LayoutHarness.FindById(root, "b2");
            Assert.NotNull(t);
            Assert.NotNull(a1);
            Assert.NotNull(a2);
            Assert.NotNull(b1);
            Assert.NotNull(b2);

            // Row 1 sits one border-spacing in from block-start - the table's own left edge for
            // vertical-lr.
            const double borderSpacing = 4;
            Assert.Equal(t!.Location.X + borderSpacing, a1!.Location.X, 1);
            Assert.Equal(t.Location.X + borderSpacing, b1!.Location.X, 1);
            Assert.True(a2!.Location.X > a1.Location.X, "row 2 should sit to the right of row 1 under vertical-lr");
            Assert.True(b2!.Location.X > b1.Location.X, "row 2 should sit to the right of row 1 under vertical-lr");

            Assert.True(a1.Location.Y < b1.Location.Y, "column 2 should sit below column 1 within a row");
        }

        [Fact]
        public async Task VerticalRl_ColumnWidths_ComeFromCellHeightNotWidth_AndAreUniformAcrossRows()
        {
            // The column axis is physical Y for a vertical table, so a cell's own column-sizing hint is
            // its `height` (not `width`) - CellInlineSize's own axis-aware property selection. And, per
            // css-tables-3, a column's own extent is shared by every cell in it: each column's Y-extent
            // must be the MAX of its own cells' heights (matching how a horizontal-tb table's columns
            // already share one width across every row), not each cell independently keeping its own
            // height - which a prior version of this test asserted, written around a real bug where the
            // auto-width "spread extra width between columns" step (DetermineMissingColumnWidths) could
            // shrink a column below its own already-computed content width whenever availCellSpace (the
            // column axis's available space) came out smaller than the columns' combined content size -
            // an indefinite/zero available column-axis space, the common case for a vertical table with
            // no definite height anywhere up its containing-block chain.
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-rl; border-spacing: 0">
                  <tr><td id="a0" style="height: 80pt; width: 20pt">A0</td><td id="a1" style="height: 20pt; width: 50pt">A1</td></tr>
                  <tr><td id="b0" style="height: 30pt; width: 20pt">B0</td><td id="b1" style="height: 60pt; width: 50pt">B1</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a0 = LayoutHarness.FindById(root, "a0");
            var a1 = LayoutHarness.FindById(root, "a1");
            var b0 = LayoutHarness.FindById(root, "b0");
            var b1 = LayoutHarness.FindById(root, "b1");
            Assert.NotNull(a0);
            Assert.NotNull(a1);
            Assert.NotNull(b0);
            Assert.NotNull(b1);

            // Column 0's Y-extent is max(80, 30) = 80, shared by both its cells - not each cell keeping
            // its own height (80 and 30) independently.
            Assert.Equal(80, a0!.ActualBottom - a0.Location.Y, 1);
            Assert.Equal(80, b0!.ActualBottom - b0.Location.Y, 1);

            // Column 1's Y-extent is max(20, 60) = 60, independently of column 0 - proving this is
            // genuine per-column sizing (driven by height, not the cells' 20pt/50pt widths) rather than
            // every column coincidentally sharing one table-wide value.
            Assert.Equal(60, a1!.ActualBottom - a1.Location.Y, 1);
            Assert.Equal(60, b1!.ActualBottom - b1.Location.Y, 1);
        }

        [Fact]
        public async Task HorizontalTb_Table_UnaffectedByAxisMapping()
        {
            // Regression guard: the ordinary horizontal-tb path (already covered exhaustively by the
            // pre-existing table test suite) still produces the same geometry through the new
            // _isVertical-gated fields.
            var html = LayoutHarness.Wrap("""
                <table id="t" style="border-spacing: 4pt">
                  <tr><td id="a1" style="width: 50pt; height: 30pt">A1</td><td id="b1" style="width: 50pt; height: 30pt">B1</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a1 = LayoutHarness.FindById(root, "a1");
            var b1 = LayoutHarness.FindById(root, "b1");
            Assert.NotNull(a1);
            Assert.NotNull(b1);

            Assert.True(a1!.Location.X < b1!.Location.X, "second column should sit to the right of the first");
            Assert.Equal(a1.Location.Y, b1.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalRl_Table_Fragment_IsMonolithic()
        {
            // CssLayoutEngineTable lays out a vertical table's row axis monolithically (its pageHeight
            // override), so MonolithicContent.IsUnresumableVerticalTable must mark it as such - otherwise
            // the outer fragmentation driver could try to slice it mid-row, which the engine has no way to
            // resume from.
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-rl">
                  <tr><td>Cell</td></tr>
                </table>
                """);

            var (_, container) = await LayoutHarness.LayoutAsync(html);

            var fragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == "t")
                .ToList();

            Assert.NotEmpty(fragments);
            Assert.All(fragments, f => Assert.True(f.IsMonolithic));
        }

        [Fact]
        public async Task HorizontalTb_Table_Fragment_IsNotMonolithic()
        {
            // Regression guard: an ordinary horizontal-tb table keeps its real per-row resumption
            // behavior (issue #464) - the new vertical-only monolithic rule must not catch it too.
            var html = LayoutHarness.Wrap("""
                <table id="t">
                  <tr><td>Cell</td></tr>
                </table>
                """);

            var (_, container) = await LayoutHarness.LayoutAsync(html);

            var fragments = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == "t")
                .ToList();

            Assert.NotEmpty(fragments);
            Assert.All(fragments, f => Assert.False(f.IsMonolithic));
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_StraddlingAPageBoundary_MovesWholeToTheNextPage(string writingMode)
        {
            // A 60pt table starting at 160pt - 20pt above the first 200pt page's 180pt content-band
            // bottom (20pt margin) - straddles the boundary unless something forbids it, mirroring
            // MonolithicContentLayoutIntegrationTests' StraddleDocument fixture shape.
            var html = LayoutHarness.Wrap(
                "<div style='height:140pt'>filler</div>" +
                $"<table id='card' style='writing-mode:{writingMode};border-spacing:0'>" +
                "<tr><td style='height:60pt;width:20pt'>Cell</td></tr></table>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: 200, margin: 20);
            var card = LayoutHarness.FindById(root, "card")!;

            var top = container.PageIndexOf(card.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);
            var bottom = container.PageIndexOf(card.ActualBottom - HtmlContainerInt.PageBoundaryEpsilon);

            Assert.Equal(top, bottom);
        }

        [Theory]
        [InlineData("vertical-lr")]
        [InlineData("vertical-rl")]
        public async Task VerticalTable_ManyRows_ExceedingOnePagesBand_LaysOutWithoutSpuriousInternalPageBreak(string writingMode)
        {
            // Regression test: the per-row and straddle-correction page-break checks used to compare
            // this table's own row-axis (physical-X) cursor against the *container's* real physical-Y
            // page bands, rather than this table's own pageHeight override (forced to MaxValue for a
            // vertical table's row loop). A table with enough rows for its row-axis extent to exceed one
            // page's band height could trip that comparison and get a spurious internal break inserted
            // mid-table - substituting a physical-Y page-top value into the row-axis accumulator and
            // corrupting every row placed after it.
            const int rowCount = 10;
            var rows = string.Join("", Enumerable.Range(0, rowCount)
                .Select(i => $"<tr><td id=\"r{i}\" style=\"height: 20pt; width: 30pt\">R{i}</td></tr>"));
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-spacing: 2pt">{rows}</table>
                """);

            // A 200pt page (160pt content band, after the 20pt margins below) is far shorter than this
            // table's own row-axis extent (10 rows * (30pt + 2pt spacing) = 320pt) - exactly the
            // situation that tripped the bug.
            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 200, margin: 20);

            var rowBoxes = Enumerable.Range(0, rowCount)
                .Select(i => LayoutHarness.FindById(root, $"r{i}"))
                .Select(b => { Assert.NotNull(b); return b!; })
                .ToList();

            // Every consecutive pair of cells must sit exactly one border-spacing apart along the row
            // axis. A reflection (vertical-rl) preserves this spacing exactly, since it is a rigid mirror
            // of the whole row-axis span - so a spurious internal break substituting a physical-Y
            // page-top value partway through would show up as a wrong (large, or negative/overlapping)
            // gap at exactly the row where it fired, instead of the uniform spacing every other
            // consecutive pair has.
            for (var i = 0; i < rowCount - 1; i++)
            {
                var a = rowBoxes[i];
                var b = rowBoxes[i + 1];

                if (writingMode == "vertical-lr")
                {
                    Assert.Equal(a.ActualRight + 2, b.Location.X, 1);
                }
                else
                {
                    Assert.Equal(b.ActualRight + 2, a.Location.X, 1);
                }
            }
        }

        [Fact]
        public async Task VerticalRl_TableWithBottomCaption_DoesNotCorruptRowPlacement()
        {
            // Regression test: the bottom-caption branch of Step 7 only ever assigned
            // _tableBox.ActualBottom, never _tableBox.ActualRight - the row-axis-max edge
            // ReflectRowAxisForVerticalRl reads as `max` to mirror every row. A vertical-rl table with a
            // bottom caption left that edge stale/unset, corrupting every row's mirrored position (not
            // just the caption's own, already-disclosed-as-unconverted placement).
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-rl; border-spacing: 4pt">
                  <caption style="caption-side: bottom">Caption</caption>
                  <tr><td id="a1" style="height: 50pt; width: 30pt">A1</td></tr>
                  <tr><td id="a2" style="height: 50pt; width: 30pt">A2</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            var a1 = LayoutHarness.FindById(root, "a1");
            var a2 = LayoutHarness.FindById(root, "a2");
            Assert.NotNull(t);
            Assert.NotNull(a1);
            Assert.NotNull(a2);

            // The table's own row-axis-max edge must be a real, settled value - not the CLR default (0)
            // a stale/unset ActualRight would leave it at.
            Assert.True(t!.ActualRight > t.Location.X + 10,
                $"table's ActualRight ({t.ActualRight}) should be well past its Location.X ({t.Location.X}), not left stale/unset by the bottom-caption branch");

            // Row 1 still sits flush (within one border-spacing) against the table's own right edge -
            // the same block-start-flush invariant VerticalRl_Rows_StackRightToLeft_ColumnsStackTopToBottom
            // asserts for a caption-less table - rather than a corrupted position derived from a stale max.
            const double borderSpacing = 4;
            Assert.Equal(t.ActualRight - borderSpacing, a1!.ActualRight, 1);
            Assert.True(a2!.Location.X < a1.Location.X, "row 2 should still sit to the left of row 1");
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}

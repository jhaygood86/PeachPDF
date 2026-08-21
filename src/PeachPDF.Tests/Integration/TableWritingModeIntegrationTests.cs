using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end layout tests for writing-mode-aware Table sizing, cell placement, captions,
    /// <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>, collapsed borders, <c>vertical-align</c>, and
    /// <c>rowspan</c> (<see cref="PeachPDF.Html.Core.Dom.CssLayoutEngineTable"/>'s axis-mapping fields),
    /// asserting actual post-layout <c>CssBox</c> geometry - not just that layout completes - per this
    /// repo's testing conventions for layout-engine changes. <c>colspan</c> straddling the row axis and
    /// real per-row pagination of a vertical table's own content remain out of scope - see issue #762.
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

        [Theory]
        [InlineData("vertical-lr")]
        [InlineData("vertical-rl")]
        public async Task VerticalTable_WithRepeatingThead_ManyRows_ExceedingOnePagesBand_LaysOutWithoutSpuriousInternalPageBreak(string writingMode)
        {
            // Regression test for a second instance of the same bug class as
            // VerticalTable_ManyRows_ExceedingOnePagesBand_LaysOutWithoutSpuriousInternalPageBreak just
            // above, found in this diff's own post-change review: SettleWhetherTheGroupsRepeat decided
            // whether a <thead>/<tfoot> repeats per page by comparing _headerHeight/_footerHeight (a
            // row-axis, i.e. physical-X, quantity for a vertical table) against a quarter of the
            // container's real physical-Y page-sheet height, with no _isVertical guard - so a vertical
            // table with a repeating <thead> small enough to pass that cap could still set _headerRepeats,
            // which in turn let SliceARowAcrossTheBandsItOverflows (also unguarded) run its physical-Y
            // page-band arithmetic against this table's row-axis cursor and corrupt it, exactly as the
            // no-thead case above already regression-tests. A vertical table's own content is placed as
            // one monolithic unit (real per-row pagination is out of scope - see #783), so _headerRepeats/
            // _footerRepeats must never become true for one regardless of measured heights.
            const int rowCount = 10;
            var rows = string.Join("", Enumerable.Range(0, rowCount)
                .Select(i => $"<tr><td id=\"r{i}\" style=\"height: 20pt; width: 30pt\">R{i}</td></tr>"));
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-spacing: 2pt">
                  <thead><tr><td id="h" style="height: 20pt; width: 20pt">H</td></tr></thead>
                  {rows}
                </table>
                """);

            // Same 200pt page (160pt content band) as the no-thead sibling test - far shorter than this
            // table's own row-axis extent, and the <thead>'s own 20pt row-axis thickness is comfortably
            // under a quarter of the 200pt page sheet, which is exactly what let _headerRepeats trip.
            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 200, margin: 20);

            var rowBoxes = Enumerable.Range(0, rowCount)
                .Select(i => LayoutHarness.FindById(root, $"r{i}"))
                .Select(b => { Assert.NotNull(b); return b!; })
                .ToList();

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

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_RowspanCell_SpansTheCombinedRowAxisExtentOfItsRows(string writingMode)
        {
            // A rowspan="2" cell's own row-axis extent (physical X) must equal the combined row-axis
            // extent of both rows it spans - the vertical-table counterpart of an ordinary horizontal-tb
            // rowspan cell's height growing to fit every row it spans (CloseSpanningCell). Row 1's own
            // extent is driven by b1 (30pt), row 2's by b2 (40pt, deliberately different so the combined
            // total - 30 + 4 (spacing) + 40 = 74 - can't be mistaken for either row's own individual
            // extent or the spanning cell's own small natural width (10pt).
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-spacing: 4pt">
                  <tr>
                    <td id="span" rowspan="2" style="width: 10pt; height: 60pt">Span</td>
                    <td id="b1" style="width: 30pt; height: 60pt">B1</td>
                  </tr>
                  <tr>
                    <td id="b2" style="width: 40pt; height: 60pt">B2</td>
                  </tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "span");
            Assert.NotNull(span);

            Assert.Equal(74, span!.ActualRight - span.Location.X, 1);
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_RowspanCell_DoesNotCorruptSiblingCellsRowAxisPosition(string writingMode)
        {
            // Regression gate for the row.ActualRight/ActualBottom bookkeeping fix: before it, a
            // rowspan cell's opening row captured its own ActualRight (the row axis, for a vertical
            // table) from the spanning cell's still-premature, not-yet-closed extent, corrupting the
            // row's own placement - and, for vertical-rl, every sibling cell's mirrored position too,
            // via ReflectRowAxisForVerticalRl's per-row OffsetLeft cascade. b1 must still sit exactly
            // where it would in a table with no rowspan at all: flush (one border-spacing in) against
            // the table's own block-start edge.
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-spacing: 4pt">
                  <tr>
                    <td rowspan="2" style="width: 10pt; height: 60pt">Span</td>
                    <td id="b1" style="width: 30pt; height: 60pt">B1</td>
                  </tr>
                  <tr>
                    <td style="width: 40pt; height: 60pt">B2</td>
                  </tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            var b1 = LayoutHarness.FindById(root, "b1");
            Assert.NotNull(t);
            Assert.NotNull(b1);

            const double borderSpacing = 4;
            if (writingMode == "vertical-rl")
            {
                Assert.Equal(t!.ActualRight - borderSpacing, b1!.ActualRight, 1);
            }
            else
            {
                Assert.Equal(t!.Location.X + borderSpacing, b1!.Location.X, 1);
            }
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_RowspanCell_InsideAShortPagedContainer_DoesNotCorruptGeometry(string writingMode)
        {
            // Regression gate for the rowMaxBottom pre-pass gate fix: SpanningCellBandGeometry reads
            // physical-Y page-band concepts (cell.Location.Y/GetMaximumBottom) that are meaningless for a
            // vertical table's own row axis, but the pre-pass that consults it was previously gated only
            // on the *container's* real page grid, not on _isVertical - so a vertical table with a
            // rowspan cell inside an ordinary paginated document could reach it. A short page (well under
            // this table's own row-axis extent) forces HasRealPageGrid true and exercises exactly that
            // path; the table stays monolithic regardless (MonolithicContent), so this only has to prove
            // the rowspan geometry itself still comes out right, not that pagination is honored.
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-spacing: 4pt">
                  <tr>
                    <td id="span" rowspan="2" style="width: 10pt; height: 60pt">Span</td>
                    <td id="b1" style="width: 30pt; height: 60pt">B1</td>
                  </tr>
                  <tr>
                    <td style="width: 40pt; height: 60pt">B2</td>
                  </tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 100, margin: 20);
            var span = LayoutHarness.FindById(root, "span");
            var b1 = LayoutHarness.FindById(root, "b1");
            Assert.NotNull(span);
            Assert.NotNull(b1);

            Assert.Equal(74, span!.ActualRight - span.Location.X, 1);
            Assert.Equal(30, b1!.ActualRight - b1.Location.X, 1);
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_RowspanAndColspanCombinedOnOneCell_SizeCorrectlyOnBothAxes(string writingMode)
        {
            // A single cell carrying both rowspan and colspan exercises the two span mechanisms together:
            // rowspan sizing (ReflectRowAxisForVerticalRl's residual correction) is entirely row-axis, and
            // colspan sizing (GetCellWidth) is entirely column-axis - reviewed as orthogonal (GetCellWidth's
            // own remarks), but not otherwise exercised together by any other test here. Three columns:
            // "span" occupies columns 0-1 across both rows (colspan=2, rowspan=2), leaving column 2 for
            // b1/b2 (one per row, matching the plain-rowspan tests' own shape).
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-spacing: 4pt">
                  <tr>
                    <td id="span" rowspan="2" colspan="2" style="width: 10pt; height: 60pt">Span</td>
                    <td id="b1" style="width: 30pt; height: 20pt">B1</td>
                  </tr>
                  <tr>
                    <td id="b2" style="width: 40pt; height: 20pt">B2</td>
                  </tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var span = LayoutHarness.FindById(root, "span");
            var b1 = LayoutHarness.FindById(root, "b1");
            var b2 = LayoutHarness.FindById(root, "b2");
            Assert.NotNull(span);
            Assert.NotNull(b1);
            Assert.NotNull(b2);

            // Row-axis extent: same combined-rows math as an ordinary rowspan cell (30 + 4 + 40 = 74),
            // unaffected by also carrying colspan.
            Assert.Equal(74, span!.ActualRight - span.Location.X, 1);

            // Column-axis extent: spans both of the table's two columns (column 0's own natural width -
            // from "Span" itself, since it's the only cell in column 0 - plus column 1's width, from
            // max(b1, b2)'s own height, 20pt, plus the interior border-spacing between them), unaffected
            // by also carrying rowspan.
            var columnAxisExtent = span.ActualBottom - span.Location.Y;
            Assert.True(columnAxisExtent > 20 + 4,
                $"colspan=2 cell's column-axis extent ({columnAxisExtent}) should include both columns, not just one");
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_WithTopAndBottomCaption_CaptionsStackAlongRowAxis_SizedAcrossColumnAxis(string writingMode)
        {
            // Captions are laid out along the table's own row axis (physical X for a vertical table),
            // sized across its full column axis (physical Y) - CSS 2.1 §17.4 reinterpreted through
            // css-tables-3's axis mapping, rather than the pre-fix "always physical Y, always full
            // physical-X width" placement.
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-spacing: 0">
                  <caption id="top" style="caption-side: top">Top</caption>
                  <caption id="bottom" style="caption-side: bottom">Bottom</caption>
                  <tr><td id="a1" style="width: 30pt; height: 50pt">A1</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var top = LayoutHarness.FindById(root, "top");
            var bottom = LayoutHarness.FindById(root, "bottom");
            var a1 = LayoutHarness.FindById(root, "a1");
            Assert.NotNull(top);
            Assert.NotNull(bottom);
            Assert.NotNull(a1);

            // Sized across the full column axis, not left at the pre-fix 0 (a caption laid out along the
            // wrong, physical-X, axis under a vertical table never received real column-axis height).
            Assert.True(top!.ActualBottom - top.Location.Y > 10);
            Assert.True(bottom!.ActualBottom - bottom.Location.Y > 10);

            // The row grid (a1) sits strictly between the two captions along the row axis - the top
            // caption on the row-axis-start side, the bottom caption on the row-axis-end side.
            if (writingMode == "vertical-rl")
            {
                Assert.True(top.Location.X >= a1!.ActualRight - 0.5,
                    "top caption should sit at or past the row grid's own row-axis-start (right) edge under vertical-rl");
                Assert.True(bottom.ActualRight <= a1.Location.X + 0.5,
                    "bottom caption should sit at or past the row grid's own row-axis-end (left) edge under vertical-rl");
            }
            else
            {
                Assert.True(top.ActualRight <= a1!.Location.X + 0.5,
                    "top caption should sit at or before the row grid's own row-axis-start (left) edge under vertical-lr");
                Assert.True(bottom.Location.X >= a1.ActualRight - 0.5,
                    "bottom caption should sit at or past the row grid's own row-axis-end (right) edge under vertical-lr");
            }
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_WithTheadAndTfoot_ProxiesFlankTheBodyAlongTheRowAxis(string writingMode)
        {
            // <thead>/<tfoot> proxies are placed along the table's own row axis (physical X for a
            // vertical table) - the pre-fix code hardcoded startX to _tableBox.ClientLeft (the column
            // axis's own start for a vertical table, not the row axis's) and never axis-swapped the
            // proxy's own RPoint construction, so a vertical table's header/footer proxies came out at
            // physically wrong positions.
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-spacing: 0">
                  <thead><tr><td style="width: 20pt; height: 30pt">H</td></tr></thead>
                  <tbody><tr><td id="b1" style="width: 20pt; height: 30pt">B</td></tr></tbody>
                  <tfoot><tr><td style="width: 20pt; height: 30pt">F</td></tr></tfoot>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            var b1 = LayoutHarness.FindById(root, "b1");
            Assert.NotNull(t);
            Assert.NotNull(b1);

            var headerProxy = t!.Boxes.OfType<CssProxyBox>()
                .First(p => p.DerivedStyle.ActualDisplay == Keywords.TableHeaderGroup);
            var footerProxy = t.Boxes.OfType<CssProxyBox>()
                .First(p => p.DerivedStyle.ActualDisplay == Keywords.TableFooterGroup);

            if (writingMode == "vertical-rl")
            {
                Assert.True(headerProxy.Location.X >= b1!.ActualRight - 0.5,
                    "header proxy should sit at or past the body's own row-axis-start (right) edge under vertical-rl");
                Assert.True(footerProxy.ActualRight <= b1.Location.X + 0.5,
                    "footer proxy should sit at or past the body's own row-axis-end (left) edge under vertical-rl");
            }
            else
            {
                Assert.True(headerProxy.ActualRight <= b1!.Location.X + 0.5,
                    "header proxy should sit at or before the body's own row-axis-start (left) edge under vertical-lr");
                Assert.True(footerProxy.Location.X >= b1.ActualRight - 0.5,
                    "footer proxy should sit at or past the body's own row-axis-end (right) edge under vertical-lr");
            }

            // Non-degenerate row-axis extent on each proxy - the writing-mode-aware generalization of the
            // #124 zero-width-Bounds regression guard (a proxy paint-culled at a real Bounds check would
            // otherwise never paint at all).
            Assert.True(headerProxy.ActualRight > headerProxy.Location.X);
            Assert.True(footerProxy.ActualRight > footerProxy.Location.X);

            // Regression gate: Step 5's closing-footer arm used to grow cursor.MaxBottom/MaxRight from the
            // footer proxy's own physical ActualBottom/ActualRight unconditionally, never axis-swapped for
            // a vertical table - so the table's own final row-axis extent (_tableBox.ActualRight) never
            // included the footer's own row-axis extent at all, leaving the footer positioned entirely
            // outside the table's own settled bounds. The footer must sit fully within [t.Location.X,
            // t.ActualRight] along the row axis.
            Assert.True(footerProxy.Location.X >= t!.Location.X - 0.5,
                $"footer proxy's near edge ({footerProxy.Location.X}) should be within the table's own bounds (starting at {t.Location.X})");
            Assert.True(footerProxy.ActualRight <= t.ActualRight + 0.5,
                $"footer proxy's far edge ({footerProxy.ActualRight}) should be within the table's own bounds (ending at {t.ActualRight})");
        }

        [Fact]
        public async Task VerticalRl_MultiRowThead_InternalRowsReverseOrder_InBothLiveBoxesAndPaintSnapshot()
        {
            // Issue #784: ReflectRowAxisForVerticalRl used to give a multi-row <thead> ONE shared delta
            // (the group as a whole), which correctly repositions the group's own aggregate bounds but
            // leaves its own internal rows in forward-grown (unreversed) order - unlike an ordinary
            // <tbody> row, which is reflected individually and so reverses correctly. H1 (topologically
            // first) must end up physically closer to the table's own block-start (physical-max, since
            // vertical-rl's row axis starts at the max edge) than H2 - the same relative order every
            // <tbody> row already gets.
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-rl; border-spacing: 0">
                  <thead>
                    <tr><td id="h1" style="width: 20pt; height: 30pt">H1</td></tr>
                    <tr><td id="h2" style="width: 20pt; height: 30pt">H2</td></tr>
                  </thead>
                  <tbody><tr><td id="b1" style="width: 20pt; height: 30pt">B</td></tr></tbody>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            Assert.NotNull(t);

            var headerProxy = t!.Boxes.OfType<CssProxyBox>()
                .First(p => p.DerivedStyle.ActualDisplay == Keywords.TableHeaderGroup);
            var rows = headerProxy.SourceBox.Boxes.Where(r => r.DerivedStyle.ActualDisplay == Keywords.TableRow).ToList();
            Assert.Equal(2, rows.Count);
            var h1 = LayoutHarness.FindById(headerProxy.SourceBox, "h1");
            var h2 = LayoutHarness.FindById(headerProxy.SourceBox, "h2");
            Assert.NotNull(h1);
            Assert.NotNull(h2);

            // Live (detached) row order - what GetGridLineY/GetGridLineX read directly.
            Assert.True(rows[0].Location.X > rows[1].Location.X,
                $"H1 (topologically first, Location.X={rows[0].Location.X}) should end up physically closer to the block-start/max edge than H2 (Location.X={rows[1].Location.X}) under vertical-rl");

            // Each row's own (non-rowspan) cell must move WITH its row, not be left behind at the
            // pre-reflection position - the exact regression this file's own OffsetLeft doc comment
            // warns about ("the row/cell rectangles moved but their text did not").
            Assert.Equal(rows[0].Location.X, h1!.Location.X, 1);
            Assert.Equal(rows[1].Location.X, h2!.Location.X, 1);

            // Painted (proxy snapshot) order - what actually renders, kept in sync via ReflectSubtree.
            Assert.True(headerProxy.SourceGeometry!.TryGetGeometry(rows[0], out var g0));
            Assert.True(headerProxy.SourceGeometry!.TryGetGeometry(rows[1], out var g1));
            Assert.True(g0.Location.X > g1.Location.X,
                "the painted snapshot should show the same reversed row order as the live boxes");
            Assert.Equal(rows[0].Location.X, g0.Location.X, 1);
            Assert.Equal(rows[1].Location.X, g1.Location.X, 1);

            // The cells' own snapshot entries must agree with the cells' own live position too - not just
            // their rows'.
            Assert.True(headerProxy.SourceGeometry!.TryGetGeometry(h1, out var hg1));
            Assert.True(headerProxy.SourceGeometry!.TryGetGeometry(h2, out var hg2));
            Assert.Equal(h1.Location.X, hg1.Location.X, 1);
            Assert.Equal(h2.Location.X, hg2.Location.X, 1);
        }

        [Fact]
        public async Task VerticalRlTable_MultiRowThead_AbsoluteContentWithNoPositionedAncestor_SnapshotIsNotShiftedByRowAxisReflection()
        {
            // Issue #787's secondary fix: BoxGeometrySnapshot.Translate/ReflectSubtree had no
            // EscapesTranslationOf-equivalent guard, so an absolutely-positioned descendant with no
            // positioned ancestor of its own - which should stay put, resolved against the true document
            // root - would incorrectly receive the same per-row residual ReflectSubtree applies to
            // reverse a multi-row <thead>'s internal row order under vertical-rl (issue #784).
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-rl; border-spacing: 0">
                  <thead>
                    <tr><td style="width: 20pt; height: 20pt">H1</td></tr>
                    <tr><td style="width: 20pt; height: 20pt; padding: 0; border: 0; vertical-align: top;">
                      <div id="abs" style="position:absolute; top:5pt; left:5pt; width:5pt; height:5pt;"></div>
                    </td></tr>
                  </thead>
                  <tr><td style="width: 30pt; height: 20pt">R0</td></tr>
                  <tr><td style="width: 30pt; height: 20pt">R1</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            Assert.NotNull(t);

            var headerProxy = t!.Boxes.OfType<CssProxyBox>()
                .First(p => p.DerivedStyle.ActualDisplay == Keywords.TableHeaderGroup);
            var abs = LayoutHarness.FindById(headerProxy.SourceBox, "abs");
            Assert.NotNull(abs);

            // Live box: resolved against the real page-content origin, not the detached header (#787's
            // primary fix).
            Assert.Equal(25, abs!.Location.X, 1.5);
            Assert.Equal(25, abs.Location.Y, 1.5);

            // Painted snapshot: must agree with the live box - i.e. must NOT have received the per-row
            // residual ReflectSubtree applies to reverse the header's own internal row order under
            // vertical-rl, since abs's containing block (the true document root) lies outside the header
            // subtree being reflected.
            Assert.True(headerProxy.SourceGeometry!.TryGetGeometry(abs, out var absGeometry));
            Assert.Equal(abs.Location.X, absGeometry.Location.X, 1);
            Assert.Equal(abs.Location.Y, absGeometry.Location.Y, 1);
        }

        [Fact]
        public async Task VerticalLrTable_HeaderOpenedRowspan_CrossingIntoBody_ClosesOnTheCorrectBodyRowAndColumn()
        {
            // Issue #788, the vertical-table sibling of CssLayoutEngineTableTests'
            // TableLayout_RowspanCrossingFromTheadIntoTbody_ClosesOnTheCorrectBodyRowAndColumn - the
            // mechanism (ComputeHeaderRowSpansCrossingIntoBody/SeedCrossBoundaryRowSpans) operates purely
            // on grid row/column indices, never physical coordinates, so it needs no axis-specific
            // handling of its own. vertical-lr (not vertical-rl) so the assertions below read as direct,
            // forward-grown positions with no ReflectRowAxisForVerticalRl residual to account for.
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-lr; border-spacing: 0">
                  <thead>
                    <tr><td id="a" rowspan="3" style="width: 20pt; height: 20pt">A</td>
                        <td id="b" style="width: 20pt; height: 20pt">B</td></tr>
                  </thead>
                  <tr><td id="d" style="width: 20pt; height: 20pt">D</td></tr>
                  <tr><td id="x" style="width: 20pt; height: 20pt">x</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            Assert.NotNull(t);

            var headerProxy = t!.Boxes.OfType<CssProxyBox>()
                .First(p => p.DerivedStyle.ActualDisplay == Keywords.TableHeaderGroup);
            var a = LayoutHarness.FindById(headerProxy.SourceBox, "a");
            var d = LayoutHarness.FindById(root, "d");
            var x = LayoutHarness.FindById(root, "x");

            Assert.NotNull(a);
            Assert.NotNull(d);
            Assert.NotNull(x);

            // x was not phantom-shifted into a column the header never declared - it lands at the same
            // column-axis (physical Y) position as D, the only column A's own span leaves free.
            Assert.Equal(d!.Location.Y, x!.Location.Y, 1);

            // A's own row-axis extent (ActualRight for a vertical table) reaches down to cover x's row,
            // not just D's.
            Assert.True(a!.ActualRight >= x.ActualRight - 1,
                $"A's own row-axis extent ({a.ActualRight}) should reach x's row's ({x.ActualRight})");
            Assert.True(a.ActualRight > d.ActualRight,
                "A should reach past its own header row group into the body, not stop at D's row.");

            var bodyRow = x.ParentBox;
            Assert.NotNull(bodyRow);
            var spacer = bodyRow!.Boxes.OfType<CssSpacingBox>().FirstOrDefault(sb => ReferenceEquals(sb.ExtendedBox, a));
            Assert.NotNull(spacer);
        }

        [Fact]
        public async Task VerticalLr_MultiRowThead_InternalRowsKeepForwardOrder()
        {
            // Sibling regression guard for the fix above: ReflectRowAxisForVerticalRl only runs for
            // vertical-rl (_rowAxisStartIsAtMax), so vertical-lr's own forward-grown row order - already
            // correct, since vertical-lr's row axis genuinely grows left-to-right - must be unaffected.
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-lr; border-spacing: 0">
                  <thead>
                    <tr><td id="h1" style="width: 20pt; height: 30pt">H1</td></tr>
                    <tr><td id="h2" style="width: 20pt; height: 30pt">H2</td></tr>
                  </thead>
                  <tbody><tr><td id="b1" style="width: 20pt; height: 30pt">B</td></tr></tbody>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            Assert.NotNull(t);

            var headerProxy = t!.Boxes.OfType<CssProxyBox>()
                .First(p => p.DerivedStyle.ActualDisplay == Keywords.TableHeaderGroup);
            var rows = headerProxy.SourceBox.Boxes.Where(r => r.DerivedStyle.ActualDisplay == Keywords.TableRow).ToList();
            Assert.Equal(2, rows.Count);

            Assert.True(rows[0].Location.X < rows[1].Location.X,
                "H1 (topologically first) should stay physically before H2 under vertical-lr's genuine left-to-right growth");
        }

        [Fact]
        public async Task VerticalRl_MultiRowTfoot_InternalRowsReverseOrder()
        {
            // Same shape as the <thead> case above, for a multi-row <tfoot>.
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-rl; border-spacing: 0">
                  <tbody><tr><td id="b1" style="width: 20pt; height: 30pt">B</td></tr></tbody>
                  <tfoot>
                    <tr><td id="f1" style="width: 20pt; height: 30pt">F1</td></tr>
                    <tr><td id="f2" style="width: 20pt; height: 30pt">F2</td></tr>
                  </tfoot>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            Assert.NotNull(t);

            var footerProxy = t!.Boxes.OfType<CssProxyBox>()
                .First(p => p.DerivedStyle.ActualDisplay == Keywords.TableFooterGroup);
            var rows = footerProxy.SourceBox.Boxes.Where(r => r.DerivedStyle.ActualDisplay == Keywords.TableRow).ToList();
            Assert.Equal(2, rows.Count);
            var f1 = LayoutHarness.FindById(footerProxy.SourceBox, "f1");
            var f2 = LayoutHarness.FindById(footerProxy.SourceBox, "f2");
            Assert.NotNull(f1);
            Assert.NotNull(f2);

            Assert.True(rows[0].Location.X > rows[1].Location.X,
                $"F1 (topologically first, Location.X={rows[0].Location.X}) should end up physically closer to the block-start/max edge than F2 (Location.X={rows[1].Location.X}) under vertical-rl");
            Assert.Equal(rows[0].Location.X, f1!.Location.X, 1);
            Assert.Equal(rows[1].Location.X, f2!.Location.X, 1);

            Assert.True(footerProxy.SourceGeometry!.TryGetGeometry(rows[0], out var g0));
            Assert.True(footerProxy.SourceGeometry!.TryGetGeometry(rows[1], out var g1));
            Assert.True(g0.Location.X > g1.Location.X,
                "the painted snapshot should show the same reversed row order as the live boxes");

            Assert.True(footerProxy.SourceGeometry!.TryGetGeometry(f1, out var fg1));
            Assert.True(footerProxy.SourceGeometry!.TryGetGeometry(f2, out var fg2));
            Assert.Equal(f1.Location.X, fg1.Location.X, 1);
            Assert.Equal(f2.Location.X, fg2.Location.X, 1);
        }

        [Fact]
        public async Task VerticalRl_RowspanCellInsideMultiRowThead_SpansCombinedExtent_InBothLiveAndSnapshot()
        {
            // Companion gap in #784: ReflectRowAxisForVerticalRl's rowspanFixups scan only ever walked
            // `row.Boxes` for the top-level entries it was given, and a multi-row <thead>'s own top-level
            // entry is the row-GROUP (whose .Boxes are <tr>s, not cells) - so a rowspan cell entirely
            // inside such a group was never found at all, and never received its own residual correction.
            // Widths 10/20/30 are deliberately distinct so the combined extent (20 + 4 + 30 = 54) can't be
            // mistaken for either individual row's own extent or the spanning cell's own small natural
            // width, mirroring VerticalTable_RowspanCell_SpansTheCombinedRowAxisExtentOfItsRows's own trick.
            var html = LayoutHarness.Wrap("""
                <table id="t" style="writing-mode: vertical-rl; border-spacing: 4pt">
                  <thead>
                    <tr>
                      <td id="span" rowspan="2" style="width: 10pt; height: 60pt">Span</td>
                      <td id="h1" style="width: 20pt; height: 30pt">H1</td>
                    </tr>
                    <tr><td id="h2" style="width: 30pt; height: 30pt">H2</td></tr>
                  </thead>
                  <tbody><tr><td id="b1" style="width: 20pt; height: 30pt">B</td></tr></tbody>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            Assert.NotNull(t);

            var headerProxy = t!.Boxes.OfType<CssProxyBox>()
                .First(p => p.DerivedStyle.ActualDisplay == Keywords.TableHeaderGroup);
            var span = LayoutHarness.FindById(headerProxy.SourceBox, "span");
            Assert.NotNull(span);

            Assert.Equal(20 + 4 + 30, span!.ActualRight - span.Location.X, 1);

            Assert.True(headerProxy.SourceGeometry!.TryGetGeometry(span, out var geometry));
            Assert.Equal(span.Location.X, geometry.Location.X, 1);
            Assert.Equal(span.ActualRight, geometry.ActualRight, 1);
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_CollapsedBorders_RowBoundarySegmentsAreTallNotWide(string writingMode)
        {
            // A row-boundary (topologically "horizontal grid line") segment paints as a physically
            // vertical stripe - tall in Y, thin in X - for a vertical table, the literal inverse of
            // horizontal-tb's own wide/thin shape, since rows stack along physical X there. IsHorizontal
            // must flip to false for these segments (BordersDrawHandler.DrawCollapsedSegment's own
            // isHorizontal parameter picks which physical draw primitive to use based on the rect's own
            // shape, not the grid line's topology).
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-collapse: collapse; border-spacing: 0">
                  <tr><td style="width: 30pt; height: 40pt; border: 2pt solid black">A1</td></tr>
                  <tr><td style="width: 30pt; height: 40pt; border: 2pt solid black">A2</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            Assert.NotNull(t);

            var segments = t!.CollapsedBorderSegments;
            Assert.NotNull(segments);
            Assert.NotEmpty(segments!);

            var rowBoundarySegments = segments!.Where(s => !s.IsHorizontal).ToList();
            Assert.NotEmpty(rowBoundarySegments);
            Assert.All(rowBoundarySegments, s => Assert.True(s.Rect.Height > s.Rect.Width,
                $"a row-boundary segment under {writingMode} should be tall (column-axis-spanning) rather than wide: {s.Rect}"));
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_CollapsedBorders_UsedWidthsInsetTheCorrectPhysicalEdges(string writingMode)
        {
            // ApplyCollapsedUsedBorderWidths's own companion fix: the table's used collapsed border width
            // is charged to the row-axis-start/-end and column-axis-start/-end physical sides, not always
            // top/bottom/left/right - so a vertical table's own ClientLeft/ClientTop (row-axis-start/
            // column-axis-start) must inset by the resolved outer border, not stay at Location.X/Y.
            var html = LayoutHarness.Wrap($"""
                <table id="t" style="writing-mode: {writingMode}; border-collapse: collapse; border-spacing: 0; border: 6pt solid black">
                  <tr><td style="width: 30pt; height: 40pt; border: 6pt solid black">A1</td></tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var t = LayoutHarness.FindById(root, "t");
            Assert.NotNull(t);

            // The table's own outer collapsed border resolves to half the 6pt width (3pt) on every edge,
            // charged to the row axis's own start/end physical sides for a vertical table - ClientLeft
            // (row-axis-start for vertical, column-axis-start for horizontal-tb) must inset accordingly.
            Assert.True(t!.ClientLeft > t.Location.X + 1,
                $"ClientLeft ({t.ClientLeft}) should be inset from Location.X ({t.Location.X}) by the resolved collapsed border");
            Assert.True(t.ClientTop > t.Location.Y + 1,
                $"ClientTop ({t.ClientTop}) should be inset from Location.Y ({t.Location.Y}) by the resolved collapsed border");
        }

        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task VerticalTable_CellVerticalAlignBottom_OffsetsContentAlongRowAxis(string writingMode)
        {
            // vertical-align repositions a cell's content along the table's own row axis (physical X for
            // a vertical table) - ApplyCellVerticalAlignment's pre-fix OffsetTop-only implementation moved
            // content along the column axis instead. Mirrors the existing horizontal-tb regression test's
            // own shape (VerticalAlignIntegrationTests.Bottom_OnATableCell_PushesShortContentLowerThanTopAligned):
            // a "wide" sibling cell (100pt) forces the row's own row-axis extent, so the short (10pt)
            // 'v' cell is stretched to match it (the row-axis equalization every cell in a vertical
            // table's row shares - LayoutBodyRow's own rowMaxBottom bookkeeping) and has real leftover
            // room for vertical-align to move content into - two separate one-row tables, since 'v' and a
            // sibling in the *same* row would be different columns with a naturally-differing column-axis
            // (Y) position of their own, unrelated to vertical-align.
            var htmlTop = LayoutHarness.Wrap($"""
                <table style="writing-mode: {writingMode}; border-spacing: 0">
                  <tr>
                    <td style="width: 100pt; height: 20pt">Wide</td>
                    <td id="v" style="width: 10pt; height: 20pt; vertical-align: top">Short</td>
                  </tr>
                </table>
                """);
            var htmlBottom = LayoutHarness.Wrap($"""
                <table style="writing-mode: {writingMode}; border-spacing: 0">
                  <tr>
                    <td style="width: 100pt; height: 20pt">Wide</td>
                    <td id="v" style="width: 10pt; height: 20pt; vertical-align: bottom">Short</td>
                  </tr>
                </table>
                """);

            var (rootTop, _) = await LayoutHarness.LayoutAsync(htmlTop);
            var (rootBottom, _) = await LayoutHarness.LayoutAsync(htmlBottom);
            var vTop = LayoutHarness.FindById(rootTop, "v");
            var vBottom = LayoutHarness.FindById(rootBottom, "v");
            Assert.NotNull(vTop);
            Assert.NotNull(vBottom);

            var topWord = LayoutHarness.Descendants(vTop!).SelectMany(b => b.Words).First();
            var bottomWord = LayoutHarness.Descendants(vBottom!).SelectMany(b => b.Words).First();

            // 'bottom'-aligned content should sit further along the row axis (toward the cell's own
            // ClientRight) than 'top'-aligned content, which stays put at its content-driven position.
            Assert.True(bottomWord.Left > topWord.Left,
                $"'bottom'-aligned word (X={bottomWord.Left}) should sit further along the row axis than 'top'-aligned word (X={topWord.Left})");

            // And it must not have moved along the column axis at all - vertical-align is a row-axis-only
            // repositioning.
            Assert.Equal(topWord.Top, bottomWord.Top, 1);
        }

        [Fact]
        public async Task VerticalTable_CellVerticalAlignMiddle_MeasuresThroughNestedExplicitWidthChild()
        {
            // GetMaximumRight's own two branches beyond the leaf-word case: recursing into a nested child
            // box (a <div> wrapping the cell's real content, one level deeper than plain text ever
            // produces) and reading an explicit (non-auto) width directly off a box that carries one -
            // the row-axis counterpart of the same two branches GetMaximumBottom already needs for
            // horizontal-tb (see VerticalAlignIntegrationTests' own explicit-height regression test).
            var html = LayoutHarness.Wrap("""
                <table style="writing-mode: vertical-rl; border-spacing: 0">
                  <tr>
                    <td style="width: 100pt; height: 20pt">Wide</td>
                    <td id="v" style="width: 10pt; height: 20pt; vertical-align: middle">
                      <div style="width: 12pt; height: 8pt"><div style="width: 6pt; height: 4pt"></div></div>
                    </td>
                  </tr>
                </table>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var v = LayoutHarness.FindById(root, "v");
            Assert.NotNull(v);

            var child = v!.Boxes.Single();
            Assert.NotEqual(Keywords.Auto, child.Width);

            // 'middle' splits the leftover row-axis room between the child's own explicit-width far edge
            // and the cell's own ClientRight - a nonzero offset here (rather than 0, the no-op every
            // no-op-driving bug in this area has produced) proves GetMaximumRight actually measured the
            // child's own explicit ActualRight rather than falling through with an unmeasured 0.
            Assert.True(child.Location.X > v.ClientLeft,
                $"child.Location.X ({child.Location.X}) should have moved past the cell's own ClientLeft ({v.ClientLeft})");
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

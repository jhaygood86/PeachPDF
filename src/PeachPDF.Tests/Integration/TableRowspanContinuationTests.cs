using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A cell whose <c>rowspan</c> reaches out of the band it was placed in is <b>fragmented</b>: its box
    /// closes at the foot of that band and the depth it covers in the next one is stated as continuation
    /// geometry (<see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before <see href="https://github.com/jhaygood86/PeachPDF/issues/511">issue #511</see> the cell was
    /// one box stretched from its own row's top to the bottom of the row ending the span, so where those
    /// lay in different bands its borders and background were drawn straight through the page edge. Three
    /// different routes reached that state — the straddle correction declining to move a row that ends a
    /// span, the row loop's prediction breaking before a row in the <i>middle</i> of one, and a forced
    /// break declared on such a row — which is why the question is asked of the bands rather than of
    /// whichever arm took the break, and why all three are asserted here.
    /// </para>
    /// <para>
    /// This is what both other engines do. Gecko re-reflows the spanning cell against the remaining
    /// block-size (<c>nsTableRowGroupFrame::SplitSpanningCells</c>) and continues it; Blink treats every
    /// cell as its own <see href="https://www.w3.org/TR/css-break-3/#parallel-flows">§2.1 parallel
    /// flow</see> and carves rowspanned cells out of the per-row compensation it applies to the rest.
    /// Neither keeps the spanned rows together, so the row that ends a span is moved like any other.
    /// </para>
    /// <para>
    /// <b>Nothing here can be a word census.</b> No word moves and none is added — what changes is the
    /// number of <see cref="BoxFragment"/>s the spanning cell produces and where each one ends. That is
    /// what is asserted, with <see cref="ASpanInsideOneBand_ProducesNoContinuation"/> as the control that
    /// stops "it has a continuation" passing against a change that gives everything one.
    /// </para>
    /// </remarks>
    public class TableRowspanContinuationTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        /// <summary>
        /// Four 40pt rows, then a row opening a two-row span, then a 120pt row that <i>ends</i> it and
        /// would land across the band boundary. The issue's own fixture, and its numbers: the ending row
        /// measured Y 214 with its bottom at 334, against a band of [20, 280].
        /// </summary>
        private static string EndingRowWouldStraddle() => LayoutHarness.Wrap(@"
            <table style='width:100%;border-collapse:collapse'>
              <tr><td colspan='2'><div style='height:40pt'>row 0</div></td></tr>
              <tr><td colspan='2'><div style='height:40pt'>row 1</div></td></tr>
              <tr><td colspan='2'><div style='height:40pt'>row 2</div></td></tr>
              <tr><td colspan='2'><div style='height:40pt'>row 3</div></td></tr>
              <tr><td><div style='height:40pt'>row 4</div></td>
                  <td id='span' rowspan='2'><div style='height:40pt'>spans 4-5</div></td></tr>
              <tr><td><div style='height:120pt'>row 5</div></td></tr>
              <tr><td colspan='2'><div style='height:40pt'>row 6</div></td></tr>
            </table>");

        /// <summary>The boundary falls before a row in the <i>middle</i> of a four-row span.</summary>
        private static string MiddleRowAtTheBoundary() => LayoutHarness.Wrap(@"
            <table style='width:100%;border-collapse:collapse'>
              <tr><td colspan='2'><div style='height:40pt'>row 0</div></td></tr>
              <tr><td colspan='2'><div style='height:40pt'>row 1</div></td></tr>
              <tr><td colspan='2'><div style='height:40pt'>row 2</div></td></tr>
              <tr><td colspan='2'><div style='height:40pt'>row 3</div></td></tr>
              <tr><td><div style='height:40pt'>row 4</div></td>
                  <td id='span' rowspan='4'>spans 4-7</td></tr>
              <tr><td><div style='height:40pt'>row 5</div></td></tr>
              <tr><td><div style='height:40pt'>row 6</div></td></tr>
              <tr><td><div style='height:40pt'>row 7</div></td></tr>
            </table>");

        /// <summary>A forced break declared on a row in the middle of a span.</summary>
        private static string ForcedBreakInsideASpan() => LayoutHarness.Wrap(@"
            <table style='width:100%;border-collapse:collapse'>
              <tr><td><div style='height:40pt'>row 0</div></td>
                  <td id='span' rowspan='3'>spans 0-2</td></tr>
              <tr style='break-before:page'><td><div style='height:40pt'>row 1</div></td></tr>
              <tr><td><div style='height:40pt'>row 2</div></td></tr>
            </table>");

        /// <summary>The whole span comfortably inside one band — the control.</summary>
        private static string SpanInsideOneBand() => LayoutHarness.Wrap(@"
            <table style='width:100%;border-collapse:collapse'>
              <tr><td><div style='height:40pt'>row 0</div></td>
                  <td id='span' rowspan='2'>spans 0-1</td></tr>
              <tr><td><div style='height:40pt'>row 1</div></td></tr>
            </table>");

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }

        /// <summary>
        /// The spanning cell's fragments, one entry per fragmentainer it appears in.
        /// </summary>
        /// <remarks>
        /// Grouped by fragmentainer rather than listed, because a <c>rowspan</c> cell is reached by the
        /// emitter once per <c>CssSpacingBox</c> standing in for it as well as through its own row, so the
        /// same rectangle can be yielded more than once. That duplication is older than this change and is
        /// recorded as an accepted limitation; what matters here is which fragmentainers hold the cell and
        /// what each one's rectangle is, and those agree across the copies.
        /// </remarks>
        private static List<(int Slot, BoxFragment Fragment)> SpanFragments(HtmlContainerInt container) =>
        [
            .. container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == "span")
                .GroupBy(f => f.FragmentainerIndex)
                .OrderBy(g => g.Key)
                .Select(g => (g.Key, g.First()))
        ];

        private static CssBox SpanningCell(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.HtmlTag?.TryGetAttribute("id") == "span");

        private static List<CssBox> RowsOf(CssBox root) =>
            LayoutHarness.Descendants(root).Where(b => b.Display == CssConstants.TableRow).ToList();

        /// <summary>
        /// The row that ends a <c>rowspan</c> is carried onto the next band whole, like any other row. It
        /// used to be left where it fell and drawn cut through by the boundary — the straddle correction
        /// declined outright, because moving it meant taking back geometry belonging to a cell of an
        /// earlier row.
        /// </summary>
        [Fact]
        public async Task ARowThatEndsARowspan_IsMovedOntoTheNextBandLikeAnyOther()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                EndingRowWouldStraddle(), pageHeight: PageHeight, margin: Margin);

            var rows = RowsOf(root);
            Assert.Equal(7, rows.Count);

            // The row the span ends on begins the band the break opened, rather than straddling at 214.
            Assert.Equal(container.PageTopOf(1), rows[5].Location.Y, 0.01);

            foreach (var row in rows)
            {
                var slot = container.SlotStartingAt(row.Location.Y);

                Assert.False(
                    HtmlContainerInt.FallsPast(row.ActualBottom, container.BandOfSlot(slot)),
                    $"a row at {row.Location.Y:F1}..{row.ActualBottom:F1} crosses out of band {slot}");
            }
        }

        /// <summary>
        /// The assertion the issue is about, stated on the object the issue is about. The spanning cell's
        /// own box ends with the band it was placed in — it is not stretched down to the moved row's
        /// bottom in the band after it.
        /// </summary>
        /// <remarks>
        /// It closes level with the table's <i>slice</i> on that band rather than at the band's foot, and
        /// the difference is visible rather than pedantic: <c>FragmentPainter</c> clips the table's bottom
        /// border to the same <c>PageBreakBottoms</c> record, so a cell closing below it is a tint drawn
        /// past the table's own edge — which is what the first draft of this change rendered.
        /// </remarks>
        [Fact]
        public async Task TheSpanningCellsBox_EndsWithTheTablesOwnSliceOnTheBandItWasPlacedIn()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                EndingRowWouldStraddle(), pageHeight: PageHeight, margin: Margin);

            var cell = SpanningCell(root);
            var table = LayoutHarness.Descendants(root).First(b => b.Display == CssConstants.Table);
            var cellSlot = container.SlotStartingAt(cell.Location.Y);

            Assert.Equal(0, cellSlot);
            Assert.Equal(table.PageBreakBottoms![0], cell.ActualBottom, 0.01);

            // And that is the bottom of the last row placed in that band, not the band's own foot.
            Assert.Equal(RowsOf(root)[4].ActualBottom, cell.ActualBottom, 0.01);
            Assert.True(cell.ActualBottom < container.PageBottomOf(0));

            Assert.False(
                HtmlContainerInt.FallsPast(cell.ActualBottom, container.BandOfSlot(cellSlot)),
                $"the spanning cell's box runs {cell.Location.Y:F1}..{cell.ActualBottom:F1}, "
                + $"out of band {cellSlot}");
        }

        /// <summary>
        /// And the depth it covers on the page its ending row moved to is stated as a fragment there, so
        /// §6.1's "every cell's box continues" holds: the cell is drawn on both pages rather than closing
        /// at the first one's foot and vanishing.
        /// </summary>
        [Fact]
        public async Task TheSpanningCell_ContinuesAsAContentFreeFragmentInTheBandItsEndingRowLandsIn()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                EndingRowWouldStraddle(), pageHeight: PageHeight, margin: Margin);

            var fragments = SpanFragments(container);

            Assert.Equal([0, 1], fragments.Select(f => f.Slot));

            var (_, continuation) = fragments[1];

            Assert.Empty(continuation.Words);
            Assert.Empty(continuation.Children);

            // One whole-box decoration rectangle: the null Line is the fragment tree's way of saying "this
            // is the border box, not a line box".
            var line = Assert.Single(continuation.Lines);
            Assert.Null(line.Line);

            // Fragment rectangles are fragmentainer-local, so the continuation runs from that band's own
            // content top down to the bottom the row ending the span reached.
            var rows = RowsOf(root);
            var localBottom = rows[5].ActualBottom - (container.PageTopOf(1) - container.MarginTop);

            Assert.Equal(container.MarginTop, continuation.Rect.Y, 0.01);
            Assert.Equal(localBottom, continuation.Rect.Bottom, 0.01);
        }

        /// <summary>
        /// The continuation does not repaint the cell's top border, which
        /// <see href="https://www.w3.org/TR/css-tables-3/#breaking-rules">css-tables-3 §6.1</see> requires
        /// in as many words — <i>"top borders must not be repainted in continuation fragments"</i> — and
        /// which <see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>
        /// says the same way for `box-decoration-break: slice`: no border is drawn at a break on either
        /// side of it.
        /// </summary>
        [Fact]
        public async Task TheSpanningCellsFragments_OwnOnlyTheBlockEdgesTheBreakDidNotMake()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                EndingRowWouldStraddle(), pageHeight: PageHeight, margin: Margin);

            var fragments = SpanFragments(container);
            var last = fragments.Count - 1;

            Assert.True(last > 0, "the spanning cell has one fragment, so there is no break edge to state");

            for (var i = 0; i <= last; i++)
            {
                var slice = Assert.Single(fragments[i].Fragment.Lines).Slice;

                Assert.Equal(i == 0, slice.HasTopEdge);
                Assert.Equal(i == last, slice.HasBottomEdge);

                // A page's fragmentainers differ in the block axis alone, so the inline edges are nobody's
                // break and both survive on every fragment.
                Assert.True(slice.HasLeftEdge);
                Assert.True(slice.HasRightEdge);
            }
        }

        /// <summary>
        /// The control. A span that stays inside one band is one box and one fragment — without this,
        /// "the cell has a continuation" would pass against a change that gave every cell one.
        /// </summary>
        [Fact]
        public async Task ASpanInsideOneBand_ProducesNoContinuation()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                SpanInsideOneBand(), pageHeight: PageHeight, margin: Margin);

            var fragments = SpanFragments(container);
            Assert.Equal([0], fragments.Select(f => f.Slot));

            var cell = SpanningCell(root);
            var rows = RowsOf(root);

            // Still stretched to the bottom of the row it ends on, which is what a rowspan means.
            Assert.Equal(rows[1].ActualBottom, cell.ActualBottom, 0.01);
        }

        /// <summary>
        /// The other two routes to a span crossing a boundary. Neither goes through the straddle
        /// correction: the first is the row loop's own prediction breaking before a row in the middle of a
        /// span, the second a <c>break-before: page</c> declared on one — which
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see> requires be honored
        /// exactly where it was declared, so the row is not moved and only the cell is fragmented.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ASpanCrossedByABreakTheRowLoopTook_IsFragmentedToo(bool forced)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                forced ? ForcedBreakInsideASpan() : MiddleRowAtTheBoundary(),
                pageHeight: PageHeight, margin: Margin);

            var cell = SpanningCell(root);
            var cellSlot = container.SlotStartingAt(cell.Location.Y);

            Assert.False(
                HtmlContainerInt.FallsPast(cell.ActualBottom, container.BandOfSlot(cellSlot)),
                $"the spanning cell's box runs {cell.Location.Y:F1}..{cell.ActualBottom:F1}, "
                + $"out of band {cellSlot}");

            Assert.Equal([0, 1], SpanFragments(container).Select(f => f.Slot));
        }

        /// <summary>
        /// Closing the cell's box does not drag its content anywhere: a <c>&lt;td&gt;</c>'s used
        /// <c>vertical-align</c> is <c>middle</c>, so the alignment re-runs against the closed box, and
        /// what it must never do is push content past the foot of the band the cell is in.
        /// </summary>
        [Fact]
        public async Task ClosingTheSpanningCell_LeavesItsContentInsideItsOwnBand()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                EndingRowWouldStraddle(), pageHeight: PageHeight, margin: Margin);

            var cell = SpanningCell(root);
            var contentBottom = CssBox.GetMaximumBottom(cell, 0d);

            Assert.True(contentBottom >= cell.Location.Y,
                $"the alignment moved the cell's content to {contentBottom:F1}, above its own top "
                + $"of {cell.Location.Y:F1}");

            Assert.False(
                HtmlContainerInt.FallsPast(contentBottom, container.BandOfSlot(0)),
                $"the cell's content reaches {contentBottom:F1}, out of band 0");
        }

        /// <summary>
        /// A spanning cell is aligned in its box <b>once</b>, however many ways the row that ends the span
        /// reaches it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The row aligns <c>row.Boxes ∪ boxesThatEndOnRow</c>, and a spanning cell is in both: once as a
        /// <c>CssSpacingBox</c>'s <c>ExtendedBox</c> and once as itself.
        /// <c>CssLayoutEngine.ApplyCellVerticalAlignment</c> <b>offsets a subtree rather than assigning a
        /// position</b>, so applying it twice is not a no-op — under the <c>vertical-align: middle</c> a
        /// <c>&lt;td&gt;</c> uses by default, the second pass adds a further quarter of the leftover room.
        /// </para>
        /// <para>
        /// Stated as an equality between two documents that differ only in which column the spanning cell
        /// is in, rather than against a number, because <b>which one has a spacer at all is the variable</b>:
        /// <c>CssLayoutEngineTable.InsertEmptyBoxes</c> walks only the later row's existing cells, so a
        /// spanning cell in the last column produces no spacer anywhere and is reached once. On <c>main</c>
        /// the two documents disagree by 24.75pt on a 99pt leftover.
        /// </para>
        /// </remarks>
        [Fact]
        public async Task ASpanningCellReachedTwice_IsAlignedOnce()
        {
            static string Table(bool spanFirst) => LayoutHarness.Wrap(
                "<table style='width:100%;border-collapse:collapse'><tr>"
                + (spanFirst
                    ? "<td rowspan='2'><div id='probe' style='height:20pt'>mid</div></td>"
                      + "<td><div style='height:60pt'>row 0</div></td>"
                    : "<td><div style='height:60pt'>row 0</div></td>"
                      + "<td rowspan='2'><div id='probe' style='height:20pt'>mid</div></td>")
                + "</tr><tr><td><div style='height:60pt'>row 1</div></td></tr></table>");

            static async Task<(double CellTop, double ContentTop)> ProbeAsync(bool spanFirst)
            {
                var (root, _) = await LayoutHarness.LayoutAsync(
                    Table(spanFirst), pageHeight: 800, margin: Margin);

                var probe = LayoutHarness.Descendants(root)
                    .First(b => b.HtmlTag?.TryGetAttribute("id") == "probe");

                return (probe.ParentBox!.Location.Y, probe.Location.Y);
            }

            var reachedTwice = await ProbeAsync(spanFirst: true);
            var reachedOnce = await ProbeAsync(spanFirst: false);

            // Same geometry either way, so the content sits the same distance into the cell.
            Assert.Equal(
                reachedOnce.ContentTop - reachedOnce.CellTop,
                reachedTwice.ContentTop - reachedTwice.CellTop,
                0.01);
        }

        /// <summary>
        /// Where the spanning cell's own content is what reaches lowest on its band, the table's slice
        /// follows it — the cell is not closed above its content, and the table's bottom border is not
        /// then drawn across the cell.
        /// </summary>
        /// <remarks>
        /// The row cursor's <c>MaxBottom</c> never counts a spanning cell before the row that ends it, so a
        /// tall cell opened by a <i>short</i> row closes below the `PageBreakBottoms` record its own table
        /// wrote — and `FragmentPainter` clips the bottom border to that record. Closing the cell at the
        /// record instead would cut its content, which is the one thing the close may never do.
        /// </remarks>
        [Fact]
        public async Task WhereTheSpanningCellReachesLowest_TheTablesSliceFollowsIt()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(@"
                    <table style='width:100%;border-collapse:collapse'>
                      <tr><td colspan='2'><div style='height:40pt'>row 0</div></td></tr>
                      <tr><td colspan='2'><div style='height:40pt'>row 1</div></td></tr>
                      <tr><td colspan='2'><div style='height:40pt'>row 2</div></td></tr>
                      <tr><td colspan='2'><div style='height:40pt'>row 3</div></td></tr>
                      <tr><td><div style='height:20pt'>short</div></td>
                          <td id='span' rowspan='2'><div style='height:90pt'>tall span</div></td></tr>
                      <tr><td><div style='height:120pt'>row 5</div></td></tr>
                    </table>"),
                pageHeight: PageHeight, margin: Margin);

            var cell = SpanningCell(root);
            var table = LayoutHarness.Descendants(root).First(b => b.Display == CssConstants.Table);
            var rows = RowsOf(root);

            // The fixture only asserts anything if the cell really does outreach the row that opened it.
            Assert.True(cell.ActualBottom > rows[4].ActualBottom,
                $"the spanning cell ends at {cell.ActualBottom:F1}, not past its opening row's "
                + $"{rows[4].ActualBottom:F1}");

            Assert.True(cell.ActualBottom >= CssBox.GetMaximumBottom(cell, 0d) - 0.01,
                "the cell was closed above its own content");

            Assert.True(table.PageBreakBottoms![0] >= cell.ActualBottom - 0.01,
                $"the table's slice on band 0 ends at {table.PageBreakBottoms[0]:F1}, above the spanning "
                + $"cell's {cell.ActualBottom:F1} — its bottom border would be drawn across the cell");
        }

        /// <summary>
        /// A row can end more than one span, and each of those cells is closed. The guard that stops a
        /// single cell being written to twice must not stop a <i>second</i> cell being written to at all.
        /// </summary>
        [Fact]
        public async Task ARowEndingTwoSpans_ClosesBothOfThem()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(@"
                    <table style='width:100%;border-collapse:collapse'>
                      <tr><td colspan='3'><div style='height:40pt'>row 0</div></td></tr>
                      <tr><td colspan='3'><div style='height:40pt'>row 1</div></td></tr>
                      <tr><td colspan='3'><div style='height:40pt'>row 2</div></td></tr>
                      <tr><td colspan='3'><div style='height:40pt'>row 3</div></td></tr>
                      <tr><td id='left' rowspan='2'>spans 4-5</td>
                          <td id='right' rowspan='2'>also spans 4-5</td>
                          <td><div style='height:40pt'>row 4</div></td></tr>
                      <tr><td><div style='height:120pt'>row 5</div></td></tr>
                    </table>"),
                pageHeight: PageHeight, margin: Margin);

            var cells = LayoutHarness.Descendants(root)
                .Where(b => b.HtmlTag?.TryGetAttribute("id") is "left" or "right")
                .ToList();

            Assert.Equal(2, cells.Count);

            foreach (var cell in cells)
            {
                var cellSlot = container.SlotStartingAt(cell.Location.Y);

                Assert.False(
                    HtmlContainerInt.FallsPast(cell.ActualBottom, container.BandOfSlot(cellSlot)),
                    $"a spanning cell runs {cell.Location.Y:F1}..{cell.ActualBottom:F1}, out of band {cellSlot}");
            }

            // Both closed at the same place, which is what says neither was skipped by the other's guard.
            Assert.Equal(cells[0].ActualBottom, cells[1].ActualBottom, 0.01);
        }

        /// <summary>
        /// A spanning cell whose <i>content</i> reaches past the band it was placed in is genuinely
        /// fragmented across every band that content occupies, not merely closed at a single band's foot
        /// and stretched the rest of the way to the row that ends the span
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/521">issue #521</see>).
        /// </summary>
        /// <remarks>
        /// Before this, <c>CloseSpanningCell</c> declined to close the box at all wherever the cell's own
        /// content also reached past the band it opened in, out of a concern that stating continuation
        /// geometry over a band the content already occupies would displace it — measured at the time as
        /// ~100 unclaimed words. That concern no longer holds: <c>FragmentEmitter.ShellIn</c> is consulted
        /// only for a band where the real per-pass walk found nothing at all, so it can never stand in for
        /// one the cell's content already occupies, and the cell's content already stops and resumes across
        /// bands like any other box's, through the table's ordinary per-cell continuation
        /// (<see cref="TableRowCursor.UnfinishedCells"/>/<see cref="TableRowCursor.Continuation"/>) — the
        /// fix only had to stop the decline itself from leaving one giant stretched box behind.
        /// </remarks>
        [Fact]
        public async Task ASpanningCellWhoseContentOverflowsItsBand_IsFragmentedAcrossEveryBandItOccupies()
        {
            var words = string.Join(" ", Enumerable.Range(0, 160).Select(i => $"word{i:0000}"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:150pt'>"
                    + $"<tr><td id='span' rowspan='2'>{words}</td><td>a</td></tr>"
                    + "<tr><td>b</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var cell = SpanningCell(root);
            var contentBottom = CssBox.GetMaximumBottom(cell, 0d);

            Assert.True(
                HtmlContainerInt.FallsPast(contentBottom, container.BandOfSlot(0)),
                "fixture does not overflow its band, so it asserts nothing");

            // The box's own close never goes above its content...
            Assert.True(cell.ActualBottom >= contentBottom - 0.01,
                $"the cell's box closes at {cell.ActualBottom:F1}, above its content's {contentBottom:F1}");

            // ...but it is no longer one box stretched across every band the content and the span both
            // touch: it produces a real fragment - not merely a content-free continuation shell - in more
            // than one of them, and no word the document authored goes unclaimed by any of them. The
            // cell's own words sit one level down, under the anonymous line boxes SpanFragments' own
            // top-level entry does not include, so this walks each span-tagged fragment's whole subtree.
            var fragments = SpanFragments(container);
            Assert.True(fragments.Count > 1,
                "the spanning cell produced only one fragment, so it is still one stretched box");

            var wordsInFragments = fragments.SelectMany(f => Flatten(f.Fragment)).SelectMany(f => f.Words).ToList();
            Assert.True(wordsInFragments.Count > 0,
                "no fragment held any of the cell's real words - it is all stated continuation shells");

            var claimed = new HashSet<CssRect>(
                container.FragmentTree!.Fragmentainers
                    .SelectMany(f => Flatten(f.Root))
                    .SelectMany(f => f.Words)
                    .Select(w => w.Word));

            // cell.Words itself is empty - a <td> with text authors its words onto an anonymous child
            // line box, not directly onto the cell - so the census has to walk the cell's own subtree
            // rather than asking the cell's own (always-empty) Words collection.
            var authoredWords = LayoutHarness.Descendants(cell).SelectMany(b => b.Words).ToList();
            Assert.NotEmpty(authoredWords);
            Assert.All(authoredWords, word => Assert.Contains(word, claimed));

            // Deliberately not asserted here: which of these fragments' own top/bottom edges are break
            // edges rather than the box's own. FragmentEmitter.RecordChain only walks a BlockBreakToken's
            // linear ChildToken chain, so it never marks a TableBreakToken's per-cell continuations
            // (TableRowCursor.UnfinishedCells) - a cell whose own content keeps producing genuinely new
            // fragments (as against a stated shell, which ResumesAnEarlierFragment/ContinuesIntoALaterFragment
            // also recognize) reports every fragment as owning both its own edges. That is a pre-existing
            // gap independent of rowspan or this fix - confirmed on a plain, non-rowspan multi-page <td> -
            // and is filed and out of scope here: see
            // https://github.com/jhaygood86/PeachPDF/issues/590.
        }

        /// <summary>
        /// A spanning cell whose own content overflows all the way into the very band the row ending the
        /// span lands in does not close short of that row's own bottom, even though the content itself
        /// stops higher up in the same band.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the shape <see cref="ASpanningCellWhoseContentOverflowsItsBand_IsFragmentedAcrossEveryBandItOccupies"/>
        /// does not cover: there, the row ending the span lands in a band strictly after the one the
        /// cell's content finishes in, so every band between the two is a stated continuation shell
        /// (<c>StateSpanningCellContinuation</c>) reaching from that band's own content-top down to
        /// <c>rowMaxBottom</c>. Where the content instead finishes in <i>the same band</i> the span ends
        /// in, there is no later, empty band left to state one over — <c>FragmentEmitter.ShellIn</c> never
        /// stands in for a band that already holds real content anywhere for the box, so a shell stated
        /// there would simply be discarded, and the close has to be the real one:
        /// <c>Math.Max(rowMaxBottom, contentBottom)</c>, not <c>Math.Max(PageBottomOf(cellSlot), ...)</c>
        /// as it is everywhere the content and the span's end are on different bands.
        /// </para>
        /// <para>
        /// Found rendering <c>paged_media_table_rowspan_break</c>'s own Q4 fixture: the first version of
        /// this fix closed the cell at its own content's bottom in this exact shape, which left the
        /// rowspan's remaining rows in the same band with no tint or border at all — a real, visible
        /// regression from the pre-fix stretched box, which at least covered every row the span still
        /// named.
        /// </para>
        /// </remarks>
        [Fact]
        public async Task ASpanningCellWhoseContentFinishesInTheSameBandTheSpanEndsIn_ClosesAtTheRowsOwnBottom()
        {
            var words = string.Join(" ", Enumerable.Range(0, 40).Select(i => $"word{i:0000}"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:150pt'>"
                    + $"<tr><td id='span' rowspan='3'>{words}</td><td>a</td></tr>"
                    + "<tr><td>b</td></tr>"
                    + "<tr><td><div style='height:100pt'>c</div></td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var cell = SpanningCell(root);
            var contentBottom = CssBox.GetMaximumBottom(cell, 0d);
            var cellSlot = container.SlotStartingAt(cell.Location.Y);
            var contentSlot = container.SlotEndingAt(contentBottom);
            var rows = RowsOf(root);
            var endingRowSlot = container.SlotStartingAt(rows[2].Location.Y);

            Assert.True(cellSlot < contentSlot, "fixture does not overflow its opening band, so it asserts nothing");
            Assert.Equal(contentSlot, endingRowSlot);

            // Precise, not just a lower bound: the row's own natural bottom (100pt of content) is smaller
            // than the spanning cell's own content here, so a formula that used rowMaxBottom alone (the
            // pre-fix stretch, or a naive same-band close) and one that used Math.Max(rowMaxBottom,
            // contentBottom) would disagree, and only the latter is correct.
            var expected = Math.Max(rows[2].ActualBottom, contentBottom);
            Assert.Equal(expected, cell.ActualBottom, 0.01);

            // And every other *real* cell in the row the span ends on closes level with it too - a
            // rowspan cell taller than the rows it spans grows those rows in ordinary (unpaginated) table
            // layout, and TableRowCursor.MaxBottom deliberately excluding the spanning cell from that
            // calculation (for the straddle correction's own sake) must not leave the row's other cells
            // behind. rows[2].Boxes also holds the CssSpacingBox placeholder standing in for the spanning
            // cell itself, whose own geometry is a separate bookkeeping concern from column alignment and
            // is not this fixture's subject, so only the row's own non-placeholder cells are checked.
            var realSiblings = rows[2].Boxes.Where(b => b is not CssSpacingBox).ToList();
            Assert.NotEmpty(realSiblings);
            foreach (var sibling in realSiblings)
            {
                Assert.Equal(cell.ActualBottom, sibling.ActualBottom, 0.01);
            }
        }

        /// <summary>
        /// A spanning cell whose own content finishes several bands <i>before</i> the row ending the span
        /// closes at the band its content actually ends in, not at the band it opened in - the content may
        /// now reach past more than one band beyond where the span itself began.
        /// </summary>
        /// <remarks>
        /// Before this, the close was always anchored to <c>cellSlot</c> (the band the box opened in),
        /// which is only correct when the content fits inside that same band - #511's original, narrower
        /// case. Where the content reaches further, anchoring to <c>cellSlot</c> still clamps the close up
        /// to <c>contentBottom</c> (never above the content), but the <i>table's own slice-bottom record</i>
        /// it raises is keyed to the wrong band - <c>cellSlot</c>, several bands short of where the content
        /// and the table's own border actually belong.
        /// </remarks>
        [Fact]
        public async Task ASpanningCellWhoseContentFinishesSeveralBandsBeforeTheSpanEnds_ClosesAtItsOwnBand()
        {
            var words = string.Join(" ", Enumerable.Range(0, 90).Select(i => $"word{i:0000}"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:150pt'>"
                    + $"<tr><td id='span' rowspan='3'>{words}</td><td>a</td></tr>"
                    + "<tr><td><div style='height:240pt'>b</div></td></tr>"
                    + "<tr><td><div style='height:240pt'>c</div></td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var cell = SpanningCell(root);
            var contentBottom = CssBox.GetMaximumBottom(cell, 0d);
            var cellSlot = container.SlotStartingAt(cell.Location.Y);
            var contentSlot = container.SlotEndingAt(contentBottom);
            var rows = RowsOf(root);
            var endingRowSlot = container.SlotStartingAt(rows[2].Location.Y);

            Assert.True(cellSlot < contentSlot,
                "fixture does not overflow past the band it opened in, so it asserts nothing");
            Assert.True(contentSlot < endingRowSlot,
                "fixture's content already reaches the ending row's own band - the other test covers that "
                + "shape, this one wants a genuinely earlier content band");

            // The close is anchored to contentSlot's own band-foot, clamped up to contentBottom - and is
            // therefore far below the ending row's own (much lower) bottom, which a stretched box, or one
            // wrongly anchored to cellSlot's much-earlier band, would not be.
            var band = container.BandOfSlot(contentSlot);
            Assert.True(cell.ActualBottom <= band.Bottom + 0.5,
                $"the cell closed at {cell.ActualBottom:F1}, past contentSlot {contentSlot}'s own band foot "
                + $"of {band.Bottom:F1}");
            Assert.True(cell.ActualBottom >= contentBottom - 0.01,
                $"the cell's box closes at {cell.ActualBottom:F1}, above its content's {contentBottom:F1}");
            Assert.True(cell.ActualBottom < rows[2].ActualBottom - 1,
                $"the cell closed at {cell.ActualBottom:F1}, at or past the ending row's own bottom of "
                + $"{rows[2].ActualBottom:F1} - it should have closed several bands earlier instead");

            // And the table's own slice-bottom record for that band follows the content, not cellSlot's.
            var table = LayoutHarness.Descendants(root).First(b => b.Display == CssConstants.Table);
            Assert.NotNull(table.PageBreakBottoms);
            Assert.True(table.PageBreakBottoms!.TryGetValue(contentSlot, out var recorded));
            Assert.True(recorded >= contentBottom - 0.01,
                $"PageBreakBottoms[{contentSlot}]={recorded:F1} is below the content's own {contentBottom:F1}");
        }

        /// <summary>
        /// A spanning cell in the table's <i>last</i> column, whose own opening row already needs more than
        /// one resumption pass to finish, still closes at the row that ends its span - it does not get
        /// stuck at its own bare content bottom.
        /// </summary>
        /// <remarks>
        /// <c>InsertEmptyBoxes</c> only creates a <see cref="CssSpacingBox"/> placeholder for a span by
        /// walking the later row's <i>existing</i> cells, so a span in the last column gets no placeholder
        /// anywhere in the tree (<see href="https://github.com/jhaygood86/PeachPDF/issues/522">issue
        /// #522</see>) - the row that ends the span reaches the cell only through
        /// <c>TableRowCursor.RowSpannedBoxes</c>, never through a <c>CssSpacingBox</c>. Every other fixture
        /// in this file spans a non-last column, so all of them reach <c>CloseSpanningCell</c> down the
        /// <c>CssSpacingBox</c> arm regardless of how the vertical-alignment loop's dispatch is ordered -
        /// which is exactly why a real regression there (<c>CloseSpanningCell</c> never being entered at
        /// all for a cell that needed more than one resumption pass, because
        /// <c>TableRowCursor.ResumedFromAnEarlierPass</c> matched a stale carried record from the cell's own
        /// opening row) passed every one of them unmodified. This fixture's last-column placement is what
        /// actually exercises the <c>RowSpannedBoxes</c>-only arm - confirmed by reverting just the
        /// vertical-alignment loop's dispatch order and watching this fail (<c>cell.ActualBottom</c> stuck
        /// at its own ~293pt content bottom rather than the ending row's ~309pt).
        /// </remarks>
        [Fact]
        public async Task ASpanningCellInTheLastColumn_StillClosesAfterMultipleResumptionPasses()
        {
            var words = string.Join(" ", Enumerable.Range(0, 40).Select(i => $"word{i:0000}"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:150pt'>"
                    + $"<tr><td>a</td><td id='span' rowspan='3'>{words}</td></tr>"
                    + "<tr><td>b</td></tr>"
                    + "<tr><td>c</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var cell = SpanningCell(root);
            var contentBottom = CssBox.GetMaximumBottom(cell, 0d);
            var cellSlot = container.SlotStartingAt(cell.Location.Y);
            var contentSlot = container.SlotEndingAt(contentBottom);
            var rows = RowsOf(root);
            var endingRowSlot = container.SlotStartingAt(rows[2].Location.Y);

            Assert.True(cellSlot < contentSlot, "fixture does not overflow its opening band, so it asserts nothing");
            Assert.Equal(contentSlot, endingRowSlot);

            // Without the dispatch fix, CloseSpanningCell is never entered for this cell at all, and it
            // closes at its own bare content bottom instead - well above the ending row's own bottom.
            var expected = Math.Max(rows[2].ActualBottom, contentBottom);
            Assert.Equal(expected, cell.ActualBottom, 0.01);
        }

        /// <summary>
        /// A spanning cell's content overflowing into the row that ends its span raises that row's own,
        /// ordinary sibling cell to match - not just the spanning cell itself.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>TableRowCursor.MaxBottom</c> deliberately excludes a spanning cell from a row's height (so the
        /// straddle correction can still move the row), so without <c>LayoutBodyRow</c>'s pre-pass, the
        /// row's own ordinary cell would close at its own bare one-line height rather than being raised to
        /// meet the spanning cell.
        /// </para>
        /// <para>
        /// The sibling has to be genuinely <i>short</i> on its own for this to discriminate anything. A
        /// first attempt at this fixture gave the ending row's sibling a 100pt <c>&lt;div&gt;</c>, meant to
        /// be shorter than the spanning cell's own overflow - but that row's own top already lands past the
        /// first page (the spanning cell's overflow pushes it there), so a 100pt div measured from that
        /// already-late Y landed <i>past</i> the spanning cell's own content bottom by coincidence, passing
        /// regardless of whether the pre-pass ran at all. A bare one-line cell has no such accidental
        /// height. Confirmed by disabling just the pre-pass (leaving the dispatch fix and everything else
        /// alone) and watching the sibling assertion below fail by roughly 115pt.
        /// </para>
        /// </remarks>
        [Fact]
        public async Task ASpanningCellsOverflowIntoTheEndingRow_RaisesItsShortSiblingToMatch()
        {
            var words = string.Join(" ", Enumerable.Range(0, 60).Select(i => $"word{i:0000}"));

            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:150pt'>"
                    + $"<tr><td>a</td><td id='span' rowspan='3'>{words}</td></tr>"
                    + "<tr><td>b</td></tr>"
                    + "<tr><td>c</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var cell = SpanningCell(root);
            var rows = RowsOf(root);

            // A single short line, reachable only via RowSpannedBoxes here (no CssSpacingBox for the
            // earlier per-cell tracking loop to have already folded the spanning cell's height into
            // rowMaxBottom through), so nothing but the pre-pass raises it to meet the spanning cell.
            var realSiblings = rows[2].Boxes.Where(b => b is not CssSpacingBox).ToList();
            Assert.NotEmpty(realSiblings);
            foreach (var sibling in realSiblings)
            {
                Assert.Equal(cell.ActualBottom, sibling.ActualBottom, 0.01);
            }
        }
    }
}

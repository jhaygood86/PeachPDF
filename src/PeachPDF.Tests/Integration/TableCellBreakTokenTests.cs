using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The table row loop's consumer for a cell that did not finish
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/390">#390</see> stage 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cell's own resumption record is readable for exactly one instant — between its layout returning
    /// and the next layout of the same box clearing it in <c>CssBox.BeginLayoutPass</c> — and until now
    /// the row loop spent that instant reading only <c>ActualBottom</c>. It now asks the cell whether it
    /// finished, and records the answer on the cursor, which <c>LayoutCells</c> publishes on the table as
    /// <see cref="CssBox.TableContinuation"/>.
    /// </para>
    /// <para>
    /// <b>Nothing acts on the answer yet, and the answer is always "yes, it finished".</b> The engine runs
    /// inside <c>CssBox.LayoutMonolithicContent</c>, which detaches the fragmentainer, so no descendant of
    /// a cell has one to run out of. Both halves are asserted here: that a real paginating table records
    /// nothing (which is what makes this step behaviour-neutral), and that the recorder is nevertheless
    /// wired to the right question rather than being dead code.
    /// </para>
    /// </remarks>
    public class TableCellBreakTokenTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        private static string Words(int count) =>
            string.Join(" ", Enumerable.Range(0, count).Select(i => $"word{i:0000}"));

        private static CssBox TableOf(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.Display == CssConstants.Table);

        private static List<CssBox> CellsOf(CssBox root) =>
            LayoutHarness.Descendants(root).Where(b => b.Display == CssConstants.TableCell).ToList();

        // ─── The record is published, and empty, for a table that really does paginate ───────────

        /// <summary>
        /// Every table shape that paginates today records no unfinished cell, because the engine's own
        /// fragmentainer is detached while it runs. This is the measurement the step rests on: the row
        /// loop's new question cannot change any of these documents, whatever answer it eventually acts
        /// on. Lifting the monolithic gate makes eight of these ten shapes record exactly one.
        /// </summary>
        [Theory]
        [InlineData("<table style='width:150pt'><tr><td>{W}</td></tr></table>")]
        [InlineData("<table style='width:150pt'><tr><td><p>{W}</p></td></tr></table>")]
        [InlineData("<table style='width:150pt'><tr><td>{W}</td></tr><tr><td>tail</td></tr></table>")]
        [InlineData("<table style='width:150pt'><thead><tr><th>H</th></tr></thead>"
                    + "<tbody><tr><td>{W}</td></tr></tbody></table>")]
        [InlineData("<table style='width:150pt'><tfoot><tr><td>F</td></tr></tfoot>"
                    + "<tbody><tr><td>{W}</td></tr></tbody></table>")]
        [InlineData("<table style='width:150pt'><tr><td><div style='column-count:2'>{W}</div></td></tr></table>")]
        [InlineData("<table style='width:150pt'><tr><td><div style='display:flex;flex-wrap:wrap'>{W}</div>"
                    + "</td></tr></table>")]
        [InlineData("<table style='width:150pt'><tr><td><div style='display:grid'>{W}</div></td></tr></table>")]
        [InlineData("<table style='width:150pt'><tr><td rowspan='2'>{W}</td><td>a</td></tr>"
                    + "<tr><td>b</td></tr></table>")]
        [InlineData("<div style='column-count:2'><table><tr><td>{W}</td></tr></table></div>")]
        public async Task APaginatingTable_RecordsNoUnfinishedCell(string markup)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(markup.Replace("{W}", Words(244))),
                pageHeight: PageHeight, margin: Margin);

            var table = TableOf(root);

            // The table really is longer than one band - otherwise this asserts nothing.
            Assert.True(table.ActualBottom - table.Location.Y > PageHeight - 2 * Margin,
                $"fixture does not paginate: table spans {table.ActualBottom - table.Location.Y:F1}pt");

            Assert.Null(table.TableContinuation);
            Assert.All(CellsOf(root), cell => Assert.Null(cell.PendingBreakToken));
        }

        /// <summary>
        /// A nested table publishes its own record rather than sharing the outer one's, since each
        /// <c>LayoutCells</c> call has its own cursor.
        /// </summary>
        [Fact]
        public async Task ANestedTable_PublishesItsOwnRecord()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table style='width:150pt'><tr><td><table><tr><td>"
                                   + Words(244) + "</td></tr></table></td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var tables = LayoutHarness.Descendants(root)
                .Where(b => b.Display == CssConstants.Table).ToList();

            Assert.Equal(2, tables.Count);
            Assert.All(tables, t => Assert.Null(t.TableContinuation));
        }

        /// <summary>
        /// A second layout of the same table replaces the record rather than adding to it — the same
        /// obligation <c>PageBreakBottoms</c> has, and the one a resumed pass will break first.
        /// </summary>
        [Fact]
        public async Task LayingTheSameTableOutTwice_DoesNotAccumulateTheRecord()
        {
            var counts = await LayoutHarness.LayoutRepeatedlyAsync(
                LayoutHarness.Wrap("<table style='width:150pt'><tr><td>" + Words(244) + "</td></tr>"
                                   + "<tr><td>tail</td></tr></table>"),
                passes: 2,
                snapshot: (root, _) => TableOf(root).TableContinuation?.UnfinishedCells.Count ?? 0,
                pageHeight: PageHeight, margin: Margin);

            Assert.Equal([0, 0], counts);
        }

        // ─── The recorder is wired to the right question ─────────────────────────────────────────

        /// <summary>
        /// The half that cannot be reached from markup while the gate is down: given a cell that says it
        /// stopped, the cursor records it, keyed by the row the loop was placing and carrying the cell's
        /// own token unchanged.
        /// </summary>
        [Fact]
        public async Task ACellThatSaysItStopped_IsRecordedAgainstTheRowBeingPlaced()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table><tr><td id='a'>a</td><td id='b'>b</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            var cursor = new TableRowCursor(top: 0, maxRight: 0, slotIndex: 0) { RowIndex = 3 };

            var token = new BlockBreakToken(b, ResumeSlotIndex: 1, ResumeChildIndex: 0, ChildToken: null,
                IsBreakBefore: false, ResumeTopOverride: null);
            b.SetPendingBreakToken(token);

            cursor.RecordIfUnfinished(a);
            cursor.RecordIfUnfinished(b);

            var recorded = Assert.Single(cursor.UnfinishedCells);
            Assert.Equal(3, recorded.RowIndex);
            Assert.Same(b, recorded.Cell);
            Assert.Same(token, recorded.Token);
        }

        /// <summary>
        /// Several cells of one row can stop, and each is recorded in the order the row loop placed them.
        /// That is what <see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3
        /// §6.1</see> asks for: a fragmented row fits as much as it can in each cell independently, and
        /// the next fragment starts each cell where <i>that</i> cell stopped — so the record is per cell,
        /// and only the cells that actually stopped are in it.
        /// </summary>
        [Fact]
        public async Task EveryCellOfARowThatStops_IsRecorded()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table><tr><td id='a'>a</td><td id='b'>b</td><td id='c'>c</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var cells = new[] { "a", "b", "c" }.Select(id => LayoutHarness.FindById(root, id)!).ToList();
            var cursor = new TableRowCursor(top: 0, maxRight: 0, slotIndex: 0) { RowIndex = 0 };

            foreach (var cell in cells)
            {
                cell.SetPendingBreakToken(new InlineBreakToken(cell, ResumeSlotIndex: 1, ResumePath: [],
                    ResumeWordIndex: 0, CompletedLineCount: 1));
                cursor.RecordIfUnfinished(cell);
            }

            Assert.Equal(cells, cursor.UnfinishedCells.Select(u => u.Cell));
        }

        /// <summary>
        /// A cell that stops during a <i>real</i> table layout is recorded on the table. This is the only
        /// thing that pins the call site itself: while the gate is down no markup can produce a cell that
        /// stops, so the row loop's question is asked of a cell that answers it by construction.
        /// </summary>
        [Fact]
        public async Task ACellThatStopsDuringLayout_IsPublishedOnTheTable()
        {
            StoppingCell? stopping = null;

            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table><tr><td>first</td><td id='anchor'>anchor</td></tr>"
                                   + "<tr><td>a</td><td>b</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin,
                prepare: tree =>
                {
                    var anchor = LayoutHarness.FindById(tree, "anchor")!;
                    var row = anchor.ParentBox!;

                    stopping = new StoppingCell(row);

                    // Constructed at the end of the row, then moved into the anchor's place - and the
                    // anchor displaced, since a row's cells are its columns. CssBox.ParentBox's setter
                    // appends, which is why this is a move rather than an insert. The displaced anchor
                    // keeps ParentBox == row while absent from row.Boxes; nothing walks up to it, so it
                    // is simply unreachable for the rest of this layout.
                    row.Boxes.Remove(stopping);
                    row.Boxes[row.Boxes.IndexOf(anchor)] = stopping;
                });

            Assert.NotNull(stopping);

            var recorded = Assert.Single(TableOf(root).TableContinuation!.UnfinishedCells);
            Assert.Same(stopping, recorded.Cell);
            Assert.Same(stopping.Record, recorded.Token);

            // The row the loop was placing when it asked, not the cell's position within it.
            Assert.Equal(0, recorded.RowIndex);
        }

        /// <summary>
        /// A table cell that reports it could not finish, so the row loop's question has an answer while
        /// the engine still runs with the fragmentainer detached and no real cell can give one.
        /// </summary>
        /// <remarks>
        /// It overrides <c>PerformLayoutImp</c> wholesale rather than extending it, which means it never
        /// runs <c>BeginLayoutPass</c> — fine for a stub with no geometry, and necessary here, since that
        /// is the method that would clear the record again on a second pass.
        /// </remarks>
        private sealed class StoppingCell : CssBox
        {
            internal StoppingCell(CssBox row) : base(row, null)
            {
                InheritStyle(row, everything: true);
                Display = CssConstants.TableCell;
                Record = new InlineBreakToken(this, ResumeSlotIndex: 1, ResumePath: [],
                    ResumeWordIndex: 0, CompletedLineCount: 1);
            }

            /// <summary>The record this cell hands back, kept so the test can assert it travelled.</summary>
            internal BreakToken Record { get; }

            protected override ValueTask PerformLayoutImp(RGraphics g)
            {
                SetPendingBreakToken(Record);
                return default;
            }
        }

        /// <summary>
        /// A <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> measurement cursor keeps its own record. Those rows
        /// are laid out once to learn their height and then repeated by proxy, so where one of them
        /// stopped says nothing about where the body has to resume — and a resumed body pass that
        /// inherited it would resume into a row group that is not in the tree any more.
        /// </summary>
        [Fact]
        public async Task ARowGroupMeasurementCursor_KeepsItsOwnRecord()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table><tr><td id='a'>a</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var a = LayoutHarness.FindById(root, "a")!;
            a.SetPendingBreakToken(new InlineBreakToken(a, ResumeSlotIndex: 1, ResumePath: [],
                ResumeWordIndex: 0, CompletedLineCount: 1));

            var body = new TableRowCursor(top: 0, maxRight: 0, slotIndex: 0) { RowIndex = 0 };
            var measurement = body.ForRowGroupMeasurement(top: 0);

            measurement.RecordIfUnfinished(a);

            Assert.Single(measurement.UnfinishedCells);
            Assert.Empty(body.UnfinishedCells);
            Assert.Equal(-1, measurement.UnfinishedCells[0].RowIndex);
        }
    }
}

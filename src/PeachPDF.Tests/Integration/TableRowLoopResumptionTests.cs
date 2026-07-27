using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The table row loop stopping where a cell stopped, and a later pass continuing it from there
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/390">#390</see> stage 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>CssLayoutEngineTable</c>'s row loop placed every body row whatever it was told, and a run
    /// continuing an earlier one started at row 0 with an empty cursor. It now stops at the first row a
    /// cell did not finish in and publishes where it stopped as <see cref="CssBox.TableContinuation"/>,
    /// and a run handed that record re-enters that row with the earlier pass's cursor — its slot, its
    /// widest edge, its rowspan bookkeeping and its per-cell records — rather than a fresh one.
    /// </para>
    /// <para>
    /// <b>The record is deliberately not the table's own <c>PendingBreakToken</c>.</b> Setting that is what
    /// makes a table resumable from outside, and unlike everything here it is reachable from real markup,
    /// so it is a separate and visible step. What is here is neutral: the loop's stop is reached by exactly
    /// one fixture in the whole suite and there it stops at the last row, and a continuation is reachable
    /// only the way these tests reach it — by running the engine again with a record in hand.
    /// </para>
    /// </remarks>
    public class TableRowLoopResumptionTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        private static CssBox TableOf(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.Display == CssConstants.Table);

        /// <summary>
        /// Runs the engine the way production does — inside the fragmentainer detach
        /// <c>CssBox.LayoutMonolithicContent</c> wraps it in, without which a cell would see a
        /// fragmentainer the real call never gives it.
        /// </summary>
        private static async Task RunEngine(
            RGraphics g, HtmlContainerInt container, CssBox table, BreakToken? resume)
        {
            var previous = container.DetachFragmentainer();

            try
            {
                await CssLayoutEngineTable.PerformLayout(g, table, resume);
            }
            finally
            {
                container.RestoreFragmentainer(previous);
            }
        }

        private static async Task WithALaidOutTable(
            string markup, Func<CssBox, HtmlContainerInt, RGraphics, Task> continuePass) =>
            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(markup), pageHeight: PageHeight, margin: Margin,
                after: (root, container, g) => continuePass(TableOf(root), container, g));

        private static List<CssBox> BodyRowsOf(CssBox table) =>
            LayoutHarness.Descendants(table)
                .Where(b => b.Display == CssConstants.TableRow)
                .ToList();

        private static string RowsTable(int rows) =>
            "<table style='width:150pt;font-size:10pt'>"
            + string.Concat(Enumerable.Range(0, rows).Select(i => $"<tr><td id='c{i}'>row {i}</td></tr>"))
            + "</table>";

        /// <summary>
        /// Replaces the cell of body row <paramref name="rowIndex"/> with one that reports it could not
        /// finish. A row's cells are its columns and <c>CssBox.ParentBox</c>'s setter appends, so the
        /// stopping cell is moved into the anchor's place rather than inserted beside it.
        /// </summary>
        private static StoppingCell StopRow(CssBox root, int rowIndex)
        {
            var anchor = LayoutHarness.FindById(root, $"c{rowIndex}")!;
            var row = anchor.ParentBox!;
            var stopping = new StoppingCell(row);

            row.Boxes.Remove(stopping);
            row.Boxes[row.Boxes.IndexOf(anchor)] = stopping;
            return stopping;
        }

        /// <summary>
        /// A cell that reports it could not finish, so the row loop's stop has something to stop on while
        /// the monolithic gate is down and no cell of these fixtures can produce one.
        /// </summary>
        /// <remarks>
        /// It overrides <c>PerformLayoutImp</c> wholesale, as the doubles in
        /// <see cref="TableCellBreakTokenTests"/> and <see cref="TableOncePerTableTests"/> do and for the
        /// same reason: that is the method that would otherwise clear the record again.
        /// </remarks>
        private sealed class StoppingCell : CssBox
        {
            internal StoppingCell(CssBox row) : base(row, null)
            {
                InheritStyle(row, everything: true);
                Display = CssConstants.TableCell;
                Record = new InlineBreakToken(this, ResumeSlotIndex: 2, ResumePath: [],
                    ResumeWordIndex: 4, CompletedLineCount: 1);
            }

            /// <summary>The record this cell hands back, kept so a test can assert it travelled.</summary>
            internal BreakToken Record { get; }

            protected override ValueTask PerformLayoutImp(RGraphics g)
            {
                SetPendingBreakToken(Record);
                return default;
            }
        }

        // ─── The row loop stops where a cell stopped ─────────────────────────────────────────────

        /// <summary>
        /// The rows after the one that did not finish belong to the fragmentainer the table resumes in, so
        /// this pass does not place them: laying them out here would put them above content that has not
        /// been placed yet.
        /// </summary>
        [Fact]
        public async Task ARowThatDidNotFinish_StopsTheRowLoop()
        {
            CssBox? root = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(4)), pageHeight: PageHeight, margin: Margin,
                prepare: tree => { root = tree; StopRow(tree, 1); });

            var rows = BodyRowsOf(TableOf(root!));
            Assert.Equal(4, rows.Count);

            Assert.True(rows[0].ActualBottom > rows[0].Location.Y, "row 0 was not placed");
            Assert.All(new[] { rows[2], rows[3] },
                row => Assert.All(row.Boxes, cell => Assert.Equal(RPoint.Empty, cell.Location)));
        }

        /// <summary>
        /// The control: the same table with nothing stopping places every row, so "rows 2 and 3 sit at the
        /// origin" above is a fact about the stop rather than about how these rows are found.
        /// </summary>
        [Fact]
        public async Task WithNoRowStopping_EveryRowIsPlaced()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(4)), pageHeight: PageHeight, margin: Margin);

            Assert.All(BodyRowsOf(TableOf(root)),
                row => Assert.All(row.Boxes, cell => Assert.NotEqual(RPoint.Empty, cell.Location)));
        }

        /// <summary>
        /// The row that stopped is still placed — only some of its cells stopped, and the cells of a row
        /// are <see href="https://www.w3.org/TR/css-break-3/#parallel-flows">§2.1 parallel flows</see>, so
        /// the ones that finished have their whole content in this fragment
        /// (<see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see>).
        /// </summary>
        [Fact]
        public async Task TheRowThatStopped_IsItselfPlaced()
        {
            CssBox? root = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table style='width:150pt;font-size:10pt'>"
                                   + "<tr><td id='c0'>row 0</td><td id='sibling'>beside</td></tr>"
                                   + "<tr><td id='c1'>row 1</td><td>beside</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin,
                prepare: tree => { root = tree; StopRow(tree, 0); });

            var sibling = LayoutHarness.FindById(root!, "sibling")!;

            Assert.True(sibling.ActualBottom > sibling.Location.Y,
                "the finished cell beside the one that stopped was not placed");
            Assert.Equal(0, TableOf(root!).TableContinuation!.ResumeRowIndex);
        }

        /// <summary>
        /// What the loop hands the next pass: the row to re-enter, the slot the break actually fell in
        /// (read off the cells, never "the pass after this one"), the table's widest edge so far, and the
        /// cells that stopped with their own records.
        /// </summary>
        [Fact]
        public async Task TheRecordNamesTheRowTheSlotAndTheCells()
        {
            CssBox? root = null;
            StoppingCell? stopping = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(4)), pageHeight: PageHeight, margin: Margin,
                prepare: tree => { root = tree; stopping = StopRow(tree, 2); });

            var table = TableOf(root!);
            var record = table.TableContinuation;

            Assert.NotNull(record);
            Assert.Same(table, record.Box);
            Assert.Equal(2, record.ResumeRowIndex);

            // The cell's own record says slot 2, and a table cannot resume before the last of its
            // parallel flows does.
            Assert.Equal(2, record.ResumeSlotIndex);
            Assert.True(record.MaxRight > 0, "the record carries no width the next pass can start from");

            var unfinished = Assert.Single(record.UnfinishedCells);
            Assert.Same(stopping, unfinished.Cell);
            Assert.Same(stopping!.Record, unfinished.Token);
            Assert.Equal(2, unfinished.RowIndex);
        }

        /// <summary>
        /// The record carries the rowspan bookkeeping the loop had built when it stopped — the cells a
        /// later row still has to find, keyed by the absolute body row each ends on. A pass that recorded
        /// only where it stopped would leave the row that ends the span with nothing to look for.
        /// </summary>
        [Fact]
        public async Task TheRecordCarriesTheRowspansOpenWhenTheLoopStopped()
        {
            CssBox? root = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table style='width:150pt;font-size:10pt'>"
                                   + "<tr><td id='spanning' rowspan='2'>tall</td><td id='c0'>row 0</td></tr>"
                                   + "<tr><td id='c1'>row 1</td></tr>"
                                   + "<tr><td id='c2'>row 2</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin,
                prepare: tree => { root = tree; StopRow(tree, 0); });

            var spanning = LayoutHarness.FindById(root!, "spanning")!;
            var record = TableOf(root!).TableContinuation;

            Assert.NotNull(record);
            Assert.Equal([spanning], record.RowSpannedBoxes[1]);
        }

        /// <summary>
        /// A table whose row loop reached the end of its body rows hands the next pass nothing. Every
        /// document today is this case, the ones that really do paginate included.
        /// </summary>
        [Fact]
        public async Task ATableThatFinishedItsRows_PublishesNoRecord()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(40)), pageHeight: PageHeight, margin: Margin);

            var table = TableOf(root);
            Assert.True(table.ActualBottom - table.Location.Y > PageHeight - 2 * Margin,
                "the fixture does not paginate, so this asserts nothing");
            Assert.Null(table.TableContinuation);
        }

        // ─── A pass that stopped has not closed the table ────────────────────────────────────────

        /// <summary>
        /// A repeating <c>&lt;tfoot&gt;</c>'s closing proxy sits under the table's <i>last</i> row, so a
        /// pass that never reached the last row would put it in the middle of the table on the page it is
        /// leaving. The pass that finishes the table writes it.
        /// </summary>
        [Fact]
        public async Task APassThatStopped_DoesNotCloseTheTableWithItsFooter()
        {
            var markup = "<table style='width:150pt;font-size:10pt'><tfoot><tr><td>F</td></tr></tfoot><tbody>"
                         + string.Concat(Enumerable.Range(0, 4)
                             .Select(i => $"<tr><td id='c{i}'>row {i}</td></tr>"))
                         + "</tbody></table>";

            CssBox? stopped = null;
            CssBox? finished = null;

            await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup),
                pageHeight: PageHeight, margin: Margin,
                prepare: tree => { stopped = tree; StopRow(tree, 1); });

            var (control, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup),
                pageHeight: PageHeight, margin: Margin);
            finished = control;

            // The control is what makes this mean anything: a table that finishes does close itself
            // with the footer, from the same markup on the same page.
            Assert.Single(TableOf(finished).Boxes.OfType<CssProxyBox>());
            Assert.Empty(TableOf(stopped!).Boxes.OfType<CssProxyBox>());
        }

        /// <summary>
        /// A <c>&lt;tbody&gt;</c>'s own box spans the rows a pass has placed, not its markup: a row no
        /// pass has reached still sits at the origin, and spanning it gives the group a box starting
        /// above the table — which is exactly the degenerate bounds this step exists to avoid, arrived at
        /// from the other side.
        /// </summary>
        [Fact]
        public async Task APassThatStopped_SpansItsRowGroupOverTheRowsItPlaced()
        {
            CssBox? root = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table style='width:150pt;font-size:10pt'><tbody>"
                                   + string.Concat(Enumerable.Range(0, 4)
                                       .Select(i => $"<tr><td id='c{i}'>row {i}</td></tr>"))
                                   + "</tbody></table>"),
                pageHeight: PageHeight, margin: Margin,
                prepare: tree => { root = tree; StopRow(tree, 1); });

            var table = TableOf(root!);
            var group = table.Boxes.First(b => b.Display == CssConstants.TableRowGroup);
            var rows = BodyRowsOf(table);

            Assert.Equal(rows[0].Location.Y, group.Location.Y, 3);
            Assert.Equal(rows[1].ActualBottom, group.ActualBottom, 3);
        }

        // ─── A continuation resumes the cursor rather than restarting it ─────────────────────────

        /// <summary>
        /// A run handed the record re-enters the row that did not finish — not row 0, and not the row
        /// after it. The rows before it were emitted by the earlier pass and are left exactly where they
        /// are, and the rows it places go in the fragmentainer the record names, which is <b>not</b> the
        /// table's own content top: a table that spans fragmentainers keeps the one <c>Location</c> it
        /// was placed at, so that still names the page it began on.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public async Task AContinuation_PlacesItsRowsInTheFragmentainerTheRecordNames(int slot)
        {
            await WithALaidOutTable(RowsTable(4), async (table, container, g) =>
            {
                var before = BodyRowsOf(table).Select(r => r.Location.Y).ToList();

                await RunEngine(g, container, table,
                    Continuation(table, resumeRow: 2, resumeSlot: slot));

                var after = BodyRowsOf(table).Select(r => r.Location.Y).ToList();

                Assert.Equal(before[0], after[0], 3);
                Assert.Equal(before[1], after[1], 3);
                Assert.Equal(container.PageTopOf(slot), after[2], 3);
                Assert.True(after[3] > after[2], "row 3 does not follow the row the pass resumed at");
            });
        }

        /// <summary>
        /// The control for the test above: a continuation naming row 0 re-places every row, so "rows 0 and
        /// 1 stayed put" is a fact about the resume index rather than about a continuation leaving rows
        /// alone in general.
        /// </summary>
        [Fact]
        public async Task AContinuationThatNamesTheFirstRow_RePlacesEveryRow()
        {
            await WithALaidOutTable(RowsTable(4), async (table, container, g) =>
            {
                table.Location = table.Location with { Y = table.Location.Y + 25 };
                var before = BodyRowsOf(table).Select(r => r.Location.Y).ToList();

                await RunEngine(g, container, table, Continuation(table, resumeRow: 0));

                var after = BodyRowsOf(table).Select(r => r.Location.Y).ToList();
                Assert.All(Enumerable.Range(0, 4),
                    i => Assert.True(Math.Abs(after[i] - before[i]) > 1,
                        $"row {i} was not re-placed ({after[i]:F1} vs {before[i]:F1})"));
            });
        }

        /// <summary>
        /// A continuation starts from the widest edge the table had already reached, so its own width does
        /// not shrink to what the rows still to come happen to need.
        /// </summary>
        [Fact]
        public async Task AContinuation_StartsFromTheWidthTheEarlierPassReached()
        {
            await WithALaidOutTable(RowsTable(4), async (table, container, g) =>
            {
                const double reached = 4242d;

                await RunEngine(g, container, table, Continuation(table, resumeRow: 3, maxRight: reached));

                Assert.True(table.ActualRight >= reached,
                    $"the table's right edge fell back to {table.ActualRight:F1}");
            });
        }

        /// <summary>
        /// Each cell an earlier pass left part-way through is handed its own record, so it continues where
        /// <i>it</i> stopped — css-tables-3 §6.1's per-cell rule. Read out of the cell's own lines: a cell
        /// resumed at word 15 of 20 gains exactly the five words it had left.
        /// </summary>
        [Fact]
        public async Task AContinuation_HandsAnUnfinishedCellItsOwnRecord()
        {
            var words = string.Join(" ", Enumerable.Range(0, 20).Select(i => $"w{i:00}"));

            await WithALaidOutTable(
                $"<table style='width:150pt;font-size:10pt'><tr><td id='a'>{words}</td></tr>"
                + "<tr><td id='b'>tail</td></tr></table>",
                async (table, container, g) =>
                {
                    var cell = LayoutHarness.FindById(table, "a")!;
                    var before = cell.LineBoxes.Sum(l => l.Words.Count);
                    Assert.Equal(20, before);

                    var carried = new InlineBreakToken(cell, ResumeSlotIndex: 1, ResumePath: [],
                        ResumeWordIndex: 15, CompletedLineCount: cell.LineBoxes.Count);

                    await RunEngine(g, container, table,
                        Continuation(table, resumeRow: 0,
                            unfinished: [new UnfinishedTableCell(0, cell, carried)]));

                    Assert.Equal(before + 5, cell.LineBoxes.Sum(l => l.Words.Count));
                });
        }

        /// <summary>
        /// The control: the same continuation carrying no record for that cell re-enters it from the
        /// start, which rebuilds the lines it already had and adds nothing.
        /// </summary>
        [Fact]
        public async Task AContinuationCarryingNoRecordForACell_EntersItFromTheStart()
        {
            var words = string.Join(" ", Enumerable.Range(0, 20).Select(i => $"w{i:00}"));

            await WithALaidOutTable(
                $"<table style='width:150pt;font-size:10pt'><tr><td id='a'>{words}</td></tr>"
                + "<tr><td id='b'>tail</td></tr></table>",
                async (table, container, g) =>
                {
                    var cell = LayoutHarness.FindById(table, "a")!;
                    var before = cell.LineBoxes.Sum(l => l.Words.Count);

                    await RunEngine(g, container, table, Continuation(table, resumeRow: 0));

                    Assert.Equal(before, cell.LineBoxes.Sum(l => l.Words.Count));
                });
        }

        /// <summary>
        /// The break point <i>before</i> the row a continuation re-enters was decided by the pass that
        /// stopped there. Re-deciding it takes the forced break a second time, which pushes the row a
        /// further page down — and
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§4.4</see>'s "no empty
        /// fragmentainer" says the same from the other side: the resumed row begins this fragmentainer,
        /// so nothing precedes it here to break from.
        /// </summary>
        [Fact]
        public async Task AContinuation_DoesNotRetakeTheForcedBreakBeforeTheRowItResumesAt()
        {
            var markup = "<table style='width:150pt;font-size:10pt'>"
                         + "<tr><td id='c0'>row 0</td></tr><tr><td id='c1'>row 1</td></tr>"
                         + "<tr><td id='c2' style='break-before:page'>row 2</td></tr>"
                         + "<tr><td id='c3'>row 3</td></tr></table>";

            await WithALaidOutTable(markup, async (table, container, g) =>
            {
                // The control: a fresh run really does take this break, so the continuation staying on
                // its own page says something.
                await RunEngine(g, container, table, resume: null);
                var fresh = BodyRowsOf(table)[2].Location.Y;
                Assert.True(fresh >= container.PageTopOf(1), $"the fresh run took no break ({fresh:F1})");

                await RunEngine(g, container, table, Continuation(table, resumeRow: 2, resumeSlot: 1));

                Assert.Equal(container.PageTopOf(1), BodyRowsOf(table)[2].Location.Y, 3);
            });
        }

        /// <summary>
        /// Taking that break again would also record this pass's <c>MaxBottom</c> — the band top it has
        /// just started at — over the slice bottom the earlier pass recorded for that page, which is what
        /// clips the table's borders there.
        /// </summary>
        [Fact]
        public async Task AContinuation_DoesNotOverwriteAPageBreakBottomAnEarlierPassRecorded()
        {
            var markup = "<table style='width:150pt;font-size:10pt'>"
                         + "<tr><td id='c0'>row 0</td></tr>"
                         + "<tr><td id='c1' style='break-before:page'>row 1</td></tr>"
                         + "<tr><td id='c2'>row 2</td></tr></table>";

            await WithALaidOutTable(markup, async (table, container, g) =>
            {
                table.PageBreakBottoms = new Dictionary<int, double> { [1] = 4242d };

                await RunEngine(g, container, table, Continuation(table, resumeRow: 1, resumeSlot: 1));

                Assert.Equal(4242d, table.PageBreakBottoms![1], 3);
            });
        }

        /// <summary>
        /// A record naming a row this table does not have belongs to a layout that no longer exists, so
        /// there is nothing to continue and the run starts from the first body row — rather than laying
        /// out no rows at all, or indexing past the end.
        /// </summary>
        [Theory]
        [InlineData(-1)]
        [InlineData(9)]
        public async Task ARecordNamingARowTheTableDoesNotHave_StartsAtTheFirstBodyRow(int resumeRow)
        {
            await WithALaidOutTable(RowsTable(4), async (table, container, g) =>
            {
                table.Location = table.Location with { Y = table.Location.Y + 25 };
                var before = BodyRowsOf(table).Select(r => r.Location.Y).ToList();

                await RunEngine(g, container, table, Continuation(table, resumeRow));

                var after = BodyRowsOf(table).Select(r => r.Location.Y).ToList();
                Assert.All(Enumerable.Range(0, 4),
                    i => Assert.True(Math.Abs(after[i] - before[i]) > 1,
                        $"row {i} was not placed ({after[i]:F1} vs {before[i]:F1})"));
            });
        }

        /// <summary>
        /// A run publishes its record only once it has finished, and clears whatever the last one left
        /// before anything can throw. A record left standing by a run that died names a row and holds
        /// <see cref="CssBox"/> references belonging to a layout that no longer exists, and a later run
        /// handed it would resume into that.
        /// </summary>
        [Fact]
        public async Task ARunThatDies_LeavesNoRecordFromThePreviousOne()
        {
            CssBox? root = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(3)), pageHeight: PageHeight, margin: Margin,
                prepare: tree => { root = tree; StopRow(tree, 0); },
                after: async (tree, container, g) =>
                {
                    var table = TableOf(tree);
                    Assert.NotNull(table.TableContinuation);

                    // The stopping cell is replaced by one that throws, in place rather than beside it —
                    // a row's cells are its columns, and ParentBox's setter appends. It has to be that
                    // cell: the row loop stops at row 0, so nothing further along is reached.
                    var row = BodyRowsOf(table)[0];
                    var thrower = new ThrowingCell(row);
                    row.Boxes.Remove(thrower);
                    row.Boxes[0] = thrower;

                    await Assert.ThrowsAnyAsync<Exception>(
                        async () => await RunEngine(g, container, table, resume: null));

                    Assert.Null(table.TableContinuation);
                });
        }

        /// <summary>A cell whose layout fails, so a run can be stopped part-way through.</summary>
        private sealed class ThrowingCell : CssBox
        {
            internal ThrowingCell(CssBox row) : base(row, null)
            {
                InheritStyle(row, everything: true);
                Display = CssConstants.TableCell;
            }

            protected override ValueTask PerformLayoutImp(RGraphics g) =>
                throw new InvalidOperationException("layout failed part-way through the row loop");
        }

        // ─── The cursor's own contract ───────────────────────────────────────────────────────────

        /// <summary>
        /// The rowspan bookkeeping travels with the cursor, keyed by the <b>absolute</b> body-row index
        /// each cell ends on — so a cell begun several pages earlier is still found by the row that ends
        /// its span, which a pass that started the map empty could never do.
        /// </summary>
        /// <remarks>
        /// Asserted on the cursor rather than on geometry, and deliberately: a row that ends a span also
        /// holds a <c>CssSpacingBox</c> placeholder for the same cell, and that path aligns it too, so the
        /// two are not distinguishable from the outside today. The carry is still what the map means, and
        /// what stops it being silently dropped is this.
        /// </remarks>
        [Fact]
        public async Task AResumedCursor_CarriesTheRowspanBookkeepingByAbsoluteRow()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table><tr><td id='a'>a</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var a = LayoutHarness.FindById(root, "a")!;
            var carried = new TableBreakToken(a, ResumeSlotIndex: 3, ResumeRowIndex: 7, MaxRight: 42,
                UnfinishedCells: [],
                RowSpannedBoxes: new Dictionary<int, IReadOnlyList<CssBox>> { [9] = [a] });

            var cursor = TableRowCursor.Continuing(carried, top: 100);

            Assert.Equal([a], cursor.RowSpannedBoxes[9]);
            Assert.Equal(3, cursor.SlotIndex);
            Assert.Equal(42, cursor.MaxRight);
            Assert.Equal(7, cursor.RowIndex);
            Assert.Equal(100, cursor.CurrentY);
            Assert.Equal(100, cursor.MaxBottom);
        }

        /// <summary>
        /// A <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> measurement cursor carries none of it: its rows are
        /// not body rows, and by the time a pass resumed the body that group is not in the tree.
        /// </summary>
        [Fact]
        public async Task ARowGroupMeasurementCursor_CarriesNoRecords()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table><tr><td id='a'>a</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var a = LayoutHarness.FindById(root, "a")!;
            var token = new InlineBreakToken(a, ResumeSlotIndex: 1, ResumePath: [], ResumeWordIndex: 0,
                CompletedLineCount: 1);

            var body = TableRowCursor.Continuing(
                new TableBreakToken(a, ResumeSlotIndex: 1, ResumeRowIndex: 0, MaxRight: 10,
                    UnfinishedCells: [new UnfinishedTableCell(0, a, token)],
                    RowSpannedBoxes: new Dictionary<int, IReadOnlyList<CssBox>>()),
                top: 0);

            Assert.Same(token, body.CarriedTokenFor(a));
            Assert.Null(body.ForRowGroupMeasurement(top: 0).CarriedTokenFor(a));
        }

        // ─── What is not a continuation of a row loop ────────────────────────────────────────────

        /// <summary>
        /// A record that does not name a row — anything but a <see cref="TableBreakToken"/> — says this run
        /// continues an earlier pass but not where its row loop got to, so the loop starts from the first
        /// body row. That is the total reading, and it is the one the once-per-table guards are tested
        /// against in <see cref="TableOncePerTableTests"/>.
        /// </summary>
        [Fact]
        public async Task AContinuationWhoseRecordNamesNoRow_StartsAtTheFirstBodyRow()
        {
            await WithALaidOutTable(RowsTable(4), async (table, container, g) =>
            {
                table.Location = table.Location with { Y = table.Location.Y + 25 };
                var before = BodyRowsOf(table).Select(r => r.Location.Y).ToList();

                await RunEngine(g, container, table,
                    new BlockBreakToken(table, ResumeSlotIndex: 1, ResumeChildIndex: 0, ChildToken: null,
                        IsBreakBefore: false, ResumeTopOverride: null));

                var after = BodyRowsOf(table).Select(r => r.Location.Y).ToList();
                Assert.True(Math.Abs(after[0] - before[0]) > 1, "the first body row was not re-placed");
            });
        }

        /// <summary>
        /// A record for a table that settled nothing is a continuation of nothing, so the run starts from
        /// the markup, cursor included. Guarding on the settled setup rather than on the record is what
        /// makes a table that has never been laid out safe to hand one to.
        /// </summary>
        [Fact]
        public async Task ARecordForATableWithNoSettledSetup_StartsTheRowLoopAtTheTop()
        {
            await WithALaidOutTable(RowsTable(4), async (table, container, g) =>
            {
                table.Location = table.Location with { Y = table.Location.Y + 25 };
                var before = BodyRowsOf(table).Select(r => r.Location.Y).ToList();

                table.TableSetup = null;

                await RunEngine(g, container, table, Continuation(table, resumeRow: 3));

                var after = BodyRowsOf(table).Select(r => r.Location.Y).ToList();
                Assert.True(Math.Abs(after[0] - before[0]) > 1,
                    "the run honoured a record naming a row on a table that had settled nothing");
            });
        }

        private static TableBreakToken Continuation(
            CssBox table, int resumeRow, double maxRight = 0,
            IReadOnlyList<UnfinishedTableCell>? unfinished = null, int resumeSlot = 1) =>
            new(table, resumeSlot, resumeRow, maxRight, unfinished ?? [],
                new Dictionary<int, IReadOnlyList<CssBox>>());
    }
}

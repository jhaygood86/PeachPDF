using PeachPDF.CSS;
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
            LayoutHarness.Descendants(root).First(b => b.Display.Value == DisplayMode.Table);

        /// <summary>
        /// Runs the engine with the fragmentainer detached, which is what keeps these fixtures hermetic:
        /// nothing inside a cell can then run out of one, so the only cell that stops is a double that
        /// says it did, and every assertion below is about the row loop rather than about where a real
        /// flow happened to break. Production stopped detaching here with
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/464">#464</see>; the end-to-end
        /// behaviour that came with that is covered by <see cref="TableCellBreakTokenTests"/>.
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
                .Where(b => b.Display.Value == DisplayMode.TableRow)
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
                Display = CssProperty<DisplayMode>.FromValue(CssConstants.TableCell, DisplayMode.TableCell);
                Record = new InlineBreakToken(this, ResumeSlotIndex: 2, ResumePath: [],
                    ResumeWordIndex: 4, CompletedLineCount: 1);
            }

            /// <summary>The record this cell hands back, kept so a test can assert it travelled.</summary>
            internal BreakToken Record { get; }

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild)
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
        /// A repeating <c>&lt;tfoot&gt;</c>'s <i>closing</i> proxy sits under the table's last row, so a pass
        /// that never reached the last row would put it in the middle of the table on the page it is
        /// leaving — measured during
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/464">#464</see> at y=36.5 under a row
        /// ending at 35.0. Such a pass still owes that page a footer, at the page's <b>bottom</b>, which is
        /// where <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">css-tables-3 §6.2</see>
        /// puts one (<see href="https://github.com/jhaygood86/PeachPDF/issues/493">#493</see>).
        /// </summary>
        /// <remarks>
        /// The two footers are different footers, which is why the gate on the closing one did not have to
        /// be relaxed to write this one: that one closes the <i>table</i>, this one closes a <i>page</i>.
        /// Stated as a position rather than as a count, since a count cannot tell them apart.
        /// </remarks>
        [Fact]
        public async Task APassThatStopped_PutsItsFooterAtThePageBottom_NotUnderTheRowItStoppedAt()
        {
            var markup = "<table style='width:150pt;font-size:10pt'><tfoot><tr><td>F</td></tr></tfoot><tbody>"
                         + string.Concat(Enumerable.Range(0, 4)
                             .Select(i => $"<tr><td id='c{i}'>row {i}</td></tr>"))
                         + "</tbody></table>";

            CssBox? stopped = null;

            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup),
                pageHeight: PageHeight, margin: Margin,
                prepare: tree => { stopped = tree; StopRow(tree, 1); });

            var (control, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(markup),
                pageHeight: PageHeight, margin: Margin);

            var stoppedTable = TableOf(stopped!);
            var pageFooters = stoppedTable.Boxes.OfType<CssProxyBox>().OrderBy(p => p.Location.Y).ToList();
            var controlFooter = Assert.Single(TableOf(control).Boxes.OfType<CssProxyBox>());

            // The cell this fixture stops never finishes, so every pass leaves a page and every page it
            // leaves is closed - one footer each, at that page's own bottom rather than wherever the row
            // loop happened to leave the cursor. Keyed to the band each footer is actually in, not to its
            // position in the list: this fixture's stopping record names slot 2, so the pages the passes
            // leave are not consecutive.
            Assert.NotEmpty(pageFooters);

            foreach (var footer in pageFooters)
            {
                var slot = container.PageIndexOf(footer.Location.Y);

                Assert.Equal(container.PageBottomOf(slot), footer.ActualBottom, 1);

                // And that page has a slice bottom recorded, or FragmentPainter clips the table's bottom
                // border above the footer just drawn under it.
                Assert.Equal(footer.ActualBottom, stoppedTable.PageBreakBottoms![slot], 1);
            }

            // The control is what makes the position mean anything: from the same markup on the same page,
            // a table that finishes closes itself with the footer tucked under its last row - which is
            // exactly where a stopped pass must not put one, and it is far above the page's bottom.
            Assert.True(controlFooter.Location.Y < pageFooters[0].Location.Y - 50,
                $"the stopped pass's first footer sits at {pageFooters[0].Location.Y:F1}, no lower than the "
                + $"closing footer of the table that finished ({controlFooter.Location.Y:F1}) - so it is "
                + "under a row in the middle of the table rather than at the foot of the page it leaves");
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
            var group = table.Boxes.First(b => b.Display.Value == DisplayMode.TableRowGroup);
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
                Display = CssProperty<DisplayMode>.FromValue(CssConstants.TableCell, DisplayMode.TableCell);
            }

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild) =>
                throw new InvalidOperationException("layout failed part-way through the row loop");
        }

        // ─── A finished cell is not an unentered one ─────────────────────────────────────────────

        /// <summary>
        /// The record names the cells of the stopped row that <b>finished</b> as well as the ones that
        /// did not. Without that a continuation cannot tell a cell whose content is already in an earlier
        /// fragment from one no pass has entered — both are simply absent from the unfinished list.
        /// </summary>
        [Fact]
        public async Task TheRecordNamesTheCellsThatFinishedAsWellAsTheOnesThatDidNot()
        {
            CssBox? root = null;
            StoppingCell? stopping = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table style='width:150pt;font-size:10pt'>"
                                   + "<tr><td id='c0'>row 0</td><td id='sibling'>beside</td></tr>"
                                   + "<tr><td id='c1'>row 1</td><td>beside</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin,
                prepare: tree => { root = tree; stopping = StopRow(tree, 0); });

            var record = TableOf(root!).TableContinuation!;
            var sibling = LayoutHarness.FindById(root!, "sibling")!;

            Assert.Equal([sibling], record.FinishedCells);
            Assert.Same(stopping, Assert.Single(record.UnfinishedCells).Cell);
        }

        /// <summary>
        /// What a record names is the stopped row's cells only. The rows before it finished as whole rows
        /// and are not re-entered at all, so carrying their cells would say nothing and grow with the
        /// table.
        /// </summary>
        [Fact]
        public async Task TheRecordNamesNoCellFromARowTheLoopHadAlreadyLeft()
        {
            CssBox? root = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(4)), pageHeight: PageHeight, margin: Margin,
                prepare: tree => { root = tree; StopRow(tree, 2); });

            var record = TableOf(root!).TableContinuation!;
            var earlier = LayoutHarness.FindById(root!, "c0")!;

            Assert.Empty(record.FinishedCells);
            Assert.DoesNotContain(earlier, record.FinishedCells);
        }

        /// <summary>
        /// A continuation places nothing at all for a cell an earlier pass finished: its whole content is
        /// in the fragment that pass emitted, and re-entering it would put that content on this
        /// fragmentainer instead
        /// (<see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see>).
        /// </summary>
        [Fact]
        public async Task AContinuation_PlacesNothingForACellAnEarlierPassFinished()
        {
            await WithALaidOutTable(
                "<table style='width:150pt;font-size:10pt'>"
                + "<tr><td id='a'>alpha</td><td id='b'>beta</td></tr>"
                + "<tr><td id='c'>gamma</td><td>delta</td></tr></table>",
                async (table, container, g) =>
                {
                    var finished = LayoutHarness.FindById(table, "b")!;
                    var before = finished.Location;
                    var beforeBottom = finished.ActualBottom;

                    await RunEngine(g, container, table,
                        Continuation(table, resumeRow: 0, resumeSlot: 1, finished: [finished]));

                    Assert.Equal(before.Y, finished.Location.Y, 3);
                    Assert.Equal(before.X, finished.Location.X, 3);
                    Assert.Equal(beforeBottom, finished.ActualBottom, 3);

                    // The cell beside it, which the record says nothing about, did move — so "it stayed
                    // put" is about the record rather than about the pass placing nothing.
                    Assert.Equal(container.PageTopOf(1), LayoutHarness.FindById(table, "a")!.Location.Y, 3);
                });
        }

        /// <summary>
        /// The control: the same continuation carrying no finished record for that cell re-enters it, so
        /// it lands in the fragmentainer this pass is filling. That is exactly the duplication the record
        /// exists to prevent, and it is what the assertion above would otherwise pass vacuously against.
        /// </summary>
        [Fact]
        public async Task AContinuationNamingNoFinishedCell_RePlacesItInTheFragmentainerItIsFilling()
        {
            await WithALaidOutTable(
                "<table style='width:150pt;font-size:10pt'>"
                + "<tr><td id='a'>alpha</td><td id='b'>beta</td></tr>"
                + "<tr><td id='c'>gamma</td><td>delta</td></tr></table>",
                async (table, container, g) =>
                {
                    var cell = LayoutHarness.FindById(table, "b")!;

                    await RunEngine(g, container, table, Continuation(table, resumeRow: 0, resumeSlot: 1));

                    Assert.Equal(container.PageTopOf(1), cell.Location.Y, 3);
                });
        }

        /// <summary>
        /// A cell that a continuation does not enter still holds its column open, so the cells beside it
        /// keep their own columns rather than sliding left into the space it would have taken.
        /// </summary>
        [Fact]
        public async Task ACellAContinuationDoesNotEnter_StillHoldsItsColumn()
        {
            await WithALaidOutTable(
                "<table style='width:150pt;font-size:10pt'>"
                + "<tr><td id='a'>alpha</td><td id='b'>beta</td><td id='c'>gamma</td></tr>"
                + "<tr><td>d</td><td>e</td><td>f</td></tr></table>",
                async (table, container, g) =>
                {
                    var skipped = LayoutHarness.FindById(table, "b")!;
                    var after = LayoutHarness.FindById(table, "c")!;
                    var beforeX = after.Location.X;

                    await RunEngine(g, container, table,
                        Continuation(table, resumeRow: 0, resumeSlot: 1, finished: [skipped]));

                    Assert.Equal(beforeX, after.Location.X, 3);
                });
        }

        // ─── A cell that stopped is not aligned against a box that does not describe it ──────────

        /// <summary>
        /// A cell that stopped never reached the line that sets its own <c>ActualBottom</c>, so the box
        /// still holds the pre-flow value its placement gave it — its own top. Reading that as the cell's
        /// height and distributing the difference is what
        /// <c>CssLayoutEngine.ApplyCellVerticalAlignment</c> does, and it pushes the whole fragment
        /// <i>up</i> out of the fragmentainer being filled. Measured with the monolithic gate lifted: a
        /// 244-word <c>&lt;td&gt;</c> put its first line 104pt above the document origin and emitted 121
        /// of its 244 words.
        /// </summary>
        [Fact]
        public async Task ACellThatStopped_KeepsItsContentWhereTheFlowPutIt()
        {
            CssBox? root = null;
            OverflowingStoppingCell? stopping = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(2)), pageHeight: PageHeight, margin: Margin,
                prepare: tree =>
                {
                    root = tree;
                    var anchor = LayoutHarness.FindById(tree, "c0")!;
                    var row = anchor.ParentBox!;
                    stopping = new OverflowingStoppingCell(row);
                    row.Boxes.Remove(stopping);
                    row.Boxes[row.Boxes.IndexOf(anchor)] = stopping;

                    // A sibling taller than the stopping cell, so the row's own MaxBottom is below
                    // where the stopped cell's content reached. Without that the alignment has nothing
                    // to distribute and skipping it would be indistinguishable from not skipping it.
                    var tall = new TallCell(row, 320);
                    row.Boxes.Remove(tall);
                    row.Boxes.Add(tall);
                });

            Assert.NotNull(stopping);
            Assert.Equal(stopping!.PlacedContentTop, stopping.OverflowingContent.Location.Y, 3);
            Assert.True(stopping.ActualBottom >= stopping.OverflowingContent.ActualBottom,
                $"the cell's bottom ({stopping.ActualBottom:F1}) is above its own content "
                + $"({stopping.OverflowingContent.ActualBottom:F1}), so nothing above it can measure the fragment");
        }

        /// <summary>
        /// The control: the identical cell that <i>finished</i> is aligned, so "the content stayed where
        /// the flow put it" above is a fact about the cell having stopped.
        /// </summary>
        [Fact]
        public async Task ACellThatFinished_IsStillVerticallyAligned()
        {
            CssBox? root = null;
            OverflowingStoppingCell? finishing = null;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(2)), pageHeight: PageHeight, margin: Margin,
                prepare: tree =>
                {
                    root = tree;
                    var anchor = LayoutHarness.FindById(tree, "c0")!;
                    var row = anchor.ParentBox!;
                    finishing = new OverflowingStoppingCell(row, stops: false);
                    row.Boxes.Remove(finishing);
                    row.Boxes[row.Boxes.IndexOf(anchor)] = finishing;

                    var tall = new TallCell(row, 320);
                    row.Boxes.Remove(tall);
                    row.Boxes.Add(tall);
                });

            Assert.NotNull(finishing);
            Assert.NotEqual(finishing!.PlacedContentTop, finishing.OverflowingContent.Location.Y, 3);
        }

        /// <summary>
        /// A cell that continues an earlier fragment keeps the one <c>Location</c> that fragment was built
        /// from. A <see cref="CssBox"/> has exactly one, so writing this pass's row top into it retracts the
        /// earlier fragment's geometry — and the emitter, notified the box moved, rebuilds that
        /// fragmentainer from where the box is <i>now</i> and finds nothing of it there.
        /// </summary>
        /// <remarks>
        /// The measured symptom is the whole table disappearing from the page it began on: 149 of a
        /// 240-word cell's words vanished from the first page of the <c>paged_media_table_cell_lines</c>
        /// showcase, borders and all, while the second page was unaffected. This is the same rule
        /// <c>CssBox.ResumeInTheNextFragmentainer</c> follows for every other box, <b>and it has the same
        /// exception</b>: inside a fragmentainer with a band of its own — a multi-column column — the cell
        /// does move, because columns differ in exactly the axis the page grid holds constant. Stating the
        /// rule without that exception cost half the content of a table nested in a multi-column container
        /// (<see cref="SuppressedPassFragmentainerTests"/> is where that shows up).
        /// </remarks>
        [Fact]
        public async Task ACellResumedFromAnEarlierPass_KeepsTheLocationItsFirstFragmentWasBuiltFrom()
        {
            OverflowingStoppingCell? resumed = null;
            var placedBefore = 0d;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(2)), pageHeight: PageHeight, margin: Margin,
                prepare: tree => resumed = ReplaceWithAContinuingCell(tree),
                after: async (root, container, g) =>
                {
                    placedBefore = resumed!.Location.Y;

                    // A record naming a slot well below the table, so a re-placement would be unmistakable.
                    await RunEngine(g, container, TableOf(root), Continuation(
                        TableOf(root), resumeRow: 0, resumeSlot: 2,
                        unfinished: [new UnfinishedTableCell(0, resumed, new InlineBreakToken(
                            resumed, ResumeSlotIndex: 2, ResumePath: [], ResumeWordIndex: 0,
                            CompletedLineCount: 0))]));
                });

            Assert.NotNull(resumed);
            Assert.Equal(placedBefore, resumed!.Location.Y, 3);
        }

        /// <summary>
        /// The control: a cell the same continuation enters fresh <i>is</i> placed at the row top this pass
        /// is filling, so the assertion above is about the cell continuing rather than about this run
        /// placing nothing.
        /// </summary>
        [Fact]
        public async Task ACellTheContinuationEntersFresh_IsPlacedAtThisPassesRowTop()
        {
            OverflowingStoppingCell? entered = null;
            var placedBefore = 0d;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(2)), pageHeight: PageHeight, margin: Margin,
                prepare: tree => entered = ReplaceWithAContinuingCell(tree),
                after: async (root, container, g) =>
                {
                    placedBefore = entered!.Location.Y;

                    await RunEngine(g, container, TableOf(root),
                        Continuation(TableOf(root), resumeRow: 0, resumeSlot: 2));
                });

            Assert.NotNull(entered);
            Assert.NotEqual(placedBefore, entered!.Location.Y, 3);
        }

        /// <summary>
        /// A cell that <i>continues</i> an earlier fragment is not vertically aligned either, and for a
        /// reason the guard above does not cover: this one finishes on the pass being run, so it is in
        /// neither the stopped list nor <c>FinishedCells</c>. Where its content sits was settled by the
        /// pass that opened it, and <c>ApplyCellVerticalAlignment</c> offsets the whole subtree — so
        /// aligning it here drags content an earlier fragmentainer has already emitted onto this page.
        /// Measured before the guard, with the gate moved: a <c>&lt;p&gt;</c> in a <c>&lt;td&gt;</c> moved
        /// from Y 22.7 to 235.3 between passes, putting one line across the page boundary and both bands
        /// claiming its 14 words.
        /// </summary>
        [Fact]
        public async Task ACellResumedFromAnEarlierPass_IsNotAlignedAgainstTheRowItFinishesIn()
        {
            OverflowingStoppingCell? resumed = null;
            var placedBefore = 0d;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(2)), pageHeight: PageHeight, margin: Margin,
                prepare: tree => resumed = ReplaceWithAContinuingCell(tree),
                after: async (root, container, g) =>
                {
                    placedBefore = resumed!.OverflowingContent.Location.Y;

                    await RunEngine(g, container, TableOf(root), Continuation(
                        TableOf(root), resumeRow: 0,
                        unfinished: [new UnfinishedTableCell(0, resumed, new InlineBreakToken(
                            resumed, ResumeSlotIndex: 1, ResumePath: [], ResumeWordIndex: 0,
                            CompletedLineCount: 0))]));
                });

            Assert.NotNull(resumed);
            Assert.Equal(placedBefore, resumed!.OverflowingContent.Location.Y, 3);
        }

        /// <summary>
        /// The control: the same cell on the same continuation, named by no record, is entered from the
        /// start and aligned as any other — so "the resumed cell's content stayed put" above is a fact
        /// about the cell continuing rather than about this run aligning nothing.
        /// </summary>
        [Fact]
        public async Task ACellTheContinuationEntersFresh_IsStillVerticallyAligned()
        {
            OverflowingStoppingCell? entered = null;
            var placedBefore = 0d;

            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(RowsTable(2)), pageHeight: PageHeight, margin: Margin,
                prepare: tree => entered = ReplaceWithAContinuingCell(tree),
                after: async (root, container, g) =>
                {
                    placedBefore = entered!.OverflowingContent.Location.Y;

                    await RunEngine(g, container, TableOf(root), Continuation(TableOf(root), resumeRow: 0));
                });

            Assert.NotNull(entered);
            Assert.NotEqual(placedBefore, entered!.OverflowingContent.Location.Y, 3);
        }

        /// <summary>
        /// Puts a cell that overflows its own box but <i>finishes</i> in body row 0, beside a sibling deep
        /// enough that the row's bottom is below it — without which the alignment has nothing to
        /// distribute and skipping it would be indistinguishable from not skipping it.
        /// </summary>
        private static OverflowingStoppingCell ReplaceWithAContinuingCell(CssBox tree)
        {
            var anchor = LayoutHarness.FindById(tree, "c0")!;
            var row = anchor.ParentBox!;

            var cell = new OverflowingStoppingCell(
                row, stops: false, keepsItsContentAfterTheFirstLayout: true);
            row.Boxes.Remove(cell);
            row.Boxes[row.Boxes.IndexOf(anchor)] = cell;

            var tall = new TallCell(row, 320);
            row.Boxes.Remove(tall);
            row.Boxes.Add(tall);

            return cell;
        }

        /// <summary>A cell that finishes, deeper than the one beside it, so the row's bottom is below it.</summary>
        private sealed class TallCell : CssBox
        {
            private readonly double _depth;

            internal TallCell(CssBox row, double depth) : base(row, null)
            {
                InheritStyle(row, everything: true);
                Display = CssProperty<DisplayMode>.FromValue(CssConstants.TableCell, DisplayMode.TableCell);
                _depth = depth;
            }

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild)
            {
                ActualBottom = Location.Y + _depth;
                Height = $"{_depth}px";
                return default;
            }
        }

        /// <summary>
        /// A cell whose content flows well past the box its placement gave it, which is the shape a cell
        /// that ran out of fragmentainer really has: <c>CssLayoutEngine.CreateLineBoxes</c> returns on the
        /// break before setting <c>ActualBottom</c>, leaving the pre-flow value behind.
        /// </summary>
        private sealed class OverflowingStoppingCell : CssBox
        {
            private readonly bool _stops;
            private readonly bool _keepsItsContentAfterTheFirstLayout;
            private bool _placed;

            /// <param name="row">the row to attach to</param>
            /// <param name="stops">whether it reports that it could not finish</param>
            /// <param name="keepsItsContentAfterTheFirstLayout">
            /// whether later layouts leave the content where the first one put it, which is what a box
            /// continuing across a <i>page</i> boundary really does — <c>ResumeInTheNextFragmentainer</c>
            /// moves a box only inside a fragmentainer with a band of its own. Without it a second layout
            /// re-places the content at the cell's new top by itself, and "the alignment did not move it"
            /// cannot be told from "the double put it back".
            /// </param>
            internal OverflowingStoppingCell(
                CssBox row, bool stops = true, bool keepsItsContentAfterTheFirstLayout = false)
                : base(row, null)
            {
                InheritStyle(row, everything: true);
                Display = CssProperty<DisplayMode>.FromValue(CssConstants.TableCell, DisplayMode.TableCell);
                VerticalAlign = CssProperty<CssKeywordOrValue<VerticalAlignment, LengthOrCalc>>.FromValue(
                    CssConstants.Middle, new CssKeywordOrValue<VerticalAlignment, LengthOrCalc>(VerticalAlignment.Middle, null));
                _stops = stops;
                _keepsItsContentAfterTheFirstLayout = keepsItsContentAfterTheFirstLayout;

                OverflowingContent = new CssBox(this, null);
                OverflowingContent.InheritStyle(this, everything: true);
                OverflowingContent.Display = CssProperty<DisplayMode>.FromValue(CssConstants.Block, DisplayMode.Block);
            }

            /// <summary>The content that flowed past the cell's own box.</summary>
            internal CssBox OverflowingContent { get; }

            /// <summary>Where this cell's layout left that content.</summary>
            internal double PlacedContentTop { get; private set; }

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild)
            {
                // What a real stopped flow leaves: content 200pt deep, and a box still holding the top it
                // was placed at.
                if (!_placed || !_keepsItsContentAfterTheFirstLayout)
                {
                    OverflowingContent.Location = new RPoint(Location.X, Location.Y);
                    PlacedContentTop = OverflowingContent.Location.Y;
                    _placed = true;
                }

                OverflowingContent.ActualBottom = OverflowingContent.Location.Y + 200;
                OverflowingContent.Height = "200px";
                ActualBottom = Location.Y;

                if (_stops)
                {
                    SetPendingBreakToken(new InlineBreakToken(this, ResumeSlotIndex: 1, ResumePath: [],
                        ResumeWordIndex: 0, CompletedLineCount: 0));
                }

                return default;
            }
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
                UnfinishedCells: [], FinishedCells: [],
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
        /// The band a cursor has reached is the one it is filling until its position has genuinely fallen
        /// past that band — not merely come within the boundary tolerance of the next one.
        /// </summary>
        /// <remarks>
        /// <c>SlotStartingAt</c>'s top-edge convention counts a coordinate within
        /// <c>PageBoundaryEpsilon</c> <i>above</i> a boundary as beginning the later band, which is right
        /// for a box placed at a band top and wrong for this cursor, a derived position. Reading it that
        /// way let the row loop ask its questions of a band it had not reached, take no break, and leave
        /// the table crossing a page boundary with no slice bottom recorded for the page it left. Font
        /// metrics decide whether a given fixture lands inside the half point — this reproduced on Windows
        /// and nowhere else — which is why it is asserted on the coordinate rather than through a document.
        /// </remarks>
        [Fact]
        public async Task ACursorWithinTheBoundaryToleranceOfTheNextBand_HasNotReachedIt()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<p>a</p>"), pageHeight: PageHeight, margin: Margin);

            var cursor = new TableRowCursor(top: container.PageTopOf(0), maxRight: 0, slotIndex: 0);

            // Inside the band, but within the epsilon of the next band's top.
            cursor.CurrentY = container.PageBottomOf(0) - (HtmlContainerInt.PageBoundaryEpsilon / 2);
            Assert.Equal(0, cursor.BandReached(container));
            Assert.Equal(0, cursor.SlotIndex);

            // Genuinely past it.
            cursor.CurrentY = container.PageTopOf(1) + 1;
            Assert.Equal(1, cursor.BandReached(container));
            Assert.Equal(1, cursor.SlotIndex);
        }

        /// <summary>
        /// Retracting a row's placement takes back exactly what that row added to the rowspan
        /// bookkeeping: a list it extended is truncated to the length it had, and a key it created
        /// outright is removed rather than left empty for <c>Continuation</c> to publish.
        /// </summary>
        /// <remarks>
        /// The row loop retracts a row it has placed and seen straddle out of its band, so it can place it
        /// again on the other side of the break. An entry left behind is a cell the row that ends the span
        /// would align against twice — the <c>ApplyCellVerticalAlignment</c> deep-offset hazard, which is
        /// a whole fragment's content moved rather than a cosmetic shift.
        /// </remarks>
        [Fact]
        public async Task RetractingARowsPlacement_TakesBackOnlyWhatThatRowAddedToTheRowspanMap()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table><tr><td id='a'>a</td><td id='b'>b</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var a = LayoutHarness.FindById(root, "a")!;
            var b = LayoutHarness.FindById(root, "b")!;

            var cursor = new TableRowCursor(top: 10, maxRight: 5, slotIndex: 0) { MaxBottom = 20 };
            cursor.RowSpannedBoxes[4] = [a];

            var placement = cursor.BeginRow();

            // What the placement being retracted did: extended one list and created another.
            cursor.RowSpannedBoxes[4].Add(b);
            cursor.RowSpannedBoxes[6] = [b];
            cursor.MaxBottom = 99;
            cursor.MaxRight = 77;

            cursor.Retract(placement);

            Assert.Equal([a], cursor.RowSpannedBoxes[4]);
            Assert.False(cursor.RowSpannedBoxes.ContainsKey(6));
            Assert.Equal(20, cursor.MaxBottom);
            Assert.Equal(5, cursor.MaxRight);
        }

        /// <summary>
        /// And it puts back what the row wrote to a cell it does <b>not own</b> — the spanning cell a
        /// <c>rowspan</c> begun in an earlier row ends on.
        /// </summary>
        /// <remarks>
        /// This is the geometry the retraction could not reach, and the reason the row loop used to decline
        /// to move a row that ends a span at all
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/511">issue #511</see>): the cursor's
        /// own totals go back and <c>PassRewind.RollBackTo</c> resets the row's own boxes, and the spanning
        /// cell is neither. The offset has to be <i>recorded</i> rather than recomputed because
        /// <c>ApplyCellVerticalAlignment</c> offsets a subtree instead of assigning a position, so it is
        /// neither idempotent nor derivable after the fact.
        /// </remarks>
        [Fact]
        public async Task RetractingARowsPlacement_PutsBackTheSpanningCellItWroteTo()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table><tr><td id='a'><div id='inner'>a</div></td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var cell = LayoutHarness.FindById(root, "a")!;
            var inner = LayoutHarness.FindById(root, "inner")!;

            var bottomBefore = cell.ActualBottom;
            var innerTopBefore = inner.Location.Y;

            var cursor = new TableRowCursor(top: 10, maxRight: 5, slotIndex: 0) { MaxBottom = 20 };
            var placement = cursor.BeginRow();

            // What closing a spanning cell does: writes a bottom the row decided and offsets the cell's
            // whole subtree to align its content in it.
            cell.ActualBottom = bottomBefore + 500;
            foreach (var child in cell.Boxes) child.OffsetTop(37);
            cursor.RecordForeignWrite(cell, bottomBefore, 37);

            cursor.Retract(placement);

            Assert.Equal(bottomBefore, cell.ActualBottom, 0.001);
            Assert.Equal(innerTopBefore, inner.Location.Y, 0.001);

            // Spent by the retraction: replaying it would offset the subtree a second time, in the
            // direction the first replay already took it.
            cursor.Retract(placement);

            Assert.Equal(innerTopBefore, inner.Location.Y, 0.001);
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
                    UnfinishedCells: [new UnfinishedTableCell(0, a, token)], FinishedCells: [],
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

        // ─── A rowspan cell "finished" at its opening row still closes at its ending row ─────────

        /// <summary>
        /// A rowspan cell a resumed pass marks "finished" at its own opening row still closes at the row
        /// that ends its span, later in the same pass — the stale-finished guard must not skip it there.
        /// </summary>
        /// <remarks>
        /// Regression for <see href="https://github.com/jhaygood86/PeachPDF/issues/593">issue #593</see>:
        /// <c>TableRowCursor._carriedFinished</c> is seeded once, when a resumed pass re-enters the row
        /// that opened a rowspan, and is never cleared for the rest of that pass — so
        /// <c>FinishedOnAnEarlierPass</c> keeps answering true for that cell all the way through the row
        /// that should actually close it, and <c>CloseSpanningCell</c> is never entered for it at all.
        /// </remarks>
        [Fact]
        public async Task ARowspanCellMarkedFinishedAtItsOpeningRow_StillClosesAtTheRowThatEndsItsSpan()
        {
            await WithALaidOutTable(
                "<table style='width:150pt;font-size:10pt'>"
                + "<tr><td id='span' rowspan='3'>short</td><td>sibling</td></tr>"
                + "<tr><td>row1</td></tr>"
                + "<tr><td>row2</td></tr></table>",
                async (table, container, g) =>
                {
                    var span = LayoutHarness.FindById(table, "span")!;

                    // A value CloseSpanningCell would never produce naturally, so a pass that skips the
                    // cell (the stale-guard bug) is distinguishable from one that actually re-closes it.
                    span.ActualBottom = span.Location.Y;

                    // A cell an earlier pass marked finished skips LayoutBodyRow's own per-cell layout
                    // entirely (the FinishedOnAnEarlierPass branch `continue`s before ever re-registering
                    // a rowspan into RowSpannedBoxes), so - exactly like a real multi-page continuation -
                    // this resumed pass has to be handed the row-2 entry itself rather than rebuild it.
                    var token = new TableBreakToken(table, ResumeSlotIndex: 1, ResumeRowIndex: 0, MaxRight: 0,
                        UnfinishedCells: [], FinishedCells: [span],
                        RowSpannedBoxes: new Dictionary<int, IReadOnlyList<CssBox>> { [2] = [span] });

                    await RunEngine(g, container, table, token);

                    var endingRow = BodyRowsOf(table)[2];
                    Assert.Equal(endingRow.ActualBottom, span.ActualBottom, 3);
                });
        }

        private static TableBreakToken Continuation(
            CssBox table, int resumeRow, double maxRight = 0,
            IReadOnlyList<UnfinishedTableCell>? unfinished = null, int resumeSlot = 1,
            IReadOnlyList<CssBox>? finished = null) =>
            new(table, resumeSlot, resumeRow, maxRight, unfinished ?? [], finished ?? [],
                new Dictionary<int, IReadOnlyList<CssBox>>());
    }
}

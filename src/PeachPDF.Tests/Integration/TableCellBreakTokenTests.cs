using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Fragments;
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
    /// The answer used to be "yes, it finished" for every document, because the engine ran inside a
    /// detached fragmentainer and no descendant of a cell had one to run out of. Since
    /// <see href="https://github.com/jhaygood86/PeachPDF/issues/464">#464</see> it does, so the shapes
    /// below really do stop and continue — and what the theory pins is the property that matters end to
    /// end, that every word the document authored is claimed by exactly one fragmentainer. The unit tests
    /// after it pin the recorder itself against cells that answer by construction, which is still the only
    /// way to state what the record <i>contains</i> at the one instant it is readable.
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

        // ─── A table that paginates loses nothing and duplicates nothing ─────────────────────────

        /// <summary>
        /// Every table shape that paginates claims each of its words in exactly one fragmentainer. It
        /// fails one way if a fragment claims a word another one also holds — a line drawn on two pages —
        /// and the other way if a word is dropped entirely.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Seven shapes, because what stops is not always the same thing: the cell's own inline flow, a
        /// block inside it, a second row, a repeating <c>&lt;thead&gt;</c> or <c>&lt;tfoot&gt;</c>, a
        /// multi-column container driving fragmentainers of its own inside a cell, and a table nested in
        /// one. Three further shapes are in <see cref="APaginatingTable_DropsNoWord"/> only, because they
        /// duplicate a line at a boundary for reasons that are not the table's — see there.
        /// </para>
        /// <para>
        /// Stated over the words the document <i>authored</i> rather than by comparing counts. A repeated
        /// <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> is detached from the tree and drawn through proxies,
        /// so its words are claimed without being reachable from the root — counting both sides would
        /// disagree by exactly that group, and pinning the disagreement would pin the repetition count.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData("<table style='width:150pt'><tr><td>{W}</td></tr></table>")]
        [InlineData("<table style='width:150pt'><tr><td><p>{W}</p></td></tr></table>")]
        [InlineData("<table style='width:150pt'><tr><td>{W}</td></tr><tr><td>tail</td></tr></table>")]
        [InlineData("<table style='width:150pt'><thead><tr><th>H</th></tr></thead>"
                    + "<tbody><tr><td>{W}</td></tr></tbody></table>")]
        [InlineData("<table style='width:150pt'><tfoot><tr><td>F</td></tr></tfoot>"
                    + "<tbody><tr><td>{W}</td></tr></tbody></table>")]
        [InlineData("<table style='width:150pt'><tr><td><div style='column-count:2'>{W}</div></td></tr></table>")]
        [InlineData("<div style='column-count:2'><table><tr><td>{W}</td></tr></table></div>")]
        public async Task APaginatingTable_ClaimsEveryWordExactlyOnce(string markup)
        {
            var (root, container) = await Paginate(markup);

            var claims = WordClaims(container);

            Assert.All(AuthoredWords(root), word =>
            {
                Assert.True(claims.TryGetValue(word, out var slots), $"'{word.Text}' was claimed by no page");
                Assert.Single(slots!);
            });

            // The last pass is the one that finished the table, so nothing is left saying otherwise. The
            // record's own contents are pinned by the unit tests below, at the instant they are readable.
            Assert.Null(TableOf(root).TableContinuation);
            Assert.All(CellsOf(root), cell => Assert.Null(cell.PendingBreakToken));
        }

        /// <summary>
        /// The weaker half of the invariant above, asked of every shape including the three that cannot
        /// satisfy the stronger one: no word the document authored goes unclaimed. This is what
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/464">#464</see> is about — before the
        /// table's row loop could be resumed, the rows after a stop were never placed at all.
        /// </summary>
        /// <remarks>
        /// The three extra shapes duplicate rather than drop, each for a reason documented as an accepted
        /// limitation rather than a table defect. A flex or grid container inside a cell does not fragment
        /// its item content, so a page boundary can cut through a line of it and that line is drawn on both
        /// pages. A <c>rowspan</c> cell is reached again through a <c>CssSpacingBox</c> placeholder for
        /// every row it spans, so its subtree is emitted once per placeholder into the same fragmentainer.
        /// </remarks>
        [Theory]
        [InlineData("<table style='width:150pt'><tr><td><div style='display:flex;flex-wrap:wrap'>{W}</div>"
                    + "</td></tr></table>")]
        [InlineData("<table style='width:150pt'><tr><td><div style='display:grid'>{W}</div></td></tr></table>")]
        [InlineData("<table style='width:150pt'><tr><td rowspan='2'>{W}</td><td>a</td></tr>"
                    + "<tr><td>b</td></tr></table>")]
        public async Task APaginatingTable_DropsNoWord(string markup)
        {
            var (root, container) = await Paginate(markup);

            var claims = WordClaims(container);

            Assert.All(AuthoredWords(root),
                word => Assert.True(claims.ContainsKey(word), $"'{word.Text}' was claimed by no page"));
        }

        /// <summary>
        /// A repeating <c>&lt;thead&gt;</c> repeats on every page the table covers, including when the
        /// break that opened those pages fell <i>inside a cell</i> rather than between two rows
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/439">#439</see>).
        /// </summary>
        /// <remarks>
        /// A single-row table whose cell overflows used to emit its header exactly once: the per-row check
        /// that carries the header onto a new page only runs between rows, and a cell's own text
        /// paginated without ever telling the engine. Now the row loop stops where the cell stopped and a
        /// later pass continues it, so each pass opens its page with the header. The header's own box is
        /// detached and drawn through one proxy per page, so this is stated on the word it holds — one
        /// <c>CssRect</c> claimed once by each fragmentainer, at that fragmentainer's own top.
        /// </remarks>
        [Theory]
        [InlineData("<table style='width:150pt'><thead><tr><th>HEADERWORD</th></tr></thead>"
                    + "<tbody><tr><td>{W}</td></tr></tbody></table>")]
        [InlineData("<table style='width:150pt'><thead><tr><th>HEADERWORD</th><th>B</th></tr></thead>"
                    + "<tbody><tr><td>{W}</td><td>short</td></tr></tbody></table>")]
        public async Task ARepeatingHeader_RepeatsWhenTheBreakFallsInsideACell(string markup)
        {
            var (_, container) = await Paginate(markup);

            var pages = container.FragmentTree!.Fragmentainers.Count;

            var header = WordClaims(container).Single(c => c.Key.Text == "HEADERWORD");

            Assert.Equal(pages, header.Value.Count);
            Assert.Equal(container.FragmentTree.Fragmentainers.Select(f => f.SlotIndex), header.Value);
        }

        /// <summary>
        /// A repeated <c>&lt;thead&gt;</c> sits <i>above</i> the continuation it repeats over, not on top of
        /// it (<see href="https://github.com/jhaygood86/PeachPDF/issues/439">#439</see>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The header repeating and the header being in the right place are two different facts, and the
        /// second is the one no count can state. The row cursor reserved the header's height by advancing,
        /// which places the rows a pass lays out and the cells it enters fresh — but a cell continuing an
        /// earlier fragmentainer keeps the <c>Location</c> its first fragment was built from, and its
        /// content goes where the flow puts it, which was this fragmentainer's own content edge. That is
        /// exactly where the header had just been drawn, so the two overlapped: measured on
        /// <c>paged_media_table_row_continuation</c> as six words hidden under the header on each of two
        /// pages, with every word still claimed exactly once.
        /// </para>
        /// <para>
        /// Stated of every fragmentainer after the first, since the first is the one the header has always
        /// been correct on — a fixture that only checked page 0 asserts the behaviour that was never
        /// broken.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData("<table style='width:150pt'><thead><tr><th>HEADERWORD</th></tr></thead>"
                    + "<tbody><tr><td>{W}</td></tr></tbody></table>")]
        [InlineData("<table style='width:150pt'><thead><tr><th>HEADERWORD</th><th>B</th></tr></thead>"
                    + "<tbody><tr><td>{W}</td><td>short</td></tr></tbody></table>")]
        public async Task ARepeatedHeader_SitsAboveTheContinuationItRepeatsOver(string markup)
        {
            var (_, container) = await Paginate(markup);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers.Skip(1))
            {
                var words = Flatten(fragmentainer.Root).SelectMany(f => f.Words).ToList();

                var header = Assert.Single(words, w => w.Word.Text == "HEADERWORD");
                var body = words.Where(w => w.Word.Text!.StartsWith("word")).ToList();

                Assert.NotEmpty(body);

                Assert.All(body, word => Assert.True(word.Rect.Top >= header.Rect.Bottom,
                    $"'{word.Word.Text}' starts at {word.Rect.Top} in slot {fragmentainer.SlotIndex}, "
                    + $"above the repeated header's bottom edge at {header.Rect.Bottom}"));
            }
        }

        /// <summary>
        /// A repeating <c>&lt;tfoot&gt;</c> closes every page the table covers, including when the break that
        /// opened those pages fell <i>inside a cell</i> rather than between two rows
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/493">#493</see>).
        /// </summary>
        /// <remarks>
        /// The mirror of <see cref="ARepeatingHeader_RepeatsWhenTheBreakFallsInsideACell"/>, and it used to
        /// measure <c>0, 0, 0, 0, 0, 0, 1</c> across seven pages against the header's <c>1, 1, 1, 1, 1, 1,
        /// 1</c>: the engine wrote a footer only at a break between two rows and once under the table's
        /// last row, and a pass that stops mid-cell reaches neither.
        /// </remarks>
        [Theory]
        [InlineData("<table style='width:150pt'><tfoot><tr><td>FOOTWORD</td></tr></tfoot>"
                    + "<tbody><tr><td>{W}</td></tr></tbody></table>")]
        [InlineData("<table style='width:150pt'><tfoot><tr><td>FOOTWORD</td><td>B</td></tr></tfoot>"
                    + "<tbody><tr><td>{W}</td><td>short</td></tr></tbody></table>")]
        public async Task ARepeatingFooter_RepeatsWhenTheBreakFallsInsideACell(string markup)
        {
            var (_, container) = await Paginate(markup);

            var pages = container.FragmentTree!.Fragmentainers.Count;

            var footer = WordClaims(container).Single(c => c.Key.Text == "FOOTWORD");

            Assert.Equal(pages, footer.Value.Count);
            Assert.Equal(container.FragmentTree.Fragmentainers.Select(f => f.SlotIndex), footer.Value);
        }

        /// <summary>
        /// A repeated <c>&lt;tfoot&gt;</c> sits <i>below</i> the continuation it closes over, not on top of
        /// it (<see href="https://github.com/jhaygood86/PeachPDF/issues/493">#493</see>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// #439's ordering constraint mirrored, and the half no count can state: two fragments may each be
        /// claimed by exactly one fragmentainer and still occupy the same rectangle in it. The row cursor
        /// leaves the footer's room out of the height a whole row is measured against, which reaches
        /// nothing inside a cell whose own lines run down toward the band's foot — so the footer drawn
        /// there lands on top of them unless the flow is told the room is spoken for.
        /// </para>
        /// <para>
        /// Stated of every fragmentainer but the last, since the last is the one the footer has always been
        /// correct on: that one carries the table's own closing footer, under its final row.
        /// </para>
        /// </remarks>
        [Theory]
        [InlineData("<table style='width:150pt'><tfoot><tr><td>FOOTWORD</td></tr></tfoot>"
                    + "<tbody><tr><td>{W}</td></tr></tbody></table>")]
        [InlineData("<table style='width:150pt'><tfoot><tr><td>FOOTWORD</td><td>B</td></tr></tfoot>"
                    + "<tbody><tr><td>{W}</td><td>short</td></tr></tbody></table>")]
        public async Task ARepeatedFooter_SitsBelowTheContinuationItClosesOver(string markup)
        {
            var (_, container) = await Paginate(markup);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers.SkipLast(1))
            {
                var words = Flatten(fragmentainer.Root).SelectMany(f => f.Words).ToList();

                var footer = Assert.Single(words, w => w.Word.Text == "FOOTWORD");
                var body = words.Where(w => w.Word.Text!.StartsWith("word")).ToList();

                Assert.NotEmpty(body);

                Assert.All(body, word => Assert.True(word.Rect.Bottom <= footer.Rect.Top,
                    $"'{word.Word.Text}' ends at {word.Rect.Bottom} in slot {fragmentainer.SlotIndex}, "
                    + $"below the repeated footer's top edge at {footer.Rect.Top}"));
            }
        }

        /// <summary>
        /// Lays <paramref name="markup"/> out over 244 words and checks it really does span more than one
        /// fragmentainer, since neither theory above asserts anything if it does not.
        /// </summary>
        /// <remarks>
        /// Asked of the fragmentainer count rather than of the table's own extent, which stopped being the
        /// right question once a table could fragment: a table continuing across a <i>column</i> boundary
        /// measures one fragment, so its box spans well under a band while the document it is in spans
        /// several.
        /// </remarks>
        private static async Task<(CssBox Root, HtmlContainerInt Container)> Paginate(string markup)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(markup.Replace("{W}", Words(244))),
                pageHeight: PageHeight, margin: Margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "fixture does not paginate, so it asserts nothing");

            return (root, container);
        }

        private static List<CssRect> AuthoredWords(CssBox root)
        {
            var words = LayoutHarness.Descendants(root).SelectMany(b => b.Words).ToList();

            Assert.NotEmpty(words);

            return words;
        }

        /// <summary>
        /// Which fragmentainer slots claim each word, keyed by the word. A word claimed by more than one
        /// is a line drawn on two pages; a word in no key at all was dropped.
        /// </summary>
        private static Dictionary<CssRect, List<int>> WordClaims(HtmlContainerInt container)
        {
            var claims = new Dictionary<CssRect, List<int>>(ReferenceEqualityComparer.Instance);

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                foreach (var word in Flatten(fragmentainer.Root).SelectMany(f => f.Words))
                {
                    if (!claims.TryGetValue(word.Word, out var slots)) claims[word.Word] = slots = [];
                    slots.Add(fragmentainer.SlotIndex);
                }
            }

            return claims;
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
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

            var (root, container) = await LayoutHarness.LayoutAsync(
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

            // This double never finishes, so the record it publishes is the same one every pass and the
            // driver's no-progress backstop is what has to end the run - not the pass cap. Stated as a
            // count rather than as elapsed time, which is a clock and would only say "slow".
            // This double never finishes, so every pass publishes the same record and the driver's
            // no-progress backstop is what has to end the run - not the 100,000-pass cap. Stated as counts
            // rather than as elapsed time, which is a clock and would only ever say "slow".
            Assert.Equal(1, container.LastResortRelayouts);
            Assert.InRange(container.FragmentainerPasses, 1, 4);
        }

        /// <summary>
        /// Two records naming the same stop are equal even though every pass builds fresh collections for
        /// them, which is what lets the driver's no-progress backstop recognize a table that got nowhere.
        /// </summary>
        /// <remarks>
        /// A <c>record</c>'s compiler-generated equality compares its members with <c>EqualityComparer</c>,
        /// so a list member is compared by reference and two identical records never matched. The backstop
        /// is an equality test, so it silently never fired for a table: measured before this, a cell that
        /// never finishes drove 100,000 passes instead of one fallback.
        /// </remarks>
        [Fact]
        public async Task TwoRecordsNamingTheSameStop_AreEqual()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<table><tr><td id='a'>a</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var table = TableOf(root);
            var cell = LayoutHarness.FindById(root, "a")!;
            var token = new InlineBreakToken(cell, ResumeSlotIndex: 1, ResumePath: [],
                ResumeWordIndex: 0, CompletedLineCount: 1);

            Assert.Equal(Record(table, cell, token, row: 0), Record(table, cell, token, row: 0));
            Assert.NotEqual(Record(table, cell, token, row: 0), Record(table, cell, token, row: 1));

            // The cells are the part the generated equality got wrong, so they need their own statement.
            Assert.NotEqual(
                Record(table, cell, token, row: 0),
                new TableBreakToken(table, 1, 0, 0, [], [], new Dictionary<int, IReadOnlyList<CssBox>>()));

            // Hashing has to agree with equality, or a record put in a set is a record that cannot be
            // found again - the obligation any hand-written Equals takes on.
            Assert.Equal(Record(table, cell, token, row: 0).GetHashCode(),
                Record(table, cell, token, row: 0).GetHashCode());
            Assert.Single(new HashSet<TableBreakToken>
            {
                Record(table, cell, token, row: 0),
                Record(table, cell, token, row: 0)
            });

            // The rowspan map is compared by contents too, key by key.
            var spanned = new TableBreakToken(table, 1, 0, 0, [], [],
                new Dictionary<int, IReadOnlyList<CssBox>> { [2] = new[] { cell } });

            Assert.Equal(spanned, new TableBreakToken(table, 1, 0, 0, [], [],
                new Dictionary<int, IReadOnlyList<CssBox>> { [2] = new[] { cell } }));
            Assert.NotEqual(spanned, new TableBreakToken(table, 1, 0, 0, [], [],
                new Dictionary<int, IReadOnlyList<CssBox>> { [3] = new[] { cell } }));
            Assert.NotEqual(spanned, new TableBreakToken(table, 1, 0, 0, [], [],
                new Dictionary<int, IReadOnlyList<CssBox>>()));

            return;

            // A fresh record every call, with fresh collections - which is the whole point.
            static TableBreakToken Record(CssBox table, CssBox cell, BreakToken token, int row) =>
                new(table, ResumeSlotIndex: 1, row, MaxRight: 0,
                    [new UnfinishedTableCell(row, cell, token)], [],
                    new Dictionary<int, IReadOnlyList<CssBox>>());
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

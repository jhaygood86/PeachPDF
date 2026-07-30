using PeachPDF.Html.Adapters;
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
    /// The half of <c>CssLayoutEngineTable</c>'s work that is settled once for a table rather than once per
    /// fragmentainer pass (<see href="https://github.com/jhaygood86/PeachPDF/issues/390">#390</see>
    /// stage 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine is constructed afresh every time it runs, and until now it also decided everything
    /// afresh. That is right for the reasons it usually runs again over a table it has already laid out —
    /// the per-page-width reflow loop, <c>ShrinkToFit</c>, a §4.3 relocation laying the subtree out again
    /// at its destination — because each of those is a fresh layout that has to start from the markup. A
    /// resumed pass is the one run that is not: it continues a table whose earlier rows are already
    /// emitted, and five of the things the engine does unconditionally destroy that table when done again
    /// on top of it.
    /// </para>
    /// <para>
    /// <b>No table receives a resumption record today</b> — <c>resume</c> is null at every table arm of
    /// <c>CssBox.LayoutContents</c> — so every guard here is unreachable from markup and the whole change
    /// is behaviour-neutral by construction. These tests reach it the only way there is: by running the
    /// engine again over a table the document already laid out, with a record in hand.
    /// </para>
    /// </remarks>
    public class TableOncePerTableTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        private static CssBox TableOf(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.Display == CssConstants.Table);

        private static List<CssProxyBox> ProxiesOf(CssBox table) =>
            table.Boxes.OfType<CssProxyBox>().ToList();

        /// <summary>
        /// A record standing for "an earlier pass laid part of this table out and stopped". Its contents
        /// are not read by anything yet — what it says today is only that this run is a continuation.
        /// </summary>
        private static BreakToken Continuation(CssBox table) =>
            new BlockBreakToken(table, ResumeSlotIndex: 1, ResumeChildIndex: 0, ChildToken: null,
                IsBreakBefore: false, ResumeTopOverride: null);

        /// <summary>
        /// Runs the table engine over <paramref name="table"/> the way production does — inside the
        /// fragmentainer detach <c>CssBox.LayoutMonolithicContent</c> wraps it in, without which a cell
        /// would see a fragmentainer the real call never gives it.
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

        /// <summary>
        /// Lays <paramref name="markup"/> out and then hands the laid-out table back to
        /// <paramref name="continuePass"/>, which runs the engine over it again.
        /// </summary>
        private static async Task WithALaidOutTable(
            string markup, Func<CssBox, HtmlContainerInt, RGraphics, Task> continuePass) =>
            await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(markup), pageHeight: PageHeight, margin: Margin,
                after: (root, container, g) => continuePass(TableOf(root), container, g));

        private static string RepeatingHeaderTable(int rows) =>
            "<table style='width:150pt;font-size:10pt'><thead><tr><th>H</th></tr></thead><tbody>"
            + string.Concat(Enumerable.Range(0, rows).Select(i => $"<tr><td>row {i}</td></tr>"))
            + "</tbody></table>";

        private static string RepeatingFooterTable(int rows) =>
            "<table style='width:150pt;font-size:10pt'><tfoot><tr><td>F</td></tr></tfoot><tbody>"
            + string.Concat(Enumerable.Range(0, rows).Select(i => $"<tr><td>row {i}</td></tr>"))
            + "</tbody></table>";

        // ─── 1. The structure an earlier pass left behind is not restored ────────────────────────

        /// <summary>
        /// The restore drops every <c>CssProxyBox</c> and puts the detached <c>&lt;thead&gt;</c> back in
        /// the table's child list. On a continuation that would destroy every earlier page's repeated
        /// header, because the proxies are the only surviving reference to the detached group.
        /// </summary>
        [Fact]
        public async Task AContinuation_LeavesTheProxiesAnEarlierPassPlaced()
        {
            await WithALaidOutTable(RepeatingHeaderTable(40), async (table, container, g) =>
            {
                var placed = ProxiesOf(table);
                var source = placed[0].SourceBox;

                // The fixture really does repeat its header across pages, or this asserts nothing.
                Assert.True(placed.Count >= 2, $"fixture placed {placed.Count} header proxies");
                Assert.DoesNotContain(source, table.Boxes);

                await RunEngine(g, container, table, Continuation(table));

                Assert.All(placed, proxy => Assert.Contains(proxy, table.Boxes));
                Assert.DoesNotContain(source, table.Boxes);
                Assert.Null(source.ParentBox);
            });
        }

        /// <summary>
        /// A fresh layout of the same table still starts from the markup — which is what stops the guard
        /// from being "once per box". Every reason the engine runs again other than a resumed pass is one.
        /// </summary>
        [Fact]
        public async Task AFreshRunOverAnAlreadyLaidOutTable_StillRestoresTheMarkupsStructure()
        {
            await WithALaidOutTable(RepeatingHeaderTable(40), async (table, container, g) =>
            {
                var placed = ProxiesOf(table);
                var source = placed[0].SourceBox;

                await RunEngine(g, container, table, resume: null);

                // Every proxy the first run left is gone — which only the restore does — and the run put
                // the same detached group back and repeated it afresh rather than accumulating on top of
                // what was there.
                Assert.All(placed, proxy => Assert.DoesNotContain(proxy, table.Boxes));
                Assert.Equal(placed.Count, ProxiesOf(table).Count);
                Assert.All(ProxiesOf(table), proxy => Assert.Same(source, proxy.SourceBox));
            });
        }

        /// <summary>
        /// A record for a table that has never been laid out names a setup that does not exist. Rather
        /// than dereferencing it, such a run starts from the markup — the safe reading, since a table with
        /// nothing settled has nothing an earlier pass could be destroyed by.
        /// </summary>
        [Fact]
        public async Task AContinuationOfATableWithNoSettledSetup_LaysOutFromTheStart()
        {
            await WithALaidOutTable(RepeatingHeaderTable(40), async (table, container, g) =>
            {
                var placed = ProxiesOf(table);
                var source = placed[0].SourceBox;

                table.TableSetup = null;

                await RunEngine(g, container, table, Continuation(table));

                // The restore ran: the earlier run's proxies are gone rather than added to, and the same
                // detached group is behind the ones that replaced them.
                Assert.All(placed, proxy => Assert.DoesNotContain(proxy, table.Boxes));
                Assert.All(ProxiesOf(table), proxy => Assert.Same(source, proxy.SourceBox));
                Assert.NotNull(table.TableSetup);
            });
        }

        /// <summary>
        /// A run publishes what it settled only once it has finished. A fresh run that dies between
        /// detaching the repeating group and recording it would otherwise leave a <b>non-null but empty</b>
        /// setup on a table whose <c>&lt;thead&gt;</c> is already out of the tree — and a later continuation
        /// would then inherit nothing while skipping the restore, which is the only way back to that group
        /// through its proxies. Leaving the setup null is what lets such a table be laid out again at all.
        /// </summary>
        [Fact]
        public async Task AFreshRunThatDiesWhileMeasuringTheHeader_LeavesNoSettledSetup()
        {
            await WithALaidOutTable(RepeatingHeaderTable(3), async (table, container, g) =>
            {
                var headerRow = table.TableSetup!.Header!.Box.Boxes
                    .First(b => b.Display == CssConstants.TableRow);

                // Replaces a header cell in place rather than being inserted beside it - a row's cells are
                // its columns, and ParentBox's setter appends.
                var thrower = new ThrowingCell(headerRow);
                headerRow.Boxes.Remove(thrower);
                headerRow.Boxes[0] = thrower;

                await Assert.ThrowsAnyAsync<Exception>(
                    async () => await RunEngine(g, container, table, resume: null));

                Assert.Null(table.TableSetup);
            });
        }

        /// <summary>
        /// A table cell whose layout fails, so a run can be stopped between two steps that no markup can
        /// separate. It overrides <c>PerformLayoutImp</c> wholesale, as the stopping cell in
        /// <see cref="TableCellBreakTokenTests"/> does and for the same reason.
        /// </summary>
        private sealed class ThrowingCell : CssBox
        {
            internal ThrowingCell(CssBox row) : base(row, null)
            {
                InheritStyle(row, everything: true);
                Display = CssConstants.TableCell;
            }

            protected override ValueTask PerformLayoutImp(RGraphics g, CssBox frame, bool framePlacesChild) =>
                throw new InvalidOperationException("layout failed while the header was being measured");
        }

        // ─── 2. The page-break bottoms earlier passes recorded accumulate ────────────────────────

        /// <summary>
        /// <c>PageBreakBottoms</c> is where the table's slice ended on each page, and it is what clips the
        /// table's borders there. Clearing it on a continuation would throw away every page an earlier
        /// pass filled.
        /// </summary>
        [Fact]
        public async Task AContinuation_KeepsThePageBreakBottomsEarlierPassesRecorded()
        {
            await WithALaidOutTable(RepeatingHeaderTable(40), async (table, container, g) =>
            {
                var recorded = table.PageBreakBottoms;
                Assert.NotNull(recorded);
                Assert.NotEmpty(recorded);

                // A slot no run of this table will visit, so only carrying the dictionary over can
                // leave it standing.
                recorded[999] = 4242d;

                await RunEngine(g, container, table, Continuation(table));

                Assert.Same(recorded, table.PageBreakBottoms);
                Assert.Equal(4242d, table.PageBreakBottoms![999]);
            });
        }

        /// <summary>
        /// A fresh run still clears it, so a re-layout does not accumulate stale entries.
        /// </summary>
        [Fact]
        public async Task AFreshRunOverAnAlreadyLaidOutTable_StillClearsThePageBreakBottoms()
        {
            await WithALaidOutTable(RepeatingHeaderTable(40), async (table, container, g) =>
            {
                table.PageBreakBottoms![999] = 4242d;

                await RunEngine(g, container, table, resume: null);

                Assert.DoesNotContain(999, table.PageBreakBottoms!.Keys);
            });
        }

        // ─── 3. The whole-table pre-checks do not move a table already part-emitted ──────────────

        /// <summary>
        /// Both whole-table pre-checks move <c>_tableBox.Location</c>, and a continuation's earlier rows
        /// are already emitted at coordinates that position is what makes sense of. Each case runs the
        /// engine twice from the <i>same</i> position — once as a continuation and once fresh — so the
        /// fresh run is what proves the pre-check would otherwise have fired.
        /// </summary>
        /// <param name="markup">
        /// the headerless pre-check's shape (a body that fits one page) and the repeating-header one's.
        /// </param>
        [Theory]
        [InlineData("<table style='width:150pt;font-size:10pt'><tr><td>one</td></tr>"
                    + "<tr><td>two</td></tr></table>")]
        [InlineData("<table style='width:150pt;font-size:10pt'><thead><tr><th>H</th></tr></thead>"
                    + "<tbody><tr><td>one</td></tr><tr><td>two</td></tr></tbody></table>")]
        public async Task AContinuation_IsNotMovedByAWholeTablePreCheck(string markup)
        {
            await WithALaidOutTable(markup, async (table, container, g) =>
            {
                // A few points short of the second page's top: the first row cannot fit what is left.
                var straddling = container.PageTopOf(1) - 5;

                table.Location = table.Location with { Y = straddling };
                await RunEngine(g, container, table, Continuation(table));
                var afterContinuation = table.Location.Y;

                table.Location = table.Location with { Y = straddling };
                await RunEngine(g, container, table, resume: null);
                var afterFreshRun = table.Location.Y;

                Assert.Equal(straddling, afterContinuation, 3);
                Assert.True(afterFreshRun > straddling + 1,
                    $"the pre-check did not fire on the fresh run either ({afterFreshRun:F1}), so the "
                    + "continuation staying put says nothing");
            });
        }

        /// <summary>
        /// The <c>margin-left: auto</c> centering in the engine's entry point is the same hazard on the
        /// other axis: the offset comes from the containing block rather than from where the table
        /// currently is, so applying it again centers an already-centered table a second time.
        /// </summary>
        [Fact]
        public async Task AContinuation_IsNotCenteredASecondTime()
        {
            await WithALaidOutTable(
                "<table style='width:100pt;margin:0 auto;font-size:10pt'><tr><td>one</td></tr></table>",
                async (table, container, g) =>
                {
                    var centered = table.Location.X;

                    await RunEngine(g, container, table, Continuation(table));
                    var afterContinuation = table.Location.X;

                    table.Location = table.Location with { X = centered };
                    await RunEngine(g, container, table, resume: null);

                    // The fresh run is the control: it re-derives the same offset from the same
                    // containing block, which is exactly why adding it to an already-centered table
                    // moves it.
                    Assert.True(table.Location.X > centered + 1,
                        $"centering is not re-applied on a fresh run either ({table.Location.X:F1})");
                    Assert.Equal(centered, afterContinuation, 3);
                });
        }

        // ─── 4. The header and footer are not measured again ─────────────────────────────────────

        /// <summary>
        /// The measurement lays the one shared source subtree out at the table's own top, and every proxy
        /// of it holds a snapshot taken from that subtree at its own position. Re-measuring on a
        /// continuation re-positions a subtree whose earlier snapshots are already frozen in the fragment
        /// emitter, so the height comes off what the earlier pass settled instead.
        /// </summary>
        /// <remarks>
        /// Asserted by giving two continuations two different inherited heights and reading the gap back
        /// out of where the first body row lands. A run that measured the header itself would place both
        /// rows identically.
        /// </remarks>
        [Fact]
        public async Task AContinuation_ReservesTheHeaderHeightTheEarlierPassMeasured()
        {
            const double inheritedA = 40d;
            const double inheritedB = 90d;

            var firstRowTops = new List<double>();

            foreach (var inherited in new[] { inheritedA, inheritedB })
            {
                await WithALaidOutTable(RepeatingHeaderTable(3), async (table, container, g) =>
                {
                    var settled = table.TableSetup!.Header;
                    Assert.NotNull(settled);
                    table.TableSetup.Header = settled with { Height = inherited };

                    await RunEngine(g, container, table, Continuation(table));

                    var firstBodyRow = LayoutHarness.Descendants(table)
                        .First(b => b.Display == CssConstants.TableRow
                                    && b.ParentBox?.Display == CssConstants.TableRowGroup);

                    firstRowTops.Add(firstBodyRow.Location.Y);
                });
            }

            Assert.Equal(inheritedB - inheritedA, firstRowTops[1] - firstRowTops[0], 3);
        }

        /// <summary>
        /// A continuation leaves what the earlier pass settled exactly as it found it. The index is the
        /// part with teeth: it is where the detached group has to go back when the table is next laid out
        /// from the markup, and a continuation that detached again would read it off a group that is not
        /// in the child list any more and record <c>-1</c>.
        /// </summary>
        [Fact]
        public async Task AContinuation_LeavesWhatTheEarlierPassSettledUntouched()
        {
            await WithALaidOutTable(RepeatingHeaderTable(40), async (table, container, g) =>
            {
                var settled = table.TableSetup!.Header;
                Assert.NotNull(settled);
                Assert.True(settled.Index >= 0, "the fixture recorded no index to lose");

                await RunEngine(g, container, table, Continuation(table));

                Assert.Equal(settled, table.TableSetup!.Header);
            });
        }

        /// <summary>
        /// The header and footer boxes themselves are inherited too, and the proof is that a continuation
        /// still repeats them: creating either proxy is gated on the box being there
        /// (<c>_headerBox != null</c>) and on the height being non-zero (<c>_footerHeight &gt; 0</c>),
        /// neither of which a run that skipped the measurement can work out for itself.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task AContinuation_StillRepeatsTheGroupTheEarlierPassDetached(bool header)
        {
            var markup = header ? RepeatingHeaderTable(40) : RepeatingFooterTable(40);

            await WithALaidOutTable(markup, async (table, container, g) =>
            {
                var before = ProxiesOf(table).Count;
                Assert.True(before > 0, "the fixture placed no proxy at all");

                await RunEngine(g, container, table, Continuation(table));

                Assert.True(ProxiesOf(table).Count > before,
                    "the continuation repeated the detached group on none of its own pages");
            });
        }

        /// <summary>
        /// A continuation of a table with neither group settles nothing and inherits nothing — the guards
        /// are about what an earlier pass left, not about tables in general.
        /// </summary>
        [Fact]
        public async Task AContinuationOfATableWithNoRepeatingGroup_InheritsNeither()
        {
            await WithALaidOutTable(
                "<table style='width:150pt;font-size:10pt'><tr><td>one</td></tr></table>",
                async (table, container, g) =>
                {
                    Assert.Null(table.TableSetup!.Header);
                    Assert.Null(table.TableSetup.Footer);

                    await RunEngine(g, container, table, Continuation(table));

                    Assert.Empty(ProxiesOf(table));
                });
        }
    }
}

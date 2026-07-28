using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A table cell that finished in an earlier fragmentainer still has a box on its row's continuation
    /// (<see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cells of one row are
    /// <see href="https://www.w3.org/TR/css-break-3/#parallel-flows">css-break-3 §2.1</see> parallel
    /// flows, so a row can stop with some cells finished and others not. Its box continues into the next
    /// fragmentainer regardless and §6.1 has <i>every</i> cell's box continue with it — the finished one
    /// as its borders and background running that fragment's depth, with no content in it.
    /// </para>
    /// <para>
    /// <b>Nothing here can be a word census.</b> The subject is a fragment that was not emitted at all,
    /// and the repository's standing "every word is claimed exactly once" check is satisfied both before
    /// and after: no word moves, and none is added. What changes is the number of <see cref="BoxFragment"/>s
    /// the finished cell produces and the geometry of the new one, so that is what is asserted — with
    /// <see cref="AnUnfragmentedRow_GivesItsCellsOneFragmentEach"/> as the control that stops
    /// "it has two fragments" passing against a change that gives everything two.
    /// </para>
    /// </remarks>
    public class TableFinishedCellContinuationTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        private static string Words(int count) =>
            string.Join(" ", Enumerable.Range(0, count).Select(i => $"word{i:0000}"));

        /// <summary>
        /// A row whose right-hand cell holds more than a page, beside a left-hand one that finishes on the
        /// first. The border is what makes the continuation observable at all.
        /// </summary>
        private static string TwoCellRow(int words) => LayoutHarness.Wrap(
            "<table style='width:150pt'><tr>"
            + "<td id='short' style='border:1pt solid #000'>short</td>"
            + $"<td id='long'>{Words(words)}</td>"
            + "</tr></table>");

        private static async Task<(CssBox Root, HtmlContainerInt Container)> PaginateAsync(int words = 244)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                TwoCellRow(words), pageHeight: PageHeight, margin: Margin);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "fixture does not paginate, so it asserts nothing");

            return (root, container);
        }

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }

        private static List<BoxFragment> FragmentsOf(HtmlContainerInt container, string id) =>
        [
            .. container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == id)
                .OrderBy(f => f.FragmentainerIndex)
        ];

        /// <summary>
        /// The cell that finished has a fragment on the row's continuation, and it holds none of the
        /// cell's content — that half is what an earlier pass already emitted, and re-placing it is the
        /// duplication the row loop's skip exists to prevent.
        /// </summary>
        [Fact]
        public async Task AFinishedCell_ContinuesAsAContentFreeFragment()
        {
            var (_, container) = await PaginateAsync();

            var fragments = FragmentsOf(container, "short");

            Assert.True(fragments.Count >= 2,
                $"the finished cell produced {fragments.Count} fragment(s); its box does not continue");

            foreach (var continuation in fragments.Skip(1))
            {
                Assert.Empty(continuation.Words);
                Assert.Empty(continuation.Children);

                // One whole-box decoration rectangle, which is what a block-level box's fragment is: the
                // null Line is the fragment-tree form of "this is the border box, not a line box".
                var line = Assert.Single(continuation.Lines);
                Assert.Null(line.Line);
            }
        }

        /// <summary>
        /// The continuation runs the full depth of the row's fragment there — it covers every line the
        /// cell beside it placed in that fragmentainer, which is what that depth <i>is</i>. This is the
        /// assertion the issue is about; every other one here guards it.
        /// </summary>
        /// <remarks>
        /// Stated against the continuing cell's <b>words</b> rather than against its
        /// <see cref="BoxFragment.Rect"/>, because that rectangle is not the row's depth on this page: a
        /// cell spanning fragmentainers keeps the one <c>Location</c> its first fragment was built from,
        /// so its own bounds mapped into a later fragmentainer's space start well above that
        /// fragmentainer (measured at −238.5 on a 300pt page). The words are positioned by the flow, which
        /// is the thing that actually knows where this fragment's content went.
        /// </remarks>
        [Fact]
        public async Task AFinishedCellsContinuation_RunsTheDepthOfTheRowsFragment()
        {
            var (_, container) = await PaginateAsync();

            var shells = FragmentsOf(container, "short").Skip(1).ToList();

            Assert.NotEmpty(shells);

            foreach (var shell in shells)
            {
                var fragmentainer = container.FragmentTree!.Fragmentainers
                    .Single(f => f.SlotIndex == shell.FragmentainerIndex);

                // Every word on this page belongs to the cell that continues onto it: the finished cell's
                // own text was emitted on the page it finished on, and the document holds nothing else.
                var words = Flatten(fragmentainer.Root).SelectMany(f => f.Words).ToList();

                Assert.NotEmpty(words);

                Assert.True(shell.Rect.Top <= words.Min(w => w.Rect.Top) + 1,
                    $"the continuation starts at {shell.Rect.Top}, below the first line beside it at "
                    + $"{words.Min(w => w.Rect.Top)}");

                Assert.True(shell.Rect.Bottom >= words.Max(w => w.Rect.Bottom) - 1,
                    $"the continuation ends at {shell.Rect.Bottom}, above the last line beside it at "
                    + $"{words.Max(w => w.Rect.Bottom)}");
            }
        }

        /// <summary>
        /// It keeps its column. CSS Fragmentation Level 3 §2 shares one inline size and position across a
        /// box's fragments, and the row loop's own column cursor is what states it here.
        /// </summary>
        [Fact]
        public async Task AFinishedCellsContinuation_KeepsItsColumn()
        {
            var (_, container) = await PaginateAsync();

            var fragments = FragmentsOf(container, "short");
            var first = fragments[0];

            Assert.True(fragments.Count > 1, "the finished cell's box does not continue, so this asserts nothing");

            foreach (var shell in fragments.Skip(1))
            {
                Assert.Equal(first.Rect.Left, shell.Rect.Left, 3);
                Assert.Equal(first.Rect.Width, shell.Rect.Width, 3);
            }
        }

        /// <summary>
        /// The emitter did this, not a second <c>Location</c>. A box carries exactly one, and the earlier
        /// fragment was built from it — so a continuation that writes its own row top into the box
        /// retracts that fragment's geometry, which was measured as a whole table disappearing from the
        /// page it began on.
        /// </summary>
        /// <remarks>
        /// The regression a future "just place it again" would trip, stated where it is observable:
        /// <c>TableRowLoopResumptionTests</c> pins the same rule on the box, and this pins that the
        /// fragment tree gained a fragment without the box gaining a position.
        /// </remarks>
        [Fact]
        public async Task AFinishedCellsContinuation_DoesNotMoveTheBoxTheFirstFragmentWasBuiltFrom()
        {
            var (root, container) = await PaginateAsync();

            var cell = LayoutHarness.FindById(root, "short")!;
            var first = FragmentsOf(container, "short")[0];
            var origin = container.FragmentTree!.Fragmentainers
                .Single(f => f.SlotIndex == first.FragmentainerIndex).LocalOriginY;

            Assert.Equal(cell.Location.Y - origin, first.Rect.Top, 3);
            Assert.Equal(cell.Location.X, first.Rect.Left, 3);
        }

        /// <summary>
        /// Each fragment owns only the block-axis edges the break did not make
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>): with
        /// <c>box-decoration-break: slice</c> — the initial value — the box is rendered unbroken and then
        /// cut, so no border is drawn at a fragmentation break on either side of it.
        /// </summary>
        [Fact]
        public async Task AFinishedCellsFragments_OwnOnlyTheBlockEdgesTheBreakDidNotMake()
        {
            var (_, container) = await PaginateAsync();

            var fragments = FragmentsOf(container, "short");
            var last = fragments.Count - 1;

            Assert.True(last > 0, "the finished cell has one fragment, so there is no break edge to state");

            for (var i = 0; i <= last; i++)
            {
                var slice = Assert.Single(fragments[i].Lines).Slice;

                Assert.Equal(i == 0, slice.HasTopEdge);
                Assert.Equal(i == last, slice.HasBottomEdge);

                // The inline axis is nobody's break here - a page's fragmentainers differ in the block
                // axis alone.
                Assert.True(slice.HasLeftEdge);
                Assert.True(slice.HasRightEdge);
            }
        }

        /// <summary>
        /// The continuation stops where the row does. A box states its continuation for the fragmentainers
        /// its row actually reaches, so a page beyond the table gets nothing — the fragment is asked for by
        /// rectangle, and there is none in that band.
        /// </summary>
        /// <remarks>
        /// The failure this guards against is a shell that repeats to the end of the document, which is
        /// what asking "does this box have a shell at all" instead of "does it have one here" would
        /// produce. Nothing else reaches <c>FragmentEmitter.ShellIn</c>'s "not in this region" answer.
        /// </remarks>
        [Fact]
        public async Task AFinishedCellsContinuation_ReachesNoPageBeyondItsRow()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:150pt'><tr>"
                    + "<td id='short' style='border:1pt solid #000'>short</td>"
                    + $"<td id='long'>{Words(60)}</td>"
                    + "</tr></table>"
                    + string.Concat(Enumerable.Range(0, 40)
                        .Select(i => $"<p style='margin:0;height:20pt'>after{i}</p>"))),
                pageHeight: PageHeight, margin: Margin);

            var reached = FragmentsOf(container, "short").Select(f => f.FragmentainerIndex).ToList();
            var lastSlot = container.FragmentTree!.Fragmentainers.Max(f => f.SlotIndex);

            Assert.True(reached.Count > 1, "the row does not continue, so this asserts nothing");
            Assert.True(lastSlot > reached.Max(),
                $"the document ends at slot {lastSlot}, the last the row reaches, so this asserts nothing");

            // Every slot past the row is one the box states nothing for, and gets nothing.
            for (var slot = reached.Max() + 1; slot <= lastSlot; slot++)
            {
                Assert.DoesNotContain(slot, reached);
            }
        }

        /// <summary>
        /// The control. A row that fits gives each of its cells exactly one fragment — without this,
        /// "the finished cell has two" is satisfied by a change that gives every box two.
        /// </summary>
        [Fact]
        public async Task AnUnfragmentedRow_GivesItsCellsOneFragmentEach()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                TwoCellRow(words: 4), pageHeight: PageHeight, margin: Margin);

            Assert.Single(container.FragmentTree!.Fragmentainers);
            Assert.Single(FragmentsOf(container, "short"));
            Assert.Single(FragmentsOf(container, "long"));
        }

        /// <summary>
        /// Laying the same document out again produces the same number of fragments, rather than one more
        /// per pass. What layout states about a continuation is per-invocation state like any other, and
        /// the row loop discards what an earlier run stated before it states anything itself.
        /// </summary>
        [Fact]
        public async Task AFinishedCellsContinuation_IsNotAccumulatedAcrossLayouts()
        {
            var counts = await LayoutHarness.LayoutRepeatedlyAsync(
                TwoCellRow(words: 244),
                passes: 3,
                snapshot: (_, container) => container.FragmentTree!.Fragmentainers
                    .SelectMany(f => Flatten(f.Root))
                    .Count(f => f.Box.HtmlTag?.TryGetAttribute("id") == "short"),
                pageHeight: PageHeight,
                margin: Margin);

            Assert.True(counts[0] > 1, "fixture does not continue the row, so it asserts nothing");
            Assert.Equal(new[] { counts[0], counts[0], counts[0] }, counts);
        }

        /// <summary>
        /// A repeated <c>&lt;thead&gt;</c> sits above the continuation rather than over it, and the
        /// finished cell's box begins beneath it — the row cursor's own top is what states the geometry,
        /// and css-tables-3 §6.2's reserved room has already been taken out of it.
        /// </summary>
        [Fact]
        public async Task AFinishedCellsContinuation_BeginsBelowARepeatedHeader()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:150pt'>"
                    + "<thead><tr><th>HEADERWORD</th><th>H2</th></tr></thead>"
                    + "<tbody><tr>"
                    + "<td id='short' style='border:1pt solid #000'>short</td>"
                    + $"<td id='long'>{Words(244)}</td>"
                    + "</tr></tbody></table>"),
                pageHeight: PageHeight, margin: Margin);

            var fragments = FragmentsOf(container, "short");

            Assert.True(fragments.Count > 1, "fixture does not continue the row, so it asserts nothing");

            foreach (var shell in fragments.Skip(1))
            {
                var header = container.FragmentTree!.Fragmentainers
                    .Single(f => f.SlotIndex == shell.FragmentainerIndex);

                var headerWord = Flatten(header.Root)
                    .SelectMany(f => f.Words)
                    .Single(w => w.Word.Text == "HEADERWORD");

                Assert.True(shell.Rect.Top >= headerWord.Rect.Bottom,
                    $"the continuation starts at {shell.Rect.Top}, above the repeated header's "
                    + $"bottom edge at {headerWord.Rect.Bottom}");
            }
        }
    }
}

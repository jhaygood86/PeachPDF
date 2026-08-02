using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A table cell whose own content spans more than one real per-pass fragment slices its top/bottom
    /// border like any other box under <c>box-decoration-break: slice</c>
    /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>;
    /// <see href="https://www.w3.org/TR/css-tables-3/#fragmentation">css-tables-3 §6.1</see> states the
    /// same rule for cells specifically — "top borders must not be repainted in continuation fragments").
    /// </summary>
    /// <remarks>
    /// Before <see href="https://github.com/jhaygood86/PeachPDF/issues/590">issue #590</see>,
    /// <c>FragmentEmitter.RecordChain</c> only walked a <c>BlockBreakToken</c>'s linear <c>ChildToken</c>
    /// chain, so it never descended into a <c>TableBreakToken</c>'s per-cell continuations
    /// (<c>TableRowCursor.UnfinishedCells</c>) — a cell whose own content kept producing genuinely new
    /// fragments (as against a stated continuation shell, which the edge predicates already recognized
    /// through <c>Draft.ShellRect</c>) reported every one of its fragments as owning both its own top and
    /// bottom edge. This is the plain, non-rowspan fixture the issue's own reproduction describes — a
    /// single <c>&lt;td&gt;</c> with no second row and no rowspan, whose content alone spans several bands.
    /// </remarks>
    public class TableCellOwnContentBreakEdgesTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        private static string SingleCellSpanningSeveralBands() => LayoutHarness.Wrap(
            "<table style='width:150pt;border-collapse:collapse'><tr>"
            + "<td id='cell' style='border:1pt solid black'>"
            + string.Join(" ", Enumerable.Range(0, 400).Select(i => $"word{i:0000}"))
            + "</td></tr></table>");

        private static IEnumerable<BoxFragment> Flatten(BoxFragment fragment)
        {
            yield return fragment;

            foreach (var child in fragment.Children)
            {
                foreach (var descendant in Flatten(child)) yield return descendant;
            }
        }

        /// <summary>
        /// The cell's fragments, one entry per fragmentainer it appears in, in fill order.
        /// </summary>
        private static List<BoxFragment> CellFragments(HtmlContainerInt container) =>
        [
            .. container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .Where(f => f.Box.HtmlTag?.TryGetAttribute("id") == "cell")
                .GroupBy(f => f.FragmentainerIndex)
                .OrderBy(g => g.Key)
                .Select(g => g.First())
        ];

        /// <summary>
        /// A cell tall enough to need more than one real fragment reports a top edge only on the first
        /// of them and a bottom edge only on the last — never on every fragment, which is what every one
        /// of them reported before the fix (<c>RecordChain</c> never marked the cell as a continuation at
        /// all, so every fragment fell back to "this is a real edge").
        /// </summary>
        [Fact]
        public async Task ACellsOwnContentSpanningSeveralBands_OwnsOnlyTheBlockEdgesTheBreakDidNotMake()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                SingleCellSpanningSeveralBands(), pageHeight: PageHeight, margin: Margin);

            var fragments = CellFragments(container);
            var last = fragments.Count - 1;

            Assert.True(last > 0,
                "fixture produced only one fragment for the cell, so it asserts nothing about break edges");

            for (var i = 0; i <= last; i++)
            {
                var slice = Assert.Single(fragments[i].Lines).Slice;

                Assert.Equal(i == 0, slice.HasTopEdge);
                Assert.Equal(i == last, slice.HasBottomEdge);

                // A page's fragmentainers differ in the block axis alone, so the inline edges are nobody's
                // break and both survive on every fragment.
                Assert.True(slice.HasLeftEdge);
                Assert.True(slice.HasRightEdge);
            }
        }

        /// <summary>
        /// The control: a cell whose content fits in one band is one fragment that owns every one of its
        /// own edges — without this, "only the first/last fragment has an edge" would pass vacuously
        /// against a fixture that never produced more than one fragment to begin with.
        /// </summary>
        [Fact]
        public async Task ACellsOwnContentFittingOneBand_OwnsEveryEdgeOnItsOneFragment()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:150pt;border-collapse:collapse'>"
                    + "<tr><td id='cell' style='border:1pt solid black'>short</td></tr></table>"),
                pageHeight: PageHeight, margin: Margin);

            var fragments = CellFragments(container);
            var only = Assert.Single(fragments);
            var slice = Assert.Single(only.Lines).Slice;

            Assert.True(slice is { HasTopEdge: true, HasBottomEdge: true, HasLeftEdge: true, HasRightEdge: true });
        }
    }
}

using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Content laid out inside a monolithic subtree or a measurement pass has no fragmentainer to ask
    /// about at all, so it cannot answer a question about one it is not in.
    /// </summary>
    /// <remarks>
    /// Suppressing <i>breaking</i> while leaving the fragmentainer visible is not the same thing: several
    /// places ask <c>CurrentFragmentainer is { HasOwnBand: true }</c> without consulting whether breaking
    /// is live at all, so a table cell nested in a multi-column container reached the column arms of the
    /// block-children loop while the table engine — which places its own rows and never reads a
    /// resumption record — was laying it out.
    /// </remarks>
    public class SuppressedPassFragmentainerTests
    {
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

        private const string TableInMulticol =
            "<div style='column-count:2;column-gap:20pt;width:300pt'>"
            + "<table style='width:100%'><tr><td>"
            + "<p>one two three four five</p>"
            + "<p>six seven eight nine ten</p>"
            + "</td></tr></table></div>";

        [Fact]
        public async Task ATableInAMultiColumnContainer_EmitsCellContent()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(TableInMulticol), pageHeight: 400, margin: 20);

            var laidOut = LayoutHarness.Descendants(root).SelectMany(b => b.Words).Count();
            var emitted = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(b => b.Words)
                .ToList();

            Assert.Equal(10, laidOut);

            // Characterization, not the invariant it should be: the cell's *first* block is emitted and
            // its second is not, so the table renders partially. Before a suppressed pass stopped naming
            // the enclosing column, the count here was 0 — the whole table was laid out and none of it
            // painted. The remaining half is a table nested in a multi-column container not being
            // fragmented by that container at all (issue #406); closing it turns this into an equality.
            Assert.Equal(5, emitted.Count);
            Assert.Equal(new[] { "one", "two", "three", "four", "five" },
                emitted.Select(w => w.Word.Text).ToArray());
        }

        [Fact]
        public async Task NoWordIsEmittedTwice_ByATableInAMultiColumnContainer()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(TableInMulticol), pageHeight: 400, margin: 20);

            var emitted = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(b => b.Words)
                .Select(w => w.Word)
                .ToList();

            // The half of the invariant that does hold, and the one a break raised against the wrong
            // fragmentainer breaks first: whatever is emitted is emitted once.
            Assert.Equal(emitted.Count, emitted.Distinct().Count());
        }

        [Fact]
        public async Task APlainTable_EmitsAllOfItsCellContent()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<table style='width:100%'><tr><td>"
                + "<p>one two three four five</p>"
                + "<p>six seven eight nine ten</p>"
                + "</td></tr></table>"), pageHeight: 400, margin: 20);

            var emitted = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(b => b.Words)
                .Count();

            // The control: the loss above is the multi-column nesting, not tables.
            Assert.Equal(LayoutHarness.Descendants(root).SelectMany(b => b.Words).Count(), emitted);
        }
    }
}

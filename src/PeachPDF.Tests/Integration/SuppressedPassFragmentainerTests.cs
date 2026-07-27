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

            // Everything laid out is emitted. This was drafted as a characterization at 0 (before a
            // suppressed pass stopped naming the enclosing column, the whole table was invisible) and
            // then at 5 (the cell's first block only, the rest below a column band derived from
            // balancing that a table cannot be fragmented to fit).
            Assert.Equal(laidOut, emitted.Count);
            Assert.Equal(new[] { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" },
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

            // The other half of the same invariant, and the one a break raised against the wrong
            // fragmentainer breaks first: whatever is emitted is emitted once. Widening a column's
            // recorded band to the one it filled could only fail this way.
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

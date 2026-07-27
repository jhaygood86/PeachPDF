using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A line box is a monolithic break unit
    /// (<see href="https://www.w3.org/TR/css-break-3/#possible-breaks">css-break-3 §4.1</see>), so it
    /// belongs to exactly one fragmentainer — including when it is a table cell's own text.
    /// </summary>
    /// <remarks>
    /// A cell used to be exempt from word flow's page-boundary check, which left its own text with no
    /// mechanism to stop a line straddling the boundary. The emitter then claimed that line in both
    /// bands and it painted sliced in half on each page. Wrapping the identical text in a block inside
    /// the same cell was already correct, because the nested box is not a cell — which is what showed
    /// the exemption to be a defect rather than a rule.
    /// </remarks>
    public class TableCellLineFragmentationTests
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

        private static string Words(int count) =>
            string.Join(" ", Enumerable.Range(1, count).Select(i => $"word{i}"));

        // The word count is swept rather than fixed: which line lands on the boundary depends on the
        // font the host resolves, so a single count would pin the defect on one machine and miss it on
        // another. 244 is where it reproduced here.
        [Theory]
        [InlineData("<table style='width:100%'><tr><td>{0}</td></tr></table>")]
        [InlineData("<table style='width:100%'><tr><td><p style='margin:0'>{0}</p></td></tr></table>")]
        [InlineData("<div>{0}</div>")]
        public async Task NoWordIsPaintedTwice_AtAPageBoundary(string wrapper)
        {
            for (var count = 200; count <= 320; count += 4)
            {
                var (root, container) = await LayoutHarness.LayoutAsync(
                    LayoutHarness.Wrap(string.Format(wrapper, Words(count))),
                    pageHeight: 300, margin: 20);

                var laidOut = LayoutHarness.Descendants(root)
                    .SelectMany(b => b.Words)
                    .Where(w => !w.IsSpaces)
                    .ToList();

                var emitted = container.FragmentTree!.Fragmentainers
                    .SelectMany(f => Flatten(f.Root))
                    .SelectMany(f => f.Words)
                    .Select(w => w.Word)
                    .ToList();

                Assert.True(emitted.Count == emitted.Distinct().Count(),
                    $"{count} words: {emitted.Count - emitted.Distinct().Count()} painted twice — a line "
                    + "straddling the boundary was claimed by both fragmentainers");

                // The other half of the same invariant: nothing was dropped to achieve it.
                Assert.All(laidOut, word => Assert.Contains(word, emitted));
            }
        }

        // The direct statement of §4.1 for the shape that used to break it: no line's own rectangle may
        // span a page boundary, so no page clip can cut one in half.
        [Fact]
        public async Task ACellsOwnLine_DoesNotSpanAPageBoundary()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<table style='width:100%'><tr><td>{Words(244)}</td></tr></table>"),
                pageHeight: 300, margin: 20);

            var boundary = container.PageTopOf(1);

            var straddling = LayoutHarness.Descendants(root)
                .SelectMany(b => b.Words)
                .Where(w => !w.IsSpaces && w.Top < boundary - 0.5 && w.Bottom > boundary + 0.5)
                .ToList();

            Assert.True(straddling.Count == 0,
                $"{straddling.Count} words straddle the boundary at {boundary:F1}, e.g. "
                + $"'{straddling.FirstOrDefault()?.Text}' at "
                + $"[{straddling.FirstOrDefault()?.Top:F1}, {straddling.FirstOrDefault()?.Bottom:F1}]");
        }

        // The cell still paginates: the fix moves the straddling line rather than truncating the cell.
        [Fact]
        public async Task ACellTallerThanAPage_StillPaginatesAllOfItsText()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<table style='width:100%'><tr><td>{Words(244)}</td></tr></table>"),
                pageHeight: 300, margin: 20);

            var laidOut = LayoutHarness.Descendants(root)
                .SelectMany(b => b.Words)
                .Where(w => !w.IsSpaces)
                .ToList();

            var emitted = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word)
                .ToHashSet();

            Assert.True(container.FragmentTree.Fragmentainers.Count > 1,
                "the fixture no longer spans more than one page; it asserts nothing as written");
            Assert.Equal(laidOut.Count, emitted.Count);
        }
    }
}

using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="Fragmentation.FragmentEmitter"/> resolves a word's fragmentainer through the line that
    /// owns it (<see cref="CssRect.Line"/>, <c>FragmentEmitter.ClaimsLine</c>) rather than the word's own
    /// rectangle - css-break-3 §4.1 makes a line box a monolithic break unit, so every word on one line
    /// must be claimed by the same fragmentainer even where a negative <c>line-height</c> lets its ink
    /// escape above the line box (CSS 2.1 §10.8.1, issue #1054). These tests exercise that at a real page
    /// and column boundary, and pin the shapes it must leave unaffected.
    /// </summary>
    public class LineBoxFragmentMembershipIntegrationTests
    {
        [Fact]
        public async Task NegativeLeadingLineAtAPageBoundary_ClaimsEveryWordOnTheSamePage()
        {
            const double PageHeight = 300;

            // The filler exactly fills page 0, so the paragraph's line has nowhere to start but page 1's
            // content top (PageHeight) - its baseline-driven, pre-escape position.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:300pt'></div>" +
                    "<p id='p' style='margin:0;font-size:20pt;line-height:10pt'>Alpha Beta Gamma</p>"),
                pageHeight: PageHeight, margin: 0);

            var p = LayoutHarness.FindById(root, "p")!;
            var words = LayoutHarness.Descendants(p).SelectMany(b => b.Words).ToList();
            Assert.NotEmpty(words);

            var font = p.ActualFont;
            var halfLeading = (p.ActualLineHeight - font.Height) / 2;
            Assert.True(halfLeading < -1, $"fixture needs a real negative leading; half-leading={halfLeading}");

            // Every word's ink actually starts above the nominal (pre-escape) line top - inside what would
            // otherwise look like page 0's own territory. This is exactly the risk issue #1054 describes:
            // if fragmentainer membership were still asked per word instead of per line, this escaped ink
            // could be claimed by the page above it, or by neither page, rather than moving as one unit
            // with the rest of its line.
            Assert.All(words, w => Assert.True(w.Top < PageHeight - 1,
                $"'{w.Text}' should show its escaped ink above the nominal line top ({PageHeight}), was {w.Top}"));

            var slots = words.Select(w => SlotsClaiming(container, w)).ToList();
            Assert.All(slots, s => Assert.True(s.Count == 1,
                $"a word was claimed by {s.Count} fragmentainer(s) instead of exactly one: [{string.Join(",", s)}]"));
            Assert.Single(slots.SelectMany(s => s).Distinct());
        }

        [Fact]
        public async Task MixedFontSizeLineWithNegativeLeading_StraddlingAColumnBoundary_StaysInOneColumn()
        {
            // Two spans sharing one line: a large span with an ordinary (positive) leading and a small
            // span whose declared line-height is shorter than its own font - negative leading. Swept over
            // a range of filler heights so the line's own position moves gradually across the column
            // boundary; whichever filler height lands it closest to the boundary is exactly the case that
            // would have split the two spans across columns under per-word membership.
            for (var fillerLines = 0; fillerLines < 40; fillerLines++)
            {
                var filler = string.Concat(Enumerable.Range(0, fillerLines)
                    .Select(i => $"<div style='height:8pt' id='f{i}'></div>"));

                var html = LayoutHarness.Wrap(
                    "<div id='mc' style='columns:2;column-gap:0;width:300pt;column-fill:auto'>" +
                    filler +
                    "<p id='mixed' style='margin:0'>" +
                    "<span id='big' style='font-size:30pt;line-height:40pt'>Big</span>" +
                    "<span id='small' style='font-size:10pt;line-height:5pt'>small</span>" +
                    "</p></div>");

                var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 400, margin: 0);

                var mc = LayoutHarness.FindById(root, "mc")!;
                var big = LayoutHarness.FindById(root, "big")!;
                var small = LayoutHarness.FindById(root, "small")!;

                var bigWords = LayoutHarness.Descendants(big).SelectMany(b => b.Words).ToList();
                var smallWords = LayoutHarness.Descendants(small).SelectMany(b => b.Words).ToList();

                if (bigWords.Count == 0 || smallWords.Count == 0) continue;

                var columnWidth = mc.ActualWidth / 2;
                var bigColumns = bigWords.Select(w => (int)((w.Left - mc.ClientLeft) / columnWidth)).Distinct().ToList();
                var smallColumns = smallWords.Select(w => (int)((w.Left - mc.ClientLeft) / columnWidth)).Distinct().ToList();

                Assert.True(bigColumns.Count == 1 && smallColumns.Count == 1 && bigColumns[0] == smallColumns[0],
                    $"fillerLines={fillerLines}: 'Big' landed in column(s) [{string.Join(",", bigColumns)}] " +
                    $"but 'small' landed in column(s) [{string.Join(",", smallColumns)}] - one physical line " +
                    "split across two columns");
            }
        }

        [Fact]
        public async Task ATableCellsTextLineStraddlingAPageBoundary_IsClaimedByOnePageOnly()
        {
            // Regression pin: ordinary (non-negative-leading) table-cell text at a page boundary is
            // unaffected by claiming fragmentainer membership through the line rather than the word.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:280pt'></div>" +
                    "<table style='width:150pt'><tbody><tr><td id='cell' style='font-size:10pt;line-height:14pt'>" +
                    string.Join(" ", Enumerable.Range(0, 80).Select(i => $"cellword{i}")) +
                    "</td></tr></tbody></table>"),
                pageHeight: 300, margin: 0);

            var cell = LayoutHarness.FindById(root, "cell")!;
            var words = LayoutHarness.Descendants(cell).SelectMany(b => b.Words).ToList();
            Assert.NotEmpty(words);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page to exercise a boundary at all");

            Assert.All(words, w => Assert.True(SlotsClaiming(container, w).Count == 1,
                $"'{w.Text}' should be claimed by exactly one page"));
        }

        [Fact]
        public async Task ARtlParagraphsLineStraddlingAPageBoundary_IsClaimedByOnePageOnly()
        {
            // Regression pin: an RTL line at a page boundary is unaffected by claiming fragmentainer
            // membership through the line rather than the word.
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<p id='p' dir='rtl' style='margin:0;direction:rtl;font-size:10pt;line-height:14pt;width:150pt'>" +
                    string.Join(" ", Enumerable.Range(0, 200).Select(i => $"מילה{i}")) +
                    "</p>"),
                pageHeight: 200, margin: 0);

            var p = LayoutHarness.FindById(root, "p")!;
            var words = LayoutHarness.Descendants(p).SelectMany(b => b.Words).ToList();
            Assert.NotEmpty(words);

            Assert.True(container.FragmentTree!.Fragmentainers.Count > 1,
                "the fixture must span more than one page to exercise a boundary at all");

            Assert.All(words, w => Assert.True(SlotsClaiming(container, w).Count == 1,
                $"'{w.Text}' should be claimed by exactly one page"));
        }

        /// <summary>
        /// The open design question the plan for this change left explicit: whether
        /// <c>FragmentEmitter</c>'s monolithic tie-break (<c>FallsPast</c>/<c>MonolithicContent.FitsNoFragmentainer</c>)
        /// should be sized against a whole physical line (every box sharing it) or against each box's own
        /// portion of that line. Resolved here in favor of staying box-keyed: <c>ClaimsLine</c> is asked
        /// once per <c>(box, line)</c> pair using that box's own <c>Rectangles[line]</c> - never a rectangle
        /// aggregated across sibling boxes on the same physical line - so a replaced element too tall for
        /// any fragmentainer is judged monolithic on its own dimensions, and ordinary text sharing its line
        /// is judged on its own, ordinary ones, rather than being dragged into the replaced element's
        /// "stays exactly where it is" treatment (issue #484) and lost.
        /// </summary>
        [Fact]
        public async Task AnOversizedReplacedElementNearAPageBoundary_DoesNotStrandTheOrdinaryTextBesideIt()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:700pt'></div>" +
                    "<p id='p' style='margin:0;font-size:10pt;line-height:12pt'>before " +
                    $"<img id='big' src='{RasterPngFixture.OnePixelDataUri}' style='width:10pt;height:900pt'> after</p>"),
                pageHeight: 842, margin: 0);

            var p = LayoutHarness.FindById(root, "p")!;
            var img = LayoutHarness.FindById(root, "big")!;

            var imageWord = LayoutHarness.Descendants(img).SelectMany(b => b.Words).Single();

            // The image is taller than any single page - monolithic overflow (css-break-3 §2) - so it is
            // clipped to the first fragmentainer it starts in rather than repeated on every page it
            // geometrically overlaps (issue #484).
            Assert.True(imageWord.Height > container.PageBottomOf(0) - container.PageTopOf(0),
                $"fixture needs an image taller than a whole page; height={imageWord.Height}");
            Assert.Single(SlotsClaiming(container, imageWord));

            var textWords = LayoutHarness.Descendants(p).SelectMany(b => b.Words)
                .Where(w => !ReferenceEquals(w, imageWord))
                .ToList();
            Assert.NotEmpty(textWords);

            // The ordinary text sharing the line with that oversized image is judged on its own dimensions
            // and claimed normally - not silently dropped by being aggregated with the image's own
            // "too tall for any fragmentainer" verdict.
            Assert.All(textWords, w => Assert.True(SlotsClaiming(container, w).Count == 1,
                $"'{w.Text}' should be claimed by exactly one page, not stranded alongside the oversized image"));
        }

        private static List<int> SlotsClaiming(HtmlContainerInt container, CssRect word) =>
            container.FragmentTree!.Fragmentainers
                .Where(f => Flatten(f.Root).SelectMany(b => b.Words).Any(w => ReferenceEquals(w.Word, word)))
                .Select(f => f.SlotIndex)
                .ToList();

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
    }
}

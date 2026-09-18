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
        /// The open design question the plan for issue #1054 left explicit: whether
        /// <c>FragmentEmitter</c>'s monolithic tie-break (<c>FallsPast</c>/<c>MonolithicContent.FitsNoFragmentainer</c>)
        /// should be sized against a whole physical line (every box sharing it) or against each box's own
        /// portion of that line. #1054 itself stayed box-keyed and left this open as issue #1184; that issue
        /// is what resolves it in favor of the aggregate: <c>ClaimsLine</c> now asks <c>FitsNoFragmentainer</c>
        /// of the union of every box's own <c>Rectangles[line]</c> sharing the physical line
        /// (<c>FragmentEmitter.AggregateLineRect</c>), not just the calling box's own share. A replaced
        /// element too tall for any fragmentainer is still judged monolithic - that verdict is unchanged
        /// when it is alone on its line - but ordinary text sharing that same physical line is no longer
        /// judged on its own, different position: it is pulled along with the line's own single verdict,
        /// so the whole line - replaced content and the ordinary text beside it alike - lands in the one
        /// fragmentainer the line's own top starts in, per
        /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">css-break-3 §4.1</see>'s line box
        /// being a monolithic break unit.
        /// </summary>
        [Fact]
        public async Task AnOversizedReplacedElementNearAPageBoundary_PullsTheOrdinaryTextBesideItOntoItsOwnPage()
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
            var imageSlots = SlotsClaiming(container, imageWord);
            Assert.Single(imageSlots);

            var textWords = LayoutHarness.Descendants(p).SelectMany(b => b.Words)
                .Where(w => !ReferenceEquals(w, imageWord))
                .ToList();
            Assert.NotEmpty(textWords);

            // The ordinary text sharing the physical line with that oversized image now lands on the same
            // single page the image itself is clipped to (issue #1184), rather than being judged, and
            // placed, on its own baseline-driven position.
            Assert.All(textWords, w => Assert.Equal(imageSlots, SlotsClaiming(container, w)));
        }

        /// <summary>
        /// The exact reproduction from issue #1184: two ordinary (neither one replaced) sibling
        /// <c>&lt;span&gt;</c>s share one physical line. The first's own font-size alone makes its own
        /// portion of the line taller than any page; the second is perfectly ordinary. Before the fix, the
        /// oversized span was clipped to the first fragmentainer it started in (issue #484) while the
        /// ordinary sibling was judged, and claimed, on its own - different - position, splitting one
        /// physical line across two pages in violation of
        /// <see href="https://www.w3.org/TR/css-break-3/#possible-breaks">css-break-3 §4.1</see>.
        /// </summary>
        [Fact]
        public async Task TwoOrdinarySiblingSpans_OneOversized_LandOnTheSamePage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:700pt'></div>" +
                    "<p style='margin:0'>" +
                    "<span id='big' style='font-size:900pt;line-height:1'>A</span>" +
                    "<span id='small' style='font-size:10pt'>b</span>" +
                    "</p>"),
                pageHeight: 842, margin: 0);

            var big = LayoutHarness.FindById(root, "big")!;
            var small = LayoutHarness.FindById(root, "small")!;

            var bigWord = LayoutHarness.Descendants(big).SelectMany(b => b.Words).Single();
            var smallWord = LayoutHarness.Descendants(small).SelectMany(b => b.Words).Single();

            Assert.True(bigWord.Height > container.PageBottomOf(0) - container.PageTopOf(0),
                $"fixture needs a span taller than a whole page; height={bigWord.Height}");

            var bigSlots = SlotsClaiming(container, bigWord);
            var smallSlots = SlotsClaiming(container, smallWord);

            Assert.Single(bigSlots);
            Assert.Equal(bigSlots, smallSlots);
        }

        /// <summary>
        /// The same shape with a third sibling, to confirm the aggregate is genuinely computed over every
        /// box sharing the line rather than only a pair.
        /// </summary>
        [Fact]
        public async Task ThreeOrdinarySiblingSpans_OneOversized_AllLandOnTheSamePage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:700pt'></div>" +
                    "<p style='margin:0'>" +
                    "<span id='before' style='font-size:10pt'>before</span>" +
                    "<span id='big' style='font-size:900pt;line-height:1'>A</span>" +
                    "<span id='after' style='font-size:10pt'>after</span>" +
                    "</p>"),
                pageHeight: 842, margin: 0);

            var before = LayoutHarness.FindById(root, "before")!;
            var big = LayoutHarness.FindById(root, "big")!;
            var after = LayoutHarness.FindById(root, "after")!;

            var beforeWord = LayoutHarness.Descendants(before).SelectMany(b => b.Words).Single();
            var bigWord = LayoutHarness.Descendants(big).SelectMany(b => b.Words).Single();
            var afterWord = LayoutHarness.Descendants(after).SelectMany(b => b.Words).Single();

            Assert.True(bigWord.Height > container.PageBottomOf(0) - container.PageTopOf(0),
                $"fixture needs a span taller than a whole page; height={bigWord.Height}");

            var bigSlots = SlotsClaiming(container, bigWord);
            Assert.Single(bigSlots);
            Assert.Equal(bigSlots, SlotsClaiming(container, beforeWord));
            Assert.Equal(bigSlots, SlotsClaiming(container, afterWord));
        }

        /// <summary>
        /// Regression guard for issue #484 in its original, single-box shape: an oversized span with no
        /// sibling content at all on its line is unaffected by #1184's aggregation - the aggregate over
        /// "every box sharing the line" degenerates to this box's own rectangle when it is the only one
        /// there, so it is still clipped to the first fragmentainer it starts in and nowhere else.
        /// </summary>
        [Fact]
        public async Task AnOversizedSpanAloneOnItsLine_IsStillClippedToItsFirstPageOnly()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:700pt'></div>" +
                    "<p style='margin:0'><span id='big' style='font-size:900pt;line-height:1'>A</span></p>"),
                pageHeight: 842, margin: 0);

            var big = LayoutHarness.FindById(root, "big")!;
            var bigWord = LayoutHarness.Descendants(big).SelectMany(b => b.Words).Single();

            Assert.True(bigWord.Height > container.PageBottomOf(0) - container.PageTopOf(0),
                $"fixture needs a span taller than a whole page; height={bigWord.Height}");

            Assert.Single(SlotsClaiming(container, bigWord));
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

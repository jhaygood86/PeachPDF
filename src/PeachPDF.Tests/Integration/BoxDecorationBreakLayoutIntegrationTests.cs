using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The layout half of <c>box-decoration-break: clone</c>: <see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3
    /// §6.2</see> requires a UA to reserve space for the cloned decorations when it works out how much of a
    /// fragmentainer is left, because each fragment closes with its own border and padding and the next opens
    /// with another set. Without that, a cloned border is painted over content instead of pushing it.
    /// <para>
    /// Asserted on box geometry after layout, per this repo's layout-test conventions. The <c>slice</c> half of
    /// each pair is the control: it reserves nothing, which is what makes the reservation visible.
    /// </para>
    /// </summary>
    public class BoxDecorationBreakLayoutIntegrationTests
    {
        private const double BorderTop = 10;
        private const double BorderBottom = 8;
        private const double PaddingBlock = 6;
        private const double InlineSpacing = 7;        // 2pt border + 5pt padding
        private const double InnerInlineSpacing = 7;   // the nested fixture's inner span: 2pt border + 5pt padding

        // ── the inline axis: line breaks ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task Clone_WrappingInline_GivesEveryLineItsOwnLeadingAndTrailingSpacing()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='border:2pt solid #000;padding:0 5pt;box-decoration-break:clone'>" +
                "Alpha<br>Beta</span></div>"));

            var div = LayoutHarness.FindById(root, "d")!;
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = Rectangles(span);
            Assert.Equal(2, rects.Count);

            // Each line is wrapped on its own, so each rectangle opens at the block's content edge with room
            // for the cloned leading border and padding...
            Assert.All(rects, r => Assert.Equal(div.ClientLeft, r.Left, 2));

            // ...and the words on it start inside that, on the continuation line as much as the first.
            var wordLefts = span.Words.Concat(span.Boxes.SelectMany(b => b.Words))
                .OrderBy(w => w.Top).Select(w => w.Left).ToList();
            Assert.All(wordLefts, left => Assert.True(left >= div.ClientLeft + InlineSpacing - 0.01,
                $"expected words to start after the cloned leading spacing ({div.ClientLeft + InlineSpacing}), got {left}"));

            // Both rectangles carry both spacings: the text plus one leading and one trailing set.
            foreach (var rect in rects)
            {
                var text = rect.Width - 2 * InlineSpacing;
                Assert.True(text > 0, $"expected the rectangle to be wider than its two spacings, got {rect.Width}");
            }
        }

        [Fact]
        public async Task Slice_WrappingInline_SpacesOnlyTheBoxsOwnEnds()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='width:200pt;font:10pt Arial'>" +
                "<span id='s' style='border:2pt solid #000;padding:0 5pt'>Alpha<br>Beta</span></div>"));

            var div = LayoutHarness.FindById(root, "d")!;
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = Rectangles(span);

            // The control: no padding is inserted at the break, so the continuation line's rectangle starts at
            // the block's content edge and its words start there too, with nothing reserved in between.
            var continuation = span.Boxes.SelectMany(b => b.Words).Concat(span.Words)
                .OrderBy(w => w.Top).Last();

            Assert.Equal(div.ClientLeft, rects[^1].Left, 2);
            Assert.Equal(div.ClientLeft, continuation.Left, 2);
        }

        [Fact]
        public async Task Clone_NaturallyWrappingInline_KeepsEveryLineInsideTheMeasure()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='d' style='width:180pt;font:10pt Arial'>" +
                "<span id='s' style='border:2pt solid #000;padding:0 5pt;box-decoration-break:clone'>" +
                string.Join(" ", Enumerable.Range(0, 40).Select(i => $"word{i}")) +
                "</span></div>"));

            var div = LayoutHarness.FindById(root, "d")!;
            var span = LayoutHarness.FindById(root, "s")!;
            var rects = Rectangles(span);
            Assert.True(rects.Count > 2, $"fixture must wrap onto several lines, got {rects.Count}");

            // Every line now ends with its own trailing border and padding, and the wrap decision left room
            // for them - so no line's decoration runs past the measure it was laid out in.
            Assert.All(rects, r => Assert.True(r.Right <= div.ClientRight + 0.01,
                $"expected the cloned trailing spacing to stay inside the measure ({div.ClientRight}), got {r.Right}"));
        }

        [Fact]
        public async Task Clone_NestedCloningInlines_ReserveBothTheirSpacings()
        {
            // The reservation is summed over a box and every cloning ancestor, so a wrap inside the inner span
            // has to leave room for - and then skip past - both boxes' border and padding. Only the outer one
            // clones in the control, which is what makes the difference attributable to the inner one.
            var (bothRoot, _) = await LayoutHarness.LayoutAsync(NestedInlines(innerClones: true));
            var (outerRoot, _) = await LayoutHarness.LayoutAsync(NestedInlines(innerClones: false));

            var both = LayoutHarness.FindById(bothRoot, "inner")!;
            var outerOnly = LayoutHarness.FindById(outerRoot, "inner")!;

            var bothRects = Rectangles(both);
            Assert.True(bothRects.Count > 1, $"fixture must wrap the inner span, got {bothRects.Count} line(s)");

            // A continuation line's text starts further in when the inner span clones too, by the inner
            // span's own leading spacing on top of the outer span's. Compared on the words rather than on the
            // rectangles, which coincide here for unrelated reasons: a cloning box's rectangle is pulled back
            // over its own leading spacing on every line, a non-cloning one's only on its first.
            var bothStart = ContinuationTextLeft(both);
            var outerStart = ContinuationTextLeft(outerOnly);

            Assert.Equal(InnerInlineSpacing, bothStart - outerStart, 2);

            // And the room was reserved rather than merely consumed: nothing spills past the measure.
            var div = LayoutHarness.FindById(bothRoot, "d")!;
            Assert.All(bothRects, r => Assert.True(r.Right <= div.ClientRight + 0.01,
                $"expected nested cloned spacing to stay inside the measure ({div.ClientRight}), got {r.Right}"));
        }

        // ── the block axis: page breaks ───────────────────────────────────────────────────────────────

        [Fact]
        public async Task Clone_ResumedContent_StartsBelowTheBorderAndPaddingTheFragmentReopensWith()
        {
            var (cloneRoot, cloneContainer) = await LayoutHarness.LayoutAsync(PaginatingBlock(clone: true), pageHeight: 160, margin: 10);
            var (sliceRoot, _) = await LayoutHarness.LayoutAsync(PaginatingBlock(clone: false), pageHeight: 160, margin: 10);

            Assert.True(cloneContainer.FragmentTree!.Fragmentainers.Count > 2, "fixture must paginate");

            var cloneTops = LineTops(LayoutHarness.FindById(cloneRoot, "b")!);
            var sliceTops = LineTops(LayoutHarness.FindById(sliceRoot, "b")!);

            // The fragment opening in each new fragmentainer re-opens with its own border and padding, so its
            // content starts that far below the content edge. Under slice the box is one unbroken frame and
            // content resumes at the edge itself, within a line's own leading of it.
            var reserved = BorderTop + PaddingBlock;

            foreach (var fragmentainer in cloneContainer.FragmentTree.Fragmentainers.Skip(1))
            {
                var bandTop = cloneContainer.PageTopOf(fragmentainer.SlotIndex);

                var firstCloned = cloneTops.FirstOrDefault(t => t >= bandTop - 0.01);
                var firstSliced = sliceTops.FirstOrDefault(t => t >= bandTop - 0.01);

                if (firstCloned == 0 || firstSliced == 0) continue;

                Assert.Equal(bandTop + reserved, firstCloned, 1);
                Assert.True(firstSliced < bandTop + reserved,
                    $"slice reserves nothing, so content should resume at the content edge ({bandTop}), got {firstSliced}");
            }
        }

        [Fact]
        public async Task Clone_PaginatingBlock_StopsShortOfItsOwnBottomBorder()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(PaginatingBlock(clone: true), pageHeight: 160, margin: 10);

            var block = LayoutHarness.FindById(root, "b")!;

            // The fragment being left behind closes with its own bottom border and padding, so the last word on
            // each page has to clear that much before the band's own bottom. Asserted on word boxes because
            // that is what the break decision is made against; a line's leading below its words is not part of
            // the reservation (see the accepted-gap note in CLAUDE.md).
            var words = block.Words.Concat(block.Boxes.SelectMany(b => b.Words)).ToList();

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                var bandTop = container.PageTopOf(fragmentainer.SlotIndex);
                var bandBottom = container.PageBottomOf(fragmentainer.SlotIndex);
                var onThisPage = words.Where(w => w.Top >= bandTop - 0.01 && w.Top < bandBottom).ToList();

                if (onThisPage.Count == 0) continue;

                var lastBottom = onThisPage.Max(w => w.Bottom);
                Assert.True(lastBottom <= bandBottom - BorderBottom - PaddingBlock + 0.01,
                    $"expected content to stop before the cloned bottom border " +
                    $"({bandBottom - BorderBottom - PaddingBlock}), got {lastBottom}");
            }
        }

        [Fact]
        public async Task Clone_PaginatingBlock_FitsFewerLinesOnAPageThanSlice()
        {
            var (cloneRoot, cloneContainer) = await LayoutHarness.LayoutAsync(PaginatingBlock(clone: true), pageHeight: 160, margin: 10);
            var (sliceRoot, sliceContainer) = await LayoutHarness.LayoutAsync(PaginatingBlock(clone: false), pageHeight: 160, margin: 10);

            // Two sets of border and padding per break is real space taken out of the band, so a page holds
            // less. Counted on the second page, where both a closing and an opening set are in play.
            var clonePage = LinesOnPage(cloneRoot, cloneContainer, page: 1);
            var slicePage = LinesOnPage(sliceRoot, sliceContainer, page: 1);

            Assert.True(clonePage < slicePage,
                $"expected clone to fit fewer lines than slice on page 1, got {clonePage} vs {slicePage}");
        }

        // ── helpers ───────────────────────────────────────────────────────────────────────────────────

        private static string NestedInlines(bool innerClones) => LayoutHarness.Wrap(
            "<div id='d' style='width:170pt;font:10pt Arial'>" +
            "<span id='outer' style='border-left:3pt solid #000;padding-left:6pt;box-decoration-break:clone'>" +
            "<span id='inner' style='border-left:2pt solid #000;padding-left:5pt" +
            (innerClones ? ";box-decoration-break:clone" : "") + "'>" +
            string.Join(" ", Enumerable.Range(0, 30).Select(i => $"word{i}")) +
            "</span></span></div>");

        private static string PaginatingBlock(bool clone) => LayoutHarness.Wrap(
            $"<div id='b' style='font:10pt Arial;line-height:14pt;border-top:{BorderTop}pt solid #000;" +
            $"border-bottom:{BorderBottom}pt solid #000;padding:{PaddingBlock}pt 0" +
            (clone ? ";box-decoration-break:clone" : "") + "'>" +
            string.Join(" ", Enumerable.Range(0, 400).Select(i => $"word{i}")) + "</div>");

        /// <summary>Where the text on a box's last line begins — its leftmost word on that line.</summary>
        private static double ContinuationTextLeft(CssBox box)
        {
            var words = box.Words.Concat(box.Boxes.SelectMany(b => b.Words)).ToList();
            var lastTop = words.Max(w => w.Top);
            return words.Where(w => w.Top >= lastTop - 0.01).Min(w => w.Left);
        }

        private static List<RRect> Rectangles(CssBox box) =>
            box.Rectangles.Values.OrderBy(r => r.Y).ThenBy(r => r.X).ToList();

        /// <summary>How many of a block's own text lines land in one page's content band.</summary>
        private static int LinesOnPage(CssBox root, HtmlContainerInt container, int page)
        {
            var bandTop = container.PageTopOf(page);
            var bandBottom = container.PageBottomOf(page);

            return LineTops(LayoutHarness.FindById(root, "b")!)
                .Count(t => t >= bandTop - 0.01 && t < bandBottom);
        }

        /// <summary>The distinct vertical positions of a block's own text lines, in document order.</summary>
        private static List<double> LineTops(CssBox box) =>
        [
            .. box.Words.Concat(box.Boxes.SelectMany(b => b.Words))
                .Select(w => w.Top)
                .Distinct()
                .OrderBy(t => t)
        ];
    }
}

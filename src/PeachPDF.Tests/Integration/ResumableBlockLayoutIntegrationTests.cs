using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout fills one fragmentainer at a time, and where content does not fit it records where it
    /// stopped so the next pass resumes from exactly that point
    /// (<see href="https://www.w3.org/TR/css-break-3/#breaking-controls">CSS Fragmentation Level 3
    /// §2/§4.4</see>).
    /// </summary>
    /// <remarks>
    /// The geometry these assert is deliberately the same geometry the old continuous-flow model
    /// produced — the point of the change is <i>how</i> the break is taken, not where content lands. A
    /// margin large enough to push a box across a page boundary by itself is the decision that became a
    /// real break-before, so it is what most of these drive.
    /// </remarks>
    public class ResumableBlockLayoutIntegrationTests
    {
        private const double PageHeight = 200;
        private const double Margin = 20;
        private const double Band = PageHeight - 2 * Margin;

        /// <summary>
        /// A first block, then a margin far taller than the remaining band, then a second block. The
        /// margin alone pushes the second block onto a later page, which is exactly the §5.2 unforced
        /// break that is now taken as a break-before.
        /// </summary>
        private static string MarginTruncationDocument() =>
            LayoutHarness.Wrap(
                "<div id='first' style='height:40pt'>first</div>" +
                "<div id='second' style='margin-top:300pt;height:40pt'>second</div>");

        [Fact]
        public async Task MarginPushingABoxAcrossABoundary_StartsItAtTheNextPagesContentTop()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                MarginTruncationDocument(), pageHeight: PageHeight, margin: Margin);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);

            // The whole margin is discarded and the box starts flush at a page top, rather than the
            // margin paginating through as blank space.
            var slot = container.PageIndexOf(second!.Location.Y);
            Assert.True(slot > 0, $"expected a later page, got slot {slot}");
            Assert.Equal(container.PageTopOf(slot), second.Location.Y, 6);
        }

        [Fact]
        public async Task BoxBrokenBefore_ProducesNoFragmentInTheFragmentainerItLeaves()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                MarginTruncationDocument(), pageHeight: PageHeight, margin: Margin);

            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(second);

            var slots = FragmentSlotsOf(container, second!);

            // §4.4: a break *before* a box means the box was never entered in the earlier fragmentainer,
            // so it has no geometry there and therefore no fragment - as against a box that was
            // partially laid out and continues.
            Assert.NotEmpty(slots);
            Assert.DoesNotContain(0, slots);

            // And it got there by actually resuming: the driver had to open a second fragmentainer and
            // re-enter layout, rather than the box simply being pushed along one continuous flow.
            Assert.Equal(2, container.FragmentainerPasses);
        }

        [Fact]
        public async Task DocumentThatFitsWithoutBreaking_TakesASinglePass()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<div style='height:40pt'>first</div><div style='height:40pt'>second</div>"),
                pageHeight: PageHeight, margin: Margin);

            // The common case costs exactly what the old single-flow model did.
            Assert.Equal(1, container.FragmentainerPasses);
        }

        [Fact]
        public async Task ContentFollowingTheBreak_IsLaidOutFreshRatherThanResumed()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='first' style='height:40pt'>first</div>" +
                    "<div id='second' style='margin-top:300pt;height:40pt'>second</div>" +
                    "<div id='third' style='height:40pt'>third</div>"),
                pageHeight: PageHeight, margin: Margin);

            var second = LayoutHarness.FindById(root, "second");
            var third = LayoutHarness.FindById(root, "third");
            Assert.NotNull(second);
            Assert.NotNull(third);

            // The sibling after the break is reached only by the resumed pass, and still stacks
            // immediately below its predecessor.
            Assert.Equal(second!.ActualBottom, third!.Location.Y, 6);
            Assert.Equal(container.PageIndexOf(second.Location.Y), container.PageIndexOf(third.Location.Y));
        }

        [Fact]
        public async Task ResumedPass_DoesNotDuplicateWordsOrRectangles()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                MarginTruncationDocument(), pageHeight: PageHeight, margin: Margin);

            // Re-running the prologue on a resumed pass would reset and rebuild these, so a duplicate
            // here is how that mistake would show up.
            foreach (var box in LayoutHarness.Descendants(root))
            {
                Assert.Equal(box.Words.Count, box.Words.Distinct().Count());
                Assert.Equal(box.LineBoxes.Count, box.LineBoxes.Distinct().Count());
            }
        }

        [Fact]
        public async Task ResumedPass_RegistersEachNamedPageElementOnce()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<style>@page chapter { margin-left: 40pt }</style>" +
                    "<div id='first' style='height:40pt'>first</div>" +
                    "<div id='second' style='page:chapter;margin-top:300pt;height:40pt'>second</div>"),
                pageHeight: PageHeight, margin: Margin);

            // The early registration lives in the prologue, which a resumed pass skips - registering a
            // second time would append a duplicate and skew ActivePageName resolution.
            var chapterRegistrations = container.NamedPageElements.Count(e => e.Name == "chapter");
            Assert.Equal(1, chapterRegistrations);
        }

        [Theory]
        [InlineData("display:flex")]
        [InlineData("display:grid")]
        [InlineData("display:table")]
        [InlineData("column-count:2")]
        [InlineData("break-inside:avoid")]
        public async Task MonolithicSubtree_LaysOutInOnePass(string containerStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='first' style='height:40pt'>first</div>" +
                    $"<div id='mono' style='{containerStyle}'>" +
                    "<div style='margin-top:300pt;height:30pt'>a</div>" +
                    "<div style='height:30pt'>b</div>" +
                    "</div>"),
                pageHeight: PageHeight, margin: Margin);

            var mono = LayoutHarness.FindById(root, "mono");
            Assert.NotNull(mono);

            // These engines paginate their own content; the driver must not try to break inside them,
            // or their internal bookkeeping would see a half-laid-out subtree.
            foreach (var box in LayoutHarness.Descendants(mono!))
            {
                Assert.Null(box.PendingBreakToken);
                Assert.Null(box.RequestedBreakBeforeTop);
            }
        }

        [Fact]
        public async Task LayoutCompletes_LeavingNoResumptionRecordBehind()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                MarginTruncationDocument(), pageHeight: PageHeight, margin: Margin);

            // Every box finished. A record left dangling would be resumed into by the next layout of the
            // same tree (the unrestricted-width double layout, the per-page-width reflow loop).
            foreach (var box in LayoutHarness.Descendants(root))
            {
                Assert.Null(box.PendingBreakToken);
                Assert.Null(box.RequestedBreakBeforeTop);
            }
        }

        [Fact]
        public async Task KeepWithNextRun_MovesWithTheBoxItIsChainedTo()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div style='height:40pt'>filler</div>" +
                    "<h2 id='heading' style='margin:0;height:20pt'>heading</h2>" +
                    // Sized so the run plus the gap above it still fits the destination band - a larger
                    // margin makes the `avoid` unsatisfiable, which §5.3 says to relax rather than honor.
                    "<div id='body' style='margin-top:120pt;height:40pt'>body</div>"),
                pageHeight: PageHeight, margin: Margin);

            var heading = LayoutHarness.FindById(root, "heading");
            var body = LayoutHarness.FindById(root, "body");
            Assert.NotNull(heading);
            Assert.NotNull(body);

            // The UA print stylesheet gives h1-h6 `break-after: avoid`. The pull happens in the
            // pass that takes the break, and its adjusted target has to survive into the resumed pass -
            // otherwise the heading is stranded on the page its content just left.
            Assert.Equal(
                container.PageIndexOf(heading!.Location.Y),
                container.PageIndexOf(body!.Location.Y));
        }

        // An out-of-flow box is positioned against its containing block, not against the page the flow has
        // reached, so a break token recorded inside one has no link in the chain to travel up -
        // CssBox.LayoutOutOfFlowChildren discards whatever a child leaves behind, and with it the content
        // that record names. That loop is the only way an absolutely-positioned child of a flex, grid or
        // table container is laid out at all, since those engines place only in-flow items.
        //
        // The invariant holds today because the loop runs inside LayoutMonolithicContent's suppressed
        // scope, so no token is ever recorded there to be dropped. It is pinned here rather than left
        // implicit because that is a property of the *caller*: narrowing the set of engines that suppress
        // (#315/#322) removes the thing currently upholding it, and this is what will say so.
        [Theory]
        [InlineData("display:flex")]
        [InlineData("display:grid")]
        [InlineData("display:table")]
        public async Task TallOutOfFlowChildOfAnEngineContainer_KeepsAllOfItsContent(string containerStyle)
        {
            var lines = string.Join("", Enumerable.Range(0, 20).Select(i => $"Line{i}<br>"));
            var html = LayoutHarness.Wrap(
                $"<div style='{containerStyle};position:relative'>" +
                "<span>in flow</span>" +
                $"<div id='abs' style='position:absolute;top:0;left:0;width:120pt;line-height:20pt'>{lines}</div>" +
                "</div>");

            var (root, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            var abs = LayoutHarness.FindById(root, "abs");
            Assert.NotNull(abs);

            // No resumption record was left behind for a box nothing would have resumed.
            Assert.Null(abs!.PendingBreakToken);
            Assert.Null(abs.RequestedBreakBeforeTop);

            var placed = container.FragmentTree!.Fragmentainers
                .SelectMany(f => Flatten(f.Root))
                .SelectMany(f => f.Words)
                .Select(w => w.Word.Text)
                .Where(t => t?.StartsWith("Line") == true)
                .Distinct()
                .Count();

            Assert.Equal(20, placed);
        }

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

        private static List<int> FragmentSlotsOf(HtmlContainerInt container, CssBox box)
        {
            List<int> slots = [];

            foreach (var fragmentainer in container.FragmentTree!.Fragmentainers)
            {
                if (Contains(fragmentainer.Root, box))
                    slots.Add(fragmentainer.SlotIndex);
            }

            return slots;
        }

        private static bool Contains(BoxFragment fragment, CssBox box) =>
            ReferenceEquals(fragment.Box, box) || fragment.Children.Any(c => Contains(c, box));
    }
}

using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Html.Core.Fragmentation
{
    /// <summary>
    /// Where a forced break (css-break-3 §3.1) puts a box is the <i>frame's</i> question, re-derived at
    /// every placement by <c>CssBox.ForcedBreakTopFor</c> rather than latched once by the box's own
    /// prologue.
    /// </summary>
    /// <remarks>
    /// These pin the target itself, asked directly of the frame, against the page grid it is defined over
    /// (<see cref="HtmlContainerInt.PageTopOf"/>) — and against where the box actually ended up, so
    /// "re-derived per placement" and "the value the placement used" cannot drift apart unnoticed. The two
    /// rules the arithmetic turns on each get their own case: §4.4's flush-boundary epsilon (a predecessor
    /// ending exactly on a slot boundary already satisfies the break, so no page is manufactured) and the
    /// consecutive-forced-break case it must <i>not</i> swallow (a zero-height predecessor sitting at the
    /// boundary occupies the later slot, so the break still pushes past it).
    /// </remarks>
    public class ForcedBreakTargetIsTheFramesTests
    {
        // Band = PageHeight - 2 * margin, so slot k's content top is Margin + k * Band.
        private const double PageHeight = 300;
        private const double Margin = 20;
        private const double Band = PageHeight - 2 * Margin;

        private static double SlotTop(int slot) => Margin + slot * Band;

        /// <summary>The target the frame resolves for <paramref name="id"/>, asked after layout.</summary>
        /// <remarks>
        /// Asking afterwards is the point: a value that is re-derived rather than consumed answers the same
        /// way whenever it is asked, so this is exactly the assertion a latched field could not pass.
        /// </remarks>
        private static double? TargetFor(CssBox root, string id)
        {
            var box = LayoutHarness.FindById(root, id);
            Assert.NotNull(box);
            Assert.NotNull(box!.ParentBox);

            return box.ParentBox!.ForcedBreakTopFor(box);
        }

        // The ordinary case: a predecessor that ends part-way down slot 0 puts the break at slot 1's own
        // content top, and the box is placed exactly there (no margin to preserve).
        [Fact]
        public async Task PlainForcedBreak_TargetsTheNextSlotsContentTop_AndIsWhereTheBoxLanded()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='first' style='height:50pt;margin:0'>first</div>"
                    + "<div id='second' style='height:50pt;margin:0;break-before:page'>second</div>"),
                pageHeight: PageHeight, margin: Margin);

            Assert.Equal(container.PageTopOf(1), TargetFor(root, "second")!.Value, 6);
            Assert.Equal(SlotTop(1), TargetFor(root, "second")!.Value, 6);
            Assert.Equal(SlotTop(1), LayoutHarness.FindById(root, "second")!.Location.Y, 6);
        }

        // §4.4: a predecessor whose content ENDS flush on a slot boundary already satisfies the break, so
        // the target is that boundary itself and not the slot after it. Sizing the first box to exactly one
        // band is the canonical shape (a full-bleed cover), and the epsilon is what stops it manufacturing
        // a blank page.
        [Fact]
        public async Task PredecessorEndingFlushOnABoundary_TargetsThatBoundary_NotTheSlotAfterIt()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<div id='first' style='height:{Band}pt;margin:0'>first</div>"
                    + "<div id='second' style='height:50pt;margin:0;break-before:page'>second</div>"),
                pageHeight: PageHeight, margin: Margin);

            // The first box occupies the whole of slot 0 and ends exactly where slot 1 begins.
            Assert.Equal(SlotTop(1), LayoutHarness.FindById(root, "first")!.ActualBottom, 6);

            // Slot 1, not slot 2: the flush end is already the break, and no page is skipped.
            Assert.Equal(container.PageTopOf(1), TargetFor(root, "second")!.Value, 6);
            Assert.Equal(SlotTop(1), LayoutHarness.FindById(root, "second")!.Location.Y, 6);
            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);
        }

        // The case the epsilon must not swallow: a zero-height marker that its OWN forced break already
        // relocated to a boundary sits AT that boundary, which is the later slot - so the break between it
        // and the next box still pushes past it, and the intentional blank page survives.
        [Fact]
        public async Task ConsecutiveForcedBreaks_StepPastTheMarkerRatherThanCollapsingOntoIt()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='first' style='height:50pt;margin:0'>first</div>"
                    + "<div id='marker' style='margin:0;break-before:page'></div>"
                    + "<div id='second' style='height:50pt;margin:0;break-before:page'>second</div>"),
                pageHeight: PageHeight, margin: Margin);

            var marker = LayoutHarness.FindById(root, "marker");
            Assert.NotNull(marker);

            // The marker took its own break to slot 1's top and contributes no height of its own.
            Assert.Equal(SlotTop(1), marker!.Location.Y, 6);
            Assert.Equal(marker.Location.Y, marker.ActualBottom, 6);

            // Its bottom is flush on slot 1's top, which SlotEndingAt reads as slot 0 - but its own top is
            // AT that boundary, so the second rule fires and the break lands one slot further on. Without
            // it the two boxes would share slot 1 and the deliberately-blank page would be lost.
            Assert.Equal(container.PageTopOf(2), TargetFor(root, "second")!.Value, 6);
            Assert.Equal(SlotTop(2), LayoutHarness.FindById(root, "second")!.Location.Y, 6);
        }

        // §5.2 preserves the margin on the new page's side of a FORCED break, so the box lands one margin
        // below the target rather than on it - which is exactly why the target is worth asserting
        // separately from the position.
        [Fact]
        public async Task TargetIsTheBoundary_AndThePreservedMarginIsAddedToIt()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='first' style='height:50pt;margin:0'>first</div>"
                    + "<div id='second' style='height:50pt;margin:0;margin-top:30pt;break-before:page'>second</div>"),
                pageHeight: PageHeight, margin: Margin);

            Assert.Equal(container.PageTopOf(1), TargetFor(root, "second")!.Value, 6);
            Assert.Equal(SlotTop(1) + 30, LayoutHarness.FindById(root, "second")!.Location.Y, 6);
        }

        // §3.1's break point before a container's FIRST in-flow child is the same break point as the one
        // before the container, so a `break-before` there is taken by the container the box begins and not
        // by the box: the container gets the target, the child gets none. The two halves are asserted
        // together because it is the pair that says the break was hoisted rather than lost.
        [Fact]
        public async Task BreakBeforeAFirstInFlowChild_IsTakenByTheContainerItBegins()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='first' style='height:50pt;margin:0'>first</div>"
                    + "<div id='wrapper' style='margin:0'>"
                    + "<div id='second' style='height:50pt;margin:0;break-before:page'>second</div>"
                    + "</div>"),
                pageHeight: PageHeight, margin: Margin);

            Assert.Equal(container.PageTopOf(1), TargetFor(root, "wrapper")!.Value, 6);
            Assert.Null(TargetFor(root, "second"));
            Assert.Equal(SlotTop(1), LayoutHarness.FindById(root, "wrapper")!.Location.Y, 6);
            Assert.Equal(SlotTop(1), LayoutHarness.FindById(root, "second")!.Location.Y, 6);
        }

        // The climb itself, exercised by the one forced break §3.1's propagation does not hoist: a used
        // page-name transition (CSS Paged Media 3 §3) is forced on the box that carries the name, first
        // in-flow child or not. That box has no previous sibling of its own, so the predecessor the target
        // resolves against can only be found by climbing out through the containers it begins.
        [Fact]
        public async Task NamedPageOnANestedFirstChild_ResolvesAgainstThePredecessorOfTheChainItBegins()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                """
                <!DOCTYPE html><html><head><style>
                body { margin: 0 }
                div { margin: 0 }
                #second { page: chapter }
                </style></head><body>
                <div id='first' style='height:50pt'>first</div>
                <div><div>
                <div id='second' style='height:50pt'>second</div>
                </div></div>
                </body></html>
                """,
                pageHeight: PageHeight, margin: Margin);

            Assert.Equal(container.PageTopOf(1), TargetFor(root, "second")!.Value, 6);
            Assert.Equal(SlotTop(1), LayoutHarness.FindById(root, "second")!.Location.Y, 6);
        }

        // Nothing precedes the box in the flow at all: there is no break to take, and §4.4 asks user
        // agents not to manufacture a blank page in front of a document's first content. A null target is
        // how that is said.
        [Fact]
        public async Task BoxThatBeginsTheFlow_HasNoTargetAtAll()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='second' style='height:50pt;margin:0;break-before:page'>second</div>"),
                pageHeight: PageHeight, margin: Margin);

            Assert.Null(TargetFor(root, "second"));
            Assert.Single(container.FragmentTree!.Fragmentainers);
        }

        // A box with no forced break before it has no target either - the method answers about the break,
        // not about the box's position, so an ordinary sibling gets null rather than "wherever it is".
        [Fact]
        public async Task BoxWithNoForcedBreak_HasNoTarget()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='first' style='height:50pt;margin:0'>first</div>"
                    + "<div id='second' style='height:50pt;margin:0'>second</div>"),
                pageHeight: PageHeight, margin: Margin);

            Assert.Null(TargetFor(root, "second"));
        }

        // CSS 2.1 §9.4.3: a relative offset moves a box visually without affecting the layout of anything
        // around it, so it must not decide which slot the break lands in. StaticBottom and the static top
        // are what the target is resolved from, and both back the offset out again.
        [Theory]
        [InlineData("top: -40pt")]
        [InlineData("top: 40pt")]
        public async Task RelativelyOffsetPredecessor_DoesNotMoveTheTarget(string offset)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    $"<div id='first' style='height:50pt;margin:0;position:relative;{offset}'>first</div>"
                    + "<div id='second' style='height:50pt;margin:0;break-before:page'>second</div>"),
                pageHeight: PageHeight, margin: Margin);

            Assert.Equal(container.PageTopOf(1), TargetFor(root, "second")!.Value, 6);
            Assert.Equal(SlotTop(1), LayoutHarness.FindById(root, "second")!.Location.Y, 6);
        }

        // A used-page-name transition forces a break too (CSS Paged Media 3 §3), through the same target
        // arithmetic and with no break-before in sight - so the frame answers for it as well.
        [Fact]
        public async Task NamedPageTransition_ResolvesThroughTheSameTarget()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                """
                <!DOCTYPE html><html><head><style>
                body { margin: 0 }
                div { margin: 0 }
                #second { page: chapter }
                </style></head><body>
                <div id='first' style='height:50pt'>first</div>
                <div id='second' style='height:50pt'>second</div>
                </body></html>
                """,
                pageHeight: PageHeight, margin: Margin);

            Assert.Equal(container.PageTopOf(1), TargetFor(root, "second")!.Value, 6);
            Assert.Equal(SlotTop(1), LayoutHarness.FindById(root, "second")!.Location.Y, 6);
        }
    }
}

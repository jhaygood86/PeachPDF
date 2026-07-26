using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see href="https://www.w3.org/TR/css-break-3/#break-between">css-break-3 §3.1</see> forced-break
    /// combination, precedence and propagation: a <c>break-before</c> on a container's first in-flow child
    /// and a <c>break-after</c> on its last state something about the break point before/after the
    /// <b>container</b>, so the container is what takes the break and travels with it.
    /// </summary>
    public class BreakPropagationIntegrationTests
    {
        private const double PageHeight = 300;

        /// <summary>The document band's own top, which is where a break lands content.</summary>
        private static double SlotTop(HtmlContainerInt container, int slot) => container.PageTopOf(slot);

        private static int SlotOf(HtmlContainerInt container, CssBox box) =>
            container.PageIndexOf(box.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);

        #region Propagation moves the container, not only the child

        // Before propagation the break moved the child alone, so the container stayed put and spanned the
        // boundary - painting an empty stub of its own border and background on the page its content left.
        [Fact]
        public async Task ForcedBreakBeforeAFirstChild_MovesTheContainer()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='lead' style='height:60pt'>lead</div>"
                    + "<div id='wrap' style='border:1pt solid black'>"
                    + "<div id='first' style='break-before:page;height:40pt'>first</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var wrap = LayoutHarness.FindById(root, "wrap");
            var first = LayoutHarness.FindById(root, "first");
            Assert.NotNull(wrap);
            Assert.NotNull(first);

            // The container's own border-box top is what lands on the new page's content edge; the child
            // then starts inside it, one border width down.
            Assert.Equal(SlotTop(container, 1), wrap!.Location.Y, 3);
            Assert.Equal(wrap.ClientTop, first!.Location.Y, 3);
        }

        // Through as many containers as the box begins, which is what "propagates up" means.
        [Fact]
        public async Task ForcedBreakBeforeANestedFirstChild_MovesTheOutermostContainerItBegins()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='lead' style='height:60pt'>lead</div>"
                    + "<div id='outer'><div id='inner'>"
                    + "<div id='deep' style='break-before:page;height:40pt'>deep</div>"
                    + "</div></div>"),
                pageHeight: PageHeight);

            var outer = LayoutHarness.FindById(root, "outer");
            Assert.NotNull(outer);

            Assert.Equal(SlotTop(container, 1), outer!.Location.Y, 3);
        }

        // A box with an in-flow sibling above it names its own break point, so nothing propagates and the
        // container stays where it is.
        [Fact]
        public async Task ForcedBreakBeforeALaterChild_LeavesTheContainerWhereItIs()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='wrap'>"
                    + "<div id='first' style='height:60pt'>first</div>"
                    + "<div id='second' style='break-before:page;height:40pt'>second</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var wrap = LayoutHarness.FindById(root, "wrap");
            var second = LayoutHarness.FindById(root, "second");
            Assert.NotNull(wrap);
            Assert.NotNull(second);

            Assert.Equal(0, SlotOf(container, wrap!));
            Assert.Equal(1, SlotOf(container, second!));
        }

        // §3.1 propagation stops before breaking through the nearest matching fragmentation context. The
        // chain here reaches the root, so nothing precedes the box in the flow at all and no break is taken -
        // which is also §4.4's "no empty fragmentainer" falling out rather than being asserted.
        [Fact]
        public async Task ForcedBreakBeforeTheFirstBoxInTheFlow_ManufacturesNoBlankPage()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='wrap'><div id='only' style='break-before:page;height:40pt'>only</div></div>"),
                pageHeight: PageHeight);

            var wrap = LayoutHarness.FindById(root, "wrap");
            Assert.NotNull(wrap);

            Assert.Equal(0, SlotOf(container, wrap!));
            Assert.Single(container.FragmentTree!.Fragmentainers);
        }

        // The engine-independence boundary (#166): a flex item, table cell or multi-column child is
        // positioned by its engine rather than by block flow, so a break travelling out of one would name a
        // position the engine is about to overwrite. The value stays where it was written.
        [Theory]
        [InlineData("display:flex")]
        [InlineData("display:grid")]
        public async Task ForcedBreakBeforeAnEngineItem_DoesNotTravelOutOfTheEngine(string containerStyle)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='lead' style='height:60pt'>lead</div>"
                    + $"<div id='wrap' style='{containerStyle}'>"
                    + "<div id='item' style='break-before:page;height:40pt'>item</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var item = LayoutHarness.FindById(root, "item");
            Assert.NotNull(item);

            Assert.False(BreakPropagation.PropagatesBreakBeforeOutward(item!));
        }

        #endregion

        #region Combination and precedence (§3.1)

        // A break-after on the last in-flow child of the preceding sibling is a value at the break point
        // *after that sibling*, which is this box's own break point.
        [Fact]
        public async Task BreakAfterOnALastChild_ForcesTheBreakBeforeTheFollowingSibling()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='wrap'><div id='tail' style='break-after:page;height:40pt'>tail</div></div>"
                    + "<div id='next' style='height:40pt'>next</div>"),
                pageHeight: PageHeight);

            var next = LayoutHarness.FindById(root, "next");
            Assert.NotNull(next);

            Assert.Equal(SlotTop(container, 1), next!.Location.Y, 3);
        }

        // Both values apply at one break point and both are honored: the directional one names a side, and a
        // directional break forces a page break as well, so it subsumes the plain `page`.
        [Fact]
        public async Task APlainPageAndADirectionalValue_ResolveToTheDirectionalOne()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='lead' style='height:60pt'>lead</div>"
                    + "<div id='prev' style='break-after:page;height:40pt'>prev</div>"
                    + "<div id='next' style='break-before:recto;height:40pt'>next</div>"),
                pageHeight: PageHeight);

            var next = LayoutHarness.FindById(root, "next");
            Assert.NotNull(next);

            // Slot 0 is page 1 (recto), so the break has to step over slot 1 to reach slot 2.
            Assert.Equal(2, SlotOf(container, next!));
        }

        // Two conflicting directional values cannot both be honored, and §3.1 says which survives: the one
        // on the latest element in flow. For a value travelling outward that is the deeper one.
        [Fact]
        public async Task ConflictingDirectionalValues_TheDeeperOneWins()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='lead' style='height:60pt'>lead</div>"
                    + "<div id='wrap' style='break-before:verso'>"
                    + "<div id='first' style='break-before:recto;height:40pt'>first</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var wrap = LayoutHarness.FindById(root, "wrap");
            Assert.NotNull(wrap);

            // recto (the inner, later value) wins over the container's own verso: slot 2 is page 3, a recto.
            Assert.Equal(2, SlotOf(container, wrap!));
        }

        #endregion

        #region Keep-with-next across a container (#361)

        // A forced break on either side of a pair beats break avoidance on the other (§3.1), and both sides
        // are read through the chains they begin and end - so a heading is not kept with a container whose
        // first child asked for a forced break.
        [Fact]
        public async Task AForcedBreakPropagatedOutOfAContainer_BreaksTheKeepWithNextChain()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<h2 id='head' style='page-break-after: avoid'>Head</h2>"
                    + "<div id='wrap'><div id='first' style='break-before:page;height:40pt'>first</div></div>"),
                pageHeight: PageHeight);

            var wrap = LayoutHarness.FindById(root, "wrap");
            Assert.NotNull(wrap);

            Assert.Empty(DomUtils.GetPrecedingKeepWithNextRun(wrap!));
        }

        // The §4.3 movers reach the same rule through EarlyBreak.Discover: the run is collected at the
        // propagation anchor, the anchor is what travels, and the decision is handed to the child loop that
        // owns the run - the anchor's parent, not the breaking box's.
        [Fact]
        public async Task BreakInsideAvoidOnAFirstChild_PullsTheRunAcrossTheContainer()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<div id='lead' style='height:200pt'>lead</div>"
                    + "<h2 id='head' style='page-break-after: avoid'>Head</h2>"
                    + "<div id='wrap'>"
                    + "<div id='body' style='break-inside:avoid;height:150pt'>body</div>"
                    + "</div>"),
                pageHeight: PageHeight);

            var head = LayoutHarness.FindById(root, "head");
            var wrap = LayoutHarness.FindById(root, "wrap");
            var body = LayoutHarness.FindById(root, "body");
            Assert.NotNull(head);
            Assert.NotNull(wrap);
            Assert.NotNull(body);

            Assert.Equal(1, SlotOf(container, body!));
            Assert.Equal(1, SlotOf(container, wrap!));
            Assert.Equal(1, SlotOf(container, head!));
        }

        #endregion
    }
}

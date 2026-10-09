using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Where an absolutely positioned child of an <c>inline-flex</c> box ends up when the box itself is
    /// moved, re-measured or fragmented — the situations <c>FlowInlineFlexChild</c> runs in that a plain
    /// single-page, left-aligned, single-pass layout does not exercise. See
    /// <see cref="InlineFlexOutOfFlowChildrenIntegrationTests"/> for the basic placement.
    /// </summary>
    public class InlineFlexOutOfFlowChildrenVariantsIntegrationTests
    {
        private const string AbsChild =
            "<div id='o' style='position:absolute; top:5pt; left:7pt; width:40pt; height:20pt'></div>";

        private static string Box(string abs = AbsChild, string style = "") =>
            $"<span id='c' style='display:inline-flex; position:relative; width:120pt; height:60pt; {style}'>{abs}<span>zz</span></span>";

        private static async Task AssertChildAtOffset(string html, double dx = 7, double dy = 5,
            string container = "c", string child = "o", double pageHeight = 842)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: pageHeight);
            var c = LayoutHarness.FindById(root, container)!;
            var o = LayoutHarness.FindById(root, child)!;

            Assert.Equal(c.Location.X + dx, o.Location.X, 1.0);
            Assert.Equal(c.Location.Y + dy, o.Location.Y, 1.0);
        }

        // Note: an atomic inline-flex/-grid/-table box is not moved by text-align at all in this engine
        // (the box itself stays at the line start), so this pins only that the child keeps its offset from
        // the container whatever the alignment does to it.
        [Theory]
        [InlineData("center")]
        [InlineData("right")]
        public async Task TextAlign_DoesNotSeparateTheChildFromItsContainer(string align) =>
            await AssertChildAtOffset(LayoutHarness.Wrap($"<div style='text-align:{align}; width:300pt'>{Box()}</div>"));

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task StaticallyPositionedChild_FollowsAnUnpositionedContainer_ThatVerticalAlignMoves(bool textInItem)
        {
            // Its containing block is outside the container (no positioned ancestor in between), so the
            // container's subtree translation skips it: it has to be moved along as a static position.
            // (Only the vertical position is pinned: a static X is the containing block's left edge here, as
            // it is for a block-level flex container, which is not what this test is about.)
            var html = LayoutHarness.Wrap(
                "<div style='font-size:30pt'>Xg "
                + "<span id='c' style='display:inline-flex; vertical-align:bottom; width:40pt; height:12pt'>"
                + "<div id='o' style='position:absolute; width:10pt; height:4pt'></div>"
                + (textInItem ? "<span>zz</span>" : "<div style='width:5pt'></div>")
                + "</span> Xg</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var c = LayoutHarness.FindById(root, "c")!;
            var o = LayoutHarness.FindById(root, "o")!;

            Assert.True(c.Rectangles.Values.Single().Y > 30, "vertical-align must have moved the container");
            Assert.Equal(c.Location.Y, o.Location.Y, 1.0);
        }

        [Fact]
        public async Task ContainerWrappedOntoASecondLine_CarriesItsChild()
        {
            var html = LayoutHarness.Wrap(
                "<div style='width:200pt'><span style='display:inline-block; width:150pt; height:10pt'></span>"
                + Box() + "</div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            Assert.True(LayoutHarness.FindById(root, "c")!.Location.X < 100, "the box must have wrapped to the line start");

            await AssertChildAtOffset(html);
        }

        [Fact]
        public async Task RightAndBottomOffsets_MeasureFromTheContainersFarEdges()
        {
            // 120x60 container, a 40x20 child 3pt/2pt in from the right/bottom edges.
            await AssertChildAtOffset(
                LayoutHarness.Wrap(Box("<div id='o' style='position:absolute; right:3pt; bottom:2pt; width:40pt; height:20pt'></div>")),
                dx: 120 - 3 - 40, dy: 60 - 2 - 20);
        }

        [Fact]
        public async Task PercentageSizes_ResolveAgainstTheSizedContainer()
        {
            var html = LayoutHarness.Wrap(Box("<div id='o' style='position:absolute; top:5pt; left:7pt; width:50%; height:50%'></div>"));
            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var o = LayoutHarness.FindById(root, "o")!;

            Assert.Equal(60, o.ActualWidth, 1.0);
            Assert.Equal(30, o.ActualHeight, 1.0);
        }

        [Fact]
        public async Task UnpositionedContainer_LeavesTheChildToTheNearestPositionedAncestor()
        {
            var html = LayoutHarness.Wrap(
                "<div id='anc' style='position:relative; margin:10pt; width:300pt; height:100pt'>"
                + "<span style='display:inline-flex; width:120pt; height:60pt'>"
                + AbsChild + "<span>zz</span></span></div>");

            await AssertChildAtOffset(html, container: "anc");
        }

        [Fact]
        public async Task NestedInlineFlex_EachContainerOwnsItsOwnChild()
        {
            var html = LayoutHarness.Wrap(
                "<span id='outer' style='display:inline-flex; position:relative; width:200pt; height:80pt'>"
                + "<div id='p' style='position:absolute; top:3pt; left:4pt; width:10pt; height:10pt'></div>"
                + "<span id='c' style='display:inline-flex; position:relative; width:100pt; height:40pt'>"
                + AbsChild + "<span>zz</span></span></span>");

            await AssertChildAtOffset(html);
            await AssertChildAtOffset(html, 4, 3, container: "outer", child: "p");
        }

        [Fact]
        public async Task HiddenChild_IsSkipped_AndDoesNotDisturbTheContainer()
        {
            var html = LayoutHarness.Wrap(Box(
                "<div id='o' style='display:none; position:absolute; top:5pt; left:7pt; width:40pt; height:20pt'></div>"));
            var (root, _) = await LayoutHarness.LayoutAsync(html);

            Assert.Equal(120, LayoutHarness.FindById(root, "c")!.ActualWidth, 1.0);
        }

        // ─── Containers that measure the inline-flex box more than once ───────────

        [Theory]
        [InlineData("<span style='display:inline-block'>{0}</span>")]
        [InlineData("<table><tr><td>{0}</td></tr></table>")]
        [InlineData("<div style='display:flex'><div>{0}</div></div>")]
        [InlineData("<div style='float:left'>{0}</div>")]
        public async Task ContainerMeasuredSeveralTimes_StillPlacesTheChildOnce(string wrapper)
        {
            var html = LayoutHarness.Wrap(string.Format(wrapper, Box()));

            await AssertChildAtOffset(html);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var o = LayoutHarness.FindById(root, "o")!;
            Assert.Equal(40, o.ActualWidth, 1.0);
            Assert.Equal(20, o.ActualHeight, 1.0);
        }

        [Fact]
        public async Task RepeatedLayoutOfTheSameDocument_GivesTheSamePlacement()
        {
            // The per-page reflow loop lays a document out more than once; a second pass must land the child
            // exactly where the first did rather than accumulating an offset.
            var html = LayoutHarness.Wrap(Box());
            var results = await LayoutHarness.LayoutRepeatedlyAsync(html, 3, (root, _) =>
            {
                var c = LayoutHarness.FindById(root, "c")!;
                var o = LayoutHarness.FindById(root, "o")!;
                return (o.Location.X - c.Location.X, o.Location.Y - c.Location.Y, c.Location.Y);
            });

            Assert.All(results, r => Assert.Equal(results[0], r));
            Assert.Equal((7d, 5d), (results[0].Item1, results[0].Item2));
        }

        // ─── Fragmentation: compared with the block-level flex container ──────────

        private const double PageHeight = 200;

        private static int SlotOf(HtmlContainerInt container, CssBox box) =>
            container.PageIndexOf(box.Location.Y + HtmlContainerInt.PageBoundaryEpsilon);

        [Theory]
        [InlineData(0, 5)]
        [InlineData(120, 5)]
        [InlineData(120, 50)]
        [InlineData(150, 5)]
        public async Task ContainerNearTheFootOfAPage_PlacesItsChildOnTheSamePageAsABlockLevelFlex(
            double fillerHeight, double childTop)
        {
            static string Html(string display, double filler, double top)
            {
                var tag = display == "flex" ? "div" : "span";
                return LayoutHarness.Wrap(
                    $"<div style='height:{filler}pt'>filler</div>"
                    + $"<{tag} id='c' style='display:{display}; position:relative; width:120pt; height:70pt'>"
                    + $"<div id='o' style='position:absolute; top:{top}pt; left:7pt; width:40pt; height:10pt'></div>"
                    + $"<span>zz</span></{tag}>");
            }

            var (inlineRoot, inlineContainer) = await LayoutHarness.LayoutAsync(
                Html("inline-flex", fillerHeight, childTop), pageHeight: PageHeight);
            var (blockRoot, blockContainer) = await LayoutHarness.LayoutAsync(
                Html("flex", fillerHeight, childTop), pageHeight: PageHeight);

            var ic = LayoutHarness.FindById(inlineRoot, "c")!;
            var io = LayoutHarness.FindById(inlineRoot, "o")!;
            var bc = LayoutHarness.FindById(blockRoot, "c")!;
            var bo = LayoutHarness.FindById(blockRoot, "o")!;

            Assert.Equal(childTop, io.Location.Y - ic.Location.Y, 1.0);
            Assert.Equal(7, io.Location.X - ic.Location.X, 1.0);

            // The child lands in the same fragmentainer slot as it does under a block-level flex container,
            // and the container itself is in the slot the block-level one is.
            Assert.Equal(SlotOf(blockContainer, bc), SlotOf(inlineContainer, ic));
            Assert.Equal(SlotOf(blockContainer, bo), SlotOf(inlineContainer, io));
        }

        [Fact]
        public async Task ContainerInsideAMultiColumnBox_CarriesItsChild()
        {
            var html = LayoutHarness.Wrap(
                "<div style='column-count:2; column-gap:10pt; width:400pt'>"
                + "<div style='height:30pt'>filler</div>" + Box() + "</div>");

            await AssertChildAtOffset(html);
        }
    }
}

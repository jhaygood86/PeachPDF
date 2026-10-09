using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-flexbox-1 §4.1: an absolutely- or fixed-positioned child of a flex container does not take
    /// part in flex layout and is positioned against its containing block. <c>display:inline-flex</c> is
    /// the same container, only inline-level, but it reaches the flex engine through a different path than
    /// a block-level flex container does — each case here is checked against the <c>flex</c> container
    /// laid out from identical content.
    /// </summary>
    public class InlineFlexOutOfFlowChildrenIntegrationTests
    {
        private const string RedSvg = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='96' height='48'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";

        private static string Markup(string display, string child) =>
            LayoutHarness.Wrap(
                $"<div style='margin:20pt'><{(display == "flex" ? "div" : "span")} id='c' style='display:{display}; position:relative; width:120pt; height:60pt'>" +
                $"{child}<span>zz</span></{(display == "flex" ? "div" : "span")}></div>");

        [Theory]
        [InlineData("<div id='o' style='position:absolute; top:5pt; left:7pt; width:40pt; height:20pt'></div>", 40, 20)]
        [InlineData("<img id='o' style='position:absolute; top:5pt; left:7pt; width:40pt; height:20pt' src=\"" + RedSvg + "\" />", 40, 20)]
        [InlineData("<svg id='o' style='position:absolute; top:5pt; left:7pt' width='40pt' height='20pt'><rect width='40' height='20'/></svg>", 40, 20)]
        [InlineData("<div id='o' style='position:fixed; top:5pt; left:7pt; width:40pt; height:20pt'></div>", 40, 20)]
        public async Task OutOfFlowChild_IsPositionedAgainstTheContainer_LikeInABlockLevelFlexContainer(
            string child, double width, double height)
        {
            var (inlineRoot, _) = await LayoutHarness.LayoutAsync(Markup("inline-flex", child));
            var (blockRoot, _) = await LayoutHarness.LayoutAsync(Markup("flex", child));

            var inlineContainer = LayoutHarness.FindById(inlineRoot, "c")!;
            var inlineChild = LayoutHarness.FindById(inlineRoot, "o")!;
            var blockContainer = LayoutHarness.FindById(blockRoot, "c")!;
            var blockChild = LayoutHarness.FindById(blockRoot, "o")!;

            Assert.Equal(width, inlineChild.ActualWidth, 1.0);
            Assert.Equal(height, inlineChild.ActualHeight, 1.0);

            // Same offset from the container's own origin as the block-level flex container gives it.
            Assert.Equal(blockChild.Location.X - blockContainer.Location.X,
                inlineChild.Location.X - inlineContainer.Location.X, 1.0);
            Assert.Equal(blockChild.Location.Y - blockContainer.Location.Y,
                inlineChild.Location.Y - inlineContainer.Location.Y, 1.0);
        }

        [Fact]
        public async Task AbsoluteChild_OfInlineFlex_SitsAtItsOffsetFromTheContainerContentOrigin()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(Markup("inline-flex",
                "<div id='o' style='position:absolute; top:5pt; left:7pt; width:40pt; height:20pt'></div>"));

            var c = LayoutHarness.FindById(root, "c")!;
            var o = LayoutHarness.FindById(root, "o")!;

            Assert.Equal(c.Location.X + 7, o.Location.X, 1.0);
            Assert.Equal(c.Location.Y + 5, o.Location.Y, 1.0);
        }

        [Theory]
        [InlineData("middle")]
        [InlineData("bottom")]
        [InlineData("text-bottom")]
        public async Task AbsoluteChild_FollowsItsContainer_WhenVerticalAlignMovesTheContainerOnTheLine(string align)
        {
            // No flex item holds text, so the container has no baseline of its own to anchor the shift to.
            static string Html(string align) => LayoutHarness.Wrap(
                "<div style='font-size:30pt'>Xg " +
                $"<span id='c' style='display:inline-flex; position:relative; vertical-align:{align}; width:40pt; height:12pt'>" +
                "<div id='o' style='position:absolute; top:5pt; left:7pt; width:10pt; height:4pt'></div>" +
                "</span> Xg</div>");

            var (baselineRoot, _) = await LayoutHarness.LayoutAsync(Html("baseline"));
            var (root, _) = await LayoutHarness.LayoutAsync(Html(align));

            var c = LayoutHarness.FindById(root, "c")!;
            var o = LayoutHarness.FindById(root, "o")!;

            // The alignment must actually have moved the container, or this proves nothing.
            Assert.NotEqual(LayoutHarness.FindById(baselineRoot, "c")!.Rectangles.Values.Single().Y,
                c.Rectangles.Values.Single().Y, 1.0);
            Assert.Equal(c.Rectangles.Values.Single().Y, c.Location.Y, 1.0);

            Assert.Equal(c.Location.X + 7, o.Location.X, 1.0);
            Assert.Equal(c.Location.Y + 5, o.Location.Y, 1.0);
        }

        [Fact]
        public async Task InFlowItem_MovesExactlyOnce_WhenVerticalAlignMovesATextlessContainer()
        {
            static string Html(string align) => LayoutHarness.Wrap(
                "<div style='font-size:30pt'>Xg " +
                $"<span id='c' style='display:inline-flex; vertical-align:{align}; width:40pt; height:12pt'>" +
                "<div id='i' style='width:10pt; height:4pt'></div></span> Xg</div>");

            var (baselineRoot, _) = await LayoutHarness.LayoutAsync(Html("baseline"));
            var (root, _) = await LayoutHarness.LayoutAsync(Html("bottom"));

            var shift = LayoutHarness.FindById(root, "c")!.Rectangles.Values.Single().Y
                - LayoutHarness.FindById(baselineRoot, "c")!.Rectangles.Values.Single().Y;
            Assert.True(shift > 5, "the alignment must actually move the container");

            var baseItem = LayoutHarness.FindById(baselineRoot, "i")!;
            var item = LayoutHarness.FindById(root, "i")!;
            var c = LayoutHarness.FindById(root, "c")!;

            // Still at the container's content origin, i.e. shifted by the container's own shift, once.
            Assert.Equal(item.Location.Y, c.Rectangles.Values.Single().Y, 1.0);
            Assert.Equal(baseItem.Location.Y + shift, item.Location.Y, 1.0);
        }

        [Fact]
        public async Task AbsoluteChild_DoesNotTakePartInTheFlexLayout()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<span id='c' style='display:inline-flex; position:relative; width:120pt; height:60pt'>" +
                "<div id='o' style='position:absolute; top:5pt; left:7pt; width:40pt; height:20pt'></div>" +
                "<span id='z'>zz</span></span>"));

            var c = LayoutHarness.FindById(root, "c")!;
            var z = LayoutHarness.FindById(root, "z")!;

            // The in-flow item still starts the flex line, as if the absolute child were not there.
            Assert.Equal(c.Location.X, z.Location.X, 1.0);
        }
    }
}

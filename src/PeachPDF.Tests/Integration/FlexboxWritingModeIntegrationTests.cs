using System.Linq;
using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end layout tests for real writing-mode-aware Flexbox
    /// (<see cref="PeachPDF.Html.Core.Dom.CssLayoutEngineFlex"/>'s <c>_mainAxisIsPhysicalX</c>/
    /// <c>_mainStartIsAtMax</c>/<c>_crossStartIsAtMax</c> axis mapping), asserting actual post-layout
    /// <c>CssBox</c> geometry - not just that layout completes - per this repo's testing conventions for
    /// layout-engine changes. <c>flex-direction: row</c> always means "main axis = inline axis" (CSS
    /// Flexbox 1 §3): under <c>vertical-rl</c>/<c>vertical-lr</c> the inline axis is physical-vertical, so
    /// these tests exercise the axis genuinely swapping, not just a translated copy of the horizontal-tb
    /// behavior already covered by <see cref="FlexboxIntegrationTests"/>.
    /// </summary>
    public class FlexboxWritingModeIntegrationTests
    {
        [Fact]
        public async Task VerticalRl_Row_ItemsStackTopToBottom_SharingOnePhysicalXColumn()
        {
            // row's main axis is inline, which is physical-vertical under vertical-rl - items should
            // stack top-to-bottom (not left-to-right the way horizontal-tb row would place them).
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl; display: flex; width: 200px; height: 300px">
                  <div id="a" style="width: 40px; height: 30px"></div>
                  <div id="b" style="width: 40px; height: 30px"></div>
                  <div id="c" style="width: 40px; height: 30px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            var c = LayoutHarness.FindById(root, "c");
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.NotNull(c);

            Assert.True(a!.Location.Y < b!.Location.Y, "second item should sit below the first");
            Assert.True(b.Location.Y < c!.Location.Y, "third item should sit below the second");
            Assert.Equal(a.Location.X, b.Location.X, 1);
            Assert.Equal(b.Location.X, c.Location.X, 1);
        }

        [Fact]
        public async Task VerticalRl_Column_ItemsStackRightToLeft_AlongPhysicalX()
        {
            // column's main axis is block, which is physical-horizontal under vertical-rl, growing from
            // the container's own right edge leftward (block-start = right for vertical-rl).
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl; display: flex; flex-direction: column; width: 300px; height: 200px">
                  <div id="a" style="width: 40px; height: 30px"></div>
                  <div id="b" style="width: 40px; height: 30px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var container = LayoutHarness.FindById(root, "a")?.ParentBox;
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(container);
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.True(a!.Location.X > b!.Location.X, "second item should sit to the left of the first");
            // The first (flow-start) item's right edge should touch the container's own right edge.
            Assert.Equal(container!.ClientRight, a.Location.X + a.ActualBoxSizingWidth, 1);
        }

        [Fact]
        public async Task VerticalLr_Column_ItemsStackLeftToRight_AlongPhysicalX()
        {
            // block-start is the left edge for vertical-lr, the mirror image of vertical-rl.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-lr; display: flex; flex-direction: column; width: 300px; height: 200px">
                  <div id="a" style="width: 40px; height: 30px"></div>
                  <div id="b" style="width: 40px; height: 30px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var container = LayoutHarness.FindById(root, "a")?.ParentBox;
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(container);
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.True(a!.Location.X < b!.Location.X, "second item should sit to the right of the first");
            Assert.Equal(container!.ClientLeft, a.Location.X, 1);
        }

        [Fact]
        public async Task VerticalRl_RowReverse_FirstItemAnchorsAtThePhysicalBottom()
        {
            // row's main-start is the inline-start edge (physical top, for any vertical writing mode);
            // row-reverse flips flow-start to the far end - physical bottom here, not physical right the
            // way it would under horizontal-tb.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl; display: flex; flex-direction: row-reverse; width: 200px; height: 300px">
                  <div id="a" style="width: 40px; height: 30px"></div>
                  <div id="b" style="width: 40px; height: 30px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var container = LayoutHarness.FindById(root, "a")?.ParentBox;
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(container);
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.Equal(container!.ClientBottom, a!.Location.Y + a.ActualBoxSizingHeight, 1);
            Assert.True(b!.Location.Y < a.Location.Y, "the later flow item sits above the flow-first one");
        }

        [Fact]
        public async Task VerticalRl_Row_AlignItemsStretch_StretchesAlongPhysicalX()
        {
            // row's cross axis is block (physical-horizontal) under vertical-rl - a stretched item's own
            // Width (not Height) should grow to fill the container's cross size.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl; display: flex; width: 200px; height: 300px">
                  <div id="a" style="height: 30px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var container = LayoutHarness.FindById(root, "a")?.ParentBox;
            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(container);
            Assert.NotNull(a);

            Assert.InRange(a!.ActualBoxSizingWidth, container!.ClientRight - container.ClientLeft - 1,
                container.ClientRight - container.ClientLeft + 1);
        }

        [Fact]
        public async Task VerticalRl_Column_AlignItemsCenter_CentersAlongPhysicalY()
        {
            // column's cross axis is inline (physical-vertical) under vertical-rl - align-items: center
            // should center each item's own Height within the container's cross extent.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl; display: flex; flex-direction: column; align-items: center; width: 300px; height: 200px">
                  <div id="a" style="width: 40px; height: 40px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var container = LayoutHarness.FindById(root, "a")?.ParentBox;
            var a = LayoutHarness.FindById(root, "a");
            Assert.NotNull(container);
            Assert.NotNull(a);

            var expectedCenterY = (container!.ClientTop + container.ClientBottom) / 2;
            var actualCenterY = a!.Location.Y + a.ActualBoxSizingHeight / 2;
            Assert.Equal(expectedCenterY, actualCenterY, 1);
        }

        [Fact]
        public async Task VerticalRl_Row_Wrap_SecondLineSitsToTheLeftOfTheFirst()
        {
            // row's cross axis is block (physical-horizontal), growing from block-start (the container's
            // own right edge, for vertical-rl) leftward, so a wrapped second line sits to the left of the
            // first - the mirror of how a horizontal-tb row's wrapped second line sits below the first.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl; display: flex; flex-wrap: wrap; width: 300px; height: 50px">
                  <div id="a" style="width: 40px; height: 30px"></div>
                  <div id="b" style="width: 40px; height: 30px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            // The 50pt-tall box forces each item onto its own line (main axis = physical Y = height).
            Assert.True(b!.Location.X < a!.Location.X, "the second line should sit to the left of the first");
        }

        [Fact]
        public async Task VerticalRl_Row_WrapReverse_SecondLineSitsToTheRightOfTheFirst()
        {
            // flex-wrap: wrap-reverse swaps the cross-start/cross-end stacking direction - the mirror of
            // VerticalRl_Row_Wrap_SecondLineSitsToTheLeftOfTheFirst above, exercising _crossStartIsAtMax's
            // interaction with the wrap-reverse reflection in DistributeCrossSpace/AssignLocations.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-rl; display: flex; flex-wrap: wrap-reverse; width: 300px; height: 50px">
                  <div id="a" style="width: 40px; height: 30px"></div>
                  <div id="b" style="width: 40px; height: 30px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.True(b!.Location.X > a!.Location.X, "the second line should sit to the right of the first under wrap-reverse");
        }

        [Fact]
        public async Task VerticalRl_Column_JustifyContentLeft_PushesItemsAwayFromBlockStart()
        {
            // Exercises _effectiveMainStartIsAtMax's XOR directly: vertical-rl's block-start (flow-start
            // for a plain column container) is the physical-right edge, so justify-content: left should
            // push items toward flow-end (physical left) - the opposite of where they'd sit by default.
            var defaultHtml = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; display: flex; flex-direction: column; width: 300px; height: 200px">
                  <div id="a" style="width: 40px; height: 30px"></div>
                </div>
                """);
            var leftHtml = LayoutHarness.Wrap("""
                <div id="el" style="writing-mode: vertical-rl; display: flex; flex-direction: column; justify-content: left; width: 300px; height: 200px">
                  <div id="a" style="width: 40px; height: 30px"></div>
                </div>
                """);

            var (defaultRoot, _) = await LayoutHarness.LayoutAsync(defaultHtml);
            var (leftRoot, _) = await LayoutHarness.LayoutAsync(leftHtml);
            var defaultA = LayoutHarness.FindById(defaultRoot, "a");
            var leftA = LayoutHarness.FindById(leftRoot, "a");
            Assert.NotNull(defaultA);
            Assert.NotNull(leftA);

            Assert.True(leftA!.Location.X < defaultA!.Location.X,
                "justify-content: left should push the item further left than the default (flush-right) position");
        }

        [Fact]
        public async Task VerticalLr_Row_ItemsStackTopToBottom_FlushAgainstTheLeftEdge()
        {
            // row's main axis is inline (physical Y, top-to-bottom, the same for vertical-lr as for
            // vertical-rl - inline-start is top for both), but the cross axis (block, physical X) flushes
            // against the opposite physical edge: left for vertical-lr instead of right.
            var html = LayoutHarness.Wrap("""
                <div style="writing-mode: vertical-lr; display: flex; width: 220px; height: 160px">
                  <div id="a" style="width: 60px; height: 40px"></div>
                  <div id="b" style="width: 60px; height: 40px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var container = LayoutHarness.FindById(root, "a")?.ParentBox;
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(container);
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.True(a!.Location.Y < b!.Location.Y, "second item should sit below the first");
            Assert.Equal(container!.ClientLeft, a.Location.X, 1);
        }

        [Fact]
        public async Task HorizontalTb_UnaffectedByTheAxisMappingChange()
        {
            // Regression guard: the ordinary horizontal-tb row/column paths (already covered exhaustively
            // by FlexboxIntegrationTests) still produce the same geometry through the new axis-mapping
            // fields, spot-checked directly here too.
            var html = LayoutHarness.Wrap("""
                <div style="display: flex; width: 300px; height: 100px">
                  <div id="a" style="width: 40px; height: 30px"></div>
                  <div id="b" style="width: 40px; height: 30px"></div>
                </div>
                """);

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var a = LayoutHarness.FindById(root, "a");
            var b = LayoutHarness.FindById(root, "b");
            Assert.NotNull(a);
            Assert.NotNull(b);

            Assert.True(a!.Location.X < b!.Location.X, "second item should sit to the right of the first");
            Assert.Equal(a.Location.Y, b!.Location.Y, 1);
        }
    }
}

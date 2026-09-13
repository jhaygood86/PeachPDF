using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>filter: drop-shadow()</c> actually paints the right geometry/color/order, not just
    /// that it parses (see <c>FilterGrammarTests</c> for parsing coverage) - per this repo's
    /// painting-test convention (<c>OutlineStylePaintIntegrationTests</c>). It reuses
    /// <c>FragmentPainter.PaintOutsetShadow</c> - the same box-shadow blur approximation, keyed off the
    /// box's own border-box - so this exercises the reuse, not a parallel implementation.
    /// </summary>
    public class FilterDropShadowPaintIntegrationTests
    {
        [Fact]
        public async Task DropShadow_PaintsARectangleOffsetFromTheBorderBox_InTheDeclaredColor()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:50pt; height:30pt; background:#eee; filter: drop-shadow(4pt 6pt rgb(10,20,30))'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;
            var bounds = div.Bounds;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var shadow = g.Log.OfType<TestRecordingGraphics.DrawRectCall>()
                .SingleOrDefault(r => r.Color == RColor.FromArgb(10, 20, 30));
            Assert.NotNull(shadow);
            Assert.Equal(bounds.X + 4, shadow.X, 1);
            Assert.Equal(bounds.Y + 6, shadow.Y, 1);
            Assert.Equal(bounds.Width, shadow.Width, 1);
            Assert.Equal(bounds.Height, shadow.Height, 1);
        }

        [Fact]
        public async Task DropShadow_PaintsBeforeTheBoxsOwnBackground()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:50pt; height:30pt; background:rgb(1,1,1); filter: drop-shadow(4pt 6pt rgb(10,20,30))'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var shadowIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawRectCall r && r.Color == RColor.FromArgb(10, 20, 30));
            var backgroundIndex = g.Log.FindIndex(e => e is TestRecordingGraphics.DrawRectCall r && r.Color == RColor.FromArgb(1, 1, 1));

            Assert.True(shadowIndex >= 0, "expected the drop-shadow to paint");
            Assert.True(backgroundIndex >= 0, "expected the background to paint");
            Assert.True(shadowIndex < backgroundIndex, "expected drop-shadow to paint before the box's own background");
        }

        [Fact]
        public async Task DropShadow_NoColorArgument_UsesTheElementsOwnColor()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; color: rgb(7,8,9); filter: drop-shadow(2pt 2pt)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            var shadow = g.Log.OfType<TestRecordingGraphics.DrawRectCall>()
                .SingleOrDefault(r => r.Color == RColor.FromArgb(7, 8, 9));
            Assert.NotNull(shadow);
        }

        [Fact]
        public async Task MultipleDropShadows_EachPaintsItsOwnShape()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; " +
                "filter: drop-shadow(2pt 2pt rgb(1,1,1)) drop-shadow(-2pt -2pt rgb(2,2,2))'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            Assert.NotNull(g.Log.OfType<TestRecordingGraphics.DrawRectCall>().SingleOrDefault(r => r.Color == RColor.FromArgb(1, 1, 1)));
            Assert.NotNull(g.Log.OfType<TestRecordingGraphics.DrawRectCall>().SingleOrDefault(r => r.Color == RColor.FromArgb(2, 2, 2)));
        }

        [Fact]
        public async Task FilterOfOnlyDocumentedNoOps_PaintsNoShadow()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div id='b' style='width:20pt; height:20pt; background:rgb(1,1,1); filter: grayscale(50%)'>x</div>"));
            var div = LayoutHarness.FindById(root, "b")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, div, g);

            // Only the background rect should be logged - grayscale() is a documented no-op and has no
            // drop-shadow() in its list, so PaintFilterDropShadows has nothing to paint.
            var rects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().ToList();
            Assert.Single(rects);
            Assert.Equal(RColor.FromArgb(1, 1, 1), rects[0].Color);
        }
    }
}

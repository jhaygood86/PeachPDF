using System.Linq;
using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// A column flex item whose <c>max-height</c> is below its content is laid out at that clamped size, its content overflowing it
    /// (CSS Flexbox 1 section 9.7 clamps the used main size; section 9.4 step 7 lays the item out at it).
    /// </summary>
    public class FlexColumnMaxHeightTests
    {
        private static double Height(CssBox box) => box.ActualBottom - box.Location.Y;

        [Fact]
        public async Task AGrowingItemIsHeldAtItsMaxHeightAndTheOtherTakesTheRest()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-direction:column;height:300pt;width:100pt'>"
                + "<div id='a' style='flex-grow:1;max-height:100pt'><div style='width:50pt;height:1000pt'></div></div>"
                + "<div id='b' style='flex-grow:1'><div style='width:50pt;height:10pt'></div></div></div>"));

            Assert.Equal(100, Height(FindById(root, "a")!), 1);
            Assert.Equal(200, Height(FindById(root, "b")!), 1);
        }

        [Fact]
        public async Task TheNextItemStartsBelowTheClampedItemNotBelowItsContent()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-direction:column;height:300pt;width:100pt'>"
                + "<div id='a' style='flex-grow:1;max-height:100pt'><div style='width:50pt;height:1000pt'></div></div>"
                + "<div id='b' style='flex-grow:1'><div style='width:50pt;height:10pt'></div></div></div>"));

            Assert.Equal(FindById(root, "a")!.Location.Y + 100, FindById(root, "b")!.Location.Y, 1);
        }

        [Fact]
        public async Task TheClampedItemsBackgroundIsPaintedAtItsMaxHeight()
        {
            var (_, container) = await LayoutAsync(Wrap(
                "<div style='display:flex;flex-direction:column;height:300pt;width:100pt'>"
                + "<div style='flex-grow:1;max-height:100pt;background:#f00'><div style='width:50pt;height:1000pt'></div></div>"
                + "<div style='flex-grow:1;background:#0f0'><div style='width:50pt;height:10pt'></div></div></div>"));

            var recording = new RecordingGraphics(new PeachPDF.Rendering.HtmlRenderContext());
            FragmentPaintHarness.PaintPage(container, recording);
            var fills = recording.Log.Where(op => op.Kind == PaintOpKind.FillRect).ToList();

            Assert.Equal(2, fills.Count);
            Assert.Equal(100, fills[0].Bounds.Height, 1);
            Assert.Equal(200, fills[1].Bounds.Height, 1);
        }

        [Fact]
        public async Task ARowItemIsStillHeldAtItsMaxWidth()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div style='display:flex;width:300pt'>"
                + "<div id='a' style='flex-grow:1;max-width:100pt'><div style='width:50pt;height:10pt'></div></div>"
                + "<div id='b' style='flex-grow:1'><div style='width:50pt;height:10pt'></div></div></div>"));

            Assert.Equal(100, FindById(root, "a")!.ActualRight - FindById(root, "a")!.Location.X, 1);
        }
    }
}

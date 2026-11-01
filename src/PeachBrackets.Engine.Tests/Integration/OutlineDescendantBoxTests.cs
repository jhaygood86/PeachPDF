using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// An inline's outline is the minimum outline enclosing all of its boxes, descendant boxes included
    /// (CSS UI 4 §3.1; Blink feeds each non-inline descendant's rectangle into the same union). An atomic
    /// inline taller than the line holding it therefore pushes the outline out around itself (issue #1207).
    /// </summary>
    public class OutlineDescendantBoxTests
    {
        private static readonly PaintColor Ring = PaintColor.FromArgb(217, 74, 74);
        private const string Outline = "outline: 3pt solid rgb(217,74,74)";

        private static async Task<Rect> RingBoundsAsync(string body)
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body));
            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);

            var shapes = g.FilledShapes.Where(s => s.PaintColor == Ring).ToList();
            Assert.NotEmpty(shapes);
            return shapes.Select(s => s.Bounds).Aggregate(Rect.Union);
        }

        [Fact]
        public async Task ATallAtomicInlineDescendantPushesTheOutlineOutAroundIt()
        {
            var plain = await RingBoundsAsync($"<div><span style='{Outline}'>text</span></div>");
            var tall = await RingBoundsAsync(
                $"<div><span style='{Outline}'>text<span style='display:inline-block; width:20pt; height:80pt'></span></span></div>");

            // The line grows to hold the 80pt box, but the inline's own rectangle is still its content area;
            // the ring has to reach the box's far edge, which the line rectangle alone does not.
            Assert.True(tall.Height >= 80 + 2 * 3 - 0.5, $"outline height {tall.Height} does not enclose the 80pt box");
            Assert.True(tall.Height > plain.Height + 40);
        }

        [Fact]
        public async Task ADescendantInsideTheLineRectangleChangesNothing()
        {
            var plain = await RingBoundsAsync($"<div><span style='{Outline}'>some text here</span></div>");
            var withBox = await RingBoundsAsync(
                $"<div><span style='{Outline}'>some text <span style='display:inline-block; width:4pt; height:2pt'></span> here</span></div>");

            Assert.Equal(plain.Height, withBox.Height, 1);
        }

        [Fact]
        public async Task ABlockLevelOutlineIsNotWidenedByItsDescendants()
        {
            // Only an inline's outline is a union of fragments; a block's is its own border box.
            var plain = await RingBoundsAsync($"<div style='{Outline}; width:100pt'>text</div>");
            var withChild = await RingBoundsAsync(
                $"<div style='{Outline}; width:100pt'>text<span style='display:inline-block; height:2pt'></span></div>");

            Assert.Equal(plain.Width, withChild.Width, 1);
        }
    }
}

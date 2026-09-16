using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #1075: under a true vertical writing mode (<c>vertical-rl</c>/<c>vertical-lr</c>), a
    /// <c>text-decoration</c> line now runs along the physical Y axis (the column's own extent) rather
    /// than being drawn as a short horizontal stroke across its physical x-range (the column's
    /// thickness). <see href="https://www.w3.org/TR/css-writing-modes-4/#line-mappings">css-writing-modes-4
    /// §6.3</see>'s "over"/"under" are the ascender-/descender-relative sides (block-start/block-end)
    /// regardless of writing mode: physical right/left for <c>vertical-rl</c>, physical left/right for
    /// <c>vertical-lr</c>.
    /// </summary>
    public class VerticalWritingModeDecorationGeometryTests
    {
        [Theory]
        [InlineData("vertical-rl")]
        [InlineData("vertical-lr")]
        public async Task Underline_RunsAlongTheColumnsFullYExtent(string writingMode)
        {
            var (s, rect, g) = await PaintAsync(writingMode, "underline");

            var line = Assert.Single(Lines(g));
            Assert.Equal(line.X1, line.X2, 3); // a true vertical stroke: constant X
            Assert.Equal(rect.Top, line.Y1, 3);
            Assert.Equal(rect.Bottom, line.Y2, 3);
        }

        [Fact]
        public async Task Underline_VerticalRl_SitsOnThePhysicalLeftSide_TheUnderEdge()
        {
            // css-writing-modes-4 §6.4: vertical-rl's block-start ("over") is physical right, so "under"
            // (where an underline belongs) is physical left.
            var (_, rect, g) = await PaintAsync("vertical-rl", "underline");

            var line = Assert.Single(Lines(g));
            Assert.True(line.X1 < rect.X + rect.Width / 2, "vertical-rl's underline should sit left of the column's own centre");
        }

        [Fact]
        public async Task Overline_VerticalRl_SitsOnThePhysicalRightSide_TheOverEdge()
        {
            var (_, rect, g) = await PaintAsync("vertical-rl", "overline");

            var line = Assert.Single(Lines(g));
            Assert.Equal(rect.Right, line.X1, 3);
        }

        [Fact]
        public async Task Underline_VerticalLr_SitsOnThePhysicalRightSide_TheUnderEdge()
        {
            // vertical-lr's block-start ("over") is physical left, so "under" is physical right - the
            // mirror image of vertical-rl.
            var (_, rect, g) = await PaintAsync("vertical-lr", "underline");

            var line = Assert.Single(Lines(g));
            Assert.True(line.X1 > rect.X + rect.Width / 2, "vertical-lr's underline should sit right of the column's own centre");
        }

        [Fact]
        public async Task Overline_VerticalLr_SitsOnThePhysicalLeftSide_TheOverEdge()
        {
            var (_, rect, g) = await PaintAsync("vertical-lr", "overline");

            var line = Assert.Single(Lines(g));
            Assert.Equal(rect.Left, line.X1, 3);
        }

        [Fact]
        public async Task LineThrough_SitsAtTheColumnsOwnHorizontalMiddle()
        {
            var (_, rect, g) = await PaintAsync("vertical-rl", "line-through");

            var line = Assert.Single(Lines(g));
            Assert.Equal(rect.X + rect.Width / 2, line.X1, 1);
        }

        [Fact]
        public async Task Double_VerticalRl_DrawsTwoVerticalStrokes_OverlineGrowingTowardTheOverSide()
        {
            var (_, rect, g) = await PaintAsync("vertical-rl", "overline double");

            var lines = Lines(g);
            Assert.Equal(2, lines.Count);
            Assert.All(lines, l => Assert.Equal(l.X1, l.X2, 3)); // both still true vertical strokes: constant X

            // Overline's near stroke sits at the "over" edge (physical right); the outer stroke grows
            // further toward "over" - i.e. further right still, for vertical-rl.
            var near = lines.OrderBy(l => System.Math.Abs(l.X1 - rect.Right)).First();
            var outer = lines.Except([near]).Single();
            Assert.Equal(rect.Right, near.X1, 3);
            Assert.True(outer.X1 > near.X1, "vertical-rl's double overline should grow further right (toward 'over')");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<(PeachPDF.Html.Core.Dom.CssBox Span, PeachPDF.Html.Adapters.Entities.RRect Rect, TestRecordingGraphics Graphics)>
            PaintAsync(string writingMode, string decoration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(Wrap(
                $"<div style='writing-mode:{writingMode}; height:300pt'>"
                + $"<span id='s' style='text-decoration:{decoration}'>total</span></div>"));
            var s = LayoutHarness.FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, s, g);

            return (s, s.Rectangles.Values.Single(), g);
        }

        private static System.Collections.Generic.List<TestRecordingGraphics.DrawLineCall> Lines(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.DrawLineCall>().OrderBy(l => l.X1).ToList();

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>";
    }
}

using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies <c>text-decoration</c> actually draws a stroke at a plausible baseline-relative
    /// position with the right color - <c>CssBox.PaintDecoration</c> was already fully implemented but
    /// had no test confirming the paint call itself (only parse-level tests existed for
    /// text-decoration/-line/-color/-style in CSS/PropertyTests/TextProperty.cs). Uses
    /// <see cref="TestRecordingGraphics"/>'s <c>DrawLine</c> logging (added in the border-style
    /// workstream) to assert the real draw-call geometry/color, not just that painting completes.
    /// </summary>
    public class TextDecorationPaintIntegrationTests
    {
        [Fact]
        public async Task Underline_DrawsLineBelowRectangleTop()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline; color:rgb(0,0,255)'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            await s.Paint(g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Equal(RColor.FromArgb(0, 0, 255), line.Color);

            var rect = s.Rectangles.Values.Single();
            Assert.True(line.Y1 > rect.Top, "underline should sit below the rectangle's top edge");
            Assert.True(line.Y1 <= rect.Bottom, "underline should still sit within the rectangle");
        }

        [Fact]
        public async Task Overline_DrawsLineAtRectangleTop()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:overline'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            await s.Paint(g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();

            Assert.Equal(rect.Top, line.Y1, 1);
        }

        [Fact]
        public async Task LineThrough_DrawsLineNearRectangleMiddle()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:line-through'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            await s.Paint(g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            var rect = s.Rectangles.Values.Single();
            var expectedMiddle = rect.Top + rect.Height / 2;

            Assert.Equal(expectedMiddle, line.Y1, 1);
        }

        [Fact]
        public async Task Underline_OverlineLineThrough_ProduceDifferentYPositions()
        {
            var underlineY = await GetDecorationYAsync("underline");
            var overlineY = await GetDecorationYAsync("overline");
            var lineThroughY = await GetDecorationYAsync("line-through");

            Assert.True(overlineY < lineThroughY, "overline should sit above line-through");
            Assert.True(lineThroughY < underlineY, "line-through should sit above underline");
        }

        [Fact]
        public async Task CombinedLines_UnderlineOverline_DrawBothAtTheirOwnPositions()
        {
            // text-decoration-line: underline overline is a single longhand value carrying two keywords
            // (CSS Text Decoration 3 §2.2). PaintDecoration must draw a line for EACH keyword. (The prior
            // implementation switched on the whole string, so a combined value matched no case and drew a
            // single stray line at y=0 — this asserts both lines now paint at their proper positions.)
            var (root, _) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline overline'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            await s.Paint(g);

            var lines = g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToList();
            Assert.Equal(2, lines.Count);

            var rect = s.Rectangles.Values.Single();
            // Overline sits at the rectangle top; underline sits below it — the two must differ, and neither
            // may be the bogus y=0 the old whole-string switch produced.
            Assert.Contains(lines, l => System.Math.Abs(l.Y1 - rect.Top) <= 1);
            Assert.Contains(lines, l => l.Y1 > rect.Top && l.Y1 <= rect.Bottom);
            Assert.All(lines, l => Assert.True(l.Y1 > 0, "no decoration line should be drawn at y=0"));
            Assert.Equal(2, lines.Select(l => System.Math.Round(l.Y1, 2)).Distinct().Count());
        }

        [Fact]
        public async Task TextDecorationColor_OverridesElementColor()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:underline; text-decoration-color:rgb(255,0,0); color:rgb(0,0,255)'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            await s.Paint(g);

            var line = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
            Assert.Equal(RColor.FromArgb(255, 0, 0), line.Color);
        }

        [Fact]
        public async Task TextDecorationNone_DrawsNoLine()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<span id='s' style='text-decoration:none'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            await s.Paint(g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.DrawLineCall>());
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<double> GetDecorationYAsync(string decorationLine)
        {
            var (root, _) = await BuildAndLayout(Wrap(
                $"<span id='s' style='text-decoration:{decorationLine}'>text</span>"));
            var s = FindById(root, "s")!;

            var g = new TestRecordingGraphics();
            await s.Paint(g);

            return Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawLineCall>()).Y1;
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}

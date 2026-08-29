using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies the legacy CSS 2.1 <c>clip: rect()</c> paint hook in <c>FragmentPainter.PaintFragment</c>:
    /// a resolved rectangle is pushed (as an <c>RRect</c>, via <c>RenderUtils.ClipGraphicsByOverflow</c>)
    /// before an absolutely/fixed positioned element paints and popped after, and has no effect at all on
    /// a statically positioned box. Uses the recording graphics adapter so we assert the actual clip call
    /// sequence and resolved coordinates, not just that painting completed.
    /// </summary>
    public class ClipRectPaintIntegrationTests
    {
        [Fact]
        public async Task AbsolutePositioned_ClipRect_PushesResolvedRect_BracketingThePaint()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='el' style='position:absolute; clip: rect(5pt, 35pt, 25pt, 10pt); width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, el, g);

            var pushes = g.Log.OfType<TestRecordingGraphics.PushClipCall>().ToList();
            Assert.Single(pushes);
            var b = el.Bounds;
            Assert.Equal(b.Y + 5, pushes[0].Rect.Y, 1);
            Assert.Equal(b.X + 10, pushes[0].Rect.X, 1);
            Assert.Equal(b.X + 35, pushes[0].Rect.Right, 1);
            Assert.Equal(b.Y + 25, pushes[0].Rect.Bottom, 1);

            var pushIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.PushClipCall);
            var popIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.PopClipCall);
            Assert.True(pushIndex >= 0 && popIndex > pushIndex, "clip must be pushed then later popped");
        }

        [Fact]
        public async Task AbsolutePositioned_ClipRectWithAutoEdges_DoesNotClipThoseEdges()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='el' style='position:absolute; clip: rect(auto, 35pt, auto, 10pt); width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, el, g);

            var push = g.Log.OfType<TestRecordingGraphics.PushClipCall>().Single();
            var b = el.Bounds;
            Assert.Equal(b.Y, push.Rect.Y, 1);                 // auto top -> the box's own top edge
            Assert.Equal(b.X + 10, push.Rect.X, 1);
            Assert.Equal(b.X + 35, push.Rect.Right, 1);
            Assert.Equal(b.Y + b.Height, push.Rect.Bottom, 1); // auto bottom -> the box's own bottom edge
        }

        [Fact]
        public async Task PositionStatic_ClipHasNoEffect()
        {
            // CSS 2.1 §11.1.2: "clip" applies only to absolutely positioned elements.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='el' style='clip: rect(5pt, 35pt, 25pt, 10pt); width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;
            Assert.Equal(PositionMode.Static, el.Position.Value);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, el, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.PushClipCall>());
        }

        [Fact]
        public async Task AutoKeyword_PushesNoClip()
        {
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='el' style='position:absolute; clip: auto; width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, el, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.PushClipCall>());
        }

        [Fact]
        public async Task InvalidClip_IsDroppedAtParse_AndPushesNoClip()
        {
            // `banana` is not a valid rect()/auto value; Layer A drops the declaration, so nothing clips.
            var (root, container) = await BuildAndLayout(Wrap(
                "<div id='el' style='position:absolute; clip: banana; width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;
            Assert.Equal("auto", el.Clip);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, el, g);

            Assert.Empty(g.Log.OfType<TestRecordingGraphics.PushClipCall>());
        }

        // ── Helpers (mirrors ClipPathPaintIntegrationTests.cs conventions) ──────────

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

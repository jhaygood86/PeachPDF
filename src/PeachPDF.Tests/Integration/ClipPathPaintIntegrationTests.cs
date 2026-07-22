using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies the <c>clip-path</c> paint hook in <see cref="CssBox.Paint"/>: a basic-shape clip is pushed
    /// (as an <see cref="PeachPDF.Html.Adapters.RGraphicsPath"/>) before the element paints and popped after,
    /// bracketing the whole element rendering, with the geometry resolved against the border-box. Uses the
    /// recording graphics adapter so we assert the actual clip call sequence and resolved coordinates, not
    /// just that painting completed.
    /// </summary>
    public class ClipPathPaintIntegrationTests
    {
        [Fact]
        public async Task Polygon_PushesResolvedClipPath_BracketingThePaint()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='clip-path: polygon(0 0, 100% 0, 50% 100%); width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            // Exactly one clip pushed (an RGraphicsPath) and one popped.
            Assert.Single(g.ClipPaths);
            var pushIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.PushClipCall);
            var popIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.PopClipCall);
            Assert.True(pushIndex >= 0 && popIndex > pushIndex, "clip must be pushed then later popped");

            // The clip path is the triangle resolved against the border box (0 0 / 100% 0 / 50% 100%).
            var b = el.Bounds;
            var pts = g.ClipPaths[0].Points;
            Assert.Equal(3, pts.Count);
            Assert.Equal(b.X, pts[0].X, 1);
            Assert.Equal(b.Y, pts[0].Y, 1);
            Assert.Equal(b.X + b.Width, pts[1].X, 1);
            Assert.Equal(b.Y, pts[1].Y, 1);
            Assert.Equal(b.X + b.Width / 2, pts[2].X, 1);
            Assert.Equal(b.Y + b.Height, pts[2].Y, 1);
        }

        [Fact]
        public async Task Inset_PushesRectangularClip_InsetFromBorderBox()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='clip-path: inset(5pt 10pt); width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            Assert.Single(g.ClipPaths);
            var b = el.Bounds;
            var pts = g.ClipPaths[0].Points;
            // inset(5pt 10pt) => top/bottom 5pt, left/right 10pt.
            Assert.All(pts, p => Assert.InRange(p.X, b.X + 10 - 1, b.X + b.Width - 10 + 1));
            Assert.All(pts, p => Assert.InRange(p.Y, b.Y + 5 - 1, b.Y + b.Height - 5 + 1));
        }

        [Theory]
        [InlineData("circle(closest-side at center)")]
        [InlineData("circle(farthest-side at center)")]
        [InlineData("circle(at center)")] // default radius = closest-side
        [InlineData("ellipse(closest-side farthest-side at center)")]
        [InlineData("ellipse(60% 40%)")]
        public async Task CircleAndEllipse_RadiusKeywords_ProduceAClip(string clipPath)
        {
            var (root, _) = await BuildAndLayout(Wrap(
                $"<div id='el' style='clip-path: {clipPath}; width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            // An ellipse/circle clip is built as four arc segments — a non-empty clip path is pushed.
            Assert.Single(g.ClipPaths);
            Assert.NotEmpty(g.ClipPaths[0].Points);
        }

        [Fact]
        public async Task NoClipPath_PushesNoClip()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            Assert.Empty(g.ClipPaths);
        }

        [Fact]
        public async Task InvalidClipPath_IsDroppedAtParse_AndPushesNoClip()
        {
            // `banana` is not a valid basic shape; Layer A drops the declaration, so nothing clips.
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='clip-path: banana; width: 40pt; height: 30pt; background: red'>x</div>"));
            var el = FindById(root, "el")!;
            Assert.Equal("none", el.ClipPath);

            var g = new TestRecordingGraphics();
            await el.Paint(g);
            Assert.Empty(g.ClipPaths);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

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

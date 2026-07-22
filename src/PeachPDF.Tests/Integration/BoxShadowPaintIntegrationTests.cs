using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies the <c>box-shadow</c> paint hook in <see cref="CssBox"/>'s fragment loop: an outset shadow
    /// draws BEFORE the element's background (so it sits behind the box), an inset shadow draws AFTER (over
    /// the background, clipped to the padding box), a zero-blur shadow fills a single solid shape, and
    /// <c>box-shadow: none</c> paints nothing. Uses the recording graphics adapter so we assert the actual
    /// call sequence and colors, not pixels.
    /// </summary>
    public class BoxShadowPaintIntegrationTests
    {
        private static bool IsOpaqueBlack(RColor c) => c.A == 255 && c is { R: 0, G: 0, B: 0 };
        private static bool IsWhite(RColor c) => c is { A: 255, R: 255, G: 255, B: 255 };

        [Fact]
        public async Task OutsetShadow_DrawsBeforeBackground()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: 5px 5px black; background: white; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            var shadowIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawRectCall r && IsOpaqueBlack(r.Color));
            var bgIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawRectCall r && IsWhite(r.Color));

            Assert.True(shadowIndex >= 0, "an outset shadow rectangle should be drawn");
            Assert.True(bgIndex >= 0, "the white background should be drawn");
            Assert.True(shadowIndex < bgIndex, "the outset shadow must paint before the background");
        }

        [Fact]
        public async Task ZeroBlurOutsetShadow_FillsASingleSolidShape()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: 5px 5px black; background: white; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            // A zero-blur shadow is a single solid fill (no gradient falloff bands).
            var shadowRects = g.Log.OfType<TestRecordingGraphics.DrawRectCall>().Where(r => IsOpaqueBlack(r.Color)).ToList();
            var shadow = Assert.Single(shadowRects);

            // The solid shape is the border box translated by the 5px (= 3.75pt) offset, spread 0.
            var b = el.Bounds;
            Assert.Equal(b.X + 3.75, shadow.X, 1);
            Assert.Equal(b.Y + 3.75, shadow.Y, 1);
            Assert.Equal(b.Width, shadow.Width, 1);
            Assert.Equal(b.Height, shadow.Height, 1);
        }

        [Fact]
        public async Task InsetShadow_DrawsAfterBackground()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: inset 5px 5px black; background: white; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            var bgIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawRectCall r && IsWhite(r.Color));
            var shadowIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawRectCall r && IsOpaqueBlack(r.Color));

            Assert.True(bgIndex >= 0, "the white background should be drawn");
            Assert.True(shadowIndex >= 0, "an inset shadow rectangle should be drawn");
            Assert.True(shadowIndex > bgIndex, "the inset shadow must paint after the background");

            // The inset shadow is bracketed by a clip (to the padding box).
            var clipBeforeShadow = g.Log.Take(shadowIndex).Any(c => c is TestRecordingGraphics.PushClipCall);
            Assert.True(clipBeforeShadow, "the inset shadow must be clipped (to the padding box)");
        }

        [Fact]
        public async Task BlurredOutsetShadow_DrawsConcentricSemiTransparentLayers()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: 0 0 10px black; background: white; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            // A blurred shadow is approximated by a stack of concentric rounded-rect fills (DrawPath), each a
            // semi-transparent black. So there are several black DrawPath calls with partial alpha - the
            // shadow is neither a no-op nor a single hard-edged solid.
            var layerPaths = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => p.Color is { R: 0, G: 0, B: 0, A: > 0 and < 255 })
                .ToList();

            Assert.True(layerPaths.Count >= 6, $"expected several concentric blur layers, got {layerPaths.Count}");

            // Those falloff layers must paint before the white background (an outset shadow sits behind).
            var firstLayer = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawPathCall p && p.Color is { R: 0, G: 0, B: 0, A: > 0 and < 255 });
            var bgIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawRectCall r && IsWhite(r.Color));
            Assert.True(firstLayer >= 0 && bgIndex > firstLayer, "blur layers must paint before the background");
        }

        [Fact]
        public async Task BlurredInsetShadow_DrawsConcentricRingsAfterBackground()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: inset 0 0 10px black; background: white; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            // The inset falloff is a stack of even-odd ring fills (DrawPath), each a semi-transparent black,
            // all painted after (over) the white background.
            var rings = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Where(p => p.Color is { R: 0, G: 0, B: 0, A: > 0 and < 255 })
                .ToList();
            Assert.True(rings.Count >= 6, $"expected several inset ring layers, got {rings.Count}");

            var bgIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawRectCall r && IsWhite(r.Color));
            var firstRing = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawPathCall p && p.Color is { R: 0, G: 0, B: 0, A: > 0 and < 255 });
            Assert.True(bgIndex >= 0 && firstRing > bgIndex, "inset rings must paint after the background");
        }

        [Fact]
        public async Task RoundedInsetShadow_ClipsToRoundedPaddingBox()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: inset 2px 2px 4px black; border-radius: 8pt; background: white; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            // A rounded box's inset shadow is clipped to a rounded-rect path (not a plain rect) before the
            // ring layers paint. (The innermost concentric layer of an opaque color is itself opaque, so
            // count all black ring fills, not just the semi-transparent ones.)
            Assert.NotEmpty(g.ClipPaths);
            var rings = g.Log.OfType<TestRecordingGraphics.DrawPathCall>()
                .Count(p => p.Color is { R: 0, G: 0, B: 0, A: > 0 });
            Assert.True(rings >= 6, $"expected inset ring layers, got {rings}");
        }

        [Fact]
        public async Task RoundedOutsetShadow_FillsAPathNotAPlainRect()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: 4px 4px black; border-radius: 10pt; background: white; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            // A rounded box's zero-blur shadow is a rounded-rect path fill (DrawPath), not a plain rectangle,
            // and it still sits behind the (also rounded, DrawPath) background.
            var shadowPath = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawPathCall p && IsOpaqueBlack(p.Color));
            var bgPath = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawPathCall p && IsWhite(p.Color));
            Assert.True(shadowPath >= 0, "the rounded shadow should be a path fill");
            Assert.True(bgPath > shadowPath, "the outset shadow path must paint before the background");
            Assert.DoesNotContain(g.Log, c => c is TestRecordingGraphics.DrawRectCall r && IsOpaqueBlack(r.Color));
        }

        [Fact]
        public async Task MultipleLayers_PaintLastListedFirst()
        {
            // First-listed (red) should end up on top, so it must be painted AFTER the last-listed (blue).
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: 4px 4px red, 10px 10px blue; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            var blueIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawRectCall r && r.Color is { R: 0, G: 0, B: 255, A: 255 });
            var redIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawRectCall r && r.Color is { R: 255, G: 0, B: 0, A: 255 });

            Assert.True(blueIndex >= 0 && redIndex >= 0, "both shadow layers should paint");
            Assert.True(blueIndex < redIndex, "the last-listed (blue) shadow paints first, under the first-listed (red)");
        }

        [Fact]
        public async Task NoShadow_PaintsNoShadow()
        {
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: none; background: white; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            Assert.DoesNotContain(g.Log, c => c is TestRecordingGraphics.DrawRectCall r && IsOpaqueBlack(r.Color));
        }

        [Fact]
        public async Task TransparentBlurredShadow_PaintsNothing()
        {
            // A fully-transparent shadow color contributes no visible falloff, so no shadow layers are drawn.
            var (root, _) = await BuildAndLayout(Wrap(
                "<div id='el' style='box-shadow: 0 0 10px transparent; background: white; width: 40pt; height: 30pt'>x</div>"));
            var el = FindById(root, "el")!;

            var g = new TestRecordingGraphics();
            await el.Paint(g);

            // Only the white background (and text) paint - no additional shadow path fills.
            Assert.DoesNotContain(g.Log, c => c is TestRecordingGraphics.DrawPathCall);
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

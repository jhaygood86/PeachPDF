using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end paint coverage for CSS <c>object-fit</c> / <c>object-position</c> on an
    /// <c>&lt;img&gt;</c>. Before this, an image was always stretched to fill its content box. A 2:1
    /// (20×10) raster image is placed in a 96×96px (72×72pt) content box and painted through a
    /// recording graphics; the recorded destination rectangle is asserted per fit/position value.
    /// </summary>
    public class ObjectFitPaintIntegrationTests
    {
        // A real 20×10 (2:1) PNG, decoded by the production adapter so its intrinsic size/ratio are real.
        private const string Png2To1 =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABQAAAAKCAIAAAA7N+mxAAAAF0lEQVR4nGO8I+fGQC5gIlvnqOYRoxkAEvkBVJUamdUAAAAASUVORK5CYII=";

        // 96px == 72pt content box; the 2:1 intrinsic is 20×10px -> 15×7.5pt.
        private static string Doc(string style) =>
            $"<!DOCTYPE html><html><head></head><body><img id='i' style='width:96px;height:96px;{style}' src='{Png2To1}'></body></html>";

        private static async Task<RRect> DrawRect(string style)
        {
            var (root, _) = await BuildAndLayout(Doc(style));
            var g = new TestRecordingGraphics();
            await root.Paint(g);
            return Assert.Single(g.DrawImageCalls).DestRect;
        }

        [Fact]
        public async Task Fill_StretchesToContentBox()
        {
            var r = await DrawRect("object-fit:fill");
            Assert.Equal(72, r.Width, 1);
            Assert.Equal(72, r.Height, 1);
        }

        [Fact]
        public async Task Default_IsFill()
        {
            // No object-fit declared -> the initial value `fill` -> stretch to the content box.
            var r = await DrawRect("");
            Assert.Equal(72, r.Width, 1);
            Assert.Equal(72, r.Height, 1);
        }

        [Fact]
        public async Task Contain_LetterboxesPreservingAspect()
        {
            // 2:1 fit inside 72×72 -> 72×36, vertically centered (18pt top offset).
            var fill = await DrawRect("object-fit:fill");
            var r = await DrawRect("object-fit:contain");
            Assert.Equal(72, r.Width, 1);
            Assert.Equal(36, r.Height, 1);
            Assert.Equal(fill.Y + 18, r.Y, 1); // centered in the 36pt of vertical slack
        }

        [Fact]
        public async Task Cover_FillsAndOverflows()
        {
            // 2:1 cover of 72×72 -> 144×72, horizontally centered (-36pt) so it overflows and is clipped.
            var fill = await DrawRect("object-fit:fill");
            var r = await DrawRect("object-fit:cover");
            Assert.Equal(144, r.Width, 1);
            Assert.Equal(72, r.Height, 1);
            Assert.Equal(fill.X - 36, r.X, 1);
        }

        [Fact]
        public async Task None_UsesIntrinsicSize()
        {
            // Intrinsic 20×10px -> 15×7.5pt, centered.
            var r = await DrawRect("object-fit:none");
            Assert.Equal(15, r.Width, 1);
            Assert.Equal(7.5, r.Height, 1);
        }

        [Fact]
        public async Task ScaleDown_UsesIntrinsicWhenItFits()
        {
            // The intrinsic 15×7.5pt fits inside 72×72, so scale-down behaves like `none`, not `contain`.
            var r = await DrawRect("object-fit:scale-down");
            Assert.Equal(15, r.Width, 1);
            Assert.Equal(7.5, r.Height, 1);
        }

        [Fact]
        public async Task ScaleDown_UsesContainWhenIntrinsicIsLargerThanTheBox()
        {
            // In a 12×12px (9×9pt) box the 2:1 intrinsic (15×7.5pt) is wider than the box, so scale-down
            // shrinks it — behaving like `contain` (9×4.5), not `none`.
            var (root, _) = await BuildAndLayout(
                $"<!DOCTYPE html><html><body><img id='i' style='width:12px;height:12px;object-fit:scale-down' src='{Png2To1}'></body></html>");
            var g = new TestRecordingGraphics();
            await root.Paint(g);
            var r = Assert.Single(g.DrawImageCalls).DestRect;
            Assert.Equal(9, r.Width, 1);
            Assert.Equal(4.5, r.Height, 1);
        }

        [Fact]
        public async Task ObjectPosition_ShiftsTheFittedImageVertically()
        {
            // With contain (72×36 in a 72×72 box) there is 36pt of vertical slack for object-position.
            var top = await DrawRect("object-fit:contain;object-position:50% 0%");
            var center = await DrawRect("object-fit:contain;object-position:50% 50%");
            var bottom = await DrawRect("object-fit:contain;object-position:50% 100%");
            Assert.Equal(18, center.Y - top.Y, 1);
            Assert.Equal(36, bottom.Y - top.Y, 1);
        }

        [Fact]
        public async Task ObjectPosition_ShiftsTheCoveredImageHorizontally()
        {
            // With cover (144×72) there is -72pt of horizontal slack for object-position.
            var left = await DrawRect("object-fit:cover;object-position:0% 50%");
            var center = await DrawRect("object-fit:cover;object-position:50% 50%");
            var right = await DrawRect("object-fit:cover;object-position:100% 50%");
            Assert.Equal(-36, center.X - left.X, 1);
            Assert.Equal(-72, right.X - left.X, 1);
        }

        // ─── object-fit also applies to <object>, <video poster>, and inline <svg> ─────

        [Fact]
        public async Task Object_WithImageData_HonorsObjectFit()
        {
            var html = $"<!DOCTYPE html><html><body><object data='{Png2To1}' style='width:96px;height:96px;object-fit:contain'></object></body></html>";
            var (root, _) = await BuildAndLayout(html);
            var g = new TestRecordingGraphics();
            await root.Paint(g);
            var r = Assert.Single(g.DrawImageCalls).DestRect;
            Assert.Equal(72, r.Width, 1);
            Assert.Equal(36, r.Height, 1);
        }

        [Fact]
        public async Task Video_Poster_IsRendered_AndHonorsObjectFit()
        {
            var html = $"<!DOCTYPE html><html><body><video poster='{Png2To1}' style='width:96px;height:96px;object-fit:contain'></video></body></html>";
            var (root, _) = await BuildAndLayout(html);
            var g = new TestRecordingGraphics();
            await root.Paint(g);
            var r = Assert.Single(g.DrawImageCalls).DestRect;
            Assert.Equal(72, r.Width, 1);
            Assert.Equal(36, r.Height, 1);
        }

        [Fact]
        public async Task Video_WithoutPoster_DrawsNoImage()
        {
            var html = "<!DOCTYPE html><html><body><video style='width:96px;height:96px'></video></body></html>";
            var (root, _) = await BuildAndLayout(html);
            var g = new TestRecordingGraphics();
            await root.Paint(g);
            Assert.Empty(g.DrawImageCalls);
        }

        [Fact]
        public async Task InlineSvg_Cover_ClipsToContentBox()
        {
            // An inline <svg> with a 2:1 intrinsic under object-fit:cover overflows its 72×72pt content
            // box, so the shared renderer clips to it. (SVG paints via paths, not DrawImage, so we assert
            // the content-box clip rather than a DrawImage rect.)
            var html = "<!DOCTYPE html><html><body>" +
                       "<svg xmlns='http://www.w3.org/2000/svg' width='20' height='10' style='width:96px;height:96px;object-fit:cover'>" +
                       "<rect width='20' height='10' fill='red'/></svg></body></html>";
            var (root, _) = await BuildAndLayout(html);
            var g = new TestRecordingGraphics();
            await root.Paint(g);
            Assert.Contains(g.Log, o => o is TestRecordingGraphics.PushClipCall c
                && System.Math.Abs(c.Rect.Width - 72) < 1 && System.Math.Abs(c.Rect.Height - 72) < 1);
        }

        // ─── Helpers (mirrors FlexReplacedElementIntegrationTests) ────────────────

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
    }
}

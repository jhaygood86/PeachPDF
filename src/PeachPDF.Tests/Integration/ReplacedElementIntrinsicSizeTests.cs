using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #150: replaced-element intrinsic sizes are CSS pixels (1px = 1/96in) — a raster's
    /// device pixels and an SVG's user units alike — so a 96-px-wide image lays out exactly 72pt
    /// (one inch), matching browser print output. Explicit width/height on the element resolve
    /// through the same shared conversion for every absolute unit (px at 0.75pt, pt at identity).
    /// </summary>
    public class ReplacedElementIntrinsicSizeTests
    {
        // A 1x1 transparent PNG (from DataUriUtilsTests).
        private const string OnePxPng =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";

        private const string Svg96X48 =
            "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='96' height='48'%3E%3Crect width='96' height='48' fill='red'/%3E%3C/svg%3E";

        // An SVG with no width/height/viewBox has no natural (intrinsic) aspect ratio.
        private const string SvgNoIntrinsic =
            "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg'%3E%3C/svg%3E";

        [Fact]
        public async Task SvgNaturalSize_UserUnitsAreCssPixels_LayoutAtThreeQuartersPt()
        {
            // 96x48 SVG user units = 96x48 CSS px = 72pt x 36pt.
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;

            Assert.Equal(72.0, img.Words[0].Width, 1);
            Assert.Equal(36.0, img.Words[0].Height, 1);
        }

        [Fact]
        public async Task WidthAttributePx_ResolvesSpecCorrect_HeightKeepsAspectRatio()
        {
            // <img width="96"> is 96 CSS px = 72pt; the 2:1 natural ratio scales height to 36pt.
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' width='96' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;

            Assert.Equal(72.0, img.Words[0].Width, 1);
            Assert.Equal(36.0, img.Words[0].Height, 1);
        }

        [Fact]
        public async Task RasterNaturalSize_DevicePixelsAreCssPixels()
        {
            // A 1x1 raster renders 0.75pt square at natural size.
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' src=\"{OnePxPng}\" />"));
            var img = FindById(root, "img")!;

            Assert.Equal(0.75, img.Words[0].Width, 2);
            Assert.Equal(0.75, img.Words[0].Height, 2);
        }

        [Fact]
        public async Task CssWidthPt_OnImage_ResolvesAtIdentity()
        {
            // Point-sized replaced elements resolve through the same shared conversion (identity
            // for pt) — all absolute units are honored, not just px.
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='width:72pt' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;

            Assert.Equal(72.0, img.Words[0].Width, 1);
            Assert.Equal(36.0, img.Words[0].Height, 1); // 2:1 ratio preserved
        }

        [Fact]
        public async Task ImageMaxWidthPx_ClampsThroughSharedConversion()
        {
            // max-width in px resolves through the same shared conversion as every other absolute
            // unit: 48px = 36pt clamps the 96px (72pt) natural width down, and the 2:1 ratio scales
            // height to 18pt.
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='max-width:48px' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;

            Assert.Equal(36.0, img.Words[0].Width, 1);
            Assert.Equal(18.0, img.Words[0].Height, 1);
        }

        [Fact]
        public async Task ImageMinHeightPt_GrowsThroughSharedConversion()
        {
            // min-height in an absolute unit grows the box past its natural size, rescaling width by
            // the aspect ratio — exercising the absolute-unit min-height branch.
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='min-height:72pt' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;

            Assert.Equal(72.0, img.Words[0].Height, 1);
            Assert.Equal(144.0, img.Words[0].Width, 1); // 2:1 ratio grows width with height
        }

        [Fact]
        public async Task CssWidthPxAndPt_AgreeAtTheSpecRatio()
        {
            var (rootPx, _) = await BuildAndLayout(Wrap($"<img id='img' style='width:96px' src=\"{Svg96X48}\" />"));
            var (rootPt, _) = await BuildAndLayout(Wrap($"<img id='img' style='width:72pt' src=\"{Svg96X48}\" />"));

            Assert.Equal(
                FindById(rootPt, "img")!.Words[0].Width,
                FindById(rootPx, "img")!.Words[0].Width, 2);
        }

        // ─── CSS aspect-ratio on replaced elements (issue #219 gap 3) ───

        [Fact]
        public async Task BareRatio_OverridesNaturalRatio_WhenOnlyWidthSet()
        {
            // A bare <ratio> overrides the element's 2:1 natural ratio: width 72pt, aspect-ratio 1/1 => the
            // height is 72pt (square), not the 36pt the natural ratio would give.
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='width:72pt; aspect-ratio:1/1' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;
            Assert.Equal(72.0, img.Words[0].Width, 1);
            Assert.Equal(72.0, img.Words[0].Height, 1);
        }

        [Fact]
        public async Task BareRatio_OverridesNaturalRatio_WhenOnlyHeightSet()
        {
            // The height→width direction on a replaced element: height 36pt, aspect-ratio 1/1 => width 36pt
            // (the CSS ratio overrides the 2:1 natural, which would give 72pt).
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='height:36pt; aspect-ratio:1/1' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;
            Assert.Equal(36.0, img.Words[0].Width, 1);
            Assert.Equal(36.0, img.Words[0].Height, 1);
        }

        [Fact]
        public async Task AutoRatio_PrefersNaturalRatio_OverSpecified()
        {
            // `auto <ratio>` prefers the natural ratio when the element has one: the 2:1 natural ratio wins
            // over the specified 1/1, so width 72pt => height 36pt (NOT the 72pt a bare 1/1 would force).
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='width:72pt; aspect-ratio:auto 1/1' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;
            Assert.Equal(72.0, img.Words[0].Width, 1);
            Assert.Equal(36.0, img.Words[0].Height, 1);
        }

        [Fact]
        public async Task AutoRatio_FallsBackToSpecified_WhenNoNaturalRatio()
        {
            // `auto <ratio>` falls back to the specified <ratio> when the element has no natural ratio: an
            // SVG with no width/height/viewBox + width 72pt + aspect-ratio auto 3/1 => height 24pt (72/3).
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='width:72pt; aspect-ratio:auto 3/1' src=\"{SvgNoIntrinsic}\" />"));
            var img = FindById(root, "img")!;
            Assert.Equal(72.0, img.Words[0].Width, 1);
            Assert.Equal(24.0, img.Words[0].Height, 1);
        }

        [Fact]
        public async Task NoAspectRatio_KeepsNaturalRatio_Unchanged()
        {
            // Regression guard: with no aspect-ratio, the natural 2:1 ratio is used exactly as before —
            // width 72pt => height 36pt.
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='width:72pt' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;
            Assert.Equal(72.0, img.Words[0].Width, 1);
            Assert.Equal(36.0, img.Words[0].Height, 1);
        }

        [Fact]
        public async Task ZeroTermRatio_FallsBackToNaturalRatio()
        {
            // A zero-term aspect-ratio (1/0) is valid but yields no usable ratio, so the element keeps its
            // 2:1 natural ratio: width 72pt => height 36pt.
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='width:72pt; aspect-ratio:1/0' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;
            Assert.Equal(72.0, img.Words[0].Width, 1);
            Assert.Equal(36.0, img.Words[0].Height, 1);
        }

        [Fact]
        public async Task CssRatio_MinHeightRescalesWidthByTheCssRatio()
        {
            // A bare CSS ratio (1/1) makes width 72pt => height 72pt; min-height:100pt then grows the height
            // and rescales the width by that same 1:1 ratio, so both become 100pt (the CSS ratio, not the
            // 2:1 natural one, drives the rescale).
            var (root, _) = await BuildAndLayout(Wrap($"<img id='img' style='width:72pt; aspect-ratio:1/1; min-height:100pt' src=\"{Svg96X48}\" />"));
            var img = FindById(root, "img")!;
            Assert.Equal(100.0, img.Words[0].Height, 1);
            Assert.Equal(100.0, img.Words[0].Width, 1);
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body style='margin:0'>{body}</body></html>";

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

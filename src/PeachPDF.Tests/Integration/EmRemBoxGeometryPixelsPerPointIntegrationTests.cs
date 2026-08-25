using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #826: <c>CssValueParser.ParseLength</c>'s <c>em</c>/<c>rem</c>/<c>ex</c>/<c>ch</c> handling
    /// (both the typed-<see cref="Html.Core.CSS.Length"/> and the raw-<c>string</c> overloads - the
    /// latter is what <c>padding-*</c>/<c>width</c>/<c>height</c>/<c>border-*-radius</c>/etc. actually
    /// resolve through, since <c>css-properties.json</c> stores them as raw strings) applied the
    /// <c>PixelsPerPoint</c> catch-up multiply (issue #814's convention) only to absolute lengths.
    /// <see cref="CssBox.GetEmHeight"/>/<see cref="CssBox.GetRemHeight"/> live in the adapter's
    /// device-scaled *font-measurement* space (<c>trueFontSizePt / PixelsPerPoint</c> - issue #631), the
    /// opposite direction from the box's internal, <c>PixelsPerPoint</c>-inflated layout space these
    /// properties resolve into, so an em/rem/ex/ch box-geometry value landed short by a full
    /// <c>PixelsPerPoint²</c> factor under a non-default <c>PixelsPerInch</c>. Every fixture here uses a
    /// non-default <c>PixelsPerInch</c> so the bug (invisible at the library's default 72) is actually
    /// exercised - mirroring <c>PixelsPerPointEmResolutionIntegrationTests</c>'s pattern for the sibling
    /// bugs issue #631 already fixed elsewhere.
    /// </summary>
    public class EmRemBoxGeometryPixelsPerPointIntegrationTests
    {
        [Fact]
        public async Task PaddingEm_ResolvesAgainstDeclaringFontSize_UnderNonDefaultPixelsPerPoint()
        {
            // Issue #826's own repro: font-size:20pt, padding:2em. True CSS points: 2 * 20pt = 40pt. Like
            // every other absolutely-resolved box geometry value, this lands in the box's internal layout
            // coordinate space, which PixelsPerPoint inflates relative to true points (issue #814) - so at
            // pixelsPerPoint=2, the internal-space value is 40 * 2 = 80. The pre-fix code instead left the
            // em basis device-scaled, landing on 20 (half of the correct 40 true points, before even the
            // missing catch-up multiply) - the "half instead of double" symptom issue #826 reports.
            var root = await BuildAndLayout(
                "<div id='target' style='font-size:20pt;padding:2em;margin:0'>content</div>", pixelsPerPoint: 2.0);

            var target = FindById(root, "target");
            Assert.NotNull(target);
            Assert.Equal(80.0, target!.ActualPaddingTop, 3);
        }

        [Fact]
        public async Task PaddingEm_IsInvariantUnderDifferentPixelsPerInch()
        {
            var rootAt1 = await BuildAndLayout(
                "<div id='target' style='font-size:20pt;padding:2em;margin:0'>content</div>", pixelsPerPoint: 1.0);
            var rootAt2 = await BuildAndLayout(
                "<div id='target' style='font-size:20pt;padding:2em;margin:0'>content</div>", pixelsPerPoint: 2.0);

            var targetAt1 = FindById(rootAt1, "target");
            var targetAt2 = FindById(rootAt2, "target");
            Assert.NotNull(targetAt1);
            Assert.NotNull(targetAt2);

            // Internal-space padding scales linearly with pixelsPerPoint (the box's own coordinate space
            // is what's inflated) - the true-CSS-point padding itself (40pt) is DPI-invariant.
            Assert.Equal(targetAt1!.ActualPaddingTop * 2.0, targetAt2!.ActualPaddingTop, 3);
        }

        [Fact]
        public async Task WidthEm_ResolvesAgainstDeclaringFontSize_UnderNonDefaultPixelsPerPoint()
        {
            // True CSS points: 10 * 20pt = 200pt -> internal space at pixelsPerPoint=2 is 400.
            var root = await BuildAndLayout(
                "<div id='target' style='font-size:20pt;width:10em;margin:0'>content</div>", pixelsPerPoint: 2.0);

            var target = FindById(root, "target");
            Assert.NotNull(target);
            Assert.Equal(400.0, target!.ActualWidth, 3);
        }

        [Fact]
        public async Task BorderRadiusRem_ResolvesAgainstRootFontSize_UnderNonDefaultPixelsPerPoint()
        {
            // rem always resolves against the root's font-size (11pt UA default here, independent of the
            // declaring div's own 30pt font-size) - true CSS points: 2 * 11pt = 22pt -> internal space at
            // pixelsPerPoint=2 is 44.
            var root = await BuildAndLayout(
                "<div id='target' style='font-size:30pt;border:1px solid black;border-radius:2rem;margin:0'>content</div>",
                pixelsPerPoint: 2.0);

            var target = FindById(root, "target");
            Assert.NotNull(target);
            Assert.Equal(44.0, target!.ActualBorderTopLeftRadiusX, 3);
        }

        // ─── Helpers (mirrors PixelsPerPointEmResolutionIntegrationTests.BuildAndLayout) ────────────────

        private static async Task<CssBox> BuildAndLayout(string bodyHtml, double pixelsPerPoint)
        {
            var html = $"<!DOCTYPE html><html><head></head><body>{bodyHtml}</body></html>";

            var adapter = new PdfSharpAdapter { PixelsPerPoint = pixelsPerPoint };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, pixelsPerPoint);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, pixelsPerPoint);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, pixelsPerPoint);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
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

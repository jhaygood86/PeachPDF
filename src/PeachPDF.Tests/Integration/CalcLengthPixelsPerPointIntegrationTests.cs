using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #829: a <c>calc()</c> length expression never received the <c>PixelsPerPoint</c> catch-up
    /// multiply (issue #814's convention) that a literal absolute/<c>em</c>/<c>rem</c>/<c>ex</c>/<c>ch</c>
    /// length now gets (issue #826) - <c>CssValueParser.ParseLength(string,...)</c>'s multiply is gated on
    /// the whole input parsing as a single <see cref="Html.Core.CSS.Length"/>, which a <c>calc()</c>
    /// expression never does, so no leaf inside one (absolute or em/rem/ex/ch, alone or mixed with a
    /// percentage/container/viewport-relative leaf) ever landed in the box's <c>PixelsPerPoint</c>-inflated
    /// internal layout space. Every fixture here uses a non-default <c>PixelsPerInch</c> so the bug
    /// (invisible at the library's default 72) is actually exercised - mirroring
    /// <c>EmRemBoxGeometryPixelsPerPointIntegrationTests</c>'s pattern for the literal-length sibling bug.
    /// </summary>
    public class CalcLengthPixelsPerPointIntegrationTests
    {
        [Fact]
        public async Task PaddingCalcEmPlusPx_ResolvesAgainstDeclaringFontSize_UnderNonDefaultPixelsPerPoint()
        {
            // Issue #829's own repro: font-size:20pt, padding:calc(2em + 10px). True CSS points:
            // 2 * 20pt + 10px(=7.5pt) = 47.5pt. Like every other absolutely-resolved box geometry value,
            // this lands in the box's internal layout coordinate space, which PixelsPerPoint inflates
            // relative to true points (issue #814) - so at pixelsPerPoint=2, the internal-space value is
            // 47.5 * 2 = 95. The pre-fix code left both leaves entirely unscaled, landing on the true-point
            // value 47.5 unchanged regardless of PixelsPerInch.
            var root = await BuildAndLayout(
                "<div id='target' style='font-size:20pt;padding:calc(2em + 10px);margin:0'>content</div>",
                pixelsPerPoint: 2.0);

            var target = FindById(root, "target");
            Assert.NotNull(target);
            Assert.Equal(95.0, target!.ActualPaddingTop, 3);
        }

        [Fact]
        public async Task PaddingCalcEmPlusPx_IsInvariantUnderDifferentPixelsPerInch()
        {
            var rootAt1 = await BuildAndLayout(
                "<div id='target' style='font-size:20pt;padding:calc(2em + 10px);margin:0'>content</div>",
                pixelsPerPoint: 1.0);
            var rootAt2 = await BuildAndLayout(
                "<div id='target' style='font-size:20pt;padding:calc(2em + 10px);margin:0'>content</div>",
                pixelsPerPoint: 2.0);

            var targetAt1 = FindById(rootAt1, "target");
            var targetAt2 = FindById(rootAt2, "target");
            Assert.NotNull(targetAt1);
            Assert.NotNull(targetAt2);

            // Internal-space padding scales linearly with pixelsPerPoint (the box's own coordinate space
            // is what's inflated) - the true-CSS-point padding itself (47.5pt) is DPI-invariant.
            Assert.Equal(targetAt1!.ActualPaddingTop * 2.0, targetAt2!.ActualPaddingTop, 3);
        }

        [Fact]
        public async Task BorderRadiusCalcRemMinusPx_ResolvesAgainstRootFontSize_UnderNonDefaultPixelsPerPoint()
        {
            // rem always resolves against the root's font-size (11pt UA default here, independent of the
            // declaring div's own 30pt font-size) - true CSS points: 2 * 11pt - 5px(=3.75pt) = 18.25pt ->
            // internal space at pixelsPerPoint=2 is 36.5.
            var root = await BuildAndLayout(
                "<div id='target' style='font-size:30pt;border:1px solid black;border-radius:calc(2rem - 5px);margin:0'>content</div>",
                pixelsPerPoint: 2.0);

            var target = FindById(root, "target");
            Assert.NotNull(target);
            Assert.Equal(36.5, target!.ActualBorderTopLeftRadiusX, 3);
        }

        [Fact]
        public async Task WidthCalcPercentPlusPx_DoesNotDoubleScaleThePercentageLeaf()
        {
            // The outer div's own absolute width:400pt already gets the issue #814 catch-up multiply, so
            // its internal-space (inflated) content width at pixelsPerPoint=2 is 400 * 2 = 800 - that is
            // the inner div's containing block width. A percentage leaf's basis is already reported in
            // that inflated space (GetContainerRelativeUnitBasis) and must not be multiplied again, while
            // the 20px leaf (true CSS points 15pt) still needs its own catch-up multiply - internal-space:
            // 50% of 800 = 400, plus 15 * 2 = 30 -> 430.
            var root = await BuildAndLayout(
                "<div style='width:400pt;margin:0'><div id='target' style='width:calc(50% + 20px);margin:0'>content</div></div>",
                pixelsPerPoint: 2.0);

            var target = FindById(root, "target");
            Assert.NotNull(target);
            Assert.Equal(430.0, target!.ActualWidth, 3);
        }

        // ─── Helpers (mirrors EmRemBoxGeometryPixelsPerPointIntegrationTests.BuildAndLayout) ────────────

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

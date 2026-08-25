using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #820: <see cref="Html.Core.MediaQueryMatcher.CompareLength"/> (shared by <c>@media</c> and
    /// <c>@container</c> size queries) resolved an absolute-unit feature value via the box-less
    /// <c>Length.ToPixels(...)</c> overload - always true CSS points - and compared it directly against
    /// <see cref="Html.Core.MediaQueryContext.ViewportWidthPt"/>/<see cref="Html.Core.ContainerQueryContext.WidthPt"/>,
    /// which despite the <c>Pt</c> naming are PeachPDF's internal, <c>PixelsPerPoint</c>-inflated layout
    /// coordinate space whenever <c>PdfGenerateConfig.PixelsPerInch</c> is non-default (issue #814's
    /// convention). Every fixture here uses a non-default <c>PixelsPerInch</c> so the bug (invisible at
    /// the library's default 72) is actually exercised, and picks a threshold that only the fixed
    /// comparison gets right - a pre-fix run would evaluate the opposite match result.
    /// </summary>
    public class MediaContainerQueryPixelsPerPointIntegrationTests
    {
        private const string Blue = "rgb(0, 0, 255)";
        private const string Red = "rgb(255, 0, 0)";

        // ── @media (issue #820's own repro shape) ────────────────────────────────

        [Fact]
        public async Task MediaMinWidth_AbsoluteUnit_DoesNotOverMatch_UnderNonDefaultPixelsPerInch()
        {
            // Real page width is 400pt. Pre-fix, CompareLength compared the unscaled 500pt feature value
            // against the internally-inflated actual (400 * PixelsPerPoint=2 = 800), so 800 >= 500
            // incorrectly matched. Post-fix the feature value is scaled up too (500 * 2 = 1000), so
            // 800 >= 1000 correctly does not match - the real page is narrower than 500pt.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 144,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                div { color: red; }
                @media (min-width: 500pt) { div { color: blue; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var (root, _) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);
            var el = DomUtils.GetBoxByTagName(root, "div");

            Assert.NotNull(el);
            Assert.Equal(Red, el!.Color);
        }

        [Fact]
        public async Task MediaMaxWidth_AbsoluteUnit_DoesNotUnderMatch_UnderNonDefaultPixelsPerInch()
        {
            // Real page width is 400pt, well within max-width:450pt. Pre-fix, the unscaled 450pt feature
            // value was compared against the inflated actual (800), so 800 <= 450 incorrectly failed to
            // match. Post-fix the feature value scales too (450 * 2 = 900), so 800 <= 900 correctly matches.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 144,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                div { color: red; }
                @media (max-width: 450pt) { div { color: blue; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var (root, _) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);
            var el = DomUtils.GetBoxByTagName(root, "div");

            Assert.NotNull(el);
            Assert.Equal(Blue, el!.Color);
        }

        [Fact]
        public async Task MediaMinWidth_AbsoluteUnit_MatchesCorrectly_AtDefaultPixelsPerInch()
        {
            // Same 500pt-threshold-vs-400pt-page shape as the discriminating fixture above, but at the
            // library's default PixelsPerInch (PixelsPerPoint == 1) - confirms the fix doesn't disturb the
            // already-correct default-DPI case.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 72,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                div { color: red; }
                @media (min-width: 500pt) { div { color: blue; } }
                </style></head><body><div id="el">text</div></body></html>
                """;

            var (root, _) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);
            var el = DomUtils.GetBoxByTagName(root, "div");

            Assert.NotNull(el);
            Assert.Equal(Red, el!.Color);
        }

        // ── @container (the shared ContainerQueryMatcher/CompareLength path) ─────

        [Fact]
        public async Task ContainerMinWidth_AbsoluteUnit_DoesNotOverMatch_UnderNonDefaultPixelsPerInch()
        {
            // The container's own real width is 300pt. Pre-fix, the unscaled 350pt feature value was
            // compared against the container's internally-inflated width (300 * PixelsPerPoint=2 = 600),
            // so 600 >= 350 incorrectly matched. Post-fix the feature value scales too (350 * 2 = 700), so
            // 600 >= 700 correctly does not match.
            var html = """
                <!DOCTYPE html><html><head><style>
                #box { container-type: inline-size; width: 300pt; }
                p { color: red; }
                @container (min-width: 350pt) { p { color: blue; } }
                </style></head><body><div id="box"><p>text</p></div></body></html>
                """;

            var box = await FindByTag(html, "p", pixelsPerPoint: 2.0);
            Assert.Equal(Red, box.Color);
        }

        [Fact]
        public async Task ContainerMinWidth_AbsoluteUnit_MatchesCorrectly_WhenActuallySatisfied_UnderNonDefaultPixelsPerInch()
        {
            // Same container, a threshold the real 300pt width does satisfy - matches both pre- and
            // post-fix at this particular threshold direction, so this only guards against the fix
            // breaking the genuinely-matching case, not the bug itself (see the DoesNotOverMatch fixture
            // above for the discriminating case).
            var html = """
                <!DOCTYPE html><html><head><style>
                #box { container-type: inline-size; width: 300pt; }
                p { color: red; }
                @container (min-width: 250pt) { p { color: blue; } }
                </style></head><body><div id="box"><p>text</p></div></body></html>
                """;

            var box = await FindByTag(html, "p", pixelsPerPoint: 2.0);
            Assert.Equal(Blue, box.Color);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static async Task<CssBox> FindByTag(string html, string tag, double pixelsPerPoint)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = pixelsPerPoint };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            // Mirrors production (PdfGenerator.SetContent/AddPdfPages): PageSize/MaxSize and the
            // GraphicsAdapter's own scale all use the same pixelsPerPoint, so the box tree's internal
            // coordinate space is consistently inflated end to end - see
            // PixelsPerPointEmResolutionIntegrationTests.BuildAndLayout's identical rationale.
            var size = new XSize(800, 600);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, pixelsPerPoint);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, pixelsPerPoint);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, pixelsPerPoint);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            var box = DomUtils.GetBoxByTagName(container.Root!, tag);
            Assert.NotNull(box);
            return box!;
        }
    }
}

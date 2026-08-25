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
    /// Issue #631: two call sites read <see cref="CssBox.GetEmHeight"/>/<see cref="CssBox.GetRemHeight"/>
    /// as if the result were already true CSS points. It is not - <c>PdfSharpAdapter.CreateFontInt</c>
    /// divides a requested size by <c>PixelsPerPoint</c> once to reach the adapter's device-scaled
    /// font-measurement space, so undoing that division (multiplying back by <c>PixelsPerPoint</c>) is
    /// required before treating either as a true-point em/rem basis - exactly the correction
    /// <see cref="DerivedStyle.ActualFont"/>'s own <c>parentSize</c>/<c>remSize</c> inputs and
    /// <see cref="CssBox.ResolveFontSizeValueComputation"/> already apply (see
    /// <see cref="FontSizeInheritanceIntegrationTests"/>). Both bugs are invisible at the library's
    /// default <c>PixelsPerInch</c> of 72 (<c>PixelsPerPoint == 1</c>), so every fixture here uses a
    /// non-default value to actually exercise the scaling.
    /// </summary>
    public class PixelsPerPointEmResolutionIntegrationTests
    {
        // Bug 1: DomParser.ApplyPageStylesOnce's @page margin em/rem basis.

        [Fact]
        public async Task PageMarginEm_ResolvesAgainstRootFontSize_UnderNonDefaultPixelsPerInch()
        {
            // No @page { font-size } is declared, so the em basis is root.GetEmHeight() - the UA-default
            // "medium" root font-size (11pt, DefaultFontResolver.FontSize) - taking the exact path this issue
            // reports. PixelsPerInch = 144 => PixelsPerPoint = 2.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 144,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><head><style>
                @page { size: 300pt 500pt; margin-left: 2em; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """;

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);

            Assert.NotNull(container.CssPageSize);

            // True CSS points: 2 * 11pt = 22pt. HtmlContainerInt.MarginLeft lives in the page-level
            // internal pixel space (true points pre-multiplied by PixelsPerPoint - see
            // DomParser.CascadeApplyPageStyles's own doc comment), so 22 * 2 = 44. The pre-fix code
            // instead divided root.GetEmHeight() by PixelsPerPoint a second time, landing on
            // 22 / PixelsPerPoint (11) internal units - a PixelsPerPoint-squared-sized error in true
            // points, exactly the 5.5pt figure this issue reports.
            Assert.Equal(44.0, container.MarginLeft, 3);
        }

        [Fact]
        public async Task PageMarginRem_ResolvesAgainstRootFontSize_UnderNonDefaultPixelsPerInch()
        {
            // A base @page { font-size } decouples the em basis (resolved directly via
            // MarginBoxRenderer.ResolveFontSizePt, bypassing the buggy path entirely) from the rem basis
            // (always root.GetRemHeight(), unaffected by @page's own font-size) - isolating this fixture
            // to the rem line specifically. The root itself keeps the UA-default 11pt font-size.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 144,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><head><style>
                @page { size: 300pt 500pt; font-size: 20pt; margin-left: 3rem; }
                body { margin: 0; }
                </style></head><body><p>content</p></body></html>
                """;

            var (_, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);

            Assert.NotNull(container.CssPageSize);

            // True CSS points: 3 * 11pt (root's own UA-default font-size, not the page's 20pt) = 33pt.
            // Internal units: 33 * 2 = 66.
            Assert.Equal(66.0, container.MarginLeft, 3);
        }

        // Bug 2: CssBox.NoEms's em basis for text-indent/word-spacing/letter-spacing.

        [Fact]
        public async Task TextIndentEm_ResolvesAgainstDeclaringFontSize_UnderNonDefaultPixelsPerPoint()
        {
            var root = await BuildAndLayout(
                "<div id='parent' style='font-size:20pt'>" +
                "<p id='target' style='margin:0;text-indent:2em'>content</p>" +
                "</div>", pixelsPerPoint: 2.0);

            var target = FindById(root, "target");
            Assert.NotNull(target);

            // True CSS points: 2 * 20pt = 40pt. Like every other absolutely-resolved box geometry value,
            // this lands in the box's internal layout coordinate space, which PixelsPerPoint inflates
            // relative to true points (issue #814) - so at pixelsPerPoint=2, the internal-space value is
            // 40 * 2 = 80, matching BuildAndLayout's now-consistent (production-mirroring) inflation of
            // PageSize/MaxSize/GraphicsAdapter by the same factor.
            Assert.Equal(80.0, target!.ActualTextIndent, 3);
        }

        [Fact]
        public async Task LetterSpacingEm_ResolvesAgainstDeclaringFontSize_UnderNonDefaultPixelsPerPoint()
        {
            var spacedRoot = await BuildAndLayout(
                "<p id='target' style='font-size:20pt;letter-spacing:0.5em'>AAAA</p>", pixelsPerPoint: 2.0);
            var plainRoot = await BuildAndLayout(
                "<p id='target' style='font-size:20pt'>AAAA</p>", pixelsPerPoint: 2.0);

            var spaced = FindById(spacedRoot, "target");
            var plain = FindById(plainRoot, "target");
            Assert.NotNull(spaced);
            Assert.NotNull(plain);

            var spacedWidth = CssBox.FirstWordOccurence(spaced!, spaced!.LineBoxes[0])!.Width;
            var plainWidth = CssBox.FirstWordOccurence(plain!, plain!.LineBoxes[0])!.Width;

            // 0.5em at a declaring font-size of 20pt is 10 true CSS points per gap (see
            // LetterWordSpacingLayoutIntegrationTests.LetterSpacing_EmValue_ResolvesAgainstFontSize_InLayoutPoints,
            // which asserts the identical true-point relationship at the default PixelsPerPoint of 1) -
            // scaled by pixelsPerPoint like every other absolute length landing in this internal
            // coordinate space (issue #814), same as plainWidth/spacedWidth themselves.
            Assert.Equal(plainWidth + 4 * 10 * 2.0, spacedWidth, 1);
        }

        [Fact]
        public async Task WordSpacingEm_ResolvesAgainstDeclaringFontSize_UnderNonDefaultPixelsPerPoint()
        {
            var spacedRoot = await BuildAndLayout(
                "<p id='target' style='font-size:20pt;word-spacing:0.5em'>AA BB</p>", pixelsPerPoint: 2.0);
            var plainRoot = await BuildAndLayout(
                "<p id='target' style='font-size:20pt'>AA BB</p>", pixelsPerPoint: 2.0);

            var spaced = FindById(spacedRoot, "target");
            var plain = FindById(plainRoot, "target");
            Assert.NotNull(spaced);
            Assert.NotNull(plain);

            var spacedWords = spaced!.LineBoxes[0].Words;
            var plainWords = plain!.LineBoxes[0].Words;

            var spacedGap = spacedWords[1].Left - spacedWords[0].Right;
            var plainGap = plainWords[1].Left - plainWords[0].Right;

            // 0.5em at a declaring font-size of 20pt is 10 true CSS points of additional inter-word gap,
            // scaled by pixelsPerPoint like every other absolute length landing in this internal
            // coordinate space (issue #814), same as plainGap/spacedGap themselves.
            Assert.Equal(plainGap + 10 * 2.0, spacedGap, 1);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static async Task<CssBox> BuildAndLayout(string bodyHtml, double pixelsPerPoint)
        {
            var html = $"<!DOCTYPE html><html><head></head><body>{bodyHtml}</body></html>";

            var adapter = new PdfSharpAdapter { PixelsPerPoint = pixelsPerPoint };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            // Mirrors production (PdfGenerator.SetContent/AddPdfPages): PageSize/MaxSize and the
            // GraphicsAdapter's own scale both use the SAME pixelsPerPoint as the adapter's font
            // resolution, so the box tree's internal coordinate space is consistently inflated end to
            // end - the same consistency CssValueParser's absolute-length resolution now relies on
            // (issue #814). A harness that inflated fonts but not geometry (as this one previously did,
            // hardcoding 1.0 here) tests a combination no real PdfGenerator render can produce.
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

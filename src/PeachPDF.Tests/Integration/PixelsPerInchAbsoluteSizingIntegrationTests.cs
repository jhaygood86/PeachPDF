using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #814: <c>PdfGenerateConfig.PixelsPerInch</c> sets <c>PdfSharpAdapter.PixelsPerPoint</c>
    /// (<c>PixelsPerInch / 72</c>), which the public <c>HtmlContainer</c> wrapper uses to inflate
    /// <c>HtmlContainerInt</c>'s whole internal layout coordinate space relative to true PDF points
    /// (see <c>HtmlContainer.PageSize</c>/<c>Location</c>/margin properties). Percentage/content-relative
    /// sizing and text are already self-consistently scaled in that space, but an absolute CSS length
    /// (px/pt/in/cm/mm/pc) resolved via <see cref="PeachPDF.Html.Core.Parse.CssValueParser.ParseLength(PeachPDF.CSS.Length, double, CssBox)"/>
    /// used to land as if the internal unit were always a true point - correct only at the library's
    /// default <c>PixelsPerInch</c> of 72 (<c>PixelsPerPoint == 1</c>), and silently wrong (scaled by
    /// <c>1/PixelsPerPoint</c>) otherwise. Reported via a replaced element (<c>&lt;img&gt;</c>/<c>&lt;svg&gt;</c>
    /// <c>width</c>/<c>height</c> attribute) specifically, but the same bug affected any absolutely-sized
    /// box - every fixture here uses a non-default <c>PixelsPerInch</c> to actually exercise the fix.
    /// </summary>
    public class PixelsPerInchAbsoluteSizingIntegrationTests
    {
        // A real 1x1 yellow-pixel PNG data URI, also used by FlexReplacedElementIntegrationTests/
        // BackgroundAttachmentFixedIntegrationTests/the Acid2 fixture.
        private const string PngDataUri =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP4/58BAAT/Af9jgNErAAAAAElFTkSuQmCC";

        [Fact]
        public async Task ImgWidthHeight_UnderNonDefaultPixelsPerInch_ResolveToRealPoints()
        {
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 96,
            };
            config.SetMargins(0);

            var html = $"<!DOCTYPE html><html><body>" +
                       $"<img id='i' width='34' height='34' src='{PngDataUri}'>" +
                       "</body></html>";

            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);
            var img = FindById(root, "i")!;
            var ppp = ((PdfSharpAdapter)container.Adapter).PixelsPerPoint;

            // A bare <img> (default display: inline, the only body content - not flex-blockified the
            // way FlexReplacedElementIntegrationTests's items are) carries its resolved size on its own
            // phantom word, not on ActualBoxSizingWidth/Height (which stay at their unset default for a
            // box that's never independently block-sized). 34px * 0.75 = 25.5pt, regardless of
            // PixelsPerInch - the exact ratio (0.5625 = 0.75^2) this issue reported when the
            // double-conversion instead landed the internal-space value at 25.5 * ppp / ppp^2 (i.e.
            // divided by ppp a second, uncompensated time at paint).
            var word = img.Words[0];
            Assert.Equal(25.5, word.Width / ppp, 3);
            Assert.Equal(25.5, word.Height / ppp, 3);
        }

        [Fact]
        public async Task SvgWidthHeight_UnderNonDefaultPixelsPerInch_ResolvesToRealPoints()
        {
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 96,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><body>
                <svg id='s' width='34' height='34' viewBox='0 0 32 32'><circle cx='16' cy='16' r='15' fill='orange'/></svg>
                </body></html>
                """;

            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);
            var svg = (CssBoxSvg)FindById(root, "s")!;
            var ppp = ((PdfSharpAdapter)container.Adapter).PixelsPerPoint;

            // See ImgWidthHeight_...'s identical comment: a bare, non-flex-blockified <svg> carries its
            // resolved size on its own phantom word (SvgWord), not on ActualBoxSizingWidth/Height.
            Assert.Equal(25.5, svg.SvgWord.Width / ppp, 3);
            Assert.Equal(25.5, svg.SvgWord.Height / ppp, 3);
        }

        [Fact]
        public async Task ImgAndSvg_DirectFlexItem_NoBlockSibling_ResolveIdenticallyUnderNonDefaultPixelsPerInch()
        {
            // The issue's own repro shape: a sized icon next to inline text in a flex row with
            // align-items: center, and no block sibling - so neither is wrapped in an anonymous flex
            // item box (see FlexReplacedElementIntegrationTests's own doc comment on that mechanism),
            // and each is measured as a flex item via its own explicit width/height.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 96,
            };
            config.SetMargins(0);

            var html = $"""
                <!DOCTYPE html><html><body>
                <div style='display:flex; align-items:center; gap:10px;'>
                <img id='i' width='34' height='34' src='{PngDataUri}'>
                <svg id='s' width='34' height='34' viewBox='0 0 32 32'><circle cx='16' cy='16' r='15' fill='orange'/></svg>
                <span>label</span>
                </div>
                </body></html>
                """;

            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);
            var img = FindById(root, "i")!;
            var svg = FindById(root, "s")!;
            var ppp = ((PdfSharpAdapter)container.Adapter).PixelsPerPoint;

            Assert.Equal(25.5, img.ActualBoxSizingWidth / ppp, 3);
            Assert.Equal(25.5, img.ActualBoxSizingHeight / ppp, 3);
            Assert.Equal(25.5, svg.ActualBoxSizingWidth / ppp, 3);
            Assert.Equal(25.5, svg.ActualBoxSizingHeight / ppp, 3);
        }

        [Fact]
        public async Task Svg_ContentTransformAgreesWithItsOwnClip_UnderNonDefaultPixelsPerInch()
        {
            // The issue's own most visible symptom: an inline <svg> icon's painted content overflowing
            // its own clip box in spec-strict viewers (Foxit, Chrome), invisible in a lenient PDFium
            // bitmap render. Per CLAUDE.md's painting-test convention, assert on the actual recorded
            // RGraphics calls (structural adjacency), not a content-stream substring or a hardcoded
            // magic number: whatever the clip rect and the viewBox-to-viewport transform resolve to,
            // they must agree with EACH OTHER - the viewBox's four corners, mapped through the recorded
            // PushTransform matrix, must land exactly on the recorded PushClip rect's four corners.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 96,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><body>
                <svg id='s' width='34' height='34' viewBox='0 0 32 32'><circle cx='16' cy='16' r='15' fill='orange'/></svg>
                </body></html>
                """;

            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);
            var ppp = ((PdfSharpAdapter)container.Adapter).PixelsPerPoint;
            Assert.True(ppp > 1.0, "fixture must exercise a non-default PixelsPerPoint");

            var g = new RecordingGraphics(container.Adapter) { PixelsPerPointOverride = ppp };
            FragmentPaintHarness.PaintBox(container, root, g);

            var clip = Assert.Single(g.Log, e => e.Kind == PaintOpKind.PushClip).Bounds;
            var matrix = Assert.Single(g.Log, e => e.Kind == PaintOpKind.PushTransform).Matrix!.Value;

            // RecordingGraphics logs exactly what was passed to PushClip/PushTransform, with none of
            // GraphicsAdapter's own real-backend conversion - PushClip's rect and a transform's own
            // OffsetX/OffsetY are still in the box's internal (PixelsPerPoint-inflated) coordinate space,
            // dividing by PixelsPerPoint only once actually reaching the PDF (see RGraphics.PixelsPerPoint's
            // own doc comment). The transform's linear part (M11/M22) is the one exception, already
            // pre-divided by SvgRenderer itself (issue #814) - the fix under test - since GraphicsAdapter's
            // own PushTransform never touches it. Dividing the clip and the offset by ppp here mirrors
            // that real conversion, so this comparison matches what the actual PDF page content stream
            // ends up with.
            var clipReal = new RRect(clip.X / ppp, clip.Y / ppp, clip.Width / ppp, clip.Height / ppp);
            var offsetXReal = matrix.OffsetX / ppp;
            var offsetYReal = matrix.OffsetY / ppp;

            // The <svg>'s own viewBox: (0, 0, 32, 32). Map its four corners through the (now fully
            // real-point) transform and confirm the result is exactly the real-point clip rect - i.e.
            // the content and its own clip were sized from the same, single source of truth.
            var (x0, y0) = (offsetXReal, offsetYReal);
            var (x1, y1) = (32 * matrix.M11 + offsetXReal, 32 * matrix.M22 + offsetYReal);
            var mappedX = Math.Min(x0, x1);
            var mappedY = Math.Min(y0, y1);
            var mappedWidth = Math.Abs(x1 - x0);
            var mappedHeight = Math.Abs(y1 - y0);

            Assert.Equal(clipReal.X, mappedX, 3);
            Assert.Equal(clipReal.Y, mappedY, 3);
            Assert.Equal(clipReal.Width, mappedWidth, 3);
            Assert.Equal(clipReal.Height, mappedHeight, 3);
        }

        [Fact]
        public async Task PlainDiv_AbsolutePxWidthHeight_UnderNonDefaultPixelsPerInch_ResolvesToRealPoints()
        {
            // Not img/svg-specific: the underlying bug (issue #814) was general to any absolutely-sized
            // box, not just replaced elements.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 96,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><body>
                <div id='d' style='width:100px;height:50px;'></div>
                </body></html>
                """;

            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);
            var div = FindById(root, "d")!;
            var ppp = ((PdfSharpAdapter)container.Adapter).PixelsPerPoint;

            // 100px * 0.75 = 75pt; 50px * 0.75 = 37.5pt.
            Assert.Equal(75.0, div.ActualBoxSizingWidth / ppp, 3);
            Assert.Equal(37.5, div.ActualBoxSizingHeight / ppp, 3);
        }

        [Fact]
        public async Task AbsolutePxWidth_NestedInsidePercentageWidthAncestor_BothResolveCorrectly()
        {
            // Proves the fix (scaling only the absolute branch) doesn't disturb percentage resolution,
            // which is already self-consistently scaled through the inflated internal coordinate space.
            var config = new PdfGenerateConfig
            {
                ManualPageWidth = 400,
                ManualPageHeight = 600,
                PixelsPerInch = 96,
            };
            config.SetMargins(0);

            const string html = """
                <!DOCTYPE html><html><body>
                <div id='outer' style='width:50%;'><div id='inner' style='width:100px;'></div></div>
                </body></html>
                """;

            var (root, container) = await PdfGeneratorLayoutHarness.LayoutAsync(html, config);
            var inner = FindById(root, "inner")!;
            var ppp = ((PdfSharpAdapter)container.Adapter).PixelsPerPoint;

            Assert.Equal(75.0, inner.ActualBoxSizingWidth / ppp, 3);
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

using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for the per-page paint window behind #125's full-bleed pages: when a per-page
    /// <c>@page</c> rule overrides margins (e.g. <c>:first { margin: 0 }</c>), <c>PdfGenerator</c>'s
    /// page loop sets <see cref="HtmlContainerInt.PageClipOverride"/> so <see cref="HtmlContainerInt.PerformPaint"/>
    /// pushes a window matching that page's own margins instead of the base-margin
    /// <see cref="HtmlContainerInt.PageBoxRect"/>. The window's height must always equal the slot's
    /// own content-band height (the base band here, since these fixtures carry no vertical @page
    /// overrides; a slot's own variable band once they do — see PerPageGeometryLayoutIntegrationTests):
    /// a window taller than the band would repaint the neighboring pagination slot's content on this
    /// page. Per this repo's painting-test convention, these assert the actual recorded call sequence
    /// (<see cref="TestRecordingGraphics"/>), not content-stream substrings.
    /// </summary>
    public class PerPageMarginPaintWindowIntegrationTests
    {
        // A Letter-like grid with round numbers: 612-wide sheet, margins L/R=50 T=70 B=60,
        // giving a 512 × 662 content band whose layout origin is (50, 70).
        private const double SheetWidth = 612;
        private const double MarginLeftPx = 50;
        private const double MarginTopPx = 70;
        private const double MarginRightPx = 50;
        private const double MarginBottomPx = 60;
        private const double BandWidth = SheetWidth - MarginLeftPx - MarginRightPx;
        private const double BandHeight = 662;

        [Fact]
        public async Task PerformPaint_NoOverride_PushesPageBoxRect()
        {
            var container = await BuildLayoutAsync(SimpleCoverHtml);

            var recording = await PaintPageAsync(container, scrollOffset: 0, clipOverride: null);

            var firstClip = Assert.IsType<TestRecordingGraphics.PushClipCall>(recording.Log[0]);
            Assert.Equal(container.PageBoxRect, firstClip.Rect);
            Assert.Equal(new RRect(MarginLeftPx, MarginTopPx, BandWidth + MarginRightPx, BandHeight), firstClip.Rect);
        }

        [Fact]
        public async Task PerformPaint_PageClipOverride_IsPushedInsteadOfPageBoxRect()
        {
            var container = await BuildLayoutAsync(SimpleCoverHtml);
            var fullBleedWindow = new RRect(MarginLeftPx, MarginTopPx, SheetWidth, BandHeight);

            var recording = await PaintPageAsync(container, scrollOffset: 0, clipOverride: fullBleedWindow);

            var firstClip = Assert.IsType<TestRecordingGraphics.PushClipCall>(recording.Log[0]);
            Assert.Equal(fullBleedWindow, firstClip.Rect);
            Assert.NotEqual(container.PageBoxRect, firstClip.Rect);
        }

        [Fact]
        public async Task FullBleedFirstPage_WidenedClip_DoesNotExposeSecondPageContent()
        {
            // The cover fills page 1's content band ([70, 731] on the slot grid); the forced break
            // lands PageTwoMarker flush at slot 1's top (732). The paint window's height must match
            // the slot's own band (here the base band — this harness paginated on it) so page 2's
            // content never leaks onto page 1, whatever width the override reclaims.
            var container = await BuildLayoutAsync(TwoPageCoverHtml);
            var fullBleedWindow = new RRect(MarginLeftPx, MarginTopPx, SheetWidth, BandHeight);

            var page1 = await PaintPageAsync(container, scrollOffset: 0, clipOverride: fullBleedWindow);
            Assert.Contains(page1.DrawStringCalls, c => c.Text.Contains("CoverMarker"));
            Assert.DoesNotContain(page1.DrawStringCalls, c => c.Text.Contains("PageTwoMarker"));

            var page2 = await PaintPageAsync(container, scrollOffset: -BandHeight, clipOverride: null);
            Assert.Contains(page2.DrawStringCalls, c => c.Text.Contains("PageTwoMarker"));
            Assert.DoesNotContain(page2.DrawStringCalls, c => c.Text.Contains("CoverMarker"));
        }

        [Fact]
        public async Task FullBleedCover_ContentBeyondBaseBand_PaintsOnlyUnderWidenedClip()
        {
            // EdgeMarker starts at layout x = 620, just beyond the base window's right clip edge
            // (PageBoxRect spans [MarginLeft, MarginLeft + PageSize.Width + MarginRight] = [50, 612]),
            // so it is culled under the base window and only paintable once the override widens the
            // window to the physical paper edge ([50, 50 + 612] = [50, 662] in layout coordinates,
            // which the page's delta translate maps to physical [0, 612]).
            var container = await BuildLayoutAsync(TwoPageCoverHtml);

            var defaultWindow = await PaintPageAsync(container, scrollOffset: 0, clipOverride: null);
            Assert.DoesNotContain(defaultWindow.DrawStringCalls, c => c.Text.Contains("EdgeMarker"));

            var fullBleedWindow = new RRect(MarginLeftPx, MarginTopPx, SheetWidth, BandHeight);
            var widened = await PaintPageAsync(container, scrollOffset: 0, clipOverride: fullBleedWindow);
            Assert.Contains(widened.DrawStringCalls, c => c.Text.Contains("EdgeMarker"));
            Assert.Contains(widened.DrawStringCalls, c => c.Text.Contains("CoverMarker"));
        }

        // --- Fixtures ---

        private const string SimpleCoverHtml = """
            <!DOCTYPE html><html><head><style>
            body { margin: 0; }
            </style></head><body>
            <p>CoverMarker</p>
            </body></html>
            """;

        private const string TwoPageCoverHtml = """
            <!DOCTYPE html><html><head><style>
            body { margin: 0; }
            /* Height is 1pt shy of the 662pt content band: a box ending exactly AT the slot
               boundary makes the forced break target the slot after the boundary slot, leaving
               a blank page - the same reason the print_catalog showcase's full-bleed cover is
               1pt shy of its band. */
            .cover { width: 612pt; height: 661pt; page-break-after: always; }
            .cover p { margin: 0; }
            .cover p.edge { margin-left: 570pt; }
            </style></head><body>
            <div class='cover'>
            <p>CoverMarker</p>
            <p class='edge'>EdgeMarker</p>
            </div>
            <p>PageTwoMarker</p>
            </body></html>
            """;

        // --- Helpers (mirroring PdfGenerator.SetContent's container geometry at PixelsPerPoint 1) ---

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.MarginLeft = MarginLeftPx;
            container.MarginTop = MarginTopPx;
            container.MarginRight = MarginRightPx;
            container.MarginBottom = MarginBottomPx;
            container.PageSize = new RSize(BandWidth, BandHeight);
            container.Location = new RPoint(MarginLeftPx, MarginTopPx);
            container.MaxSize = new RSize(BandWidth, 0);

            var measure = XGraphics.CreateMeasureContext(new XSize(BandWidth, BandHeight), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static async Task<TestRecordingGraphics> PaintPageAsync(
            HtmlContainerInt container, double scrollOffset, RRect? clipOverride)
        {
            var recording = new TestRecordingGraphics();
            container.ScrollOffset = new RPoint(0, scrollOffset);
            container.PageClipOverride = clipOverride;
            await container.PerformPaint(recording);
            return recording;
        }
    }
}

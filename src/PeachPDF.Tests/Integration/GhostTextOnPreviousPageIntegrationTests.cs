using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for a second, contributing bug found while diagnosing #113:
    /// DomParser.CascadeApplyPageStyles wrote an @page rule's margin values straight into
    /// HtmlContainerInt.MarginTop/Bottom/Left/Right without the PixelsPerPoint scaling every other
    /// margin-setting path applies (HtmlContainer.MarginTop's own public setter; PdfGenerator.SetContent,
    /// which combines margins with PageSize entirely through that public wrapper). PixelsPerPoint is
    /// usually 1.0 (making this invisible), but ShrinkToFit/ScaleToPageSize commonly nudge it away from
    /// 1.0 by a small fraction for perfectly ordinary content, at which point the container's own notion
    /// of its page-content-band height silently drifted from the true, CSS-declared margin.
    /// </summary>
    public class PageMarginPixelsPerPointIntegrationTests
    {
        [Fact]
        public async Task PageRuleMargin_RoundTripsToTrueCssPoints_RegardlessOfPixelsPerPoint()
        {
            const string html = """
                <!DOCTYPE html><html><head><style>
                @page { margin: 15mm }
                </style></head><body>
                <p>hello</p>
                </body></html>
                """;

            // Mirrors ShrinkToFit's own effect: AddPdfPages recomputes PixelsPerPoint away from 1.0
            // whenever actual content width differs even slightly from the nominal page width - here
            // simulated directly so the test doesn't depend on triggering that heuristic exactly.
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.5 };
            using var container = new HtmlContainer(adapter);
            await container.SetHtml(html, null);

            // 15mm = 42.51968503937008pt exactly - must read back as true CSS points via the public
            // wrapper regardless of the adapter's PixelsPerPoint.
            Assert.Equal(42.51968503937008, container.MarginTop, 0.0001);
        }

        [Fact]
        public async Task PageRuleMargin_ProducesConsistentPageContentBand_RegardlessOfPixelsPerPoint()
        {
            const string html = """
                <!DOCTYPE html><html><head><style>
                @page { size: a4; margin: 15mm }
                </style></head><body>
                <p>hello</p>
                </body></html>
                """;

            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };

            // One container with PixelsPerPoint left at its default (1.0), one forced away from it -
            // both must derive the identical true-points page-content-band height.
            var adapterDefault = new PdfSharpAdapter();
            using var containerDefault = new HtmlContainer(adapterDefault);
            var orgPageSize = PdfSharpCore.PageSizeConverter.ToSize(config.PageSize);
            await PdfGenerator.SetContent(containerDefault, config, html, null, orgPageSize);

            var adapterScaled = new PdfSharpAdapter { PixelsPerPoint = 1.5 };
            using var containerScaled = new HtmlContainer(adapterScaled);
            await PdfGenerator.SetContent(containerScaled, config, html, null, orgPageSize);

            Assert.Equal(containerDefault.PageSize.Height, containerScaled.PageSize.Height, 0.0001);
            Assert.Equal(containerDefault.MarginTop, containerScaled.MarginTop, 0.0001);
        }

        // Regression test for a bug the post-change review caught in the fix above: ParseLength's
        // percentage/em/rem branches resolve against hundredPercent/emFactor/remFactor bases that are
        // THEMSELVES already in PixelsPerPoint-scaled internal space (HtmlContainerInt.PageSize.Width,
        // CssBoxProperties.GetEmHeight/GetRemHeight) - unlike the absolute-unit branches (pt/mm/cm/in/pc),
        // which resolve to raw, unscaled points. Naively multiplying every ParseLength result by
        // PixelsPerPoint once (correct for absolute units) double-scales a percentage/em/rem @page
        // margin. DomParser.CascadeApplyPageStyles's ParseMarginLength divides the three bases down to
        // true-point space first so the single final *pixelsPerPoint scale is correct for every unit type.
        [Fact]
        public async Task PercentagePageRuleMargin_RoundTripsToTrueCssPoints_RegardlessOfPixelsPerPoint()
        {
            const string html = """
                <!DOCTYPE html><html><head><style>
                @page { margin: 10% }
                </style></head><body>
                <p>hello</p>
                </body></html>
                """;

            // A percentage @page margin resolves against HtmlContainer.PageSize.Width, which must
            // already be set (mirroring how a real PdfGenerator caller only sees percentage margins
            // resolve meaningfully once PageSize carries a real page width, not its unset 0 default).
            var pageSize = new PeachPDF.PdfSharpCore.Drawing.XSize(595, 842);

            var adapterDefault = new PdfSharpAdapter();
            using var containerDefault = new HtmlContainer(adapterDefault) { PageSize = pageSize };
            await containerDefault.SetHtml(html, null);

            var adapterScaled = new PdfSharpAdapter { PixelsPerPoint = 1.5 };
            using var containerScaled = new HtmlContainer(adapterScaled) { PageSize = pageSize };
            await containerScaled.SetHtml(html, null);

            Assert.True(containerDefault.MarginTop > 0,
                "test setup should resolve a real, non-zero percentage margin");
            Assert.Equal(containerDefault.MarginTop, containerScaled.MarginTop, 0.0001);
        }
    }

    /// <summary>
    /// Regression tests for GitHub issue #113: a box relocated to the next page's content top (forced
    /// breaks, keep-with-next, break-inside:avoid, orphans/widows) lands with its own painted
    /// Rectangles/word rect exactly flush against the previous page's clip bottom. RRect.Intersect
    /// (mirroring the .NET RectangleF convention) treats two rects that merely touch at an edge as a
    /// valid, non-Empty zero-area result - and floating-point rounding across the several arithmetic
    /// steps a relocated box's Y goes through can even land that intersection a hair on the POSITIVE
    /// side of exactly zero. Comparing against the literal RRect.Empty value (or a strict &lt;= 0 check)
    /// missed both cases, so CssBox.Paint/PaintWords painted a fully-clipped (invisible on screen, but
    /// present in the content stream and any text-extraction layer) duplicate of the relocated box on
    /// the page it just left. Fixed via a small epsilon (CssBox.VisibilityClipEpsilon) in the four
    /// paint-time visibility culls that compare a clip-intersection result against empty.
    ///
    /// Per this repo's painting-test convention, these assert the actual recorded DrawString call
    /// sequence per page (TestRecordingGraphics), not content-stream substrings or rendered pixels -
    /// the ghost is invisible to rasterization, which is exactly why it shipped undetected.
    /// </summary>
    public class GhostTextOnPreviousPageIntegrationTests
    {
        private const double PageHeight = 400.0;

        [Fact]
        public async Task ForcedBreak_RelocatedHeading_DoesNotPaintOnPreviousPage()
        {
            // .filler pushes the flow close to the page-1 boundary; the forced break on .second lands
            // it flush at exactly PageHeight (zero margins keep the landing position an exact,
            // deterministic multiple of PageHeight - the precise scenario that triggers the "merely
            // touching the clip edge" bug).
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                .filler { height: 350px; }
                .second { page-break-before: always; margin: 0; }
                </style></head><body>
                <div class='filler'></div>
                <h2 class='second'>RelocatedHeadingMarker</h2>
                </body></html>
                """;

            var container = await BuildLayoutAsync(html);
            var heading = FindByClass(container.Root!, "second");
            Assert.NotNull(heading);

            // Confirm the test is actually exercising the boundary-touching case: the heading must
            // land exactly at the page-1 top, not merely somewhere on page 1.
            Assert.Equal(PageHeight, heading!.Location.Y, 0.01);

            var page0 = await PaintPageAsync(container, scrollOffset: 0);
            var page1 = await PaintPageAsync(container, scrollOffset: -PageHeight);

            Assert.DoesNotContain(page0.DrawStringCalls, c => c.Text.Contains("RelocatedHeadingMarker"));
            Assert.Contains(page1.DrawStringCalls, c => c.Text.Contains("RelocatedHeadingMarker"));
        }

        [Fact]
        public async Task BreakInsideAvoid_RelocatedBox_DoesNotPaintOnPreviousPage()
        {
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                .filler { height: 380px; }
                .avoid { break-inside: avoid; page-break-inside: avoid; margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <div class='filler'></div>
                <div class='avoid'>
                <p>AvoidedParagraphMarker</p>
                <p>Second line</p>
                <p>Third line</p>
                </div>
                </body></html>
                """;

            var container = await BuildLayoutAsync(html);
            var avoidBox = FindByClass(container.Root!, "avoid");
            Assert.NotNull(avoidBox);
            Assert.Equal(PageHeight, avoidBox!.Location.Y, 0.01);

            var page0 = await PaintPageAsync(container, scrollOffset: 0);
            var page1 = await PaintPageAsync(container, scrollOffset: -PageHeight);

            Assert.DoesNotContain(page0.DrawStringCalls, c => c.Text.Contains("AvoidedParagraphMarker"));
            Assert.Contains(page1.DrawStringCalls, c => c.Text.Contains("AvoidedParagraphMarker"));
        }

        // --- Helpers ---

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, PageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static async Task<TestRecordingGraphics> PaintPageAsync(HtmlContainerInt container, double scrollOffset)
        {
            var recording = new TestRecordingGraphics();
            container.ScrollOffset = new PeachPDF.Html.Adapters.Entities.RPoint(0, scrollOffset);
            await container.PerformPaint(recording);
            return recording;
        }

        private static CssBox? FindByClass(CssBox box, string className)
        {
            var classAttr = box.HtmlTag?.TryGetAttribute("class", "");
            if (!string.IsNullOrEmpty(classAttr) &&
                classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className))
            {
                return box;
            }

            foreach (var child in box.Boxes)
            {
                var found = FindByClass(child, className);
                if (found != null) return found;
            }
            return null;
        }
    }
}

using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layer J (floats) of mixed page orientation/size support: a float's own displacement scan
    /// (<see cref="CssLayoutEngine.FloatBox"/>/<c>FloatBoxLeft</c>/<c>FloatBoxRight</c>) captured its
    /// page-area boundary ONCE, before the scan's own drop-and-retry loop could carry the float onto a
    /// different page - so a <c>float: right</c> on any page whose per-page measure differs from its
    /// containing block's own (fixed-once) measure placed against the WRONG page's right edge. A
    /// float's own auto/percentage width already resolved correctly per landing page (issue #320/#540);
    /// only its position was wrong. Same <c>@page :first</c> mirror-margin harness convention as
    /// <see cref="PerPageHorizontalReflowLayoutIntegrationTests"/>, whose fix this mirrors for floats.
    /// </summary>
    public class FloatPerPageMeasureIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;
        private const double BaseMargin = 50;
        private const double BaseRightEdge = SheetW - BaseMargin; // 562
        private const double WideFirstPageRightEdge = SheetW; // 612 (margin-left: 0 on page 0 only)

        [Fact]
        public async Task FloatRight_OnAWiderFirstPage_ReachesThatPagesOwnRightEdge()
        {
            // The containing block (body) itself is placed once, against page 0's wide measure - before
            // the fix, ANY float:right anywhere in the document used that single stale value. f1 (on the
            // base-measure page 1) must reach 562, not the page-0 measure (612) its containing block's
            // own ClientRight would report.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <div id='f0' style='float:right; width:100pt; height:20pt;'></div>
                <p id='p0'>page zero</p>
                <p id='p1' style='page-break-before: always'>page one</p>
                <div id='f1' style='float:right; width:100pt; height:20pt;'></div>
                </body></html>
                """);

            var f0 = FindById(container.Root!, "f0")!;
            var f1 = FindById(container.Root!, "f1")!;

            Assert.Equal(0, container.PageIndexOf(f0.Location.Y));
            Assert.Equal(1, container.PageIndexOf(f1.Location.Y));

            Assert.Equal(WideFirstPageRightEdge, f0.ActualRight, 0.5);
            Assert.Equal(BaseRightEdge, f1.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(f0.Location.Y), f0.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(f1.Location.Y), f1.ActualRight, 0.5);
        }

        [Fact]
        public async Task ClearedFloatRight_ReplacedAtTheLandingPagesRightEdge()
        {
            // clearedRight's OWN initial placement (before clear resolves) is against page 0's wide
            // measure; clearance (from the tall preceding float:left) then pushes it well into page 1.
            // The re-placement after ClearBox must use page 1's own (narrower) right edge.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <div id='tallLeft' style='float:left; width:50pt; height:700pt;'></div>
                <div id='clearedRight' style='float:right; clear:left; width:100pt; height:20pt;'></div>
                </body></html>
                """);

            var clearedRight = FindById(container.Root!, "clearedRight")!;

            Assert.Equal(1, container.PageIndexOf(clearedRight.Location.Y));
            Assert.Equal(BaseRightEdge, clearedRight.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(clearedRight.Location.Y), clearedRight.ActualRight, 0.5);
        }

        [Fact]
        public async Task FloatLeft_KeepsTheBaseLeftOrigin_OnEveryPage()
        {
            // Contrast/regression guard: a float's left edge is already page-independent (document space
            // is anchored at the base left origin; the painter's per-page deltaX handles the shift), on
            // every page - not just the base-measure ones.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <div id='f0' style='float:left; width:80pt; height:20pt;'></div>
                <p id='p0'>page zero</p>
                <p id='p1' style='page-break-before: always'>page one</p>
                <div id='f1' style='float:left; width:80pt; height:20pt;'></div>
                </body></html>
                """);

            var f0 = FindById(container.Root!, "f0")!;
            var f1 = FindById(container.Root!, "f1")!;

            Assert.Equal(0, container.PageIndexOf(f0.Location.Y));
            Assert.Equal(1, container.PageIndexOf(f1.Location.Y));
            Assert.Equal(BaseMargin, f0.Location.X, 0.5);
            Assert.Equal(BaseMargin, f1.Location.X, 0.5);
        }

        [Fact]
        public async Task ClearedFloatLeft_ReplacedAtTheLandingPage_ViaTheLeftReplacementBranch()
        {
            // Mirrors ClearedFloatRight_ReplacedAtTheLandingPagesRightEdge for the Floating.Left half of
            // FloatBox's post-clear re-placement (the two are separate branches - a float:right sibling
            // contributes to clear:right's clearance per CSS 2.1 §9.5.2, pushing this float well past the
            // page boundary; the base-anchored left edge doesn't itself vary by page, but the
            // re-placement call must still run for the Left direction too).
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <div id='tallRight' style='float:right; width:50pt; height:700pt;'></div>
                <div id='clearedLeft' style='float:left; clear:right; width:100pt; height:20pt;'></div>
                </body></html>
                """);

            var clearedLeft = FindById(container.Root!, "clearedLeft")!;

            Assert.Equal(1, container.PageIndexOf(clearedLeft.Location.Y));
            Assert.Equal(BaseMargin, clearedLeft.Location.X, 0.5);
        }

        [Fact]
        public async Task FloatRight_DroppedByANarrowedScanBoundary_RederivesAtTheLandingPage()
        {
            // Exercises FloatBoxRight's own mid-scan re-derivation (inside the drop branch, not the
            // simpler "re-run from scratch after clear" path above). `ghost` is a float:right box with a
            // (valid, if unusual) negative margin-left - IsFloatIntersecting's Floating.Right case reads
            // an existing candidate's own margin-left-adjusted left edge, so a small negative value is
            // what makes an already-in-bounds box register as "intersecting" at all under that formula,
            // which is the only way this branch is reachable through DomUtils.GetFirstIntersectingFloatBox
            // as it exists today. Once found, `ghost`'s height carries the drop's MaxBottom well past the
            // page-0/page-1 boundary, and f2's own width (wider than either page's own remaining room
            // once narrowed) forces the drop that must re-derive the boundary at the Y it lands on.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                div, p { margin: 0; }
                </style></head><body>
                <div id='ghost' style='float:right; width:10pt; margin-left:-20pt; height:700pt;'></div>
                <div id='f2' style='float:right; width:560pt; height:20pt;'></div>
                </body></html>
                """);

            var f2 = FindById(container.Root!, "f2")!;

            Assert.Equal(1, container.PageIndexOf(f2.Location.Y));
            Assert.Equal(BaseRightEdge, f2.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(f2.Location.Y), f2.ActualRight, 0.5);
        }

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.PageSize = new RSize(
                SheetW * ppp - container.MarginLeft - container.MarginRight,
                SheetH * ppp - container.MarginTop - container.MarginBottom);
            container.Location = new RPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new RSize(container.PageSize.Width, 0);

            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, ppp);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, System.StringComparison.OrdinalIgnoreCase))
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

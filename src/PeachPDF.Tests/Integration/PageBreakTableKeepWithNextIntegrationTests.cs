using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for a customer bug report: a section heading with the UA default
    /// <c>h1-h6 { page-break-after: avoid }</c> could be stranded alone at the bottom of a page
    /// while the table immediately following it started on the next page.
    ///
    /// Two independent gaps produced this symptom:
    /// - Gap 1: the margin-crossing unforced-break branch in CssBox.PerformLayoutImp (the
    ///   general block Static/Relative positioning path) relocated a box to the next page
    ///   without pulling along a preceding css-break §3.1 keep-with-next run.
    /// - Gap 2: CssLayoutEngineTable.LayoutCells's whole-table pre-check was gated on
    ///   <c>!_shouldRepeatHeaders</c>, so a table with a repeating &lt;thead&gt; whose header
    ///   fit on the current page but whose first body row didn't was never relocated - the
    ///   per-row break check also can't catch it (it requires <c>i &gt; 0</c>).
    /// </summary>
    public class PageBreakTableKeepWithNextIntegrationTests
    {
        // A4 at 1:1 point scale: 595 x 842 pt, no margins (test default).
        private const double PageHeight = 842.0;

        [Fact]
        public async Task Heading_MarginCrossesPageBoundary_PullsHeadingWithTable()
        {
            // The heading itself (and its content) comfortably stays on page 1, but its
            // collapsed bottom margin against the table's top margin is large enough that the
            // table's *natural* top (before any page-break correction) lands on page 2 - the
            // margin-crossing branch (Gap 1), not the table engine's own EstimateRowHeight
            // pre-check, is what relocates the table here.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                h2 { margin: 0 0 140px 0; font-size: 14px; }
                table { border-collapse: collapse; width: 100%; margin: 0; }
                th, td { padding: 3px; }
                </style></head><body>
                <div style='height: 700px'></div>
                <h2 class='heading'>Transactions</h2>
                <table>
                  <thead><tr><th>Date</th><th>Amount</th></tr></thead>
                  <tbody><tr><td>1/1</td><td>$1.00</td></tr></tbody>
                </table>
                </body></html>
                """;

            var (heading, table, _) = await GetHeadingTableAndPageHeight(html);

            Assert.NotNull(heading);
            Assert.NotNull(table);

            // Before the Gap 1 fix: the table alone moved to page 2's top (Y == PageHeight)
            // while the heading stayed behind on page 1 - stranded.
            Assert.True(table!.Location.Y >= PageHeight,
                $"Table should be relocated to page 2 (Y >= {PageHeight}) but Y={table.Location.Y:F1}");
            Assert.Equal(Math.Floor(table.Location.Y / PageHeight), Math.Floor(heading!.Location.Y / PageHeight));
            Assert.True(heading.ActualBottom <= table.Location.Y + 1.0,
                $"Heading (bottom={heading.ActualBottom:F1}) must sit above the moved table (top={table.Location.Y:F1})");
        }

        [Fact]
        public async Task HeaderFitsButNoBodyRowDoes_MovesWholeTableAndHeadingTogether()
        {
            // The <thead> row fits comfortably under the heading on page 1, but the tall
            // .rbox body row does not - only Gap 2's post-Step-2 pre-check can catch this,
            // since the per-row break check requires i > 0 (never true for the first row) and
            // the headerless pre-check is explicitly gated off for repeating-header tables.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                h2 { margin: 0; font-size: 12px; }
                table { border-collapse: collapse; width: 100%; margin: 0; }
                th, td { padding: 3px; }
                .rbox { height: 60px; }
                </style></head><body>
                <div style='height: 800px'></div>
                <h2 class='heading'>Transactions</h2>
                <table>
                  <thead><tr><th>Date</th><th>Amount</th></tr></thead>
                  <tbody><tr><td><div class='rbox'></div></td><td><div class='rbox'></div></td></tr></tbody>
                </table>
                </body></html>
                """;

            var (heading, table, _) = await GetHeadingTableAndPageHeight(html);

            Assert.NotNull(heading);
            Assert.NotNull(table);

            Assert.True(table!.Location.Y >= PageHeight,
                $"Table (with its thead) should be relocated to page 2 (Y >= {PageHeight}) but Y={table.Location.Y:F1}");
            Assert.Equal(Math.Floor(table.Location.Y / PageHeight), Math.Floor(heading!.Location.Y / PageHeight));
            Assert.True(heading.ActualBottom <= table.Location.Y + 1.0,
                $"Heading (bottom={heading.ActualBottom:F1}) must sit above the moved table (top={table.Location.Y:F1})");
            Assert.True(table.ActualBottom - table.Location.Y <= PageHeight,
                "Moved table must fit within a single page");
        }

        [Fact]
        public async Task GapOneThenGapTwo_BothPrechecksFireForSameTable_ComposeWithoutDoubleCounting()
        {
            // Constructs a case where BOTH fixes fire, in sequence, for the same table in one
            // layout pass. The 10px spacer + 800px heading + 40px margin gives the table a
            // natural top of 850pt, crossing the 842pt page-1 boundary on its own - Gap 1 pulls
            // the heading along, landing it exactly at the page-2 top (842) and the table just
            // 2pt short of page 2's own bottom (842 + 840 = 1682, leaving only 2pt of the
            // 842pt-tall page 2 for the table's header). That's nowhere near enough for even
            // the thead's own first row, so Gap 2's header-aware pre-check fires a *second* time
            // and relocates the table (with its already-relocated header) again, onto page 3.
            // Gap 2's own guard correctly declines to pull the heading a second time - re-pulling
            // it this far would no longer leave room for the header alongside it either - so the
            // heading stays exactly where Gap 1 alone put it while the table moves on without it.
            // This is the same "unsatisfiable avoid relaxes" rule as everywhere else in this fix,
            // just triggered a second time in the same layout pass: verifies the two prechecks
            // compose from exact, hand-derived positions rather than double-counting the offset
            // or looping.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                h2 { margin: 0 0 40px 0; font-size: 14px; height: 800px; }
                table { border-collapse: collapse; width: 100%; margin: 0; }
                th, td { padding: 3px; }
                </style></head><body>
                <div style='height: 10px'></div>
                <h2 class='heading'>Transactions</h2>
                <table>
                  <thead><tr><th>Date</th><th>Amount</th></tr></thead>
                  <tbody><tr><td>1/1</td><td>$1.00</td></tr></tbody>
                </table>
                </body></html>
                """;

            var (heading, table, _) = await GetHeadingTableAndPageHeight(html);

            Assert.NotNull(heading);
            Assert.NotNull(table);

            // Gap 1 lands the heading exactly at the page-2 content top - no double-counted
            // offset from a stray extra pull.
            Assert.True(Math.Abs(heading!.Location.Y - PageHeight) < 0.1,
                $"Heading should land exactly at page 2's top ({PageHeight}) but Y={heading.Location.Y:F1}");

            // Gap 2 fires a second time and pushes the table on to page 3 - past what Gap 1
            // alone would have produced (which stopped just short of page 2's own bottom).
            Assert.True(table!.Location.Y > 2 * PageHeight,
                $"Table should be pushed on to page 3 (Y > {2 * PageHeight}) by Gap 2's second pass but Y={table.Location.Y:F1}");

            // Document order is preserved even though heading and table now sit on different
            // pages - a direct consequence of Gap 2's guard correctly declining to re-pull an
            // already-relocated run a second time once it no longer fits alongside the header.
            Assert.True(heading.ActualBottom <= table.Location.Y,
                $"Heading (bottom={heading.ActualBottom:F1}) must still precede the table (top={table.Location.Y:F1})");
        }

        [Fact]
        public async Task HeaderAndFirstBodyRowBothFit_NothingIsMoved()
        {
            // Negative case: plenty of room remains under the header for the first body row -
            // neither Gap 1 nor Gap 2's pre-check should trigger.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                h2 { margin: 0; font-size: 12px; }
                table { border-collapse: collapse; width: 100%; margin: 0; }
                th, td { padding: 3px; }
                .rbox { height: 60px; }
                </style></head><body>
                <div style='height: 400px'></div>
                <h2 class='heading'>Transactions</h2>
                <table>
                  <thead><tr><th>Date</th><th>Amount</th></tr></thead>
                  <tbody><tr><td><div class='rbox'></div></td><td><div class='rbox'></div></td></tr></tbody>
                </table>
                </body></html>
                """;

            var (heading, table, _) = await GetHeadingTableAndPageHeight(html);

            Assert.NotNull(heading);
            Assert.NotNull(table);
            Assert.True(table!.Location.Y < PageHeight,
                $"Table that fits alongside its heading should stay on page 1 (Y < {PageHeight}) but Y={table.Location.Y:F1}");
            Assert.True(heading!.Location.Y < PageHeight);
        }

        [Fact]
        public async Task LongRepeatingHeaderTable_StartingNearPageBottom_StartsOnNextPageAndRepeatsHeaders()
        {
            // A table whose entire body clearly does not fit on one page must still start
            // fresh on the next page rather than orphan its header (Gap 2 is explicitly NOT
            // gated on the whole body fitting one page) - and then fragments normally,
            // repeating its <thead> on every page it spans.
            var rows = string.Concat(Enumerable.Range(0, 60)
                .Select(i => $"<tr><td><div class='rbox'></div></td><td>{i}</td></tr>"));

            var html = $$"""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                h2 { margin: 0; font-size: 12px; }
                table { border-collapse: collapse; width: 100%; margin: 0; }
                th, td { padding: 3px; }
                .rbox { height: 60px; }
                </style></head><body>
                <div style='height: 800px'></div>
                <h2 class='heading'>Transactions</h2>
                <table>
                  <thead><tr><th>Amount</th><th>Row</th></tr></thead>
                  <tbody>{{rows}}</tbody>
                </table>
                </body></html>
                """;

            var (heading, table, pageHeight) = await GetHeadingTableAndPageHeight(html);

            Assert.NotNull(heading);
            Assert.NotNull(table);
            Assert.True(table!.Location.Y >= PageHeight,
                $"Long table should start fresh on page 2 rather than orphan its header (Y >= {PageHeight}) but Y={table.Location.Y:F1}");
            Assert.Equal(Math.Floor(table.Location.Y / PageHeight), Math.Floor(heading!.Location.Y / PageHeight));

            // The table body clearly spans more than one page from its new starting point.
            Assert.True(table.ActualBottom - table.Location.Y > pageHeight,
                "A 60-row table of 60px rows must span more than a single page");

            // The <thead> repeats: one header proxy per page the table's body actually spans.
            var headerProxyCount = table.Boxes.OfType<CssProxyBox>()
                .Count(b => b.Display == CssConstants.TableHeaderGroup);
            Assert.True(headerProxyCount >= 3,
                $"Expected the repeating header to appear on at least 3 pages, found {headerProxyCount}");
        }

        [Fact]
        public async Task HeadingTallerThanOnePage_UnsatisfiableAvoidIsRelaxed_NoInfiniteLoopAndTableStillRenders()
        {
            // The heading alone is taller than a full page, so pulling it along with the table
            // can never satisfy the avoid - css-break §3.1 says an unsatisfiable avoid is
            // relaxed. This must not infinite-loop and the table must still render somewhere
            // after the heading.
            const string html = """
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                h2 { margin: 0; font-size: 12px; height: 1000px; }
                table { border-collapse: collapse; width: 100%; margin: 0; }
                th, td { padding: 3px; }
                </style></head><body>
                <h2 class='heading'>Transactions</h2>
                <table>
                  <thead><tr><th>Date</th><th>Amount</th></tr></thead>
                  <tbody><tr><td>1/1</td><td>$1.00</td></tr></tbody>
                </table>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var ex = await Record.ExceptionAsync(() => generator.GeneratePdf(html, PageSize.A4));
            Assert.Null(ex);

            var (heading, table, _) = await GetHeadingTableAndPageHeight(html);
            Assert.NotNull(heading);
            Assert.NotNull(table);
            Assert.True(heading!.Location.Y <= table!.Location.Y,
                "Document order must be preserved - the heading still precedes the table");
        }

        // --- Helpers ---

        private static async Task<(CssBox? heading, CssBox? table, double pageHeight)> GetHeadingTableAndPageHeight(string html)
        {
            // Matches PdfGenerator's own setup (PixelsPerPoint = config.PixelsPerInch / 72d, i.e. 1.0
            // at the default 72 DPI) - PdfSharpAdapter's bare default of 72 double-applies the
            // pt<->internal-pixel-space conversion in font resolution (CreateFontInt divides by
            // PixelsPerPoint), producing a font ~72x too small with this harness's 1:1 PageSize scale.
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, PageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            var table = FindFirst(container.Root!, b => b.Display == "table");
            var heading = FindFirst(container.Root!, b => b.HtmlTag?.Name == "h2");
            return (heading, table, container.PageSize.Height);
        }

        private static CssBox? FindFirst(CssBox box, Func<CssBox, bool> predicate)
        {
            if (predicate(box)) return box;
            foreach (var child in box.Boxes)
            {
                var found = FindFirst(child, predicate);
                if (found != null) return found;
            }
            return null;
        }
    }
}

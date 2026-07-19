using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Verifies table page-break behaviour for single-row tables.
    ///
    /// The bug: the per-row page-break check in CssLayoutEngineTable.LayoutCells
    /// guards with `i &gt; 0`, so single-row tables (always i == 0) never get
    /// repositioned and their content straddles page boundaries.
    ///
    /// The fix adds a pre-check before the row loop: if the first row would cross
    /// a page boundary AND the entire table body fits on one page, the whole table
    /// is moved to the next page.
    /// </summary>
    public class PageBreakTableIntegrationTests
    {
        // A4 at 1:1 point scale: 595 × 842 pt, no margins (test default).
        private const double PageHeight = 842.0;

        // A spacer this tall pushes the table close enough to the page end to
        // trigger WillCrossPageBoundary for a typical single-row table height
        // (~14 pt estimated). Leaves ~8 pt of space, which is less than the estimate.
        private const double SpacerThatCrossesPage = 833;

        // A spacer this tall leaves plenty of room — no page-break needed.
        private const double SpacerThatFits = 200;

        [Fact]
        public async Task SingleRowTable_CrossingPageBoundary_IsMovedToNextPage()
        {
            // Single-row table near the end of page 1.
            // Before the fix: the row content (tall .rbox div) straddled the boundary.
            // After the fix: the whole table is relocated to page 2.
            var html = BuildHtml(SpacerThatCrossesPage, rowCount: 1);
            var (table, _) = await GetTableAndPageHeight(html);

            Assert.NotNull(table);
            Assert.True(table!.Location.Y >= PageHeight,
                $"Single-row table should be on page 2 (Y ≥ {PageHeight}) but Y={table.Location.Y:F1}");
        }

        [Fact]
        public async Task SingleRowTable_FitsOnCurrentPage_IsNotMoved()
        {
            // Table starts well within page 1 — the pre-check guard
            // (WillCrossPageBoundary returns false) must not move it.
            var html = BuildHtml(SpacerThatFits, rowCount: 1);
            var (table, _) = await GetTableAndPageHeight(html);

            Assert.NotNull(table);
            Assert.True(table!.Location.Y < PageHeight,
                $"Table that fits should stay on page 1 (Y < {PageHeight}) but Y={table.Location.Y:F1}");
        }

        [Fact]
        public async Task MultiRowTable_CrossingPageBoundary_PerRowBreakStillWorks()
        {
            // Multi-row table near the end of page 1. The existing per-row page-break
            // logic (i > 0) should still split the table between pages normally.
            // We just verify the table generates a PDF without throwing.
            var html = BuildHtml(SpacerThatCrossesPage, rowCount: 3);
            var ex = await Record.ExceptionAsync(() =>
            {
                var generator = new PdfGenerator();
                return generator.GeneratePdf(html, PageSize.A4);
            });
            Assert.Null(ex);
        }

        [Fact]
        public async Task SingleRowTable_WithRoundedBoxes_GeneratesPdf()
        {
            // Reproduces the "6 — Combined Styles" section from the border-radius
            // test harness: a single-row table with tall .rbox content near the bottom
            // of a page full of preceding sections.
            const string html = """
                <!DOCTYPE html><html><head><style>
                @page { size: a4; margin: 15mm }
                body { font: 8.5pt Arial, sans-serif; margin: 0 }
                h2 { font-size: 10pt; margin: 0.9em 0 0.3em; padding-bottom: 2px; border-bottom: 1px solid #999 }
                table.sw { border-collapse: collapse; width: 100%; margin-bottom: 0.3em }
                table.sw td { padding: 3px; vertical-align: top; width: 25% }
                .rbox { height: 60px; background: steelblue; border: 2px solid #1a6b8a; margin-bottom: 3px }
                .desc { font-size: 7pt; font-weight: bold; color: #444; margin-bottom: 1px }
                .css  { font-size: 6pt; color: #666; line-height: 1.3; word-break: break-all }
                </style></head><body>
                <h2>1</h2><table class="sw"><tr>
                  <td><div class="rbox" style="border-radius:20px"></div><div class="desc">a</div><div class="css">a</div></td>
                  <td><div class="rbox" style="border-radius:10px 30px"></div><div class="desc">b</div><div class="css">b</div></td>
                  <td><div class="rbox" style="border-radius:8px 20px 35px"></div><div class="desc">c</div><div class="css">c</div></td>
                  <td><div class="rbox" style="border-radius:5px 15px 30px 45px"></div><div class="desc">d</div><div class="css">d</div></td>
                </tr></table>
                <h2>2</h2><table class="sw"><tr>
                  <td><div class="rbox" style="border-top-left-radius:30px"></div><div class="desc">a</div><div class="css">a</div></td>
                  <td><div class="rbox" style="border-bottom-right-radius:30px"></div><div class="desc">b</div><div class="css">b</div></td>
                  <td><div class="rbox" style="border-top-left-radius:25px;border-bottom-right-radius:25px"></div><div class="desc">c</div><div class="css">c</div></td>
                  <td><div class="rbox" style="border-top-left-radius:10px;border-top-right-radius:20px;border-bottom-right-radius:30px;border-bottom-left-radius:15px"></div><div class="desc">d</div><div class="css">d</div></td>
                </tr></table>
                <h2>3</h2><table class="sw"><tr>
                  <td><div class="rbox" style="border-radius:40px/10px"></div><div class="desc">a</div><div class="css">a</div></td>
                  <td><div class="rbox" style="border-radius:10px/40px"></div><div class="desc">b</div><div class="css">b</div></td>
                  <td><div class="rbox" style="border-radius:30px/20px"></div><div class="desc">c</div><div class="css">c</div></td>
                  <td><div class="rbox" style="border-radius:20px 0/0 20px"></div><div class="desc">d</div><div class="css">d</div></td>
                </tr></table>
                <h2>4</h2><table class="sw"><tr>
                  <td><div class="rbox" style="border-radius:50%"></div><div class="desc">a</div><div class="css">a</div></td>
                  <td><div class="rbox" style="border-radius:25%"></div><div class="desc">b</div><div class="css">b</div></td>
                  <td><div class="rbox" style="border-radius:50%/25%"></div><div class="desc">c</div><div class="css">c</div></td>
                  <td><div class="rbox" style="border-radius:25%/50%"></div><div class="desc">d</div><div class="css">d</div></td>
                </tr></table>
                <h2>5</h2><table class="sw"><tr>
                  <td><div class="rbox" style="width:80px;border-radius:60px"></div><div class="desc">a</div><div class="css">a</div></td>
                  <td><div class="rbox" style="width:80px;border-radius:100px"></div><div class="desc">b</div><div class="css">b</div></td>
                  <td><div class="rbox" style="width:80px;height:80px;border-radius:50%"></div><div class="desc">c</div><div class="css">c</div></td>
                  <td><div class="rbox" style="width:80px;border-radius:20px"></div><div class="desc">d</div><div class="css">d</div></td>
                </tr></table>
                <h2>6 — Combined Styles</h2><table class="sw"><tr>
                  <td><div class="rbox" style="border-radius:15px"></div><div class="desc">solid border + bg</div><div class="css">border-radius: 15px</div></td>
                  <td><div class="rbox" style="border-style:dashed;border-radius:15px"></div><div class="desc">dashed border</div><div class="css">border-radius: 15px</div></td>
                  <td><div class="rbox" style="border-style:dotted;border-radius:15px"></div><div class="desc">dotted border</div><div class="css">border-radius: 15px</div></td>
                  <td><div class="rbox" style="border:none;border-radius:15px"></div><div class="desc">no border, bg only</div><div class="css">border-radius: 15px</div></td>
                </tr></table>
                </body></html>
                """;

            var generator = new PdfGenerator();
            var ex = await Record.ExceptionAsync(() => generator.GeneratePdf(html, PageSize.A4));
            Assert.Null(ex);
        }

        // The pre-check decides from EstimateRowHeight - a one-line-of-text heuristic that is
        // blind to tall block content inside a cell (the Custom Properties showcase's themeable
        // card, ~120pt of styled divs, was estimated as ~one line). When the estimate concludes
        // "fits" but real layout straddles the boundary, the post-check in LayoutCells must move
        // the fully laid-out table to the next page - a single-row table is never row-split, so
        // without the move its content paints sliced across two pages.
        [Fact]
        public async Task SingleRowTable_TallCellContentMissedByEstimate_IsMovedToNextPageAfterLayout()
        {
            // Table top at ~500: any plausible one-line estimate (tens of points, real or
            // fallback font metrics) stays far from the 842pt boundary, so the pre-check
            // declines - only the actual 400px cell content reveals the crossing.
            var html = BuildTallContentHtml(spacerHeight: 500, contentHeight: 400);
            var (table, pageHeight) = await GetTableAndPageHeight(html);

            Assert.NotNull(table);
            Assert.True(table!.Location.Y >= PageHeight,
                $"Table with tall cell content should be moved to page 2 (Y ≥ {PageHeight}) but Y={table.Location.Y:F1}");
            Assert.True(Math.Abs(table.Location.Y - PageHeight) < 1.0,
                $"Moved table should start flush at the next page top ({PageHeight}) but Y={table.Location.Y:F1}");
            Assert.True(table.ActualBottom - table.Location.Y <= pageHeight,
                "Moved table must fit within a single page");
        }

        [Fact]
        public async Task SingleRowTable_TallerThanOnePage_IsLeftInPlace()
        {
            // An unsatisfiable move: the row is taller than a whole page, so relocating the
            // table can't avoid a straddle - the post-check must leave it where it is.
            var html = BuildTallContentHtml(spacerHeight: 500, contentHeight: 900);
            var (table, _) = await GetTableAndPageHeight(html);

            Assert.NotNull(table);
            Assert.True(table!.Location.Y < PageHeight,
                $"Table taller than a page should stay on page 1 (Y < {PageHeight}) but Y={table.Location.Y:F1}");
        }

        // The post-check's whole-table move introduces a break between the table and whatever
        // precedes it, so it must honor css-break §3.1 keep-with-next exactly like the
        // pre-check: an avoid-chained heading (the UA default h1-h6 { page-break-after: avoid }
        // under print media) comes along instead of being stranded at the old page's bottom.
        [Fact]
        public async Task SingleRowTable_MovedByPostCheck_PullsAvoidChainedHeadingAlong()
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                h2 { margin: 6px 0; }
                table { border-collapse: collapse; width: 100%; }
                td { padding: 3px; }
                </style></head><body>
                <div style='height: 500px'></div>
                <h2 class='heading'>Section heading</h2>
                <table><tr><td><div style='height: 400px'>tall content</div></td></tr></table>
                </body></html>
                """;

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
            var table = FindFirst(container.Root!, b => b.Display == "table");
            var heading = FindFirst(container.Root!, b => b.HtmlTag?.Name == "h2");
            Assert.NotNull(table);
            Assert.NotNull(heading);

            Assert.True(table!.Location.Y >= PageHeight,
                $"Test setup expects the table to be moved to page 2 (Y ≥ {PageHeight}) but Y={table.Location.Y:F1}");
            Assert.Equal(Math.Floor(table.Location.Y / PageHeight), Math.Floor(heading!.Location.Y / PageHeight));
            Assert.True(heading.ActualBottom <= table.Location.Y + 1.0,
                $"Heading (bottom={heading.ActualBottom:F1}) must sit above the moved table (top={table.Location.Y:F1})");
        }

        // --- Helpers ---

        private static string BuildTallContentHtml(double spacerHeight, double contentHeight)
        {
            return $$"""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                table { border-collapse: collapse; width: 100%; }
                td { padding: 3px; }
                </style></head><body>
                <div style='height: {{spacerHeight}}px'></div>
                <table><tr><td><div style='height: {{contentHeight}}px'>tall content</div></td></tr></table>
                </body></html>
                """;
        }

        private static string BuildHtml(double spacerHeight, int rowCount)
        {
            var rows = new System.Text.StringBuilder();
            for (var r = 0; r < rowCount; r++)
            {
                rows.Append("<tr>");
                rows.Append("<td><div class='rbox'></div></td>");
                rows.Append("<td><div class='rbox'></div></td>");
                rows.Append("</tr>");
            }

            return $$"""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                .spacer { height: {{spacerHeight}}px; }
                table { border-collapse: collapse; width: 100%; }
                td { padding: 3px; }
                .rbox { height: 60px; }
                </style></head><body>
                <div class='spacer'></div>
                <table>{{rows}}</table>
                </body></html>
                """;
        }

        private static async Task<(CssBox? table, double pageHeight)> GetTableAndPageHeight(string html)
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
            var table = FindFirst(container.Root!, b => b.Display == "table");
            return (table, container.PageSize.Height);
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

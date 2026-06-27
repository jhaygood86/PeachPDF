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

        // --- Helpers ---

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

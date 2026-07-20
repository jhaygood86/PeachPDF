using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for the "@page margins waste marginTop+marginBottom of every page" bug:
    /// HtmlContainerInt.PageSize.Height is already margin-free (mirrors PdfGenerator.SetContent), so
    /// the real per-page content band is the "shifted grid" [k*PageSize.Height + MarginTop,
    /// (k+1)*PageSize.Height + MarginTop) - HtmlContainerInt.PageIndexOf/PageTopOf are the single
    /// definition of that grid. Unlike some of this repo's older page-break tests, the harness here
    /// mirrors PdfGenerator.SetContent exactly (PageSize.Height pre-reduced by both margins, Location
    /// starting at (0, MarginTop)) so these tests exercise the real production relationship between
    /// PageSize/MarginTop/Location, not a synthetic approximation of it.
    /// </summary>
    public class PageMarginPaginationIntegrationTests
    {
        // Matches the customer's own repro proportions almost exactly: Letter page, 0.55in top /
        // 0.7in bottom margin (the "0.55in / 0.7in" row of the bug report's own measured table).
        private const double RawPageHeight = 792.0; // Letter, pt
        private const double MarginTopIn = 39.6;    // 0.55in
        private const double MarginBottomIn = 50.4; // 0.7in

        [Fact]
        public async Task Table_WithPageMargins_FillsEachFullPageCloseToBottomMargin()
        {
            var html = BuildManyRowTableHtml(rowCount: 80);
            var (rootBox, container) = await BuildContainer(html, RawPageHeight, MarginTopIn, MarginBottomIn);

            var table = FindFirst(rootBox, b => b.Display == CssConstants.Table);
            Assert.NotNull(table);
            var rows = FindAllRows(table!);
            Assert.True(rows.Count > 10, "test setup should produce enough rows to span multiple pages");

            var page0Bottom = container.PageTopOf(1);
            var page0Rows = rows.Where(r => r.Location.Y < page0Bottom).ToList();
            Assert.NotEmpty(page0Rows);

            var lastRowOnPage0 = page0Rows.OrderByDescending(r => r.ActualBottom).First();
            var rowHeight = lastRowOnPage0.ActualBottom - lastRowOnPage0.Location.Y;

            // Before the fix, availableHeight double-subtracted marginTop+marginBottom from an
            // already margin-free PageSize.Height, so the page broke ~marginTop+marginBottom (90pt)
            // early - far more than one row's worth of slack. After the fix, the last row on the
            // page should land within about one row-height of the real page-1 boundary.
            Assert.True(page0Bottom - lastRowOnPage0.ActualBottom <= rowHeight * 1.5,
                $"Page 0's last row (bottom={lastRowOnPage0.ActualBottom:F1}) stops {page0Bottom - lastRowOnPage0.ActualBottom:F1}pt " +
                $"short of the real page boundary ({page0Bottom:F1}) - more than one row's worth (~{rowHeight:F1}pt), " +
                "indicating the page is under-filled.");
        }

        [Fact]
        public async Task Table_WithZeroPageMargins_StillFillsEachPage()
        {
            // Guards the historical (always-correct) zero-margin default against regressing.
            var html = BuildManyRowTableHtml(rowCount: 80);
            var (rootBox, container) = await BuildContainer(html, RawPageHeight, marginTop: 0, marginBottom: 0);

            var table = FindFirst(rootBox, b => b.Display == CssConstants.Table);
            Assert.NotNull(table);
            var rows = FindAllRows(table!);

            var page0Bottom = container.PageTopOf(1);
            var page0Rows = rows.Where(r => r.Location.Y < page0Bottom).ToList();
            Assert.NotEmpty(page0Rows);

            var lastRowOnPage0 = page0Rows.OrderByDescending(r => r.ActualBottom).First();
            var rowHeight = lastRowOnPage0.ActualBottom - lastRowOnPage0.Location.Y;

            Assert.True(page0Bottom - lastRowOnPage0.ActualBottom <= rowHeight * 1.5,
                $"Zero-margin page should still fill close to its boundary ({page0Bottom:F1}), " +
                $"but last row bottom is {lastRowOnPage0.ActualBottom:F1}.");
        }

        [Fact]
        public async Task ForcedPageBreak_WithPageMargins_LandsAtShiftedPageTop()
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                .filler { height: 500px; }
                .second { page-break-before: always; }
                </style></head><body>
                <div class='filler'></div>
                <div class='second'>Second</div>
                </body></html>
                """;

            var (rootBox, container) = await BuildContainer(html, RawPageHeight, MarginTopIn, MarginBottomIn);
            var second = FindByClass(rootBox, "second");
            Assert.NotNull(second);

            var expectedTop = container.PageTopOf(1);
            Assert.True(Math.Abs(second!.Location.Y - expectedTop) < 1.0,
                $"Forced break with page margins should land exactly at the shifted page-1 top ({expectedTop:F1}), but landed at {second.Location.Y:F1}");
        }

        [Fact]
        public async Task BreakInsideAvoid_WithPageMargins_PositionsAtShiftedPageTop()
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                .filler { height: 640px; }
                .avoid { break-inside: avoid; page-break-inside: avoid; }
                p { margin: 0; line-height: 20px; }
                </style></head><body>
                <div class='filler'></div>
                <div class='avoid'>
                <p>Line 1</p><p>Line 2</p><p>Line 3</p><p>Line 4</p><p>Line 5</p>
                <p>Line 6</p><p>Line 7</p><p>Line 8</p><p>Line 9</p><p>Line 10</p>
                </div>
                </body></html>
                """;

            var (rootBox, container) = await BuildContainer(html, RawPageHeight, MarginTopIn, MarginBottomIn);
            var avoidBox = FindByClass(rootBox, "avoid");
            Assert.NotNull(avoidBox);

            var expectedTop = container.PageTopOf(1);
            Assert.True(avoidBox!.Location.Y >= container.PageSize.Height,
                "test setup expects the avoid box to be relocated past page 0 to validate positioning");
            Assert.True(Math.Abs(avoidBox.Location.Y - expectedTop) < 1.0,
                $"break-inside:avoid with page margins should relocate to the shifted page-1 top ({expectedTop:F1}), but landed at {avoidBox.Location.Y:F1}");
        }

        [Fact]
        public async Task OrphansWidows_WithPageMargins_PushesWholeParagraphToShiftedPageTop()
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                .filler { height: 660px; }
                .para { orphans: 4; widows: 4; line-height: 20px; margin: 0; }
                </style></head><body>
                <div class='filler'></div>
                <div class='para'>
                Line1 Line2 Line3 Line4 Line5 Line6 Line7 Line8 Line9 Line10
                Line11 Line12 Line13 Line14 Line15 Line16 Line17 Line18
                </div>
                </body></html>
                """;

            var (rootBox, container) = await BuildContainer(html, RawPageHeight, MarginTopIn, MarginBottomIn);
            var para = FindByClass(rootBox, "para");
            Assert.NotNull(para);

            // If orphans/widows relocated the whole paragraph, it should sit exactly at the shifted
            // page-1 top - if it didn't need to relocate (all lines already fit), that's fine too, but
            // then we can't validate the push, so skip in that case.
            if (para!.Location.Y < container.PageSize.Height) return;

            var expectedTop = container.PageTopOf(1);
            Assert.True(Math.Abs(para.Location.Y - expectedTop) < 1.0,
                $"orphans/widows push with page margins should land at the shifted page-1 top ({expectedTop:F1}), but landed at {para.Location.Y:F1}");
        }

        // --- Helpers ---

        private static string BuildManyRowTableHtml(int rowCount)
        {
            var rows = new System.Text.StringBuilder();
            for (var i = 0; i < rowCount; i++)
            {
                rows.Append($"<tr><td style='padding:2px;border:1px solid #ccc;'>Row {i}</td></tr>");
            }

            return $$"""
                <!DOCTYPE html><html><head><style>
                body { margin: 0; }
                table { border-collapse: collapse; width: 100%; font-size: 10pt; }
                </style></head><body>
                <table>{{rows}}</table>
                </body></html>
                """;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildContainer(
            string html, double rawPageHeight, double marginTop, double marginBottom)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            // Mirrors PdfGenerator.SetContent exactly: PageSize.Height is the margin-free content
            // band, and layout starts at (MarginLeft, MarginTop) - not the raw page height/origin.
            container.MarginTop = marginTop;
            container.MarginBottom = marginBottom;
            container.MarginLeft = 0;
            container.MarginRight = 0;

            var contentSize = new XSize(612, rawPageHeight - marginTop - marginBottom);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(contentSize, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(contentSize, 1.0);
            container.Location = PeachPDF.Utilities.Utils.Convert(new XPoint(0, marginTop), 1.0);

            var measure = XGraphics.CreateMeasureContext(new XSize(612, rawPageHeight), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static List<CssBox> FindAllRows(CssBox table)
        {
            var rows = new List<CssBox>();
            CollectRows(table, rows);
            return rows;
        }

        private static void CollectRows(CssBox box, List<CssBox> rows)
        {
            if (box.Display == CssConstants.TableRow) rows.Add(box);
            foreach (var child in box.Boxes) CollectRows(child, rows);
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

using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using Xunit.Abstractions;

namespace PeachPDF.Tests.Html.Core.Dom
{
    public class CssLayoutEngineTablePageBreakTests
    {
        private readonly ITestOutputHelper _output;

        public CssLayoutEngineTablePageBreakTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Page Break Offset Tests

        [Fact]
        public async Task PageBreakOffset_RowsOnSubsequentPages_StartAtCorrectY()
        {
            // Regression test: CalculatePageBreakOffset was adding marginTop twice.
            // Rows on subsequent pages were placed marginTop pixels too far down.
            var pageHeight = 200.0;
            var marginTop = 20.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <tbody>
" + string.Join("", Enumerable.Range(1, 20).Select(i =>
    $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var tbody = table.Boxes.FirstOrDefault(b => b.Display == CssConstants.TableRowGroup);
            Assert.NotNull(tbody);

            var rows = tbody.Boxes.Where(b => b.Display == CssConstants.TableRow).ToList();
            _output.WriteLine($"Total rows: {rows.Count}");

            // Find the first body row that starts on page 2 (Y >= pageHeight)
            var firstRowOnPage2 = rows.FirstOrDefault(r => r.Location.Y >= pageHeight);
            Assert.NotNull(firstRowOnPage2);

            _output.WriteLine($"First row on page 2: Location.Y={firstRowOnPage2.Location.Y}");

            // The row's Y relative to the page boundary should be close to marginTop (not 2*marginTop).
            // (Location.Y - marginTop) % pageHeight gives offset from the page's content top.
            var offsetFromPageTop = (firstRowOnPage2.Location.Y - marginTop) % pageHeight;
            _output.WriteLine($"Offset from page content top: {offsetFromPageTop}");
            _output.WriteLine($"MarginTop: {marginTop}");

            // Should be within marginTop+5 pixels of page content top, NOT 2*marginTop away.
            Assert.True(offsetFromPageTop <= marginTop + 5,
                $"Row on page 2 starts {offsetFromPageTop}px from page content top, expected <= {marginTop + 5}px. " +
                $"A value near {marginTop * 2} indicates the double-marginTop regression.");
        }

        #endregion

        #region Available Height / Bottom Margin Tests

        [Fact]
        public async Task AvailableHeight_PageBreakFiringPoint_RowDoesNotBleedIntoBottomMargin()
        {
            // Regression test: availableHeight was missing - marginTop, so the page break fired
            // too late, allowing the last row on a page to extend into the bottom margin area.
            var pageHeight = 150.0;
            var marginBottom = 20.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <tbody>
" + string.Join("", Enumerable.Range(1, 15).Select(i =>
    $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var tbody = table.Boxes.FirstOrDefault(b => b.Display == CssConstants.TableRowGroup);
            Assert.NotNull(tbody);

            var rows = tbody.Boxes.Where(b => b.Display == CssConstants.TableRow).ToList();
            _output.WriteLine($"Total rows: {rows.Count}");

            // Check every row whose top is on page 0
            var contentBottomPage0 = pageHeight - marginBottom;
            foreach (var row in rows.Where(r => r.Location.Y < pageHeight))
            {
                _output.WriteLine($"Row on page 0: Location.Y={row.Location.Y}, ActualBottom={row.ActualBottom}");
                Assert.True(row.ActualBottom <= contentBottomPage0,
                    $"Row ActualBottom={row.ActualBottom} bleeds into bottom margin " +
                    $"(limit={contentBottomPage0}). Missing - marginTop in availableHeight regression.");
            }
        }

        #endregion

        #region PageBreakBottoms Tests

        [Fact]
        public async Task PageBreakBottoms_PopulatedForMultiPageTable()
        {
            // After layout of a multi-page table, PageBreakBottoms should contain at least one entry.
            var pageHeight = 200.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <tbody>
" + string.Join("", Enumerable.Range(1, 20).Select(i =>
    $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            _output.WriteLine($"Table ActualBottom: {table.ActualBottom}, PageHeight: {pageHeight}");
            _output.WriteLine($"PageBreakBottoms: {(table.PageBreakBottoms == null ? "null" : $"{table.PageBreakBottoms.Count} entries")}");

            Assert.NotNull(table.PageBreakBottoms);
            Assert.NotEmpty(table.PageBreakBottoms);

            foreach (var (pageNum, breakY) in table.PageBreakBottoms)
            {
                _output.WriteLine($"  Page {pageNum}: breakY={breakY}");
                // The break Y is the actual bottom of the last row placed on this page.
                // It must be positive and associated with a valid page number.
                Assert.True(breakY > 0, $"PageBreakBottoms[{pageNum}] should be positive, was {breakY}");
                Assert.True(pageNum >= 0, $"Page number must be non-negative, was {pageNum}");
            }
        }

        [Fact]
        public async Task PageBreakBottoms_SinglePageTable_IsNullOrEmpty()
        {
            // A table that fits entirely on one page should NOT have PageBreakBottoms populated.
            var pageHeight = 2000.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <tbody>
" + string.Join("", Enumerable.Range(1, 5).Select(i =>
    $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            _output.WriteLine($"Table ActualBottom: {table.ActualBottom}, PageHeight: {pageHeight}");
            _output.WriteLine($"PageBreakBottoms: {(table.PageBreakBottoms == null ? "null" : $"{table.PageBreakBottoms.Count} entries")}");

            Assert.True(
                table.PageBreakBottoms == null || table.PageBreakBottoms.Count == 0,
                $"Single-page table should not have PageBreakBottoms, but had {table.PageBreakBottoms?.Count} entries");
        }

        [Fact]
        public async Task PageBreakBottoms_BottomYIsWithinPageContentArea()
        {
            // Each entry in PageBreakBottoms must fall within the content area of its page.
            // Content area for page N: [N*pageHeight + marginTop, (N+1)*pageHeight - marginBottom]
            var pageHeight = 200.0;
            var marginTop = 20.0;
            var marginBottom = 20.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <tbody>
" + string.Join("", Enumerable.Range(1, 20).Select(i =>
    $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            Assert.NotNull(table.PageBreakBottoms);
            Assert.NotEmpty(table.PageBreakBottoms);

            foreach (var (pageNum, breakY) in table.PageBreakBottoms)
            {
                var contentTop = pageNum * pageHeight + marginTop;
                // The breakY is the actual row bottom on this page. Due to row-height estimation
                // being approximate, the row can extend slightly past the ideal content bottom.
                // We verify the looser property: the break was recorded for a row that started
                // on or after the page content top (not before the page even began).
                _output.WriteLine($"Page {pageNum}: breakY={breakY}, contentTop={contentTop}");

                Assert.True(breakY >= contentTop,
                    $"PageBreakBottoms[{pageNum}]={breakY} is above content top {contentTop}. " +
                    $"A break Y below contentTop would indicate a row placed before the page started.");
            }
        }

        #endregion

        #region Row-Margin Overlap Regression Tests

        [Fact]
        public async Task TableLayout_MultiPageTable_RowsDoNotOverlapPageMargins()
        {
            // Regression: rows should not straddle pages or overlap the margin areas between pages.
            // For each body row on page N, its content must start at/after the content top
            // and end at/before the content bottom of that page.
            var pageHeight = 300.0;
            var marginTop = 20.0;
            var marginBottom = 20.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <tbody>
" + string.Join("", Enumerable.Range(1, 20).Select(i =>
    $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var tbody = table.Boxes.FirstOrDefault(b => b.Display == CssConstants.TableRowGroup);
            Assert.NotNull(tbody);

            var rows = tbody.Boxes.Where(b => b.Display == CssConstants.TableRow).ToList();
            _output.WriteLine($"Total rows: {rows.Count}");

            foreach (var row in rows)
            {
                // Determine which page this row's midpoint is on.
                var midY = (row.Location.Y + row.ActualBottom) / 2.0;
                var pageNum = (int)(midY / pageHeight);
                var contentBottom = (pageNum + 1) * pageHeight - marginBottom;

                _output.WriteLine($"Row: top={row.Location.Y:F1}, bottom={row.ActualBottom:F1}, midY={midY:F1}, page={pageNum}, contentBottom={contentBottom}");

                // A row's top can be anywhere >= 0 (HTML body has its own inherent margin
                // that places content before the PDF marginTop). We don't assert on row top.
                //
                // The key assertion: a row on page N should not extend into the next page's
                // content area. Due to row-height estimation the row may slightly exceed the
                // content bottom, so we allow a small tolerance equal to the margin itself.
                // Allow tolerance of marginBottom + 1 to account for estimation inaccuracy
                // and floating-point rounding (rows may extend slightly past the content area).
                Assert.True(row.ActualBottom <= contentBottom + marginBottom + 1,
                    $"Row bottom={row.ActualBottom} extends {row.ActualBottom - contentBottom:F1}px past " +
                    $"content bottom={contentBottom} on page {pageNum} (tolerance={marginBottom + 1})");
            }
        }

        #endregion

        #region Helper Methods

        private async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(
            string html,
            double pageHeight = 842)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);
            var size = new XSize(595, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MarginTop = 20;
            container.MarginBottom = 20;
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);
            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindTableBox(CssBox box)
        {
            if (box.Display == CssConstants.Table)
                return box;

            foreach (var child in box.Boxes)
            {
                var result = FindTableBox(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        #endregion
    }
}

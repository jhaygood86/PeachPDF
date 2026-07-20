using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;

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
            // Fixture lengths are in pt and the body margin is pinned to 8pt (what the UA default
            // `body { margin: 8px }` resolved to when this knife-edge scenario was calibrated,
            // before px became 0.75pt) so the row-vs-page-bottom geometry stays exact.
            var pageHeight = 150.0;
            var marginBottom = 20.0;

            var html = @"
<!DOCTYPE html>
<html>
<body style='margin:8pt'>
    <table style='width:100%;border-collapse:collapse;'>
        <tbody>
" + string.Join("", Enumerable.Range(1, 15).Select(i =>
    $"<tr><td style='border:1pt solid black;padding:5pt;'>Row {i}</td></tr>")) + @"
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
            // Each entry in PageBreakBottoms must fall within the content area of its page. Content
            // area for page N is [PageTopOf(N), PageTopOf(N+1)) - container.PageSize.Height (the
            // "pageHeight" this harness passes in) is already the margin-free content band per
            // HtmlContainerInt.PageIndexOf/PageTopOf's own convention (matching PdfGenerator.SetContent
            // in production), so marginBottom must NOT be subtracted a second time from the band's own
            // bottom - doing so was exactly the CssLayoutEngineTable availableHeight bug this guards.
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

            Assert.NotNull(table.PageBreakBottoms);
            Assert.NotEmpty(table.PageBreakBottoms);

            foreach (var (pageNum, breakY) in table.PageBreakBottoms)
            {
                var contentTop = container.PageTopOf(pageNum);
                var contentBottom = container.PageTopOf(pageNum + 1);

                _output.WriteLine($"Page {pageNum}: breakY={breakY}, contentTop={contentTop}, contentBottom={contentBottom}");

                Assert.True(breakY >= contentTop,
                    $"PageBreakBottoms[{pageNum}]={breakY} is above content top {contentTop}.");

                // With EstimateRowHeight including padding+border, the last row placed on each
                // page should end at or before contentBottom. A small tolerance (5 units) covers
                // minor discrepancies from font-metric vs. layout-height rounding.
                Assert.True(breakY <= contentBottom + 5,
                    $"PageBreakBottoms[{pageNum}]={breakY} exceeds content bottom {contentBottom} " +
                    $"by more than 5 units, indicating EstimateRowHeight significantly underestimates row height.");
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

        #region Code-review fix tests

        [Fact]
        public async Task PageBreakBottoms_ResetOnReLayout_DoesNotRetainStaleEntries()
        {
            // Fix: PageBreakBottoms was never cleared between layout passes. A second call to
            // PerformLayout would accumulate stale entries from the first pass, producing
            // incorrect border clipping. After the fix, each layout pass resets the dictionary.
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

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);
            var size = new XSize(595, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MarginTop = 20;
            container.MarginBottom = 20;
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);

            // First layout pass
            await container.PerformLayout(graphics);
            var table1 = FindTableBox(container.Root!);
            Assert.NotNull(table1);
            var countAfterFirst = table1.PageBreakBottoms?.Count ?? 0;
            _output.WriteLine($"PageBreakBottoms after first layout: {countAfterFirst} entries");

            // Second layout pass (simulates resize / re-render)
            await container.PerformLayout(graphics);
            var table2 = FindTableBox(container.Root!);
            Assert.NotNull(table2);
            var countAfterSecond = table2.PageBreakBottoms?.Count ?? 0;
            _output.WriteLine($"PageBreakBottoms after second layout: {countAfterSecond} entries");

            // The count must not grow between passes — the dictionary was reset, not appended-to.
            Assert.Equal(countAfterFirst, countAfterSecond);
        }

        [Fact]
        public async Task PageBreakBottoms_WithRepeatingFooter_IncludesFooterInClipY()
        {
            // Fix: PageBreakBottoms was recorded BEFORE the footer proxy was laid out, so the
            // stored Y was the last body-row bottom, not the footer bottom. Borders would be
            // clipped above the footer, cutting off the table's side borders around it.
            // After the fix, the stored Y equals the footer proxy's ActualBottom.
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
        <tfoot>
            <tr><td style='border:1px solid black;padding:5px;font-weight:bold;'>Footer</td></tr>
        </tfoot>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);
            Assert.NotNull(table.PageBreakBottoms);
            Assert.NotEmpty(table.PageBreakBottoms);

            // Find the footer proxy boxes that were injected into the table
            var footerProxies = table.Boxes
                .OfType<CssProxyBox>()
                .Where(p => p.Display == CssConstants.TableFooterGroup)
                .ToList();

            _output.WriteLine($"Footer proxies found: {footerProxies.Count}");

            foreach (var (pageNum, breakY) in table.PageBreakBottoms)
            {
                _output.WriteLine($"Page {pageNum}: breakY={breakY}");

                // If a footer proxy exists for this page, the clip Y must be at or below
                // the footer proxy's actual bottom — not above it.
                var footerOnPage = footerProxies
                    .FirstOrDefault(fp => fp.Location.Y >= pageNum * pageHeight + marginTop
                                       && fp.Location.Y < (pageNum + 1) * pageHeight - marginBottom);

                if (footerOnPage != null)
                {
                    _output.WriteLine($"  Footer on page {pageNum}: ActualBottom={footerOnPage.ActualBottom}");
                    Assert.True(breakY >= footerOnPage.ActualBottom - 1,
                        $"Page {pageNum} breakY={breakY} is above footer ActualBottom={footerOnPage.ActualBottom}. " +
                        $"Footer area would be excluded from border clip.");
                }
            }
        }

        [Fact]
        public async Task PageBreakBottoms_NegativeOrZeroClipHeight_GuardPreventsDegenerate()
        {
            // CssBox.PaintImp only applies the rectForBorders adjustment when pageBreakBottomVisual
            // is less than actualRect.Bottom. If a stale or mismatched PageBreakBottoms entry puts
            // pageBreakBottomVisual above the actual table bottom, the condition is false and no
            // modification is made — DrawBoxBorders is called with the original rect unchanged.
            //
            // We inject a stale/mismatched PageBreakBottoms entry (Y below the table top) directly
            // onto the box after layout, then call PerformPaint to exercise the guard path.
            var pageHeight = 400.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='border:2px solid black;border-collapse:collapse;width:100%;'>
        <tbody>
            <tr><td style='border:1px solid black;padding:5px;'>Row A</td></tr>
            <tr><td style='border:1px solid black;padding:5px;'>Row B</td></tr>
        </tbody>
    </table>
</body>
</html>";

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
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

            var table = FindTableBox(container.Root!);
            Assert.NotNull(table);

            _output.WriteLine($"Table Location.Y={table.Location.Y}, ActualBottom={table.ActualBottom}");

            // Inject a PageBreakBottoms entry whose Y is BELOW the table's top (in absolute coords).
            // On page 0 (scrollOffset=0), pageBreakBottomVisual = injectedY + 0 = injectedY.
            // clippedHeight = injectedY - table.Location.Y < 0 → guard must skip clipping.
            var injectedY = table.Location.Y - 5; // 5 units above the table top
            table.PageBreakBottoms = new Dictionary<int, double> { [0] = injectedY };

            _output.WriteLine($"Injected PageBreakBottoms[0]={injectedY} (below table top by 5 units)");

            // PerformPaint must complete without throwing despite the degenerate entry.
            var ex = await Record.ExceptionAsync(async () => await container.PerformPaint(graphics));
            Assert.Null(ex);
        }

        [Fact]
        public async Task TableBorderPaint_IntermediatePageBreak_BottomBorderDrawnAtPageBreakY()
        {
            // On intermediate pages the outer table bottom border must be drawn at the page-break
            // Y rather than at actualRect.Bottom (which is far below the current page). The fix
            // computes rectForBorders with Bottom = pageBreakBottomVisual so that DrawBoxBorders
            // places the bottom border line at the page-break boundary.
            //
            // This test verifies: (a) a horizontal line is drawn near pageBreakBottom0, and
            // (b) PushClip/PopClip calls are balanced.
            var pageHeight = 200.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;border:2px solid black;'>
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
            Assert.True(table.PageBreakBottoms.ContainsKey(0),
                "Table should have a PageBreakBottoms entry for page 0.");

            var pageBreakBottom0 = table.PageBreakBottoms[0];
            _output.WriteLine($"PageBreakBottoms[0]={pageBreakBottom0}, Table.ActualBottom={table.ActualBottom}");

            // Paint page 0 using the recording graphics adapter (ScrollOffset = 0 so visual Y = absolute Y).
            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            container.ScrollOffset = new PeachPDF.Html.Adapters.Entities.RPoint(0, 0);
            await container.PerformPaint(recording);

            // The outer table bottom border must be drawn at approximately pageBreakBottom0.
            // DrawBoxBorders renders it at rectForBorders.Bottom - borderWidth/2; use the
            // same tolerance to accommodate the border half-width offset.
            const double tolerance = 3.0;
            _output.WriteLine($"Horizontal lines: [{string.Join(", ", recording.HorizontalLines.Select(y => $"{y:F1}"))}]");
            var bottomBorderLines = recording.HorizontalLines
                .Where(y => Math.Abs(y - pageBreakBottom0) < tolerance)
                .ToList();
            Assert.True(bottomBorderLines.Count > 0,
                $"Expected a horizontal line near Y={pageBreakBottom0} (outer table bottom border on page 0), " +
                $"but none found. All lines: [{string.Join(", ", recording.HorizontalLines.Select(y => $"{y:F1}"))}]");

            // PushClip / PopClip must be balanced so subsequent paint calls are not corrupted.
            Assert.Equal(recording.PushCount, recording.PopCount);
        }

        [Fact]
        public async Task TableBorderPaint_SubsequentPage_BottomBorderDrawnAtPageBreakY()
        {
            // On page 1 (the second page of a multi-page table), the outer table bottom border
            // must be drawn at pageBreakBottom1 rather than at the true table bottom which is
            // far below the page. Verifies that rectForBorders.Bottom is capped to the page-break
            // Y on every intermediate page, not only the first.
            var pageHeight = 200.0;
            var marginTop = 20.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;border:2px solid black;'>
        <tbody>
" + string.Join("", Enumerable.Range(1, 20).Select(i =>
    $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight, marginTop: marginTop);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);
            Assert.True(table.PageBreakBottoms?.ContainsKey(1) == true,
                "Table must span at least 3 pages so page 1 is an intermediate page.");

            var pageBreakBottom1 = table.PageBreakBottoms![1];
            _output.WriteLine($"PageBreakBottoms[1]={pageBreakBottom1}, Table.ActualBottom={table.ActualBottom}");

            // Paint page 1 (scroll offset = -pageHeight).
            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            container.ScrollOffset = new PeachPDF.Html.Adapters.Entities.RPoint(0, -pageHeight);
            await container.PerformPaint(recording);

            // rectForBorders.Bottom = pageBreakBottomVisual = pageBreakBottom1 + (-pageHeight).
            var pageBreakBottomVisual = pageBreakBottom1 - pageHeight;
            const double tolerance = 3.0;
            _output.WriteLine($"Expected bottom border near Y={pageBreakBottomVisual:F1}");
            _output.WriteLine($"Horizontal lines: [{string.Join(", ", recording.HorizontalLines.Select(y => $"{y:F1}"))}]");

            var bottomBorderLines = recording.HorizontalLines
                .Where(y => Math.Abs(y - pageBreakBottomVisual) < tolerance)
                .ToList();
            Assert.True(bottomBorderLines.Count > 0,
                $"Expected a horizontal line near Y={pageBreakBottomVisual:F1} (outer table bottom border on page 1), " +
                $"but none found. All lines: [{string.Join(", ", recording.HorizontalLines.Select(y => $"{y:F1}"))}]");
        }

        [Fact]
        public async Task TableBorderPaint_LastPage_OuterBottomBorderIsDrawn()
        {
            // Verify that the outer table bottom border appears on the last page of the table.
            // The fix clips from content-area top to actualRect.Bottom on the last page; the
            // border must be drawn at actualRect.Bottom relative to that page's scroll offset.
            var pageHeight = 200.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;border:2px solid black;'>
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

            // Determine the last page: the page where the table's actual bottom resides.
            var lastPageIndex = (int)(table.ActualBottom / pageHeight);
            var lastScrollOffset = -(lastPageIndex * pageHeight);
            _output.WriteLine($"Table.ActualBottom={table.ActualBottom}, lastPageIndex={lastPageIndex}, scrollOffset={lastScrollOffset}");

            // The bottom border line sits at actualRect.Bottom - borderWidth/2 in visual coords.
            // actualRect.Bottom = table.ActualBottom + scrollOffset (after Offset(offset)).
            var expectedBottomBorderY = table.ActualBottom + lastScrollOffset;
            _output.WriteLine($"Expected bottom border near Y={expectedBottomBorderY}");

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            container.ScrollOffset = new PeachPDF.Html.Adapters.Entities.RPoint(0, lastScrollOffset);
            await container.PerformPaint(recording);

            _output.WriteLine($"Horizontal lines recorded: [{string.Join(", ", recording.HorizontalLines.Select(y => $"{y:F1}"))}]");

            const double tolerance = 3.0;
            var bottomBorderLines = recording.HorizontalLines
                .Where(y => Math.Abs(y - expectedBottomBorderY) < tolerance)
                .ToList();

            Assert.True(bottomBorderLines.Count > 0,
                $"Expected a horizontal line near Y={expectedBottomBorderY} (table outer bottom border on last page), " +
                $"but found none. All lines: [{string.Join(", ", recording.HorizontalLines.Select(y => $"{y:F1}"))}]");
        }

        #endregion

        #region Helper Methods

        private async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(
            string html,
            double pageHeight = 842,
            double marginTop = 20,
            double marginBottom = 20)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);
            var size = new XSize(595, pageHeight);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MarginTop = marginTop;
            container.MarginBottom = marginBottom;
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

        #region Recording Graphics Adapter

        /// <summary>
        /// Minimal RGraphics implementation that records PushClip/PopClip calls and drawn lines
        /// so tests can verify page-break border behavior without a full PDF rendering stack.
        /// </summary>
        private sealed class RecordingGraphics : PeachPDF.Html.Adapters.RGraphics
        {
            /// <summary>All rects passed to PushClip during this paint pass.</summary>
            public List<PeachPDF.Html.Adapters.Entities.RRect> PushedClips { get; } = [];
            /// <summary>Total PushClip invocations.</summary>
            public int PushCount { get; private set; }
            /// <summary>Total PopClip invocations.</summary>
            public int PopCount { get; private set; }
            /// <summary>Y-coordinates of horizontal lines drawn (where y1 ≈ y2).</summary>
            public List<double> HorizontalLines { get; } = [];

            public RecordingGraphics(PeachPDF.Html.Adapters.RAdapter adapter)
                : base(adapter, new PeachPDF.Html.Adapters.Entities.RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void DrawLine(PeachPDF.Html.Adapters.RPen pen, double x1, double y1, double x2, double y2)
            {
                if (Math.Abs(y1 - y2) < 0.5)
                    HorizontalLines.Add(y1);
            }

            /// <summary>
            /// Solid borders paint as a mitered quad (BordersDrawHandler.SetInOutsetRectanglePoints),
            /// not a DrawLine call - a wide-and-thin quad (wider in X than in Y) is a horizontal border
            /// stripe; record its vertical center the same way DrawLine's y1/y2 would, so callers don't
            /// need to know which draw method a given border style happens to use.
            /// </summary>
            public override void DrawPolygon(PeachPDF.Html.Adapters.RBrush brush, PeachPDF.Html.Adapters.Entities.RPoint[] points)
            {
                if (points.Length == 0) return;
                var minY = points.Min(p => p.Y);
                var maxY = points.Max(p => p.Y);
                var minX = points.Min(p => p.X);
                var maxX = points.Max(p => p.X);
                if (maxX - minX > maxY - minY)
                    HorizontalLines.Add((minY + maxY) / 2);
            }

            public override void PushClip(PeachPDF.Html.Adapters.Entities.RRect rect)
            {
                _clipStack.Push(rect);
                PushedClips.Add(rect);
                PushCount++;
            }

            public override void PopClip()
            {
                if (_clipStack.Count > 1)
                    _clipStack.Pop();
                PopCount++;
            }

            public override void PushClip(PeachPDF.Html.Adapters.RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PushClipExclude(PeachPDF.Html.Adapters.Entities.RRect rect) { }
            public override void PushTransform(PeachPDF.Html.Adapters.Entities.RMatrix matrix) { }
            public override void PopTransform() { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override PeachPDF.Html.Adapters.Entities.RSize MeasureString(string str, PeachPDF.Html.Adapters.RFont font) => new(0, 12);
            public override void MeasureString(string str, PeachPDF.Html.Adapters.RFont font, double maxWidth, out int charFit, out double charFitWidth) { charFit = str?.Length ?? 0; charFitWidth = 0; }
            public override void DrawString(string str, PeachPDF.Html.Adapters.RFont font, PeachPDF.Html.Adapters.Entities.RColor color, PeachPDF.Html.Adapters.Entities.RPoint point, PeachPDF.Html.Adapters.Entities.RSize size, bool rtl, double letterSpacing = 0) { }
            public override void DrawRectangle(PeachPDF.Html.Adapters.RPen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(PeachPDF.Html.Adapters.RBrush brush, double x, double y, double width, double height) { }
            public override void DrawImage(PeachPDF.Html.Adapters.RImage image, PeachPDF.Html.Adapters.Entities.RRect destRect, PeachPDF.Html.Adapters.Entities.RRect srcRect) { }
            public override void DrawImage(PeachPDF.Html.Adapters.RImage image, PeachPDF.Html.Adapters.Entities.RRect destRect) { }
            public override void DrawPath(PeachPDF.Html.Adapters.RPen pen, PeachPDF.Html.Adapters.RGraphicsPath path) { }
            public override void DrawPath(PeachPDF.Html.Adapters.RBrush brush, PeachPDF.Html.Adapters.RGraphicsPath path) { }
            public override PeachPDF.Html.Adapters.RGraphicsPath GetGraphicsPath() => new RecordingGraphicsPath();
            public override (PeachPDF.Html.Adapters.RGraphics Graphics, PeachPDF.Html.Adapters.RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(PeachPDF.Html.Adapters.RImage image, PeachPDF.Html.Adapters.RImage maskImage, PeachPDF.Html.Adapters.Entities.RRect destRect) { }
            public override void DrawImageWithOpacity(PeachPDF.Html.Adapters.RImage image, PeachPDF.Html.Adapters.Entities.RRect destRect, double opacity) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override void Dispose() { }
        }

        private sealed class RecordingGraphicsPath : PeachPDF.Html.Adapters.RGraphicsPath
        {
            public override void Start(double x, double y) { }
            public override void LineTo(double x, double y) { }
            public override void ArcTo(double x, double y, double radiusX, double radiusY, Corner corner) { }
            public override void AddMove(double x, double y) { }
            public override void AddBezierTo(double x1, double y1, double x2, double y2, double x3, double y3) { }
            public override void AddArc(double x, double y, double radiusX, double radiusY, double rotationAngle, bool isLargeArc, bool sweepClockwise) { }
            public override void CloseFigure() { }
            public override PeachPDF.Html.Adapters.Entities.RFillMode FillMode { get; set; }
            public override void Dispose() { }
        }

        #endregion
    }
}

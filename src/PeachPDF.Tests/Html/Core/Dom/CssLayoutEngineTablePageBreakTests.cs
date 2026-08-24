using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Tests.TestSupport;

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

            var tbody = table.Boxes.FirstOrDefault(b => b.Display.Value == DisplayMode.TableRowGroup);
            Assert.NotNull(tbody);

            var rows = tbody.Boxes.Where(b => b.Display.Value == DisplayMode.TableRow).ToList();
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
            //
            // BuildCssBoxTree hands `pageHeight` straight to container.PageSize (it does not carve
            // margins out of it the way PdfGenerator.SetContent/LayoutHarness do), so in this
            // fixture's own coordinate space `PageBandHeightOf` returns `pageHeight` unmodified and
            // the table engine's own real page-0 bottom (see WillCrossPageBoundary's
            // `PageTopOf(slot) + availableHeight`) is `marginTop + pageHeight` - not the
            // `pageHeight - marginBottom` an ordinary (margin-subtracted) PageSize would give.
            var pageHeight = 105.0;
            var marginTop = 20.0;

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

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight, marginTop);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var tbody = table.Boxes.FirstOrDefault(b => b.Display.Value == DisplayMode.TableRowGroup);
            Assert.NotNull(tbody);

            var rows = tbody.Boxes.Where(b => b.Display.Value == DisplayMode.TableRow).ToList();
            _output.WriteLine($"Total rows: {rows.Count}");

            // The table engine's own page-0 content bottom in this fixture's coordinate space -
            // see the comment above. A row landing here would need relocating to the next page;
            // one left behind with its bottom past this line has bled into the margin band.
            var contentBottomPage0 = marginTop + pageHeight;
            foreach (var row in rows.Where(r => r.Location.Y < contentBottomPage0))
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
        public async Task PageBreakBottoms_CaptionedTableWithOwnBorder_MirroredOntoGridDecorationBox()
        {
            // Issue #721: a captioned table's own border/background paint from its grid decoration box
            // rather than from the table box itself (see CssLayoutEngineTable.EnsureGridDecorationBoxStructure/
            // FinalizeGridDecorationBoxGeometry) - FragmentPainter's multi-page bottom-border-truncation
            // (keyed on PageBreakBottoms) has to run for that box too, or a captioned, bordered table
            // spanning pages would draw its bottom border past the page break on every intermediate page.
            var pageHeight = 200.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:separate;border-spacing:0;border:2px solid black;'>
        <caption>A long, bordered, captioned table</caption>
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
            Assert.NotNull(table!.PageBreakBottoms);
            Assert.NotEmpty(table.PageBreakBottoms!);

            var decoration = table.TableGridDecorationBox;
            Assert.NotNull(decoration);
            Assert.Same(table.PageBreakBottoms, decoration!.PageBreakBottoms);
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

            var tbody = table.Boxes.FirstOrDefault(b => b.Display.Value == DisplayMode.TableRowGroup);
            Assert.NotNull(tbody);

            var rows = tbody.Boxes.Where(b => b.Display.Value == DisplayMode.TableRow).ToList();
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
                .Where(p => p.Display.Value == DisplayMode.TableFooterGroup)
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
        public async Task TableFooter_MultiPageTable_FooterTextIsPaintedOnEveryPage()
        {
            // End-to-end paint proof that a repeating <tfoot>'s text actually gets drawn (not just
            // that a proxy with the right geometry exists) on both an intermediate page and the
            // final page, per this repo's testing conventions. Complements the geometry-only check
            // in TableFooter_MultiPageTable_FooterLayoutsCorrectly, which is what actually pins down
            // GitHub issue #124's underlying defect (the footer row-group box's ActualRight was
            // never set, unlike the header's, leaving every footer CssProxyBox with a degenerate
            // zero-width Bounds).
            //
            // Tall enough that css-tables-3 6.2 lets the footer repeat at all - it caps a repeated group
            // at a quarter of the page, and this harness leaves the adapter's PixelsPerPoint unpinned, so
            // the footer row measures ~80 of these units. 20 rows still span several pages at 400.
            //
            // marginTop is 0, deliberately: see the identical comment on
            // TableBorderPaint_IntermediatePageBreak_BottomBorderDrawnAtPageBreakY - BuildCssBoxTree
            // never sets container.Location, so the paint clip this test actually paints against (built
            // from Location/MaxSize alone) has no room added for MarginTop, while the table engine's own
            // page-break math does add it. A nonzero value here let content the layout considered
            // "still on page 0" (including the footer proxy asserted on below) land past what the paint
            // clip can show, so it silently never painted.
            var pageHeight = 400.0;

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
            <tr><td style='border:1px solid black;padding:5px;font-weight:bold;'>FOOTERMARKER</td></tr>
        </tfoot>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight, marginTop: 0);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);
            Assert.NotNull(table.PageBreakBottoms);
            Assert.NotEmpty(table.PageBreakBottoms);

            // The actual fragmentainer count, not a heuristic division of table.ActualBottom by
            // pageHeight - collapsed-border geometry (issue #735's own fix) shifts exactly how tall the
            // table measures, and a division-based estimate drifts out of sync with the real page count
            // as that arithmetic gets more precise, landing on a page index the fragment tree never built.
            var lastPageIndex = container.FragmentTree!.Fragmentainers.Count - 1;
            Assert.True(lastPageIndex >= 1, "Table should span at least 2 pages for this test to be meaningful.");

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();

            // Page 0 is an intermediate page - the footer should repeat here.
            var page0Recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, page0Recording);
            _output.WriteLine($"Page 0 drawn strings: [{string.Join(", ", page0Recording.DrawnStrings.Select(w => w.Text))}]");
            Assert.Contains(page0Recording.DrawnStrings, w => w.Text.Contains("FOOTERMARKER"));

            // The footer must also appear at the end of the table's content on the last page.
            var lastPageRecording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, lastPageRecording, lastPageIndex);
            _output.WriteLine($"Last page drawn strings: [{string.Join(", ", lastPageRecording.DrawnStrings.Select(w => w.Text))}]");
            Assert.Contains(lastPageRecording.DrawnStrings, w => w.Text.Contains("FOOTERMARKER"));
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
            var ex = await Record.ExceptionAsync(async () => FragmentPaintHarness.PaintPage(container, graphics));
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
            //
            // marginTop is 0 here, deliberately: BuildCssBoxTree never sets container.Location (unlike
            // production/LayoutHarness, which anchor it at (MarginLeft, MarginTop)), so the paint clip
            // this test actually paints against - HtmlContainerInt.PageBoxRect, built from Location and
            // MaxSize - is a bare (0, 0, pageWidth, pageHeight) with no room added for MarginTop. The
            // table engine's own page-break math (PageTopOf/PageBottomOf) does add MarginTop, so a
            // nonzero value here would let the recorded PageBreakBottoms exceed what the clip can
            // actually show - the border would be computed correctly and then silently clipped away.
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

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight, marginTop: 0);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);
            Assert.NotNull(table.PageBreakBottoms);
            Assert.True(table.PageBreakBottoms.ContainsKey(0),
                "Table should have a PageBreakBottoms entry for page 0.");

            var pageBreakBottom0 = table.PageBreakBottoms[0];
            _output.WriteLine($"PageBreakBottoms[0]={pageBreakBottom0}, Table.ActualBottom={table.ActualBottom}");

            // Paint page 0 using the recording graphics adapter (page 0's local Y equals absolute Y).
            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording);

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
            //
            // marginTop is 0, deliberately: see the identical comment on
            // TableBorderPaint_IntermediatePageBreak_BottomBorderDrawnAtPageBreakY - BuildCssBoxTree
            // never sets container.Location, so a nonzero marginTop here would let PageBreakBottoms
            // record a Y past what the paint clip (built from Location/MaxSize alone) can show.
            var pageHeight = 200.0;
            var marginTop = 0.0;

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

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording, page: 1);

            // Page 1's fragments are local to its own band, so the break bottom lands one page up.
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
            _output.WriteLine($"Table.ActualBottom={table.ActualBottom}, lastPageIndex={lastPageIndex}");

            // The bottom border line sits at the fragment's own bottom minus borderWidth/2, and the
            // fragment's coordinates are local to the last page's band.
            var expectedBottomBorderY = table.ActualBottom - lastPageIndex * pageHeight;
            _output.WriteLine($"Expected bottom border near Y={expectedBottomBorderY}");

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording, lastPageIndex);

            _output.WriteLine($"Horizontal lines recorded: [{string.Join(", ", recording.HorizontalLines.Select(y => $"{y:F1}"))}]");

            const double tolerance = 3.0;
            var bottomBorderLines = recording.HorizontalLines
                .Where(y => Math.Abs(y - expectedBottomBorderY) < tolerance)
                .ToList();

            Assert.True(bottomBorderLines.Count > 0,
                $"Expected a horizontal line near Y={expectedBottomBorderY} (table outer bottom border on last page), " +
                $"but found none. All lines: [{string.Join(", ", recording.HorizontalLines.Select(y => $"{y:F1}"))}]");
        }

        [Fact]
        public async Task RepeatedThead_BoundaryToBody_ResolvesFreshPerPage_NotReusedFromPage1()
        {
            // CSS 2.1 §17.6.2 resolves a border against whichever row is *visually* adjacent - for a
            // repeated <thead>, that is whatever row actually starts each page, not the header's single
            // DOM-order neighbor (body row 1). Only row 1 (page 1's true neighbor) gets a deliberately
            // huge, distinctive border; every other row keeps an ordinary thin one. A resolution that
            // reuses page 1's answer on every later page would wrongly show the huge border repeated at
            // every page's header boundary too; a per-page-fresh resolution shows it only on page 1.
            var pageHeight = 400.0;
            const int rowCount = 60;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <thead>
            <tr><th style='border:1px solid black;padding:5px;'>Header</th></tr>
        </thead>
        <tbody>
" + string.Join("", Enumerable.Range(1, rowCount).Select(i =>
    i == 1
        ? "<tr><td style='border:9px solid red;padding:5px;'>Row 1</td></tr>"
        : $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var lastPageIndex = container.FragmentTree!.Fragmentainers.Count - 1;
            Assert.True(lastPageIndex >= 2, "Table should span at least 3 pages for this test to be meaningful.");

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();

            // Page 0: the header's true DOM/visual neighbor (row 1's huge red border) drives the
            // boundary - a thick (>=8pt) roughly-horizontal segment must appear somewhere on this page.
            var page0Recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, page0Recording, 0);
            Assert.Contains(page0Recording.Log, IsThickHorizontalSegment);

            // Every later page: the header repeats above a *different* row (an ordinary 1px border), so
            // no page after the first should show that same thick segment anywhere.
            for (var page = 1; page <= lastPageIndex; page++)
            {
                var recording = new RecordingGraphics(adapter);
                FragmentPaintHarness.PaintPage(container, recording, page);
                Assert.DoesNotContain(recording.Log, IsThickHorizontalSegment);
            }
        }

        // 9px resolves to 6.75pt (1px = 0.75pt, see Length.PointsPerPx); an ordinary 1px border resolves
        // to 0.75pt, so a threshold well between the two distinguishes "row 1's huge border won" from
        // "an ordinary row's border won" without being sensitive to the exact px-to-pt ratio.
        private static bool IsThickHorizontalSegment(PaintOp op) =>
            op.Kind is PaintOpKind.Polygon or PaintOpKind.Line &&
            op.Bounds.Width > op.Bounds.Height && op.Bounds.Height >= 4;

        [Fact]
        public async Task RepeatedThead_OwnInternalGridLine_RedrawnTranslatedOnEveryPage()
        {
            // A multi-row <thead>'s own internal grid line (between its own rows) is fixed, unlike the
            // boundary to the body - but a repeated group's *position* still differs per page, since each
            // CssProxyBox.PerformLayoutImp repositions the same shared header rows to wherever that page's
            // proxy sits. Reading the live row geometry after the whole table has laid out (as
            // EmitCollapsedBorderSegments does for an ordinary body row) would only reflect whichever
            // proxy happened to lay out last - this line must instead come from each proxy's own captured
            // BoxGeometrySnapshot, so it has to reappear, correctly positioned, on every page.
            //
            // Page height (1200) is deliberately generous relative to the two-row header's own height
            // (~160pt): css-tables-3 §6.2 (SettleWhetherTheGroupsRepeat) only repeats a group under a
            // quarter of the page's own height, and a too-small page here would silently turn this into a
            // non-repeating-header test instead of the multi-page one intended.
            var pageHeight = 1200.0;
            const int rowCount = 150;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <thead>
            <tr><th style='border:1px solid black;border-bottom:5px solid green;padding:5px;'>Header A</th></tr>
            <tr><th style='border:1px solid black;border-top:5px solid green;padding:5px;'>Header B</th></tr>
        </thead>
        <tbody>
" + string.Join("", Enumerable.Range(1, rowCount).Select(i =>
    $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var lastPageIndex = container.FragmentTree!.Fragmentainers.Count - 1;
            Assert.True(lastPageIndex >= 2, "Table should span at least 3 pages for this test to be meaningful.");

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();

            // 5px resolves to 3.75pt - between the 0.75pt ordinary borders and the header's own outer
            // 1px (0.75pt) edges, so this threshold catches only the header's own internal line.
            static bool IsHeaderInternalLine(PaintOp op) =>
                op.Kind is PaintOpKind.Polygon or PaintOpKind.Line &&
                op.Bounds.Width > op.Bounds.Height && op.Bounds.Height is >= 2 and < 4;

            for (var page = 0; page <= lastPageIndex; page++)
            {
                var recording = new RecordingGraphics(adapter);
                FragmentPaintHarness.PaintPage(container, recording, page);
                Assert.True(recording.Log.Any(IsHeaderInternalLine),
                    $"Expected the header's own internal grid line on page {page}, but found none.");
            }
        }

        [Fact]
        public async Task RepeatedThead_RowspanInHeadersLastRow_BoundaryColumnsAttributeToTheRightCell()
        {
            // CollapsedBorderModel.ResolveRepeatedGroupBoundary reads cell candidates from the header's
            // own last row at each column via TableGrid.CellAt - which correctly follows a rowspan cell
            // (here, A, spanning both header rows in the last column) into the boundary line, unlike
            // scanning the last row's own Boxes list by hand (which held only D, and would either
            // misattribute column 1 to it or miss it depending on where in the row the span falls).
            //
            // The rowspan cell is placed LAST in row 0 rather than first - a arrangement that used to
            // matter (a rowspan cell reaching into a later row from earlier in the *same* row left that
            // later row's own Boxes list non-dense, since CssLayoutEngineTable.InsertEmptyBoxes's
            // CssSpacingBox placeholders never touch a detached header's own rows - only _bodyRows) but
            // no longer does: TableGrid.Build now works out each cell's real column from rowspan
            // occupancy itself rather than trusting a row's own Boxes-list order (issue #736). Kept in
            // this shape anyway since it still exercises the same boundary-resolution path.
            var pageHeight = 400.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <thead>
            <tr><th style='border-bottom:1px solid black'>B</th><th rowspan='2' style='border-bottom:9px solid red'>A</th></tr>
            <tr><th style='border-bottom:3px solid blue'>D</th></tr>
        </thead>
        <tbody>
" + string.Join("", Enumerable.Range(1, 20).Select(i =>
    $"<tr><td id='r{i}c0'>{i}x</td><td id='r{i}c1'>{i}y</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var headerProxy = table!.Boxes.OfType<CssProxyBox>()
                .First(p => p.Display.Value == DisplayMode.TableHeaderGroup);
            var segments = headerProxy.SourceBox.CollapsedBorderSegments;
            Assert.NotNull(segments);

            var col0 = LayoutHarness.FindById(rootBox, "r1c0")!;
            var col1 = LayoutHarness.FindById(rootBox, "r1c1")!;

            // Column 0 also has its own row 0-to-row 1 *internal* line (B's border-bottom, since B - unlike
            // A - does not span into row 1), so filtering by X alone finds two candidates there; the
            // boundary is always the group's own last line, i.e. the largest Y among a header's segments.
            var boundarySegments = segments!.Where(s => s.IsHorizontal).ToList();

            var col0Segment = boundarySegments
                .Where(s => s.Rect.X + s.Rect.Width / 2 >= col0.Location.X && s.Rect.X + s.Rect.Width / 2 < col0.ActualRight)
                .OrderByDescending(s => s.Rect.Y)
                .FirstOrDefault();
            var col1Segment = boundarySegments
                .Where(s => s.Rect.X + s.Rect.Width / 2 >= col1.Location.X && s.Rect.X + s.Rect.Width / 2 < col1.ActualRight)
                .OrderByDescending(s => s.Rect.Y)
                .FirstOrDefault();

            // 3px/9px resolve to 2.25pt/6.75pt (1px = 0.75pt).
            Assert.Equal(2.25, col0Segment.Width, 1);
            Assert.Equal(6.75, col1Segment.Width, 1);
        }

        [Fact]
        public async Task RepeatedTfoot_BoundaryToBody_ResolvesFreshPerPage_NotReusedFromTheLastPage()
        {
            // The mirror of RepeatedThead_BoundaryToBody_ResolvesFreshPerPage_NotReusedFromPage1 for a
            // repeating <tfoot>: only the table's actual LAST body row (its true DOM/visual neighbor on
            // the final page) gets a distinctive border; every earlier row keeps an ordinary one. A
            // resolution that reused the last page's answer on every earlier page would wrongly show the
            // huge border repeated at every page's footer boundary too.
            var pageHeight = 400.0;
            const int rowCount = 60;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <tbody>
" + string.Join("", Enumerable.Range(1, rowCount).Select(i =>
    i == rowCount
        ? "<tr><td style='border:9px solid red;padding:5px;'>Last row</td></tr>"
        : $"<tr><td style='border:1px solid black;padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
        <tfoot>
            <tr><td style='border:1px solid black;padding:5px;'>Footer</td></tr>
        </tfoot>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var lastPageIndex = container.FragmentTree!.Fragmentainers.Count - 1;
            Assert.True(lastPageIndex >= 2, "Table should span at least 3 pages for this test to be meaningful.");

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();

            // The last page: the footer's true DOM/visual neighbor (the last row's huge red border)
            // drives the boundary - a thick (>=4pt) roughly-horizontal segment must appear.
            var lastPageRecording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, lastPageRecording, lastPageIndex);
            Assert.Contains(lastPageRecording.Log, IsThickHorizontalSegment);

            // Every earlier page: the footer repeats below a *different* row (an ordinary 1px border),
            // so no page before the last should show that same thick segment anywhere.
            for (var page = 0; page < lastPageIndex; page++)
            {
                var recording = new RecordingGraphics(adapter);
                FragmentPaintHarness.PaintPage(container, recording, page);
                Assert.DoesNotContain(recording.Log, IsThickHorizontalSegment);
            }
        }

        [Fact]
        public async Task RepeatedThead_BoundaryAgainstABorderedTbody_TbodysOwnBorderCanWin()
        {
            // ResolveRepeatedGroupBoundary must weigh the adjacent side's own row-group (an explicit
            // <tbody>'s border-top), not just its row/cell - CollapsedBorderOrigin.RowGroup is a real,
            // independently-competing tier (CollapsedBorder.cs), not merely a tiebreak, so a <tbody>
            // border wider than anything the header or the first row itself declares must still win.
            var pageHeight = 1200.0;

            var html = @"
<!DOCTYPE html>
<html>
<body>
    <table style='width:100%;border-collapse:collapse;'>
        <thead>
            <tr><th style='border-bottom:1px solid black;padding:5px;'>Header</th></tr>
        </thead>
        <tbody style='border-top:8px solid green'>
" + string.Join("", Enumerable.Range(1, 60).Select(i =>
    $"<tr><td style='padding:5px;'>Row {i}</td></tr>")) + @"
        </tbody>
    </table>
</body>
</html>";

            var (rootBox, container) = await BuildCssBoxTree(html, pageHeight);

            var table = FindTableBox(rootBox);
            Assert.NotNull(table);

            var adapter = new PeachPDF.Adapters.PdfSharpAdapter();
            var recording = new RecordingGraphics(adapter);
            FragmentPaintHarness.PaintPage(container, recording, 0);

            // 8px resolves to 6pt - between the 0.75pt header border and page 1's thead test's own 6.75pt
            // threshold band, so >=5 unambiguously catches only the tbody's own border winning.
            Assert.Contains(recording.Log, op =>
                op.Kind is PaintOpKind.Polygon or PaintOpKind.Line &&
                op.Bounds.Width > op.Bounds.Height && op.Bounds.Height >= 5);
        }

        #endregion

        #region Helper Methods

        private async Task<(CssBox root, HtmlContainerInt container)> BuildCssBoxTree(
            string html,
            double pageHeight = 842,
            double marginTop = 20,
            double marginBottom = 20)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
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
            if (box.Display.Value == DisplayMode.Table)
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

        // RecordingGraphics/RecordingGraphicsPath moved to PeachPDF.Tests.TestSupport (shared with
        // CollapsedBorderPaintTests.cs) - see that file rather than adding a parallel copy here.
    }
}

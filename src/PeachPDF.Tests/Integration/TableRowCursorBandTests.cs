using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// What the table engine's row cursor does with the band it is filling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TableRowCursor.SlotIndex</c> used to be a counter — one increment per break the row loop took —
    /// so it named the band the loop last <i>opened</i> rather than the band <c>CurrentY</c> had reached.
    /// A row taller than a band carried the cursor several bands past it, the offset that moved the next
    /// row "to the next page" came out negative, and the rows after it were placed <i>inside</i> it
    /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/432">issue #432</see>).
    /// </para>
    /// <para>
    /// Both halves of that had to go together, and this class holds both. The band is now derived from
    /// where the cursor is, which alone stopped the loop breaking at all — measured on this tree as a
    /// repeating <c>&lt;thead&gt;</c> on one page instead of five and two header proxies instead of
    /// three. What makes deriving it safe is that the loop no longer decides only from
    /// <c>EstimateRowHeight</c>: it places the row and asks the question again of the bottom the row
    /// really reached. <see cref="AnOrdinaryTableBreaksBetweenTheBandsItsRowsActuallyFill"/> and
    /// <see cref="AManyRowedTableRecordsOneBreakPerBandItFills"/> are what say the second half is still
    /// there.
    /// </para>
    /// </remarks>
    public class TableRowCursorBandTests
    {
        private const double PageHeight = 300;
        private const double Margin = 20;

        private static string TallFirstRowTable(double tallPt) => LayoutHarness.Wrap($@"
            <table style='width:100%'>
              <tr><td><div style='height:{tallPt}pt'>tall</div></td></tr>
              <tr><td>second row</td></tr>
              <tr><td>third row</td></tr>
            </table>");

        private static List<CssBox> RowsOf(CssBox root) =>
            LayoutHarness.Descendants(root).Where(b => b.Display == CssConstants.TableRow).ToList();

        private static CssBox TableOf(CssBox root) =>
            LayoutHarness.Descendants(root).First(b => b.Display == CssConstants.Table);

        /// <summary>
        /// A row taller than a page band is left where it is — moving it could only recreate the straddle
        /// — and the rows after it continue immediately below it rather than being placed back inside it.
        /// </summary>
        /// <remarks>
        /// The expected positions are the band the tall row's bottom lands in, plus the table's own
        /// vertical spacing: bands are 260pt here, so a 700 / 1000 / 1400pt first row ends in band
        /// 2 / 3 / 5 and the row after it starts 1.5pt below it. Every one of these was
        /// <c>PageTopOf(1) == 280</c> before, whatever the height.
        /// </remarks>
        [Theory]
        [InlineData(700, 723)]
        [InlineData(1000, 1023)]
        [InlineData(1400, 1423)]
        public async Task RowsAfterARowTallerThanABand_ContinueBelowIt(double tall, double expectedSecondRowTop)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                TallFirstRowTable(tall), pageHeight: PageHeight, margin: Margin);

            var rows = RowsOf(root);
            Assert.Equal(3, rows.Count);

            Assert.Equal(expectedSecondRowTop, rows[1].Location.Y, 0.01);

            // The general statement, which is what §4.3 asks for: a break target that lies above the
            // content it is meant to follow is not a break.
            for (var i = 1; i < rows.Count; i++)
            {
                Assert.True(rows[i].Location.Y >= rows[i - 1].ActualBottom - 1.01,
                    $"tall={tall}: row {i} starts at {rows[i].Location.Y:F1}, above row {i - 1}'s bottom "
                    + $"of {rows[i - 1].ActualBottom:F1}");
            }
        }

        /// <summary>
        /// The tall row itself is not moved, and no break is recorded around it: it did not fragment, it
        /// overflowed, and the §4.3 whole-table mover declines a table no fragmentainer could hold.
        /// </summary>
        [Fact]
        public async Task ARowTallerThanABand_IsNotMovedAndRecordsNoBreak()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                TallFirstRowTable(1400), pageHeight: PageHeight, margin: Margin);

            var rows = RowsOf(root);
            var table = TableOf(root);

            Assert.Equal(container.PageTopOf(0), table.Location.Y, 1.01);
            Assert.True(rows[0].ActualBottom - rows[0].Location.Y > container.PageBandHeightOf(0),
                "the fixture's first row has to be taller than a band, or it asserts nothing");
            Assert.Null(table.PageBreakBottoms);
        }

        // Where the cursor never leaves the band the counter named — every ordinary table — the loop is
        // right, and this is the behaviour the correction above must not disturb.
        [Fact]
        public async Task AnOrdinaryTableBreaksBetweenTheBandsItsRowsActuallyFill()
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                TallFirstRowTable(240), pageHeight: PageHeight, margin: Margin);

            var rows = RowsOf(root);
            var table = TableOf(root);

            Assert.True(rows[1].ActualBottom <= container.PageBottomOf(0),
                "the second row still fits the first band");
            Assert.Equal(container.PageTopOf(1), rows[2].Location.Y, 0.01);
            Assert.True(rows[2].Location.Y >= rows[1].ActualBottom,
                "and the third row is below the second, not inside it");
            Assert.Equal([0], table.PageBreakBottoms!.Keys.ToList());
        }

        // A many-rowed table breaks once per band it fills, and the header repeats on each — the
        // behaviour a cursor that re-derived its band from CurrentY silently lost, because every row then
        // found itself comfortably inside a fresh band and none of them ever asked for a break.
        [Fact]
        public async Task AManyRowedTableRecordsOneBreakPerBandItFills()
        {
            var rows = string.Concat(Enumerable.Range(1, 40).Select(i =>
                $"<tr><td><div style='height:30pt'>row {i}</div></td></tr>"));

            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<table style='width:100%;border-collapse:collapse'>{rows}</table>"),
                pageHeight: 842, margin: 0);

            var table = TableOf(root);

            Assert.NotNull(table.PageBreakBottoms);
            Assert.True(table.PageBreakBottoms!.Count >= 1,
                "a 1400pt table on 842pt pages has to break inside itself somewhere");
        }

        /// <summary>
        /// The correction the estimate cannot make. <c>EstimateRowHeight</c> is one line of text per cell
        /// and blind to a block inside it, so a row whose height comes from a block was placed straddling
        /// a band boundary and only noticed a row later. The loop now asks again once the row is placed,
        /// so no row crosses one.
        /// </summary>
        [Fact]
        public async Task NoRowStraddlesABandBoundaryWhenTheEstimateUndershootsIt()
        {
            var rows = string.Concat(Enumerable.Range(1, 12).Select(i =>
                $"<tr><td><div style='height:40pt'>row {i}</div></td></tr>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<table style='width:100%;border-collapse:collapse'>{rows}</table>"),
                pageHeight: PageHeight, margin: Margin);

            var placed = RowsOf(root);
            Assert.Equal(12, placed.Count);

            foreach (var row in placed)
            {
                var slot = container.SlotStartingAt(row.Location.Y);

                Assert.False(
                    HtmlContainerInt.FallsPast(row.ActualBottom, container.BandOfSlot(slot)),
                    $"a row at {row.Location.Y:F1}..{row.ActualBottom:F1} crosses out of band {slot} "
                    + $"({container.PageTopOf(slot):F1}..{container.PageBottomOf(slot):F1})");
            }
        }

        /// <summary>
        /// Every recorded slice bottom is keyed to the band it actually falls in. <c>FragmentPainter</c>
        /// reads the record by the fragment's own fragmentainer index, so a key naming any other band
        /// pulls the table's bottom border up on a page whose slice did not end there.
        /// </summary>
        [Fact]
        public async Task EverySliceBottomIsKeyedToTheBandItFallsIn()
        {
            var rows = string.Concat(Enumerable.Range(1, 12).Select(i =>
                $"<tr><td><div style='height:40pt'>row {i}</div></td></tr>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<table style='width:100%;border-collapse:collapse'>{rows}</table>"),
                pageHeight: PageHeight, margin: Margin);

            var table = TableOf(root);

            Assert.NotNull(table.PageBreakBottoms);
            Assert.NotEmpty(table.PageBreakBottoms!);

            foreach (var (slot, bottom) in table.PageBreakBottoms!)
            {
                Assert.Equal(slot, container.SlotEndingAt(bottom));
            }
        }

        /// <summary>
        /// A repeated <c>&lt;thead&gt;</c> is drawn at the top of the band a break opens, so a break that
        /// moved the row backwards drew the header over the tail of the row before it. Asserted on the
        /// proxies' own geometry, because the repository's standing per-word check is satisfied by one box
        /// drawn underneath another.
        /// </summary>
        [Fact]
        public async Task ARepeatedHeaderIsNeverDrawnOverTheRowBeforeIt()
        {
            var rows = string.Concat(Enumerable.Range(1, 30).Select(i =>
                $"<tr><td style='height:14pt'>Row {i}</td></tr>"));

            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:100%;border-collapse:collapse'>"
                    + "<thead><tr><th>HEADERMARKER</th></tr></thead>"
                    + $"<tbody>{rows}</tbody></table>"),
                pageHeight: 200, margin: 10);

            var bodyRows = RowsOf(root);
            var proxies = LayoutHarness.Descendants(root).OfType<CssProxyBox>().ToList();

            Assert.True(proxies.Count >= 2,
                $"the fixture has to repeat its header, or this asserts nothing — got {proxies.Count}");

            foreach (var proxy in proxies)
            {
                // Every row placed before this header, by where it *starts* — a row selected by its bottom
                // would exclude the very row a header drawn over it overlaps, which is the case this is
                // about.
                var above = bodyRows.Where(r => r.Location.Y < proxy.Location.Y - 1.01).ToList();
                if (above.Count == 0) continue;

                var lastBottom = above.Max(r => r.ActualBottom);

                Assert.True(proxy.Location.Y >= lastBottom - 1.01,
                    $"a repeated header at {proxy.Location.Y:F1} is above the row bottom {lastBottom:F1} "
                    + "it follows");
            }
        }

        /// <summary>
        /// A row corrected onto the next band may have opened a <c>rowspan</c> that ends several rows
        /// later, and the entry the abandoned placement made for it has to go with it — otherwise the row
        /// that ends the span aligns against the same cell twice.
        /// </summary>
        [Fact]
        public async Task ARowThatOpensARowspanCanStillBeCorrectedOntoTheNextBand()
        {
            // The spanning cell is opened by the row that straddles — the seventh, which at 40pt a row
            // starts inside the estimate's blind spot and ends past the band — so the correction has an
            // entry keyed to the row two below it to take back. A row that *ends* a span is moved too,
            // and what that costs the spanning cell is TableRowspanContinuationTests' subject.
            var rows = string.Concat(Enumerable.Range(1, 8).Select(i =>
                i == 7
                    ? "<tr><td><div style='height:40pt'>row 7</div></td>"
                      + "<td rowspan='2'><div style='height:40pt'>spans two</div></td></tr>"
                    : i == 8
                        ? "<tr><td><div style='height:40pt'>row 8</div></td></tr>"
                        : $"<tr><td colspan='2'><div style='height:40pt'>row {i}</div></td></tr>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap($"<table style='width:100%;border-collapse:collapse'>{rows}</table>"),
                pageHeight: PageHeight, margin: Margin);

            var placed = RowsOf(root);
            Assert.Equal(8, placed.Count);

            foreach (var row in placed)
            {
                var slot = container.SlotStartingAt(row.Location.Y);

                Assert.False(
                    HtmlContainerInt.FallsPast(row.ActualBottom, container.BandOfSlot(slot)),
                    $"a row at {row.Location.Y:F1}..{row.ActualBottom:F1} crosses out of band {slot}");
            }

            for (var i = 1; i < placed.Count; i++)
            {
                Assert.True(placed[i].Location.Y >= placed[i - 1].ActualBottom - 1.01,
                    $"row {i} starts at {placed[i].Location.Y:F1}, above row {i - 1}'s bottom of "
                    + $"{placed[i - 1].ActualBottom:F1}");
            }
        }

        // No page grid, so there is one band and the cursor stays in it whatever the rows do.
        [Fact]
        public async Task WithARollTallEnoughToHoldItAllNoBreakIsRecorded()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                TallFirstRowTable(400), pageHeight: 4000, margin: Margin);

            var rows = RowsOf(root);
            var table = TableOf(root);

            Assert.Null(table.PageBreakBottoms);
            Assert.True(rows[1].Location.Y >= rows[0].ActualBottom - 1.01);
            Assert.True(rows[2].Location.Y >= rows[1].ActualBottom - 1.01);
        }

        /// <summary>
        /// Content after a table whose row loop band-jumped (a row taller than a band, or a many-rowed
        /// table breaking between several bands) asks its own fragmentation questions of the band the
        /// table's row loop actually reached, not the band the document-level pass cursor was still
        /// naming when the table started. <see cref="HtmlContainerInt.CursorSpills"/> would count every
        /// such divergence — see #435, and <c>TableRowCursor.MoveToSlot</c>/<c>BandReached</c>'s own
        /// callers, which step <c>FragmentainerContext.SlotIndex</c> to match precisely so this stays
        /// zero.
        /// </summary>
        [Theory]
        [InlineData(700)]
        [InlineData(1400)]
        public async Task ContentAfterABandJumpingTable_SeesATruthfulCursor(double tall)
        {
            var html = LayoutHarness.Wrap($@"
                <table style='width:100%'>
                  <tr><td><div style='height:{tall}pt'>tall</div></td></tr>
                  <tr><td>second row</td></tr>
                </table>
                <p>content after the table, on whatever band the table's row loop actually left it at</p>");

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageHeight: PageHeight, margin: Margin);

            Assert.Equal(0, container.CursorSpills);
        }

        /// <summary>
        /// The many-rowed, many-band-break table from <see cref="AManyRowedTableRecordsOneBreakPerBandItFills"/>,
        /// with content after it: every band the row loop's own <c>MoveToSlot</c> stepped through via
        /// <c>TakeBreakBeforeRow</c> has to leave the document-level cursor agreeing with it.
        /// </summary>
        [Fact]
        public async Task ContentAfterAManyRowedTable_SeesATruthfulCursor()
        {
            var rows = string.Concat(Enumerable.Range(1, 40).Select(i =>
                $"<tr><td><div style='height:30pt'>row {i}</div></td></tr>"));

            var html = LayoutHarness.Wrap(
                $"<table style='width:100%;border-collapse:collapse'>{rows}</table>"
                + "<p>content after the table</p>");

            var (_, container) = await LayoutHarness.LayoutAsync(html, pageHeight: 842, margin: 0);

            Assert.Equal(0, container.CursorSpills);
        }
    }
}

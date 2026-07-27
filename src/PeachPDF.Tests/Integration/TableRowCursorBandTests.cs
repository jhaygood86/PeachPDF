using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// What the table engine's row cursor does with the band it is filling, pinned as it stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TableRowCursor.SlotIndex</c> is a counter — one increment per break the row loop takes — so it
    /// names the band the loop last <i>opened</i>, not the band <c>CurrentY</c> has reached. A row taller
    /// than <c>EstimateRowHeight</c> predicted carries the cursor past it, and the offset that moves the
    /// next row "to the next page" then comes out negative.
    /// </para>
    /// <para>
    /// The overlap that produces is characterized here rather than asserted away, because deriving the
    /// band from the cursor instead is not the correction it looks like: the stale counter is what
    /// compensates for the estimate's undershoot, and removing it stops the loop breaking at all. See
    /// <see href="https://github.com/jhaygood86/PeachPDF/issues/432">issue #432</see>; closing it turns
    /// <see cref="RowsAfterARowTallerThanABand_AreCurrentlyPlacedInsideIt"/> into the no-overlap assertion
    /// its remarks describe.
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
        /// A row taller than a page band leaves the cursor several slots below the counter, so the break
        /// offset is negative and the rows after it are placed <i>inside</i> it — painted over its
        /// content. What this should assert is that no row starts above the bottom of the row before it.
        /// </summary>
        [Theory]
        [InlineData(700)]
        [InlineData(1000)]
        [InlineData(1400)]
        public async Task RowsAfterARowTallerThanABand_AreCurrentlyPlacedInsideIt(double tall)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                TallFirstRowTable(tall), pageHeight: PageHeight, margin: Margin);

            var rows = RowsOf(root);
            Assert.Equal(3, rows.Count);

            // The characterization: the second row lands at the top of the band after the one the table
            // began in, which the first row is still filling.
            Assert.Equal(container.PageTopOf(1), rows[1].Location.Y, 0.01);
            Assert.True(rows[1].Location.Y < rows[0].ActualBottom,
                $"tall={tall}: the fixture no longer overlaps, so it characterizes nothing as written");
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

        // A table taller than one band breaks inside itself, and its repeating header appears once per
        // page it broke onto. This is the behaviour a cursor that re-derived its band from CurrentY
        // silently lost: every row then found itself comfortably inside a fresh band, so none of them ever
        // asked for a break, no header proxy was created, and nothing failed.
        [Fact]
        public async Task ATableTallerThanABand_BreaksInsideItselfAndRepeatsItsHeader()
        {
            var rows = string.Concat(Enumerable.Range(1, 40).Select(i =>
                $"<tr><td><div style='height:30pt'>row {i}</div></td></tr>"));

            var (root, container) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap(
                    "<table style='width:100%;border-collapse:collapse'>"
                    + "<thead><tr><th>head</th></tr></thead><tbody>" + rows + "</tbody></table>"),
                pageHeight: 842, margin: 0);

            var table = TableOf(root);

            // 40 rows of 30pt plus a header is ~1210pt on 842pt bands, so the rows cross exactly one
            // boundary and the break is recorded against the band they crossed out of.
            Assert.Equal([0], table.PageBreakBottoms!.Keys.OrderBy(k => k).ToList());

            // One header proxy for the page the table starts on, plus one per break it took.
            Assert.Equal(2, LayoutHarness.Descendants(root).Count(b => b is CssProxyBox));
            Assert.Equal(2, container.FragmentTree!.Fragmentainers.Count);
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
    }
}

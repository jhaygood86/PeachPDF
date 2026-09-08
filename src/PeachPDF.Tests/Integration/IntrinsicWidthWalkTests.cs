using PeachPDF.Html.Core.Dom;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout assertions for <c>CssBox.GetMinMaxWidth</c>/<c>GetMinMaxSumWords</c> — the intrinsic
    /// (min-content/max-content) walk that sizes a shrink-to-fit box, an auto table column, and a
    /// flex item. It is a flat walk carrying one running line total across a whole subtree, which is
    /// where each of these cases went wrong.
    /// <para>
    /// Asserted on laid-out geometry rather than on the walk's own outputs, because the walk is
    /// private and because a number it returns only matters through where it puts content.
    /// </para>
    /// </summary>
    public class IntrinsicWidthWalkTests
    {
        [Fact]
        public async Task ForcedBreakInACell_DoesNotWidenTheColumnToTheWholeRun()
        {
            // A <br> ends a line, so a cell's max-content is the WIDEST line it holds, not the sum of
            // every line's words. Measured as the sum, the column is sized for a run that never draws
            // on one line and the table takes width from its neighbour.
            var narrow = await ColumnStartAsync("H-095<br>H-095-A1 HOSE");
            var oneLine = await ColumnStartAsync("H-095-A1 HOSE");

            // The two cells' widest single line is the same text, so the second column must start at
            // the same x in both. Summed across the break, the first is pushed measurably right.
            Assert.Equal(oneLine, narrow, 3);
        }

        [Fact]
        public async Task ForcedBreak_StillTakesTheWidestLine_NotTheFirst()
        {
            // The contrast case: taking the widest line must not degrade into taking the FIRST line,
            // which is the other way to get this wrong. Here the second line is the wider one.
            var wideSecond = await ColumnStartAsync("A<br>WWWWWWWWWWWWWWWW");
            var justTheWideLine = await ColumnStartAsync("WWWWWWWWWWWWWWWW");

            Assert.Equal(justTheWideLine, wideSecond, 3);
        }

        [Fact]
        public async Task InlineChildHorizontalMargins_CountTowardTheLineWidth()
        {
            // An inline child's horizontal margins sit ON the line and are part of its width. Left
            // out, a shrink-to-fit box is measured 80pt narrower than the run it has to hold and
            // wraps text that fits. position:absolute is used because it is genuinely shrink-to-fit
            // (CSS 2.1 §10.3.7) — a float in a full-width container stretches instead, and would say
            // nothing about the intrinsic walk.
            var html = LayoutHarness.Wrap(@"
                <div id='shrink' style='position:absolute;'>
                    <span style='margin-left:40pt; margin-right:40pt;'>one two three</span>
                </div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var shrink = LayoutHarness.FindById(root, "shrink")!;

            // 80pt of margin plus the run itself: one line, and the box holds it.
            Assert.Equal(1, LayoutHarness.Descendants(shrink).Sum(b => b.LineBoxes.Count));

            var widestWord = LayoutHarness.Descendants(shrink).SelectMany(b => b.Words)
                .Select(w => w.Right).DefaultIfEmpty(0).Max();
            Assert.True(shrink.ActualRight >= widestWord,
                $"the box must contain the run it was measured for: right {shrink.ActualRight} vs content {widestWord}");
        }

        [Fact]
        public async Task FlexRow_IsMeasuredAsTheSumOfItsItems_NotOneRunningLine()
        {
            // A flex row's items sit side by side, so the row's max-content is the SUM of theirs and
            // each item is measured in isolation. The flat walk carries one running total across the
            // whole subtree, so a <br> inside one item used to terminate the total the previous item
            // had contributed to — the row then measured as "first item + the second's first line".
            var html = LayoutHarness.Wrap(@"
                <div id='row' style='float:left; display:flex;'>
                    <div>LOGO</div>
                    <div>line one<br>a much longer second line</div>
                </div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var row = LayoutHarness.FindById(root, "row")!;

            // Every word must sit inside the row it was measured for. Measured short, the second
            // item is handed less than it needs and its text draws over the first.
            foreach (var word in LayoutHarness.Descendants(row).SelectMany(b => b.Words))
            {
                Assert.True(word.Right <= row.ActualRight + 0.5,
                    $"a word drew past the row's right edge ({word.Right} > {row.ActualRight})");
            }
        }

        [Fact]
        public async Task ABlockWhoseFirstWordIsAForcedBreak_MeasuresWideEnoughForItsContent()
        {
            // The shape that reaches `widestLine = Math.Max(widestLine, maxSum - trailingSpace)`
            // before any word of this box has been measured — a block whose very first word is a
            // forced break, following a block that left a hanging space behind. trailingSpace is now
            // reset with the rest of the per-line state so nothing carries across, though this
            // fixture passes either way: the value only ever reaches a Math.Max against a running
            // maximum that already dominates it. Kept as a guard on the shape, not as proof of the
            // reset.
            var html = LayoutHarness.Wrap(@"
                <div id='shrink' style='float:left;'>
                    <div>alpha bravo charlie</div>
                    <div style='margin-left:30pt;'><br>delta</div>
                </div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var shrink = LayoutHarness.FindById(root, "shrink")!;

            foreach (var word in LayoutHarness.Descendants(shrink).SelectMany(b => b.Words))
            {
                Assert.True(word.Right <= shrink.ActualRight + 0.5,
                    $"a word drew past the box's right edge ({word.Right} > {shrink.ActualRight})");
            }
        }

        /// <summary>
        /// Lays out a two-column auto table whose first cell holds <paramref name="firstCellHtml"/>,
        /// and returns where the SECOND column starts — the observable the first column's intrinsic
        /// width decides.
        /// </summary>
        private static async Task<double> ColumnStartAsync(string firstCellHtml)
        {
            var html = LayoutHarness.Wrap($@"
                <table style='width:400pt; table-layout:auto;'>
                    <tr><td>{firstCellHtml}</td><td id='second'>SECOND</td></tr>
                </table>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            return LayoutHarness.FindById(root, "second")!.Location.X;
        }
    }
}

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

        [Fact]
        public async Task ABlocksFinalLine_HangsItsTrailingSpace()
        {
            // css-text-3 §4.1.2: the white space that ends a line hangs and is not part of the
            // line's width. The walk applied this at a <br> only, so a block whose own final line
            // ended in a space measured (and drew a shrink-to-fit box) one space wider than the
            // glyphs it holds. Chromium 148 gives both of these 17.5938px = 13.1953pt (issue #1014).
            Assert.Equal(await FloatWidthAsync("AB"), await FloatWidthAsync("AB "), 3);
        }

        [Fact]
        public async Task ATrailingSpaceBetweenTwoInlines_IsStillAnOrdinaryInterWordGap()
        {
            // The contrast case, and the reason the rule cannot be applied per box: the walk carries
            // ONE running total across a whole subtree, so the first span's last word is not the
            // LINE's last word — a sibling follows it there and that space is a real gap. Hung here,
            // the line would measure a space short of what it draws.
            var space = await SpaceWidthAsync();

            Assert.Equal(
                await FloatWidthAsync("<span>AB</span><span>CD</span>") + space,
                await FloatWidthAsync("<span>AB </span><span>CD</span>"), 3);
        }

        [Fact]
        public async Task TheLastOfSeveralInlines_StillHangsItsOwnTrailingSpace()
        {
            // The pair of the case above: a space after the LAST span ends the block's line and
            // hangs, while the one between the spans does not. Both spaces counted, this measured
            // 39.5859pt against Chromium's 43.9844px = 32.9883pt.
            Assert.Equal(
                await FloatWidthAsync("<span>AB </span><span>CD</span>"),
                await FloatWidthAsync("<span>AB </span><span>CD </span>"), 3);
        }

        [Fact]
        public async Task TheLineAForcedBreakEnds_KeepsItsOwnWidth_WhenALaterLineHangsASpace()
        {
            // A <br> pushes the line it closes aside before the block's own final line is measured,
            // so hanging the final line's space must not reach back and shorten it. Here the first
            // line is the widest and the second ends in a space: the box has to stay as wide as
            // "AB CD" (Chromium: 43.9844px = 32.9883pt).
            Assert.Equal(await FloatWidthAsync("AB CD"), await FloatWidthAsync("AB CD<br>E "), 3);
        }

        [Fact]
        public async Task AnEarlierBlockSiblingsLine_KeepsItsOwnWidth_WhenALaterOneHangsASpace()
        {
            // Same guard for the other way a line gets closed — a block-level sibling. The first
            // <div>'s line is parked in its own saved total before the second <div> runs, so it must
            // not lose a space the second one hangs.
            Assert.Equal(
                await FloatWidthAsync("AB CD"),
                await FloatWidthAsync("<div>AB CD</div><div>E </div>"), 3);
        }

        [Fact]
        public async Task ANonBreakingSpace_IsStillCounted()
        {
            // &nbsp; is not white space for §4.1.2 purposes and never hangs — the narrow line
            // between "measure what draws" and "swallow a real advance". Chromium: 26.3906px =
            // 19.7930pt, one space wider than plain "AB".
            Assert.Equal(
                await FloatWidthAsync("AB") + await SpaceWidthAsync(),
                await FloatWidthAsync("AB&nbsp;"), 3);
        }

        [Theory]
        [InlineData("pre")]
        [InlineData("pre-wrap")]
        public async Task APreservedTrailingSpace_IsStillCounted(string whiteSpace)
        {
            // Under `white-space: pre`/`pre-wrap` the trailing space is preserved as its own word
            // rather than being an inter-word gap, and both engines count it: Chromium gives
            // 26.3906px = 19.7930pt for both. Nothing here may take it back off.
            Assert.Equal(
                await FloatWidthAsync("AB") + await SpaceWidthAsync(),
                await FloatWidthAsync($"<span style='white-space:{whiteSpace}'>AB </span>"), 3);
        }

        [Fact]
        public async Task ACellsFinalLine_HangsItsTrailingSpace_Too()
        {
            // A table cell never reaches the block-boundary epilogue — `display: table-cell` is
            // excluded from the walk's "starts a line of its own" test, and the table engine
            // measures each cell with its own top-level call. The rule has to be applied there as
            // well, or an auto column sized from that cell stays one space wide of its content.
            Assert.Equal(await ColumnStartAsync("AB"), await ColumnStartAsync("AB "), 3);
        }

        [Fact]
        public async Task AFlexRowLandingOnTheLineAfterASpace_DoesNotSwallowIt()
        {
            // A flex row's items are measured by their own top-level walk and added to the running
            // line as one number, not as words — so the space before the row stops being trailing
            // the moment the row lands on the line, and the epilogue must not still hang it. Needs
            // `white-space: nowrap` for the row to share the line at all (the walk otherwise treats
            // an inline-level box as starting one of its own). Chromium: 43.9844px = 32.9883pt with
            // the space, 35.1875px = 26.3906pt without.
            var space = await SpaceWidthAsync();

            Assert.Equal(
                await NowrapFloatWidthAsync("AB<span style='display:inline-flex'>CD</span>") + space,
                await NowrapFloatWidthAsync("AB <span style='display:inline-flex'>CD</span>"), 3);
        }

        [Theory]
        [InlineData("inline-block")]
        [InlineData("inline-table")]
        public async Task AnExplicitChildWidthLandingOnTheLineAfterASpace_DoesNotSwallowIt(string display)
        {
            // Same shape through the other path that puts something on the line without measuring a
            // word: a child with no content of its own whose explicit `width` is folded in as a
            // floor for the line. Once that floor raises the total, the measured tail — and the
            // space hanging off it — is no longer what the total ends in.
            var space = await SpaceWidthAsync();

            Assert.Equal(
                await NowrapFloatWidthAsync($"AB<span style='display:{display};width:50pt'></span>") + space,
                await NowrapFloatWidthAsync($"AB <span style='display:{display};width:50pt'></span>"), 3);
        }

        /// <summary>
        /// The advance of one collapsible space at the fixture font, measured as the difference
        /// between a two-word run and the same letters with no space — so the assertions above read
        /// as "exactly one space wider" without hard-coding a font metric.
        /// </summary>
        private static async Task<double> SpaceWidthAsync() =>
            await FloatWidthAsync("AB CD") - await FloatWidthAsync("ABCD");

        /// <summary>
        /// Lays out <paramref name="body"/> inside a left float and returns the float's used width —
        /// the shrink-to-fit observable that CSS 2.1 §10.3.5 takes straight from the intrinsic walk.
        /// The monospace font makes every expected value a whole number of character advances.
        /// </summary>
        private static Task<double> FloatWidthAsync(string body) => FloatWidthAsync(body, "");

        /// <summary>
        /// <see cref="FloatWidthAsync(string)"/> under an inherited <c>white-space: nowrap</c>, which
        /// is what makes an inline-level child share the line in this walk rather than be treated as
        /// opening one of its own.
        /// </summary>
        private static Task<double> NowrapFloatWidthAsync(string body) =>
            FloatWidthAsync(body, "; white-space:nowrap");

        private static async Task<double> FloatWidthAsync(string body, string extraStyle)
        {
            var html = LayoutHarness.Wrap(
                $"<div style=\"font:16px monospace{extraStyle}\"><div id='float' style='float:left'>{body}</div></div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            return LayoutHarness.FindById(root, "float")!.ActualWidth;
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

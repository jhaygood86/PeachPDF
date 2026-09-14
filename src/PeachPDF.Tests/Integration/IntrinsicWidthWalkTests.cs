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
            // the moment the row lands on the line, and the epilogue must not still hang it.
            // Chromium: 43.9844px = 32.9883pt with the space, 35.1875px = 26.3906pt without. Run
            // with and without `white-space: nowrap`, because until issue #1017 the inherited
            // `nowrap` was the only thing putting the row on the line at all — the shape this
            // fixture is about was unreachable in a document that did not declare one.
            var space = await SpaceWidthAsync();

            Assert.Equal(
                await NowrapFloatWidthAsync("AB<span style='display:inline-flex'>CD</span>") + space,
                await NowrapFloatWidthAsync("AB <span style='display:inline-flex'>CD</span>"), 3);
            Assert.Equal(
                await FloatWidthAsync("AB<span style='display:inline-flex'>CD</span>") + space,
                await FloatWidthAsync("AB <span style='display:inline-flex'>CD</span>"), 3);
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
            Assert.Equal(
                await FloatWidthAsync($"AB<span style='display:{display};width:50pt'></span>") + space,
                await FloatWidthAsync($"AB <span style='display:{display};width:50pt'></span>"), 3);
        }

        [Fact]
        public async Task ANowrapBlockSibling_StillStartsItsOwnLine()
        {
            // `white-space` says whether a box's content wraps WITHIN a line; it says nothing about
            // whether the box begins one, and a block-level box always does (CSS 2.1 §9.4.1). Read
            // as "does not start a line", a `nowrap` sibling had its line ADDED to the previous
            // sibling's instead of competing with it for "widest line wins": 46.1836pt against
            // Chromium 148's 43.9844px = 32.9883pt, one whole sibling line too wide (issue #1017).
            // Only the LATER sibling needs the `nowrap` to reach it.
            Assert.Equal(
                await FloatWidthAsync("<div>AB CD</div><div>EF</div>"),
                await FloatWidthAsync("<div>AB CD</div><div style='white-space:nowrap'>EF</div>"), 3);
        }

        [Fact]
        public async Task NowrapBlockSiblings_DoNotAccumulate_AsTheirCountGrows()
        {
            // The error was one whole sibling line each time, so it grew linearly with the sibling
            // count — a third `<div>` took the same float from 46.1836pt to 59.3789pt while
            // Chromium stayed at 32.9883pt. `white-space` inherits, so one declaration on the
            // container puts every sibling in the shape.
            Assert.Equal(
                await FloatWidthAsync("AB CD"),
                await NowrapFloatWidthAsync("<div>AB CD</div><div>EF</div><div>GH</div>"), 3);
        }

        [Fact]
        public async Task ANowrapBlockSiblingsBorder_IsScopedToItsOwnLine()
        {
            // paddingSum loses its per-line scoping the same way and for the same reason —
            // oldPaddingSum is saved in the branch this predicate guards, so a sibling that never
            // opened a line never restored it either, and both siblings' borders summed into one
            // float's width. That is the Acid2 `#eyes-a`/`#eyes-b`/`#eyes-c` regression the
            // `oldPaddingSum` comment in GetMinMaxSumWords describes, reachable again whenever the
            // siblings are `nowrap`: 56.1836pt for the second fixture against the first's 42.9883pt.
            Assert.Equal(
                await NowrapFloatWidthAsync("<div style='border-left:10pt solid'>AB CD</div>"),
                await NowrapFloatWidthAsync(
                    "<div style='border-left:10pt solid'>AB CD</div><div style='border-left:10pt solid'>EF</div>"), 3);
        }

        [Fact]
        public async Task ABlockLinesDecoration_StaysAttachedToThatLinesContentWidth()
        {
            // Each block child is a competing line. The first has the wider content (100pt) and the
            // second has the wider decoration (40pt), but neither is 140pt wide: their complete outer
            // widths are 102pt and 120pt. Choosing max-content and decoration independently invents a
            // line that does not exist and over-sizes the shrink-to-fit parent.
            Assert.Equal(
                120,
                await FloatWidthAsync(
                    "<div style='width:100pt;border:1pt solid'></div>"
                    + "<div style='width:80pt;border:20pt solid'></div>"),
                precision: 3);
        }

        [Fact]
        public async Task NestedBlockDecoration_StillAddsThroughTheContainmentChain()
        {
            // Sibling lines compete, but nested decorations do not: the child's 20pt border sits
            // inside the parent's content box, whose own 4pt border remains outside it.
            Assert.Equal(
                124,
                await FloatWidthAsync(
                    "<div style='border:2pt solid'><div style='width:100pt;border:10pt solid'></div></div>"),
                precision: 3);
        }

        [Fact]
        public async Task AnInlineFlexChild_SharesTheLine_WithoutNeedingNowrap()
        {
            // The other half of the same predicate: an inline-level box never begins a line, so it
            // adds to the one in progress. The walk treated every inline-level display as opening
            // one of its own, and an inherited `nowrap` was what accidentally put it back — the
            // right answer for the wrong reason, and out of reach of a document that does not
            // declare one. Without it this measured 19.7930pt, the row's own width alone, against
            // Chromium's 43.9844px = 32.9883pt.
            Assert.Equal(
                await FloatWidthAsync("AB CD"),
                await FloatWidthAsync("AB <span style='display:inline-flex'>CD</span>"), 3);
        }

        [Theory]
        [InlineData("inline-block")]
        [InlineData("inline-table")]
        [InlineData("inline-grid")]
        public async Task AnInlineLevelChildsExplicitWidth_LandsOnTheLine_WithoutNeedingNowrap(string display)
        {
            // Same through the explicit-width fold, the third call site that has to agree on the
            // predicate: an inline-level child's width is added to the line in progress rather than
            // competing with it. Chromium 148 gives 93.0469px = 69.7852pt; this measured the 50pt
            // alone, and a shrink-to-fit box was sized to overlap the run beside it.
            Assert.Equal(
                await FloatWidthAsync("AB") + await SpaceWidthAsync() + 50,
                await FloatWidthAsync($"AB <span style='display:{display};width:50pt'></span>"), 3);
        }

        [Fact]
        public async Task AnInlineLevelBoxAfterABlockSibling_StillGetsItsOwnLine()
        {
            // The contrast case for the two above, and what they could plausibly have broken: an
            // inline-level box FOLLOWING a block-level sibling is on a line of its own, so its width
            // must not be added to that sibling's. It still is not — the parser wraps it in an
            // anonymous block (CSS 2.1 §9.2.1.1), which is block-level and does open the line — but
            // that is the only thing standing between these fixtures and 46.1836/82.9883pt.
            Assert.Equal(
                await FloatWidthAsync("AB CD"),
                await FloatWidthAsync("<div>AB CD</div><span style='display:inline-block'>EF</span>"), 3);
            Assert.Equal(
                50,
                await FloatWidthAsync("<div>AB CD</div><span style='display:inline-block;width:50pt'></span>"), 3);
        }

        [Theory]
        [InlineData("inline-block")]
        [InlineData("inline-flex")]
        [InlineData("inline-table")]
        [InlineData("inline-grid")]
        [InlineData("inline")]
        public async Task AFlexOrGridItem_StartsItsOwnLine_WhateverItsOwnDisplayIs(string display)
        {
            // A flex or grid item is blockified by the formatting context it is in (css-display-3 §2.7,
            // as css-flexbox-1 §4 and css-grid-2 §6 require), so a single-line COLUMN of them is as
            // wide as its widest item, not as wide as all of them laid end to end. PeachPDF
            // deliberately leaves an inline-level item's COMPUTED display alone and blockifies at
            // layout time instead (issue #1003), so `ActualDisplay` alone answers "inline-level" here
            // and is the wrong oracle — `StartsNewLine` has to ask the PARENT. Without that, this
            // measured 72.5742pt against the 39.5859pt the block-item control gives, and took the
            // difference out of whatever sat beside it.
            var control = await ColumnContainerWidthAsync("flex-column", "div", "");

            Assert.Equal(control, await ColumnContainerWidthAsync("flex-column", "span", display), 3);
            Assert.Equal(control, await ColumnContainerWidthAsync("grid", "span", display), 3);
        }

        [Fact]
        public async Task AFloat_AddsToTheLineItSitsBeside()
        {
            // A float is taken out of the flow but is still placed BESIDE the inline content of the
            // block it is in (CSS 2.1 §9.5), so for max-content sizing its width adds to that line.
            // The walk saw only that §9.7 blockifies it and had it open a competing line, measuring
            // 26.3906pt — `max("XY ", "ZZZZ")` — against the 39.5859pt Chromium and Firefox both
            // give (issue #1033).
            //
            // Compared against the six characters WITHOUT the space, not the seven with it: the
            // float is out of flow, and css-text-3 §1.5 ignores out-of-flow elements when deciding
            // adjacency, so the space still ends the line's own in-flow content and hangs (§4.1.2).
            // Landing the float's width on the line does not make it content following that space.
            //
            // Run under `white-space: nowrap` too: that half is a regression from issue #1017, where
            // the old predicate excused any `nowrap` box from opening a line and summed the float
            // onto it for a reason that had nothing to do with floats. A `nowrap` line's trailing
            // space hangs the same way (css-text-3 §4.1.2 hangs it under `normal` and `nowrap`
            // alike), so the two agree.
            Assert.Equal(
                await FloatWidthAsync("XYZZZZ"),
                await FloatWidthAsync("XY <span style='float:left'>ZZZZ</span>"), 3);
            Assert.Equal(
                await NowrapFloatWidthAsync("XYZZZZ"),
                await NowrapFloatWidthAsync("XY <span style='float:left'>ZZZZ</span>"), 3);
        }

        [Fact]
        public async Task InlineContentEitherSideOfAFloat_StaysOnOneLine()
        {
            // What `SharesItsLineWithAFloat` is for, now that hanging the space is decided separately:
            // the running line total carries THROUGH the float, so a second run of inline content after
            // it is still on the first run's line rather than competing with it. Without that, the two
            // runs sit in separate §9.2.1.1 wrappers that each open a line and this measures 39.5859pt
            // — the first run plus the float — while holding three things.
            //
            // Expected value is derived, not browser-measured: the two collapsible spaces around the
            // float collapse to one (css-text-3 §4.1.1 collapses across the out-of-flow element, which
            // §1.5 ignores for adjacency), and that one is interior to the line, so the text measures
            // as the single run "XY more" and the float's width adds to it.
            Assert.Equal(
                await FloatWidthAsync("XY moreZZZZ"),
                await FloatWidthAsync("XY <span style='float:left'>ZZZZ</span> more"), 3);
        }

        [Fact]
        public async Task AFloatBesideInlineContent_HasRoomReservedForItByTheLine()
        {
            // The consequence the measurement has, asserted on geometry: the shrink-to-fit box has to
            // be wide enough to hold the text AND the float beside it. Sized to the wider of the two
            // instead of their sum it is not, and the two can only overlap. Where layout then PUTS
            // the float is a separate question this fixture deliberately does not ask — see
            // .claude/accepted-gaps/a-float-after-inline-content-is-placed-on-the-next-line.md.
            var html = LayoutHarness.Wrap(@"
                <div style='font:16px monospace'>
                    <div id='float' style='float:left'>XY <span id='inner' style='float:left'>ZZZZ</span></div>
                </div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var outer = LayoutHarness.FindById(root, "float")!;
            var inner = LayoutHarness.FindById(root, "inner")!;

            var innerBoxes = LayoutHarness.Descendants(inner).ToHashSet();
            var textWidth = LayoutHarness.Descendants(outer).Where(b => !innerBoxes.Contains(b))
                .SelectMany(b => b.Words).Select(w => w.Right - outer.Location.X).DefaultIfEmpty(0).Max();

            Assert.True(outer.ActualWidth >= textWidth + inner.ActualWidth - 0.5,
                $"the box holds the text ({textWidth}) and the float ({inner.ActualWidth}) side by side, "
                + $"so it must be at least their sum wide, not {outer.ActualWidth}");
        }

        [Fact]
        public async Task ConsecutiveFloats_AddUp_RatherThanCompeting()
        {
            // Two floats with no inline content between them still sit side by side, so their widths
            // add the same way. Measured as competing lines the container was as wide as one of them
            // and the second was pushed onto a line of its own with a hole beside the first.
            Assert.Equal(
                await FloatWidthAsync("AAABBB"),
                await FloatWidthAsync(
                    "<span style='float:left'>AAA</span><span style='float:left'>BBB</span>"), 3);
        }

        [Fact]
        public async Task AFloatBetweenBlockLevelSiblings_StillGetsItsOwnLine()
        {
            // The contrast case, and what "a float adds to the line" must not become: a float whose
            // in-flow sibling is genuinely block-level is not on that sibling's line, so the two
            // still compete for "widest line wins". Summed, this would be 46.1836pt.
            Assert.Equal(
                await FloatWidthAsync("AB CD"),
                await FloatWidthAsync("<p>AB CD</p><span style='float:left'>EF</span>"), 3);
        }

        [Fact]
        public async Task AFloatsDeclaredWidthAndMargins_LandOnTheLine()
        {
            // A float leaves the recursive path that folds a child's explicit `width` into the line,
            // so its own declared width and horizontal margins have to be counted where it is
            // measured instead — a float declaring a width is the ordinary case, not an exotic one.
            Assert.Equal(
                await FloatWidthAsync("XY") + 50,
                await FloatWidthAsync("<span style='float:left;width:50pt'>Z</span>XY"), 3);
            // The declared width is a CONTENT width, so the float's own padding goes on top of it.
            Assert.Equal(
                await FloatWidthAsync("XY") + 70,
                await FloatWidthAsync("<span style='float:left;width:50pt;padding:0 10pt'>Z</span>XY"), 3);
            // And it REPLACES the measured content rather than flooring it — a non-auto width IS the
            // used width (CSS 2.1 §10.3.5), so content wider than it overflows instead of widening the
            // float. Asserted through an auto table column, which takes the walk's answer directly;
            // a shrink-to-fit box's own used width would not show it, because
            // CssLayoutEngine.GetLargestChildWidth separately maxes in every descendant's own width
            // and the overflowing run wins there.
            Assert.Equal(
                await ColumnStartAsync("<span style='float:left;width:10pt'>Z</span>"),
                await ColumnStartAsync("<span style='float:left;width:10pt'>ZZZZZZZZ</span>"), 3);
            Assert.Equal(
                await FloatWidthAsync("ZZZZXY") + 10,
                await FloatWidthAsync("<span style='float:left;margin-right:10pt'>ZZZZ</span>XY"), 3);
        }

        [Theory]
        // A 48pt float inside a box whose own border is 12pt a side. When the float ALSO has a 12pt
        // border, both decorations are on the line: 24 (container) + 24 (float) + 48 = 96pt.
        [InlineData("border:12pt solid black", 96d)]
        // ...and with no decoration of its own, just the container's: 24 + 48 = 72pt.
        [InlineData("", 72d)]
        public async Task AFloatsOwnDecoration_AddsToTheLine_RatherThanMergingWithItsContainers(
            string floatStyle, double expectedWidth)
        {
            // The running padding total this walk keeps is combined down a chain of boxes by Math.Max,
            // because a descendant's border/padding sits INSIDE its ancestor's and counting both would
            // double it. A float is not on that chain - CSS 2.1 §9.5 places it BESIDE the content of the
            // block it is in, so its decoration sits beside the container's and genuinely adds. Folding
            // it in by Math.Max instead merged the two whenever the container's was the larger, losing
            // the float's entirely: Acid2's ".smile div div" (a 1em border around one 1em-bordered
            // float) measured 96px where Chrome gives 120px, and the mouth's yellow flanks painted black.
            //
            // Both expectations are Chrome's own on this markup.
            var html = LayoutHarness.Wrap(
                $"<div id='t' style=\"position:absolute; top:0; left:400pt; border:12pt solid yellow\">" +
                $"<div style=\"float:right; width:48pt; height:12pt; {floatStyle}\"></div></div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            var box = LayoutHarness.FindById(root, "t")!;

            Assert.Equal(expectedWidth, box.ActualRight - box.Location.X, precision: 6);
        }

        [Fact]
        public async Task AnOutOfFlowOrUndisplayedSibling_DoesNotStopTheFloatSharingTheLine()
        {
            // Neither a `position: absolute` sibling (which contributes nothing to its containing
            // block's intrinsic size, CSS 2.1 §10.3.7) nor a `display: none` one is in-flow
            // block-level content, so neither may make the float look like it sits between blocks.
            var beside = await FloatWidthAsync("XYZZZZ");

            Assert.Equal(beside, await FloatWidthAsync(
                "XY <span style='float:left'>ZZZZ</span><span style='position:absolute'>Q</span>"), 3);
            Assert.Equal(beside, await FloatWidthAsync(
                "XY <span style='float:left'>ZZZZ</span><div style='display:none'>QQQQQQQQ</div>"), 3);
        }

        /// <summary>
        /// Lays out a left float that is a single-line flex column (or a one-column grid) holding two
        /// items of differing width, built from <paramref name="tag"/> with an optional explicit
        /// <paramref name="display"/>, and returns the container's used width. Single-line and
        /// single-column on purpose: that is the shape whose max-content is the WIDEST item rather
        /// than the sum, so a container measured as the sum is unmistakable.
        /// </summary>
        private static async Task<double> ColumnContainerWidthAsync(string kind, string tag, string display)
        {
            var containerStyle = kind == "grid"
                ? "float:left; display:grid"
                : "float:left; display:flex; flex-direction:column";
            var itemStyle = display.Length > 0 ? $" style='display:{display}'" : "";
            var html = LayoutHarness.Wrap(
                $"<div style=\"font:16px monospace\"><div id='float' style=\"{containerStyle}\">"
                + $"<{tag}{itemStyle}>AB CD</{tag}><{tag}{itemStyle}>EFGHIJ</{tag}></div></div>");

            var (root, _) = await LayoutHarness.LayoutAsync(html);
            return LayoutHarness.FindById(root, "float")!.ActualWidth;
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
        /// <see cref="FloatWidthAsync(string)"/> under an inherited <c>white-space: nowrap</c>. It no
        /// longer changes whether an inline-level child shares the line — since issue #1017 that is
        /// decided by the display alone — which is exactly why the fixtures below assert the two agree.
        /// It is still what puts a <c>nowrap</c> block-level sibling in the shape that issue was about.
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

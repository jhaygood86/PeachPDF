using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout-level tests for per-page horizontal reflow (issue #143): when a per-page <c>@page</c> rule
    /// overrides a left/right margin, top-level (main-column) auto-width block content is re-wrapped to
    /// that page's own content-box width — CSS Paged Media 3's "the edges of the page area act as a
    /// containing block for layout that occurs between page breaks" — instead of being laid out once at
    /// the base measure and merely shifted/clipped at paint time. A paragraph that spans a page boundary
    /// genuinely re-wraps at each fragment's own measure (css-break-3 §5.1: a fragment recalculates sizes
    /// and positions using its own fragmentainer's size) rather than sharing one measure across every
    /// fragment. Follows the repo's layout-harness convention (build a container, PerformLayout, assert
    /// box positions/sizes), with a harness mirroring PdfGenerator.SetContent's geometry derivation.
    /// </summary>
    public class PerPageHorizontalReflowLayoutIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;

        // Base fixture margins: @page { margin: 60pt 50pt } -> content box 512 wide at left origin 50,
        // so the base right edge is 562. Band 672 tall.
        private const double BaseMargin = 50;
        private const double BaseContentWidth = SheetW - 2 * BaseMargin; // 512
        private const double BaseRightEdge = BaseMargin + BaseContentWidth; // 562

        [Fact]
        public async Task FirstPageMarginLeftZero_ReflowsToWiderMeasure()
        {
            // @page :first { margin-left: 0 } widens page 0 only: content box 562 wide (612 - 0 - 50),
            // right edge 612; pages 2+ keep the base 562 right edge.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>page zero paragraph whose auto width should reflow to the wider first-page measure</p>
                <p id='p1' style='page-break-before: always'>page one paragraph at the base measure</p>
                </body></html>
                """);

            var p0 = FindById(container.Root!, "p0")!;
            var p1 = FindById(container.Root!, "p1")!;

            Assert.Equal(0, container.PageIndexOf(p0.Location.Y));
            Assert.Equal(1, container.PageIndexOf(p1.Location.Y));

            // p0 adopts page 0's own (wider) measure; p1 reverts to the base measure.
            // Page 0: left origin stays at the base 50, content width = 612 - 0 - 50 = 562, so the right
            // edge is 50 + 562 = 612. Page 1 keeps the base right edge (562).
            const double firstPageRightEdge = BaseMargin + (SheetW - 0 - BaseMargin); // 612
            Assert.Equal(firstPageRightEdge, p0.ActualRight, 0.5);
            Assert.Equal(BaseRightEdge, p1.ActualRight, 0.5);

            Assert.Equal(container.PageContentRightOf(p0.Location.Y), p0.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(p1.Location.Y), p1.ActualRight, 0.5);
            Assert.True(p0.ActualRight > BaseRightEdge, "first page should reflow wider than the base measure");
        }

        [Fact]
        public async Task MirrorMargins_DifferingWidths_EachPageOwnMeasure()
        {
            // Binding-style mirror margins: right (odd) pages inset 20 on the left, left (even) pages inset
            // 100 — same right margin — so each page has a genuinely different measure.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :right { margin-left: 20pt; }
                @page :left  { margin-left: 100pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='pRight'>right page content</p>
                <p id='pLeft' style='page-break-before: always'>left page content</p>
                </body></html>
                """);

            var pRight = FindById(container.Root!, "pRight")!; // page 0 -> pageNumber 1 -> :right
            var pLeft = FindById(container.Root!, "pLeft")!;   // page 1 -> pageNumber 2 -> :left

            Assert.Equal(0, container.PageIndexOf(pRight.Location.Y));
            Assert.Equal(1, container.PageIndexOf(pLeft.Location.Y));

            // Right page: right edge = 50 + (612 - 20 - 50) = 592. Left page: 50 + (612 - 100 - 50) = 512.
            Assert.Equal(BaseMargin + (SheetW - 20 - BaseMargin), pRight.ActualRight, 0.5); // 592
            Assert.Equal(BaseMargin + (SheetW - 100 - BaseMargin), pLeft.ActualRight, 0.5); // 512

            Assert.True(pRight.ActualRight > pLeft.ActualRight,
                "the wider-measure right page should extend further than the narrower left page");
            Assert.Equal(container.PageContentRightOf(pRight.Location.Y), pRight.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(pLeft.Location.Y), pLeft.ActualRight, 0.5);
        }

        [Fact]
        public async Task StraddlingParagraph_RewrapsToEachPagesOwnMeasure()
        {
            // A single long paragraph starts on the wide first page and flows onto the base-margin page 2.
            // css-break-3 §5.1: a fragment recalculates sizes and positions using its own fragmentainer's
            // size - so the paragraph's continuation lines on page 2 re-wrap to the narrower base measure
            // rather than keeping the wider first-page measure. This inverts what this test asserted before
            // mid-fragment block/text rewrap landed (see .claude/migration-notes), when PeachPDF laid the
            // whole paragraph out once at its start-page measure and merely shifted/clipped continuations.
            var words = string.Join(" ", Enumerable.Range(0, 900).Select(i => $"word{i}"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='flow'>{{words}}</p>
                </body></html>
                """);

            var flow = FindById(container.Root!, "flow")!;
            Assert.Equal(0, container.PageIndexOf(flow.Location.Y)); // starts on page 0

            var flowWords = new List<CssRect>();
            CollectWords(flow, flowWords);

            var pageZeroWords = flowWords
                .Where(w => w.Width > 0 && container.PageIndexOf(w.Top) == 0)
                .ToList();
            var pageOneWords = flowWords
                .Where(w => w.Width > 0 && container.PageIndexOf(w.Top) >= 1)
                .ToList();

            Assert.NotEmpty(pageOneWords); // the paragraph really does span onto page 2

            // Page 0's own lines still use the wide first-page measure - some word extends past the base
            // right edge, which it could not do at the narrower base measure. Proves the two assertions
            // below are a genuine per-page difference, not merely a uniformly narrow layout throughout.
            Assert.True(pageZeroWords.Max(w => w.Right) > BaseRightEdge,
                "page 0's own lines use its wider own-page measure");

            // Continuation lines re-wrap to the narrower base measure: no word extends past the base right
            // edge, which it could only avoid by having re-wrapped rather than kept the wide start-page
            // measure.
            Assert.True(pageOneWords.Max(w => w.Right) <= BaseRightEdge + 0.5,
                "a spanning paragraph re-wraps to each page's own (narrower) measure");
        }

        [Fact]
        public async Task StraddlingParagraph_RightAligned_FlushesEachPagesLinesToItsOwnRightEdge()
        {
            // text-align:right on a straddling paragraph must flush every line - on every page it lands
            // on - to that page's own right edge (CssLayoutEngine.ApplyRightAlignment now reads the
            // line's own ContentRight rather than the block's single start-page-fixed ClientRight). Proves
            // per-line alignment tracks the per-line rewrap this layer adds, not just the words' wrap
            // points.
            var words = string.Join(" ", Enumerable.Range(0, 900).Select(i => $"word{i}"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                p { margin: 0; text-align: right; }
                </style></head><body>
                <p id='flow'>{{words}}</p>
                </body></html>
                """);

            var flow = FindById(container.Root!, "flow")!;
            Assert.Equal(0, container.PageIndexOf(flow.Location.Y)); // starts on page 0

            var flowWords = new List<CssRect>();
            CollectWords(flow, flowWords);

            var pageZeroWords = flowWords.Where(w => w.Width > 0 && container.PageIndexOf(w.Top) == 0).ToList();
            var pageOneWords = flowWords.Where(w => w.Width > 0 && container.PageIndexOf(w.Top) >= 1).ToList();

            Assert.NotEmpty(pageOneWords); // the paragraph really does span onto page 2

            // Page 0's own lines flush right to its own (wider) content edge.
            const double firstPageRightEdge = BaseMargin + (SheetW - 0 - BaseMargin); // 612
            Assert.Equal(firstPageRightEdge, pageZeroWords.Max(w => w.Right), 0.5);

            // Continuation lines flush right to the narrower base content edge instead - not the wide
            // first-page edge a shared, start-page-fixed measure would have flushed them to.
            Assert.Equal(BaseRightEdge, pageOneWords.Max(w => w.Right), 0.5);
        }

        [Fact]
        public async Task NestedBlock_InsideAFixedWidthContainer_KeepsOneMeasureAcrossPages()
        {
            // The remaining scope boundary after #200's fix: content whose containing block carries an
            // explicit (non-auto) width - here, a fixed-width div - still keeps one measure across a page
            // straddle. #200 widened IsUnconstrainedMainColumn to accept any plain, auto-width block-level
            // wrapper (not just root/html/body) as a chain link, but an explicit length still disqualifies
            // that link exactly as it always has: CssLayoutEngine.ContentRightOf/IsUnconstrainedMainColumn
            // requires EVERY level up to the root to be unconstrained, so a fixed-width div still breaks
            // the chain regardless of what page a line inside it lands on. A fixed length (rather than a
            // percentage) keeps the div's own width fully deterministic, independent of whatever the
            // main-column chain above it resolves to - isolating exactly the property this test means to
            // guard. Regression guard: this must stay the non-rewrapping behavior, unlike the plain
            // auto-width nested-div case NestedDivInsideAnotherDiv_NowReflowsPerPage_MatchingIssue200sFix
            // (StraddlingBlockInlineExtentLayoutIntegrationTests) now characterizes.
            var words = string.Join(" ", Enumerable.Range(0, 900).Select(i => $"word{i}"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                div { width: 600pt; }
                p { margin: 0; }
                </style></head><body>
                <div><p id='flow'>{{words}}</p></div>
                </body></html>
                """);

            var flow = FindById(container.Root!, "flow")!;
            Assert.Equal(0, container.PageIndexOf(flow.Location.Y)); // starts on page 0

            var flowWords = new List<CssRect>();
            CollectWords(flow, flowWords);

            var pageZeroWords = flowWords.Where(w => w.Width > 0 && container.PageIndexOf(w.Top) == 0).ToList();
            var pageOneWords = flowWords.Where(w => w.Width > 0 && container.PageIndexOf(w.Top) >= 1).ToList();

            Assert.NotEmpty(pageZeroWords);
            Assert.NotEmpty(pageOneWords); // the paragraph really does span onto page 2

            // Both pages' lines wrap against the same (div-derived, ~600pt) edge, well past the base
            // per-page measure (562pt) - unlike the plain main-column case, page 2's continuation lines do
            // NOT narrow down to it. (Exact equality isn't asserted between the two pages' own maxima: the
            // last word before a wrap naturally lands a little short of the boundary itself, by whatever
            // slack that particular word left, so the two pages' maxima are only approximately equal even
            // when both wrap against the identical edge - see the >BaseRightEdge check on each instead.)
            Assert.True(pageZeroWords.Max(w => w.Right) > BaseRightEdge);
            Assert.True(pageOneWords.Max(w => w.Right) > BaseRightEdge,
                "a paragraph nested inside a non-main-column container keeps its start-page measure across fragments");
        }

        [Fact]
        public async Task MaxWidthConstrainedWrapper_StaysUnaffected_UnlikeAPlainAutoWidthWrapper()
        {
            // #200's widened IsUnconstrainedMainColumn still excludes a wrapper carrying its own
            // max-width clamp (CssLayoutEngine.IsOrdinaryUnconstrainedBlock): an auto-width div bearing a
            // max-width can't be assumed to genuinely span the page area the way a plain unconstrained
            // wrapper now can (see StraddlingBlockInlineExtentLayoutIntegrationTests's
            // NestedDivInsideAnotherDiv_NowReflowsPerPage_MatchingIssue200sFix). The max-width (900pt) is
            // deliberately wider than either page's own content box - what matters is that ANY valid
            // max-width on the wrapper disqualifies the chain, not that it actually clamps anything here.
            var words = string.Join(" ", Enumerable.Range(0, 900).Select(i => $"word{i}"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                #outer { max-width: 900pt; }
                p { margin: 0; }
                </style></head><body>
                <div id='outer'><p id='flow'>{{words}}</p></div>
                </body></html>
                """);

            var flow = FindById(container.Root!, "flow")!;
            Assert.Equal(0, container.PageIndexOf(flow.Location.Y));

            var flowWords = new List<CssRect>();
            CollectWords(flow, flowWords);

            var pageZeroWords = flowWords.Where(w => w.Width > 0 && container.PageIndexOf(w.Top) == 0).ToList();
            var pageOneWords = flowWords.Where(w => w.Width > 0 && container.PageIndexOf(w.Top) >= 1).ToList();

            Assert.NotEmpty(pageZeroWords);
            Assert.NotEmpty(pageOneWords); // the paragraph really does span onto page 2

            Assert.True(pageZeroWords.Max(w => w.Right) > BaseRightEdge);
            Assert.True(pageOneWords.Max(w => w.Right) > BaseRightEdge,
                "a max-width-constrained wrapper's descendant keeps its start-page measure across fragments");
        }

        [Fact]
        public async Task PercentageWidthBlock_InsideAutoWidthMainColumn_ReflowsToEachPagesOwnMeasure()
        {
            // #199: a percentage width now resolves against its containing block's own PAGE-AWARE measure
            // (CssLayoutEngine.PageAwareWidthBasis) rather than the single Size.Width value that block was
            // last resolved to. body itself is an unconstrained main-column box, so its own width already
            // varies per page (issue #143); this div's 50% must track that.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                div { width: 50%; }
                </style></head><body>
                <div id='d0'>page zero</div>
                <div id='d1' style='page-break-before: always'>page one</div>
                </body></html>
                """);

            var d0 = FindById(container.Root!, "d0")!;
            var d1 = FindById(container.Root!, "d1")!;

            Assert.Equal(0, container.PageIndexOf(d0.Location.Y));
            Assert.Equal(1, container.PageIndexOf(d1.Location.Y));

            // Page 0's body is 562pt wide (612 - 0 - 50), so 50% is 281; page 1's body is the base 512pt
            // wide (612 - 50 - 50), so 50% is 256.
            Assert.Equal(281, d0.Size.Width, 0.5);
            Assert.Equal(256, d1.Size.Width, 0.5);
        }

        [Fact]
        public async Task FixedPositionBox_PercentageWidth_ResolvesAgainstItsOwnPagesBand()
        {
            // #201: a position:fixed box's containing block is the page area itself (CSS2.1 §10.1); its
            // percentage width now resolves against THAT page's own content-right edge
            // (HtmlContainerInt.PageContentRightOf) instead of the document's base PageSize.Width, the
            // fixed-position analogue of CommitBlockChildOffset's own left/top basis for a fixed box.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                #fixed { position: fixed; width: 50%; height: 20pt; top: 0; }
                </style></head><body>
                <div id='fixed'></div>
                </body></html>
                """);

            var fixedBox = FindById(container.Root!, "fixed")!;

            // Page 0's own band is 562pt wide (612 - 0 - 50); half of that is 281.
            Assert.Equal(281, fixedBox.Size.Width, 0.5);
        }

        [Fact]
        public async Task AbsolutelyPositionedBox_PercentageWidth_ResolvesAgainstEachPagesOwnMeasure()
        {
            // An absolutely-positioned box's percentage width resolves against its containing block (CSS
            // 2.1 §10.1) - here, nothing else in the document is positioned, so that's the initial
            // containing block itself, whose own width is now page-aware the same way an ordinary block's
            // percentage width is (#199/#201), via the same PageAwareWidthBasis helper.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                #abs { position: absolute; width: 50%; height: 20pt; top: 0; }
                </style></head><body>
                <div id='abs'></div>
                </body></html>
                """);

            var abs = FindById(container.Root!, "abs")!;

            // Page 0's own ICB band is 562pt wide (612 - 0 - 50); half of that is 281.
            Assert.Equal(281, abs.Size.Width, 0.5);
        }

        [Fact]
        public async Task AbsolutelyPositionedBox_LeftAndRightInsets_FillsTheFirstPagesOwnIcbWidth()
        {
            // #201: an absolutely-positioned box with auto width and both `left`/`right` set fills the
            // space between them in its containing block (CSS 2.1 §10.3.7) - here, the initial containing
            // block itself (nothing else in the document is positioned), whose own width is now pinned to
            // the FIRST page's own resolved band (css-page-3 §3, HtmlContainerInt.IcbWidthSeed) rather
            // than the document's base configured width.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                #abs { position: absolute; left: 0; right: 0; height: 20pt; }
                </style></head><body>
                <div id='abs'></div>
                </body></html>
                """);

            var abs = FindById(container.Root!, "abs")!;

            // Page 0's own band (612 - 0 - 50 = 562), not the base 512 the un-fixed ICB would have used.
            Assert.Equal(562, abs.Size.Width, 0.5);
        }

        [Fact]
        public async Task MultiColumnContainer_SpanningPagesWithDifferentMargins_EachPageOwnColumnWidth()
        {
            // #198: CssLayoutEngineColumns.Layout now resolves the container's own width fresh on every
            // per-page continuation (boxTop - THIS invocation's own top, since PerformLayout re-enters
            // this method fresh for every page the container spans) rather than always the box's own
            // (start-page) Location.Y, so a container spanning a page boundary lays out each page's own
            // columns at that page's own width/pitch instead of reusing the start page's.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                #mc { columns: 2; column-gap: 0; column-fill: auto; }
                .item { height: 400pt; break-inside: avoid; margin: 0; }
                </style></head><body>
                <div id='mc'>
                    <div class='item' id='i1'></div>
                    <div class='item' id='i2'></div>
                    <div class='item' id='i3'></div>
                    <div class='item' id='i4'></div>
                </div>
                </body></html>
                """);

            var i1 = FindById(container.Root!, "i1")!;
            var i2 = FindById(container.Root!, "i2")!;
            var i3 = FindById(container.Root!, "i3")!;
            var i4 = FindById(container.Root!, "i4")!;

            // Each page's own 2 columns (672pt tall band, 400pt items) hold exactly one item per column
            // before the container continues onto the next page's fresh columns.
            Assert.Equal(0, container.PageIndexOf(i1.Location.Y));
            Assert.Equal(0, container.PageIndexOf(i2.Location.Y));
            Assert.Equal(1, container.PageIndexOf(i3.Location.Y));
            Assert.Equal(1, container.PageIndexOf(i4.Location.Y));

            var page0Pitch = i2.Location.X - i1.Location.X;
            var page1Pitch = i4.Location.X - i3.Location.X;

            // Page 0's container is 562pt wide (612 - 0 - 50), so each of its 2 (gap:0) columns is 281pt;
            // page 1's is the base 512pt wide (612 - 50 - 50), so each column is 256pt.
            Assert.Equal(281, page0Pitch, 0.5);
            Assert.Equal(256, page1Pitch, 0.5);
        }

        [Fact]
        public async Task TableCell_TextWrapWidth_UnaffectedByPerPageMeasureOverride()
        {
            // Layer D's per-line rewrap must not reach into a table cell: a cell's own width comes from
            // the table's column-width algorithm (CSS2.1 §17.5), not the auto-width-fills-containing-block
            // rule LineContentRightOf otherwise assumes for an ordinary block. Feeding it the containing
            // block's (the table's) edge instead of the cell's own would regress cell text to wrap at
            // roughly the table's full width rather than its own narrow column - guarded against by
            // CssLayoutEngine.FillsContainingBlockWidth. Deliberately no explicit `width` on the `td` here
            // (its CSS `width` stays "auto", exactly as an unstyled cell's does) - LineContentRightOf's
            // *first* guard already special-cases an explicit/percentage width, so an explicit `width: 80pt`
            // would pass this test even with the table-cell exclusion removed, proving nothing about the
            // fix this test exists to guard. Three equal-content columns, not two - a two-column table where
            // one column is much narrower than the other lets "table width minus the cell's own spacing"
            // coincidentally land close to the true (dominant) column's width even with the guard removed;
            // three columns each wanting a roughly equal, genuinely narrow third of the table's width make
            // the table's *whole* width a clearly distinguishable (~3x too wide) wrong answer.
            var longText = string.Join(" ", Enumerable.Range(0, 40).Select(i => $"word{i}"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                table { border-collapse: collapse; width: 300pt; }
                td { vertical-align: top; }
                </style></head><body>
                <table id='t'><tr>
                <td id='cell'>{{longText}}</td><td>{{longText}}</td><td>{{longText}}</td>
                </tr></table>
                </body></html>
                """);

            var cell = FindById(container.Root!, "cell")!;
            Assert.Equal(0, container.PageIndexOf(cell.Location.Y)); // page 0's per-page measure override is active
            Assert.Equal("auto", cell.Width); // the guard this test exercises only matters while Width is auto

            var cellWords = new List<CssRect>();
            CollectWords(cell, cellWords);
            Assert.NotEmpty(cellWords);

            var wrapWidth = cellWords.Max(w => w.Right) - cellWords.Min(w => w.Left);

            // Three equally-demanding columns split the table's constrained 300pt roughly evenly - the
            // first column's wrapped text must stay near its own ~100pt share, not balloon toward the
            // table's full 300pt (itself already far short of the page's much wider ~612pt per-page
            // content measure on this wide first page).
            Assert.True(wrapWidth < 150,
                $"a table cell's text wrap width ({wrapWidth}) must track its own narrow column, not the whole table's (or page's) width");
        }

        [Fact]
        public async Task Table_StartingOnALaterDifferentlyMarginedPage_UsesThatPagesOwnMeasure()
        {
            // #197: a table's own width/column-widths are ONE value for the whole table (its columns
            // are a single shared grid across every row, unlike multicol's genuinely independent
            // per-page column runs - #198), so the fix here is not "vary per page the table spans" but
            // "resolve against whichever page the table itself STARTS on" - which the old
            // `_tableBox.ContainingBlock.Size.Width` got wrong whenever that differs from the page the
            // table's containing block (body, which always itself starts on page 0) was measured
            // against. Forcing the table onto page 1 (page-break-before) while body/page 0 uses the
            // wider :first override isolates exactly that: body.Size.Width is fixed at page 0's 562pt,
            // but the table itself lives on page 1, whose own measure is the base 512pt.
            // CssLayoutEngineTable.GetAvailableTableWidth now resolves through
            // CssLayoutEngine.PageAwareWidthBasis at the table's own starting page (InlineSizeBlockTop)
            // instead of reading body's stale, page-0-cached Size.Width.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                p { margin: 0; }
                table { table-layout: fixed; width: 100%; border-spacing: 0; border-collapse: collapse; page-break-before: always; }
                td { padding: 0; margin: 0; }
                </style></head><body>
                <p>page zero filler</p>
                <table>
                    <tr><td id='c1'></td><td id='c2'></td></tr>
                </table>
                </body></html>
                """);

            var c1 = FindById(container.Root!, "c1")!;
            var c2 = FindById(container.Root!, "c2")!;

            Assert.Equal(1, container.PageIndexOf(c1.Location.Y)); // forced onto page 1 by page-break-before

            var columnWidth = c2.Location.X - c1.Location.X;

            // Page 1 is the base 512pt wide (612 - 50 - 50), so each of the table's 2 evenly-split
            // columns is 256pt - NOT page 0's wider 281pt (562 / 2), which body's own cached Size.Width
            // would incorrectly report for a table that doesn't actually live on page 0.
            Assert.Equal(256, columnWidth, 0.5);
        }

        [Fact]
        public async Task BodyMargin_RightInsetRespected_ContentDoesNotOverrunBodyMargin()
        {
            // The containing block (body) carries a non-zero margin, so a reflowed main-column paragraph
            // must stay inside body's margin box: its right edge lands one body-right-margin short of the
            // page-area edge, not flush against it. Regression guard for the containing-block right inset
            // (the horizontal mirror of ClientLeft) - without it the block overruns body's right margin.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 20pt; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>first page paragraph inside a margined body</p>
                </body></html>
                """);

            var p0 = FindById(container.Root!, "p0")!;
            Assert.Equal(0, container.PageIndexOf(p0.Location.Y));

            // Page 0's area right edge is 612 (margin-left:0, right stays base 50). body's 20pt margins
            // inset the content box on both sides: left = base 50 + 20 = 70, right = 612 - 20 = 592.
            const double pageAreaRight = BaseMargin + (SheetW - 0 - BaseMargin); // 612
            Assert.Equal(70, p0.Location.X, 0.5);
            Assert.Equal(pageAreaRight - 20, p0.ActualRight, 0.5);
            Assert.True(p0.ActualRight < container.PageContentRightOf(p0.Location.Y),
                "content must stay inside body's right margin, not reach the page-area edge");
        }

        [Fact]
        public async Task ManyParagraphsAcrossPages_ReflowConverges_EachBlockOwnPageMeasure()
        {
            // Many separate main-column paragraphs flow across several pages with a wide first page.
            // The initial pass lays every box out at page 0's (wide) measure; the reflow loop then
            // re-wraps the later-page paragraphs to the base measure, making them taller and shifting
            // the page boundaries - so the box->page assignment changes between the first and second
            // reflow iterations and the loop runs more than once before converging. Asserts the loop
            // reaches a stable state where every paragraph carries exactly its own page's measure.
            // Very narrow base pages (200pt L/R margins -> ~212pt measure) vs a full-bleed first page
            // (612pt): a paragraph is a couple of lines wide on page 0 but several lines tall at the base
            // measure, so re-wrapping the later pages materially shifts every page boundary - guaranteeing
            // the assignment changes between iterations and the loop runs more than once.
            var paragraphs = string.Concat(Enumerable.Range(1, 90).Select(i =>
                $"<p class='b'>Block {i}: lorem ipsum dolor sit amet consectetur adipiscing elit sed " +
                "do eiusmod tempor incididunt ut labore et dolore magna aliqua ut enim ad minim.</p>"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 200pt; }
                @page :first { margin: 0; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>{{paragraphs}}</body></html>
                """);

            var blocks = new List<CssBox>();
            CollectByClass(container.Root!, "b", blocks);
            Assert.True(blocks.Count > 3, "fixture should span several pages worth of blocks");

            // Every block ended up at exactly its own page's measure (the reflow converged, so no block
            // is left carrying a neighbouring page's width).
            foreach (var block in blocks)
                Assert.Equal(container.PageContentRightOf(block.Location.Y), block.ActualRight, 0.5);

            // Page 0 blocks are genuinely wider than later-page blocks.
            var pageZero = blocks.Where(b => container.PageIndexOf(b.Location.Y) == 0).ToList();
            var laterPages = blocks.Where(b => container.PageIndexOf(b.Location.Y) >= 1).ToList();
            Assert.NotEmpty(pageZero);
            Assert.NotEmpty(laterPages);
            Assert.True(pageZero.Max(b => b.ActualRight) > laterPages.Min(b => b.ActualRight),
                "the full-bleed first page should reflow wider than the base-margin later pages");
        }

        // A box's page is provisional while the reflow loop is settling which page each box is on, so §5.4's
        // two line minimums are decided in one final layout entered once it has settled
        // (HtmlContainerInt.PageWidthsSettled). Pinned here rather than only in the orphans tests, because
        // the failure mode belongs to *this* feature: a break moved from a provisional assignment feeds back
        // into the loop, and the fixture above stopped converging within its cap - leaving a paragraph
        // wrapped to a neighbouring page's measure, which is a worse defect than the orphan it avoided.
        [Fact]
        public async Task OrphansAndWidows_AreEnforced_WithoutDisturbingThePerPageMeasure()
        {
            var paragraphs = string.Concat(Enumerable.Range(1, 40).Select(i =>
                $"<p class='b'>Block {i}: lorem ipsum dolor sit amet consectetur adipiscing elit sed " +
                "do eiusmod tempor incididunt ut labore et dolore magna aliqua ut enim ad minim.</p>"));

            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 200pt; }
                @page :first { margin: 0; }
                body { margin: 0; }
                p { margin: 0; orphans: 4; widows: 4 }
                </style></head><body>{{paragraphs}}</body></html>
                """);

            var blocks = new List<CssBox>();
            CollectByClass(container.Root!, "b", blocks);

            Assert.True(container.UseVariableInlineMeasure, "fixture must exercise per-page reflow");

            // The invariant the reflow loop exists for still holds — the corrections are taken against the
            // settled assignment, so no block is left carrying a neighbouring page's width.
            foreach (var block in blocks)
                Assert.Equal(container.PageContentRightOf(block.Location.Y), block.ActualRight, 0.5);

            // And the minimums are genuinely enforced, which they were not while the gate was unconditional:
            // no block straddles a boundary keeping fewer than four lines on either side of it. Pages 1 and
            // up share one measure here, so every correction the fixture asks for is one that may be taken.
            foreach (var block in blocks.Where(b => container.PageIndexOf(b.Location.Y) >= 1))
            {
                var split = LinesEitherSideOfABoundary(container, block);

                if (split is not { } straddle) continue;

                Assert.True(straddle.Before >= 4 && straddle.After >= 4,
                    $"block at {block.Location.Y} splits {straddle.Before}/{straddle.After} across a boundary");
            }
        }

        // The per-line half of §5.4, which is what the gate actually costs a document: without it a widows
        // violation degrades to the retroactive whole-box push, which relocates the paragraph entirely and
        // leaves the foot of its page blank. With it, the pass that placed the first fragment is re-entered
        // with a smaller line budget and only the line it takes is moved. The geometry is the one
        // OrphansWidowsIntegrationTests calibrates against, on a small sheet.
        //
        // The `@page :left` rule restates the base margins rather than changing them: that is enough to put
        // the document on the per-page reflow path — which is what this test is about — while leaving every
        // page the same measure, so the correction is one that may be taken (see the test below).
        [Fact]
        public async Task WidowsViolation_MovesTheLinesItTakes_RatherThanTheWholeParagraph()
        {
            var container = await BuildSmallPageLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 0 8pt; }
                @page :left { margin-left: 8pt; margin-right: 8pt }
                p { margin: 0; padding: 0; width: 200pt; line-height: 20pt }
                </style></head><body style='margin:8pt'>
                <div style='height:30pt'></div>
                <p id='p'>L1<br>L2<br>L3<br>L4</p>
                </body></html>
                """);

            Assert.True(container.UseVariableInlineMeasure, "fixture must exercise per-page reflow");
            Assert.True(container.MeasureIsSharedBetween(0, 1));

            var paragraph = FindById(container.Root!, "p")!;
            var boundary = container.PageTopOf(1);

            // The paragraph stays where ordinary flow put it — the whole-box push, which is what this
            // document had before, would have moved it down to the boundary itself.
            Assert.Equal(38, paragraph.Location.Y, 1);

            var words = new List<CssRect>();
            CollectWords(paragraph, words);
            var tops = words.Select(w => w.Top).Distinct().OrderBy(t => t).ToList();

            // Its natural split was 3-before/1-after, violating the default widows: 2. One line moved.
            Assert.Equal(2, tops.Count(t => t < boundary));
            Assert.Equal(2, tops.Count(t => t >= boundary));
        }

        // The final layout keys every width off the settled assignment, so it cannot re-wrap what it
        // moves: a correction that moved a box onto a page of a *different* measure would leave it wrapped
        // for the page it left. That is declined instead — §4.3's own last rung, the constraint given up
        // rather than traded for a worse violation.
        //
        // This guard was written, argued away on the grounds that the retroactive whole-box push makes the
        // same trade already, and put back when CI failed on windows-latest with exactly the defect this
        // file's other tests exist to catch: a block wrapped to the full-bleed first page's 812pt measure,
        // sitting on a 412pt page.
        [Fact]
        public async Task ACorrectionOntoAPageOfADifferentMeasure_IsDeclined()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 200pt; }
                @page :first { margin: 0; }
                body { margin: 0; }
                p { margin: 0; orphans: 4; widows: 4; line-height: 20pt }
                </style></head><body>
                <div style='height:640pt'></div>
                <p id='p'>L1<br>L2<br>L3<br>L4<br>L5<br>L6<br>L7<br>L8</p>
                </body></html>
                """);

            Assert.True(container.UseVariableInlineMeasure, "fixture must exercise per-page reflow");

            // The full-bleed first page and the base-margin second do not share a measure, so no §5.4
            // correction may move content between them.
            Assert.False(container.MeasureIsSharedBetween(0, 1));

            // What that protects: the paragraph carries the measure of the page it is on, whichever page
            // that turns out to be. Left ungated, it was moved to page 1 while still wrapped for page 0.
            var paragraph = FindById(container.Root!, "p")!;
            Assert.Equal(container.PageContentRightOf(paragraph.Location.Y), paragraph.ActualRight, 0.5);
        }

        /// <summary>
        /// How <paramref name="block"/>'s lines fall either side of the page boundary it crosses, or null
        /// when it crosses none.
        /// </summary>
        private static (int Before, int After)? LinesEitherSideOfABoundary(HtmlContainerInt container, CssBox block)
        {
            var words = new List<CssRect>();
            CollectWords(block, words);

            var tops = words.Select(w => w.Top).Distinct().OrderBy(t => t).ToList();
            if (tops.Count == 0) return null;

            var boundary = container.PageTopOf(container.PageIndexOf(tops[0]) + 1);
            var after = tops.Count(t => t >= boundary);

            return after == 0 ? null : (tops.Count - after, after);
        }

        [Fact]
        public async Task ConstrainedBody_ExplicitWidth_DoesNotReflow()
        {
            // body has an explicit (fixed-length) width, so the main column no longer spans the page
            // area: per-page reflow is not applied and a child resolves against body's constrained width
            // instead of the wide page-0 measure. This is correct CSS behavior, not a gap - an author's
            // fixed-length width must not vary by page (unlike a PERCENTAGE width, which #199's fix now
            // resolves against the containing block's own page-aware measure - see
            // PercentageWidthBlock_InsideAutoWidthMainColumn_ReflowsToEachPagesOwnMeasure below).
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; width: 300pt; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>constrained-body paragraph</p>
                </body></html>
                """);

            var p0 = FindById(container.Root!, "p0")!;
            Assert.Equal(0, container.PageIndexOf(p0.Location.Y));
            // body is 300pt wide at the base left origin (50); p0 fills that, NOT the wide page-0 area.
            Assert.Equal(BaseMargin + 300, p0.ActualRight, 0.5);
            Assert.True(p0.ActualRight < container.PageContentRightOf(p0.Location.Y),
                "a constrained containing block must not be widened to the page area");
        }

        [Fact]
        public async Task DegenerateOverride_MarginsConsumeSheet_FallsBackToBaseMeasure()
        {
            // Left+right margins wider than the sheet would collapse the content box; PageContentRightOf
            // falls back to the base measure (mirror of the vertical band-height clamp) so content never
            // gets a zero/negative width.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 700pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>degenerate-margin paragraph</p>
                </body></html>
                """);

            var p0 = FindById(container.Root!, "p0")!;
            Assert.Equal(BaseRightEdge, container.PageContentRightOf(p0.Location.Y), 0.5);
            Assert.Equal(BaseRightEdge, p0.ActualRight, 0.5);
        }

        [Fact]
        public async Task DegenerateOverride_SheetSmallerThanBaseMargins_DiscardsMarginsEntirely()
        {
            // A named page's own resolved sheet (80pt wide) is smaller than even the BASE margins
            // (50+50=100) - PageContentRightOf's first fallback (base margins on the resolved sheet) is
            // ALSO degenerate here, so it falls all the way to zero margin on the slot's own resolved
            // sheet, mirroring PageGeometryTable.Compute's own second-level band-height clamp - reachable
            // only via a per-slot size override (issue #143's mixed page-size case), never by a margin-only
            // override.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page tiny { size: 80pt 400pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p>default page</p>
                <p id='p0' style='page: tiny'>tiny-sheet paragraph</p>
                </body></html>
                """);

            var p0 = FindById(container.Root!, "p0")!;
            Assert.Equal(1, container.PageIndexOf(p0.Location.Y));

            // Zero margin on the tiny page's own 80pt sheet: right edge = base MarginLeft(50) + 80 = 130.
            Assert.Equal(BaseMargin + 80, container.PageContentRightOf(p0.Location.Y), 0.5);
        }

        [Fact]
        public async Task UniformMargins_NoHorizontalOverride_IdenticalToBase()
        {
            // Only a top-margin per-page override — no left/right override — so the horizontal reflow path
            // stays dormant and content lays out at the historical single base measure.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-top: 80pt; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='p'>ordinary paragraph at the base measure</p>
                </body></html>
                """);

            Assert.False(container.UseVariableInlineMeasure);
            var p = FindById(container.Root!, "p")!;
            Assert.Equal(BaseRightEdge, p.ActualRight, 0.5);
            Assert.Equal(BaseRightEdge, container.PageContentRightOf(p.Location.Y), 0.5);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public async Task ReflowWidth_ScalesWithPixelsPerPoint(double ppp)
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body { margin: 0; }
                p { margin: 0; }
                </style></head><body>
                <p id='p0'>first page paragraph</p>
                <p id='p1' style='page-break-before: always'>second page paragraph</p>
                </body></html>
                """, ppp);

            var p0 = FindById(container.Root!, "p0")!;
            var p1 = FindById(container.Root!, "p1")!;

            // The wide first-page measure and the base measure both scale linearly with PixelsPerPoint —
            // no double-scaling (issue #113 discipline): the layout-space right edges are the point values
            // times ppp.
            const double firstPageRightEdge = BaseMargin + (SheetW - 0 - BaseMargin); // 612 at ppp 1
            Assert.Equal(firstPageRightEdge * ppp, p0.ActualRight, 0.5);
            Assert.Equal(BaseRightEdge * ppp, p1.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(p0.Location.Y), p0.ActualRight, 0.5);
            Assert.Equal(container.PageContentRightOf(p1.Location.Y), p1.ActualRight, 0.5);
        }

        // --- Harness (mirrors PdfGenerator.SetContent's geometry derivation; see
        //     PerPageGeometryLayoutIntegrationTests) ---

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter);
            // SetHtml runs CascadeApplyPageStyles: base @page margins land on the container (already
            // PixelsPerPoint-scaled) and PageRules are captured for per-page selection.
            await container.SetHtml(html, null);

            container.PageSize = new RSize(
                SheetW * ppp - container.MarginLeft - container.MarginRight,
                SheetH * ppp - container.MarginTop - container.MarginBottom);
            container.Location = new RPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new RSize(container.PageSize.Width, 0);

            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, ppp);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        /// <summary>
        /// The same harness on a 400x100 sheet — small enough to place a page boundary inside a
        /// four-line paragraph, which is what tells a per-line correction from a whole-box push.
        /// </summary>
        private static async Task<HtmlContainerInt> BuildSmallPageLayoutAsync(string html)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            container.PageSize = new RSize(
                400 - container.MarginLeft - container.MarginRight,
                100 - container.MarginTop - container.MarginBottom);
            container.Location = new RPoint(container.MarginLeft, container.MarginTop);
            container.MaxSize = new RSize(container.PageSize.Width, 0);

            var measure = XGraphics.CreateMeasureContext(
                new XSize(container.PageSize.Width, container.PageSize.Height), XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static void CollectWords(CssBox box, List<CssRect> words)
        {
            foreach (var word in box.Words)
                words.Add(word);

            foreach (var child in box.Boxes)
                CollectWords(child, words);
        }

        private static void CollectByClass(CssBox box, string className, List<CssBox> result)
        {
            var classAttr = box.HtmlTag?.TryGetAttribute("class", "");
            if (!string.IsNullOrEmpty(classAttr) &&
                classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className))
            {
                result.Add(box);
            }

            foreach (var child in box.Boxes)
                CollectByClass(child, className, result);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }

            return null;
        }
    }
}

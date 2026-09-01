using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Issue #876 (<c>Draft.InlineExtentDeltaWidth</c>): an auto-width main-column block's own outer
    /// border box must resize per fragment to match each page's own content-right edge, catching up to
    /// its text content, which already re-wraps per page (issue #143's mid-fragment block/text rewrap
    /// layer). Mirrors <see cref="FixedPositionPerPageSizeLayoutIntegrationTests"/>'s own harness and
    /// asserts directly on the fragment tree (<c>FragmentEmitter.ComputeInlineExtentDelta</c>'s effect)
    /// rather than parsing PDF content streams, per this repo's layout-testing convention.
    /// </summary>
    public class StraddlingBlockInlineExtentLayoutIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;
        private const double BaseMt = 60;
        private const double BaseMb = 60;
        private const double BaseMl = 50;
        private const double BaseMr = 50;
        private const double BaseContentWidth = SheetW - BaseMl - BaseMr; // 512

        [Fact]
        public async Task StraddlingAutoWidthDiv_ResizesPerFragment_OnAMarginOverrideDocument()
        {
            // The first page has no left margin (a wider content area than every later page) - a div tall
            // enough to straddle onto page 2 must show its own page's own width on each fragment, not the
            // first page's width repeated onto the second.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body, div, p { margin: 0; }
                #frame { background: rgb(10,20,200); }
                p { height: 900pt; }
                </style></head><body>
                <div id="frame"><p>filler</p></div>
                </body></html>
                """);

            var frame = FindById(container.Root!, "frame")!;
            var tree = container.FragmentTree!;
            Assert.Equal(2, tree.Fragmentainers.Count);

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, frame);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, frame);
            Assert.NotNull(page0Fragment);
            Assert.NotNull(page1Fragment);

            // Page 0 (first page, no left margin): content spans the full 612pt sheet minus the 50pt
            // right margin = 562pt. Page 1 (base margins both sides): 512pt.
            Assert.Equal(SheetW - BaseMr, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(BaseContentWidth, page1Fragment!.WholeBoxRect.Width, 0.5);
            Assert.NotEqual(page0Fragment.WholeBoxRect.Width, page1Fragment.WholeBoxRect.Width);

            // The left edge never moves - only the content-right edge does.
            Assert.Equal(page0Fragment.WholeBoxRect.X, page1Fragment.WholeBoxRect.X, 0.5);
        }

        [Fact]
        public async Task StraddlingDiv_MixedPageSizeDocument_ResizesToEachPagesOwnMeasure()
        {
            // A per-page `size` override (not just a margin override) must drive the same per-fragment
            // resize - the mixed page orientation/size work's own headline scenario. `:first` gives page 0
            // a genuinely different physical sheet size than every later (base-rule) page.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                #frame { background: rgb(10,20,200); }
                p { height: 900pt; }
                </style></head><body>
                <div id="frame"><p>filler</p></div>
                </body></html>
                """);

            var frame = FindById(container.Root!, "frame")!;
            var tree = container.FragmentTree!;
            Assert.Equal(2, tree.Fragmentainers.Count);

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, frame);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, frame);
            Assert.NotNull(page0Fragment);
            Assert.NotNull(page1Fragment);

            const double firstPageContentWidth = 800 - 20 - 20;
            Assert.Equal(firstPageContentWidth, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(BaseContentWidth, page1Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task StraddlingDiv_ExplicitWidth_StaysUnaffected()
        {
            // An explicit-length width is already page-independent (LineContentRightOf's own guard) - its
            // own outer frame must not gain a delta either, matching the content that already ignores the
            // per-page measure.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body, div, p { margin: 0; }
                #frame { width: 300pt; background: rgb(10,20,200); }
                p { height: 900pt; }
                </style></head><body>
                <div id="frame"><p>filler</p></div>
                </body></html>
                """);

            var frame = FindById(container.Root!, "frame")!;
            var tree = container.FragmentTree!;

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, frame);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, frame);

            Assert.Equal(300, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(300, page1Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task NestedDivInsideAnotherDiv_StaysUnaffected_MatchingIssue143sOwnScope()
        {
            // IsUnconstrainedMainColumn requires every level up to root to be root/html/body - a div whose
            // containing block is another (non-main-column) div is out of scope for per-page text rewrap
            // too (LineContentRightOf falls back to ClientRight the same way), so its own frame must agree
            // and stay pinned rather than opening a new gap between the two.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body, div, p { margin: 0; }
                #outer { background: rgb(5,5,5); }
                #inner { background: rgb(10,20,200); }
                p { height: 900pt; }
                </style></head><body>
                <div id="outer"><div id="inner"><p>filler</p></div></div>
                </body></html>
                """);

            var inner = FindById(container.Root!, "inner")!;
            var tree = container.FragmentTree!;

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, inner);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, inner);
            Assert.NotNull(page0Fragment);
            Assert.NotNull(page1Fragment);

            Assert.Equal(page0Fragment!.WholeBoxRect.Width, page1Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task StraddlingDiv_ClampedByMaxWidth_ResolvesTheClampedValueOnBothPages()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body, div, p { margin: 0; }
                #frame { max-width: 100pt; background: rgb(10,20,200); }
                p { height: 900pt; }
                </style></head><body>
                <div id="frame"><p>filler</p></div>
                </body></html>
                """);

            var frame = FindById(container.Root!, "frame")!;
            var tree = container.FragmentTree!;

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, frame);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, frame);

            Assert.Equal(100, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(100, page1Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task StraddlingDiv_ClampedByMinWidth_ResolvesTheClampedValueOnBothPages()
        {
            // The min-width counterpart of the max-width clamp test above - min-width: 600pt exceeds both
            // pages' own genuinely different content-right edges, so both must report the clamped floor.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body, div, p { margin: 0; }
                #frame { min-width: 600pt; background: rgb(10,20,200); }
                p { height: 900pt; }
                </style></head><body>
                <div id="frame"><p>filler</p></div>
                </body></html>
                """);

            var frame = FindById(container.Root!, "frame")!;
            var tree = container.FragmentTree!;

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, frame);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, frame);

            Assert.Equal(600, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(600, page1Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task StraddlingTableDirectlyUnderBody_StaysUnaffected()
        {
            // A table's own width comes from CssLayoutEngineTable's column-width algorithm, never from
            // GetBoxWidth's auto-width branch (CssBox.ResolveOwnInlineSize skips GetBoxWidth entirely for
            // display: table) - even though a table sitting directly under <body> otherwise satisfies
            // every other ComputeInlineExtentDelta guard (auto width, in-flow, no own words,
            // IsUnconstrainedMainColumn(body) is true), it must not get a bogus delta applied to a width
            // that was never derived from the formula the delta reproduces.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body, table, tr, td { margin: 0; }
                table { border-collapse: collapse; }
                td { height: 100pt; }
                </style></head><body>
                <table id="tbl">
                <tr><td>row1</td></tr>
                <tr><td>row2</td></tr>
                <tr><td>row3</td></tr>
                <tr><td>row4</td></tr>
                <tr><td>row5</td></tr>
                <tr><td>row6</td></tr>
                <tr><td>row7</td></tr>
                <tr><td>row8</td></tr>
                </table>
                </body></html>
                """);

            var table = FindById(container.Root!, "tbl")!;
            var tree = container.FragmentTree!;
            Assert.Equal(2, tree.Fragmentainers.Count);

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, table);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, table);
            Assert.NotNull(page0Fragment);
            Assert.NotNull(page1Fragment);

            Assert.Equal(page0Fragment!.WholeBoxRect.Width, page1Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task StraddlingFlexContainerDirectlyUnderBody_StaysUnaffected()
        {
            // The flex/grid counterpart of the table test above - a flex container's own width comes from
            // CssLayoutEngineFlex, not GetBoxWidth's auto-width branch, even though it sits directly under
            // <body> and would otherwise satisfy every other guard.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body, div { margin: 0; }
                #flexbox { display: flex; flex-direction: column; }
                .item { height: 400pt; }
                </style></head><body>
                <div id="flexbox">
                <div class="item">one</div>
                <div class="item">two</div>
                <div class="item">three</div>
                </div>
                </body></html>
                """);

            var flexBox = FindById(container.Root!, "flexbox")!;
            var tree = container.FragmentTree!;
            Assert.Equal(2, tree.Fragmentainers.Count);

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, flexBox);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, flexBox);
            Assert.NotNull(page0Fragment);
            Assert.NotNull(page1Fragment);

            Assert.Equal(page0Fragment!.WholeBoxRect.Width, page1Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task FloatedBox_StaysUnaffected()
        {
            // A float is sized/positioned against its own placement, not its containing block's measure
            // (FillsContainingBlockWidth's own exclusion) - ComputeInlineExtentDelta's IsOutOfFlow guard
            // must agree.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin-left: 0; }
                body, div, p { margin: 0; }
                #frame { float: left; width: 100pt; height: 900pt; background: rgb(10,20,200); }
                </style></head><body>
                <div id="frame"></div>
                <p style="height: 900pt">filler</p>
                </body></html>
                """);

            var frame = FindById(container.Root!, "frame")!;
            var tree = container.FragmentTree!;

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, frame);
            Assert.NotNull(page0Fragment);
            Assert.Equal(100, page0Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task StraddlingDiv_NoMarginOrSizeOverridesInDocument_StaysIdenticalAcrossPages()
        {
            // Regression guard: UseVariableInlineMeasure is false for a uniform document, so
            // ComputeInlineExtentDelta short-circuits to 0 and every page shows the same rect -
            // byte-identical to pre-#876 behavior.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                body, div, p { margin: 0; }
                #frame { background: rgb(10,20,200); }
                p { height: 900pt; }
                </style></head><body>
                <div id="frame"><p>filler</p></div>
                </body></html>
                """);

            var frame = FindById(container.Root!, "frame")!;
            var tree = container.FragmentTree!;
            Assert.Equal(2, tree.Fragmentainers.Count);

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, frame);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, frame);

            Assert.Equal(page0Fragment!.WholeBoxRect.Width, page1Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(BaseContentWidth, page0Fragment.WholeBoxRect.Width, 0.5);
        }

        private static BoxFragment? FindBoxFragment(BoxFragment root, CssBox target)
        {
            if (ReferenceEquals(root.Box, target)) return root;

            foreach (var child in root.Children)
            {
                if (FindBoxFragment(child, target) is { } found) return found;
            }

            return null;
        }

        private static async Task<HtmlContainerInt> BuildLayoutAsync(string html, double ppp = 1.0)
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = ppp };
            var container = new HtmlContainerInt(adapter);
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

        private static CssBox? FindById(CssBox box, string id)
        {
            if (string.Equals(box.HtmlTag?.TryGetAttribute("id", ""), id, System.StringComparison.OrdinalIgnoreCase))
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

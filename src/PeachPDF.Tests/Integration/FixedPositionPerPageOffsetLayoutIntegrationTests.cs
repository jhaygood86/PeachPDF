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
    /// Layer E of mixed page orientation/size support: a <c>position: fixed</c> box's percentage
    /// <c>left</c>/<c>top</c> must resolve against EACH page's own area (CSS2.1 §10.1 / CSS Position 3),
    /// not the single value <c>CssBox.CommitBlockChildOffset</c> resolves once, globally. That area is
    /// also the box's containing block, so every coordinate below is measured from the page's own
    /// CONTENT corner, not the sheet corner - an offset of 0 sits on the margin, exactly where a browser
    /// printing the same document puts it. Asserts
    /// directly on the fragment tree (<c>FragmentEmitter.ComputeFixedPageOffset</c>'s effect),
    /// following the repo's layout-harness convention rather than parsing PDF content streams, since
    /// this is specifically about per-page fragment geometry.
    /// </summary>
    public class FixedPositionPerPageOffsetLayoutIntegrationTests
    {
        private const double SheetW = 612;
        private const double SheetH = 792;
        private const double BaseMt = 60;
        private const double BaseMb = 60;
        private const double BaseMl = 50;
        private const double BaseMr = 50;
        private const double BaseContentWidth = SheetW - BaseMl - BaseMr; // 512
        private const double BaseContentHeight = SheetH - BaseMt - BaseMb; // 672

        [Fact]
        public async Task FixedPercentOffset_ResolvesToEachPagesOwnArea_OnAMixedSizeDocument()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 50%; top: 50%; width: 10pt; height: 10pt; }
                </style></head><body>
                <div class="fixedBox" id="fixed"></div>
                <p>page zero</p>
                <div style="page: landscape; height: 50pt">landscape section</div>
                </body></html>
                """);

            var fixedBox = FindById(container.Root!, "fixed")!;
            var tree = container.FragmentTree;
            Assert.NotNull(tree);
            Assert.Equal(2, tree!.Fragmentainers.Count);

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, fixedBox);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, fixedBox);
            Assert.NotNull(page0Fragment);
            Assert.NotNull(page1Fragment);

            // Page 0 (base, 512x672 content area at 50pt/60pt): 50% => (50 + 256, 60 + 336).
            Assert.Equal(BaseMl + BaseContentWidth / 2, page0Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(BaseMt + BaseContentHeight / 2, page0Fragment.WholeBoxRect.Y, 0.5);

            // Page 1 (named "landscape", 800x500 sheet, 20pt margins => 760x460 content area):
            // 50% => (20 + 380, 20 + 230) - genuinely different from page 0, proving the offset is
            // resolved per page rather than shared from the single global Location.
            const double landscapeMargin = 20;
            const double landscapeContentWidth = 800 - 20 - 20;
            const double landscapeContentHeight = 500 - 20 - 20;
            Assert.Equal(landscapeMargin + landscapeContentWidth / 2, page1Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(landscapeMargin + landscapeContentHeight / 2, page1Fragment.WholeBoxRect.Y, 0.5);

            Assert.NotEqual(page0Fragment.WholeBoxRect.X, page1Fragment.WholeBoxRect.X);
            Assert.NotEqual(page0Fragment.WholeBoxRect.Y, page1Fragment.WholeBoxRect.Y);
        }

        [Fact]
        public async Task FixedAbsoluteOffset_SitsAtTheSameOffsetIntoEveryPagesOwnArea()
        {
            // An absolute-length offset doesn't depend on the percentage basis at all - but it IS
            // measured from its containing block's corner, and that is each page's own content corner
            // (CSS 2.1 §10.1). So on a document whose two pages have different margins, `left: 80pt`
            // lands 80pt into each page's own area, at two different distances from the sheet edge.
            // While a fixed box was anchored at the sheet corner instead, this test asserted the two
            // pages' rectangles were identical; that identity was the bug, not the guarantee.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 80pt; top: 80pt; width: 10pt; height: 10pt; }
                </style></head><body>
                <div class="fixedBox" id="fixed"></div>
                <p>page zero</p>
                <div style="page: landscape; height: 50pt">landscape section</div>
                </body></html>
                """);

            var fixedBox = FindById(container.Root!, "fixed")!;
            var tree = container.FragmentTree!;

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, fixedBox);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, fixedBox);

            // Base page: 50pt/60pt margins + 80pt. Landscape page: 20pt margins + 80pt.
            Assert.Equal(BaseMl + 80, page0Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(BaseMt + 80, page0Fragment.WholeBoxRect.Y, 0.5);
            Assert.Equal(20 + 80, page1Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(20 + 80, page1Fragment.WholeBoxRect.Y, 0.5);
        }

        [Fact]
        public async Task FixedPercentOffset_ResolvesToEachPagesOwnArea_OnAMarginOnlyOverrideDocument()
        {
            // Issue #146: a margin-only override (no `size` override anywhere in the document) must
            // also drive ComputeFixedPageOffset - its gate previously only checked HasSizeOverrides,
            // so a plain `@page :first { margin: 0 }` document (no named page, no size override at
            // all) wrongly left every page sharing page 0's single global offset.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page :first { margin: 0; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 25%; top: 25%; width: 10pt; height: 10pt; }
                </style></head><body>
                <div class="fixedBox" id="fixed"></div>
                <p>page zero</p>
                <p style="page-break-before: always">page one</p>
                </body></html>
                """);

            var fixedBox = FindById(container.Root!, "fixed")!;
            var tree = container.FragmentTree!;
            Assert.Equal(2, tree.Fragmentainers.Count);

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, fixedBox);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, fixedBox);
            Assert.NotNull(page0Fragment);
            Assert.NotNull(page1Fragment);

            // 25%, not 50%: with the containing block now being each page's own area, a 50% offset
            // lands on the centre of the content area - which, for symmetric margins, is the centre of
            // the sheet on every page, so the two pages would agree by coincidence and the test would
            // prove nothing. A quarter of the way in does not have that symmetry.
            //
            // Page 0 (`:first`, margin 0, so the full 612x792 sheet IS its content area): 25% => (153, 198).
            Assert.Equal(SheetW * 0.25, page0Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(SheetH * 0.25, page0Fragment.WholeBoxRect.Y, 0.5);

            // Page 1 (base margins, 512x672 content area at 50pt/60pt): 25% => (50 + 128, 60 + 168) -
            // genuinely different.
            Assert.Equal(BaseMl + BaseContentWidth * 0.25, page1Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(BaseMt + BaseContentHeight * 0.25, page1Fragment.WholeBoxRect.Y, 0.5);

            Assert.NotEqual(page0Fragment.WholeBoxRect.X, page1Fragment.WholeBoxRect.X);
            Assert.NotEqual(page0Fragment.WholeBoxRect.Y, page1Fragment.WholeBoxRect.Y);
        }

        [Fact]
        public async Task FixedPercentOffset_NoSizeOverridesInDocument_StaysIdenticalAcrossPages()
        {
            // Regression guard: HasSizeOverrides is false for a uniform document, so
            // ComputeFixedPageOffset short-circuits to (0, 0) and every page shows the same rect -
            // byte-identical to pre-Layer-E behavior.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 50%; top: 50%; width: 10pt; height: 10pt; }
                </style></head><body>
                <div class="fixedBox" id="fixed"></div>
                <p>page zero</p>
                <p style="page-break-before: always">page one</p>
                </body></html>
                """);

            var fixedBox = FindById(container.Root!, "fixed")!;
            var tree = container.FragmentTree!;
            Assert.Equal(2, tree.Fragmentainers.Count);

            var page0Fragment = FindBoxFragment(tree.Fragmentainers[0].Root, fixedBox);
            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, fixedBox);

            Assert.Equal(page0Fragment!.WholeBoxRect.X, page1Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(page0Fragment.WholeBoxRect.Y, page1Fragment.WholeBoxRect.Y, 0.5);
            Assert.Equal(BaseMl + BaseContentWidth / 2, page0Fragment.WholeBoxRect.X, 0.5);
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

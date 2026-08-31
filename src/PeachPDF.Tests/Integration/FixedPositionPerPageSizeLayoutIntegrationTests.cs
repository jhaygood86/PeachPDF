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
    /// Layer K Tier 1 of mixed page orientation/size support: a <c>position: fixed</c> box's percentage
    /// <c>width</c>/<c>height</c> must resolve against EACH page's own area (CSS2.1 §10.1 / CSS Position 3),
    /// not the single value <c>CssLayoutEngine.GetBoxWidth</c>/<c>GetBoxHeight</c> resolve once, globally.
    /// Mirrors <see cref="FixedPositionPerPageOffsetLayoutIntegrationTests"/> (Layer E) exactly, for size
    /// instead of position, asserting directly on the fragment tree
    /// (<c>FragmentEmitter.ComputeFixedSizeOverride</c>'s effect) rather than parsing PDF content streams.
    /// Tier 2 (re-flowing the fixed box's own content to the new size) is explicitly out of scope - see
    /// the accepted-gap note - so these fixtures use replaced-content-shaped (childless) boxes, exactly
    /// the case Tier 1 is scoped to.
    /// </summary>
    public class FixedPositionPerPageSizeLayoutIntegrationTests
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
        public async Task FixedPercentSize_ResolvesToEachPagesOwnArea_OnAMixedSizeDocument()
        {
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 0; top: 0; width: 50%; height: 50%; }
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

            // Page 0 (base, 512x672 content area): 50% => 256x336.
            Assert.Equal(BaseContentWidth / 2, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(BaseContentHeight / 2, page0Fragment.WholeBoxRect.Height, 0.5);

            // Page 1 (named "landscape", 800x500 sheet, 20pt margins => 760x460 content area):
            // 50% => 380x230 - genuinely different from page 0, proving the size is resolved per
            // page rather than shared from the single global Size.
            const double landscapeContentWidth = 800 - 20 - 20;
            const double landscapeContentHeight = 500 - 20 - 20;
            Assert.Equal(landscapeContentWidth / 2, page1Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(landscapeContentHeight / 2, page1Fragment.WholeBoxRect.Height, 0.5);

            Assert.NotEqual(page0Fragment.WholeBoxRect.Width, page1Fragment.WholeBoxRect.Width);
            Assert.NotEqual(page0Fragment.WholeBoxRect.Height, page1Fragment.WholeBoxRect.Height);
        }

        [Fact]
        public async Task FixedAbsoluteSize_StaysTheSameOnEveryPage_RegardlessOfSizeOverrides()
        {
            // Regression guard: an absolute-length width/height doesn't depend on the basis at all, so
            // ComputeFixedSizeOverride must yield a zero delta here.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 80pt; top: 80pt; width: 40pt; height: 30pt; }
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

            Assert.Equal(40, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(30, page0Fragment.WholeBoxRect.Height, 0.5);
            Assert.Equal(page0Fragment.WholeBoxRect.Width, page1Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(page0Fragment.WholeBoxRect.Height, page1Fragment.WholeBoxRect.Height, 0.5);
        }

        [Fact]
        public async Task FixedPercentWidthOnly_HeightStaysAuto_OnlyWidthResizes()
        {
            // A percentage width with auto height (content-derived, here from a fixed 20pt padding so the
            // box has a real, non-degenerate extent to assert on) - only the dimension that is actually a
            // percentage should carry a delta.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 80pt; top: 80pt; width: 25%; padding: 20pt; box-sizing: border-box; }
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

            const double landscapeContentWidth = 800 - 20 - 20;
            Assert.Equal(BaseContentWidth / 4, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(landscapeContentWidth / 4, page1Fragment!.WholeBoxRect.Width, 0.5);
            Assert.NotEqual(page0Fragment.WholeBoxRect.Width, page1Fragment.WholeBoxRect.Width);

            // The (auto, content-derived) height is unaffected by the size override - both pages
            // report the box's single, globally-resolved height.
            Assert.Equal(page0Fragment.WholeBoxRect.Height, page1Fragment.WholeBoxRect.Height, 0.5);
        }

        [Fact]
        public async Task FixedPercentHeight_ShrunkOnALandscapePage_OverflowingChildContentDoesNotRegrowTheFrame()
        {
            // A fixed box's own content is laid out once and is NOT re-flowed to a per-page resize (the
            // whole point of Tier 1's scope) - so when a percentage height resolves SMALLER on a page whose
            // area is narrower than the one the content was measured against, the content must overflow
            // visibly rather than silently re-growing the box's own frame back out
            // (FragmentEmitter.ExtentOf's BoundsEndAtItsContent guard). Many short wrapped lines in a narrow
            // box give the content a real, measurable height that exceeds the landscape page's own 230pt
            // band (50% of its 460pt content height) - the base page's 336pt band (50% of 672pt) comfortably
            // fits it, so only the landscape page's frame is actually being tested for the regrow.
            var words = string.Join(" ", System.Linq.Enumerable.Range(0, 60).Select(i => $"word{i}"));
            var container = await BuildLayoutAsync($$"""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 80pt; top: 80pt; width: 60pt; height: 50%; overflow: visible; }
                </style></head><body>
                <div class="fixedBox" id="fixed">{{words}}</div>
                <p>page zero</p>
                <div style="page: landscape; height: 50pt">landscape section</div>
                </body></html>
                """);

            var fixedBox = FindById(container.Root!, "fixed")!;
            var tree = container.FragmentTree!;

            var page1Fragment = FindBoxFragment(tree.Fragmentainers[1].Root, fixedBox);
            Assert.NotNull(page1Fragment);

            const double landscapeContentHeight = 500 - 20 - 20;
            Assert.Equal(landscapeContentHeight / 2, page1Fragment!.WholeBoxRect.Height, 0.5);
        }

        [Fact]
        public async Task FixedPercentWidth_ClampedByMaxWidth_ResolvesTheClampedValueOnEveryPage()
        {
            // The same min/max-width clamp CssLayoutEngine.GetBoxWidth itself applies must also apply to
            // the per-page override - an 80% width would resolve to a much larger value on the landscape
            // page's own (760pt) content area, but max-width: 100pt caps it there exactly as it already
            // caps the base page's own (512pt-based) resolution.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 80pt; top: 80pt; width: 80%; height: 30pt; max-width: 100pt; }
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

            Assert.Equal(100, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(100, page1Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task FixedPercentWidth_ClampedByMinWidth_ResolvesTheClampedValueOnEveryPage()
        {
            // The min-width counterpart of the max-width clamp test above - a 5% width would resolve
            // narrower than min-width on both pages, so both must report the clamped floor rather than
            // their own genuinely different (but both-too-narrow) percentage results.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 80pt; top: 80pt; width: 5%; height: 30pt; min-width: 200pt; }
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

            Assert.Equal(200, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(200, page1Fragment!.WholeBoxRect.Width, 0.5);
        }

        [Fact]
        public async Task FixedPercentHeight_ClampedByMaxHeight_ResolvesTheClampedValueOnEveryPage()
        {
            // The height counterpart of the max-width clamp test above.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 80pt; top: 80pt; width: 30pt; height: 80%; max-height: 100pt; }
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

            Assert.Equal(100, page0Fragment!.WholeBoxRect.Height, 0.5);
            Assert.Equal(100, page1Fragment!.WholeBoxRect.Height, 0.5);
        }

        [Fact]
        public async Task FixedCalcWidth_WithAPercentageLeaf_ResolvesPerPageLikeAPlainPercentage()
        {
            // ComputeFixedSizeOverride's gate is "not auto, not empty" (mirroring GetBoxWidth), not
            // narrowed to values textually ending in '%' - a calc() expression containing a percentage
            // leaf must also be recomputed per page, not left stuck at its single global value.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                @page landscape { size: 800pt 500pt; margin: 20pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 80pt; top: 80pt; width: calc(50% + 10pt); height: 30pt; }
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

            const double landscapeContentWidth = 800 - 20 - 20;
            Assert.Equal(BaseContentWidth / 2 + 10, page0Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(landscapeContentWidth / 2 + 10, page1Fragment!.WholeBoxRect.Width, 0.5);
            Assert.NotEqual(page0Fragment.WholeBoxRect.Width, page1Fragment.WholeBoxRect.Width);
        }

        [Fact]
        public async Task FixedPercentSize_NoSizeOverridesInDocument_StaysIdenticalAcrossPages()
        {
            // Regression guard: HasSizeOverrides is false for a uniform document, so
            // ComputeFixedSizeOverride short-circuits to (0, 0) and every page shows the same rect -
            // byte-identical to pre-Layer-K behavior.
            var container = await BuildLayoutAsync("""
                <!DOCTYPE html><html><head><style>
                @page { margin: 60pt 50pt; }
                body, div, p { margin: 0; }
                .fixedBox { position: fixed; left: 0; top: 0; width: 50%; height: 50%; }
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

            Assert.Equal(page0Fragment!.WholeBoxRect.Width, page1Fragment!.WholeBoxRect.Width, 0.5);
            Assert.Equal(page0Fragment.WholeBoxRect.Height, page1Fragment.WholeBoxRect.Height, 0.5);
            Assert.Equal(BaseContentWidth / 2, page0Fragment.WholeBoxRect.Width, 0.5);
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

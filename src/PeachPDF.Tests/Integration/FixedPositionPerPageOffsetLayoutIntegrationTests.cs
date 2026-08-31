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
    /// not the single value <c>CssBox.CommitBlockChildOffset</c> resolves once, globally. Asserts
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

            // Page 0 (base, 512x672 content area): 50% => (256, 336).
            Assert.Equal(BaseContentWidth / 2, page0Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(BaseContentHeight / 2, page0Fragment.WholeBoxRect.Y, 0.5);

            // Page 1 (named "landscape", 800x500 sheet, 20pt margins => 760x460 content area):
            // 50% => (380, 230) - genuinely different from page 0, proving the offset is resolved
            // per page rather than shared from the single global Location.
            const double landscapeContentWidth = 800 - 20 - 20;
            const double landscapeContentHeight = 500 - 20 - 20;
            Assert.Equal(landscapeContentWidth / 2, page1Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(landscapeContentHeight / 2, page1Fragment.WholeBoxRect.Y, 0.5);

            Assert.NotEqual(page0Fragment.WholeBoxRect.X, page1Fragment.WholeBoxRect.X);
            Assert.NotEqual(page0Fragment.WholeBoxRect.Y, page1Fragment.WholeBoxRect.Y);
        }

        [Fact]
        public async Task FixedAbsoluteOffset_StaysTheSameDeltaOnEveryPage_RegardlessOfSizeOverrides()
        {
            // Regression guard: an absolute-length offset doesn't depend on the basis at all, so
            // ComputeFixedPageOffset must yield a zero delta here - the existing per-page paint-time
            // margin translate (unrelated to this layer) is what already positions it correctly.
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

            Assert.Equal(page0Fragment!.WholeBoxRect.X, page1Fragment!.WholeBoxRect.X, 0.5);
            Assert.Equal(page0Fragment.WholeBoxRect.Y, page1Fragment.WholeBoxRect.Y, 0.5);
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
            Assert.Equal(BaseContentWidth / 2, page0Fragment.WholeBoxRect.X, 0.5);
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

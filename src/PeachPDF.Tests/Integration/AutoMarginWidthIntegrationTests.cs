using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// CSS 2.1 §10.3.3: an in-flow, non-replaced block with <c>width: auto</c> and
    /// <c>margin: … auto</c> <b>fills</b> its containing block (the auto margins resolve to 0);
    /// centering via auto margins only applies when the used width is <b>definite</b> — an explicit
    /// <c>width</c>, or an <c>auto</c> width clamped below the fill width by <c>max-width</c>.
    ///
    /// Regression coverage for the "Sunny Farm" invoice bug: a <c>max-width</c>d
    /// <c>margin: 0 auto</c> wrapper on a page narrower than its <c>max-width</c> collapsed to
    /// shrink-to-fit and re-centered (dragging its <c>width: 100%</c> table off the page) instead of
    /// filling the page.
    /// </summary>
    public class AutoMarginWidthIntegrationTests
    {
        // Page is 400pt wide with zero page margins (see BuildAndLayout), so the body's content box
        // spans x:0..400 and a filling child should be 400pt wide at x=0.

        [Fact]
        public async Task AutoWidth_MarginZeroAuto_FillsContainingBlock()
        {
            // The invoice's `.content { max-width: 960px; margin: 0 auto }` shape: max-width (720pt)
            // exceeds the 400pt page, so it does not bind — the box must fill, auto margins = 0.
            var html = Wrap("<div id='c' style='max-width:960px; margin:0 auto'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "c")!;

            Assert.InRange(box.Location.X, -0.5, 0.5);
            Assert.InRange(box.ActualWidth, 399.5, 400.5);
        }

        [Fact]
        public async Task AutoWidth_MarginZeroAuto_WithPadding_FillsAndOffsetsByBorderPadding()
        {
            // Padding must not turn the fill into a shrink-wrap: the border box still spans the full
            // containing block; only the content box is inset by the padding.
            var html = Wrap("<div id='c' style='max-width:960px; margin:0 auto; padding:0 25px'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "c")!;

            Assert.InRange(box.Location.X, -0.5, 0.5);
            Assert.InRange(box.ActualWidth, 399.5, 400.5); // border-box width fills
        }

        [Fact]
        public async Task WidthPercent100_Child_FillsInsideAutoMarginWrapper()
        {
            // The exact cascade the invoice broke: a `width:100%` table/child inside a `margin:0 auto`
            // wrapper must resolve 100% against the now-filled wrapper, not a collapsed one.
            var html = Wrap(
                "<div id='wrap' style='max-width:960px; margin:0 auto'>"
                + "<div id='inner' style='width:100%'>x</div></div>");
            var (root, _) = await BuildAndLayout(html);
            var inner = FindById(root, "inner")!;

            Assert.InRange(inner.Location.X, -0.5, 0.5);
            Assert.InRange(inner.ActualWidth, 399.5, 400.5);
        }

        [Fact]
        public async Task DefiniteWidth_MarginZeroAuto_Centers()
        {
            // A definite width still centers via auto margins (unchanged behavior): (400 - 100)/2 = 150.
            var html = Wrap("<div id='c' style='width:100pt; margin:0 auto'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "c")!;

            Assert.InRange(box.Location.X, 149.5, 150.5);
            Assert.InRange(box.ActualWidth, 99.5, 100.5);
        }

        [Fact]
        public async Task AutoWidth_MaxWidthBinds_MarginZeroAuto_Centers()
        {
            // When max-width is smaller than the page it clamps the used width, which becomes definite
            // and centers: width = 200pt, (400 - 200)/2 = 100pt each side.
            var html = Wrap("<div id='c' style='max-width:200pt; margin:0 auto'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "c")!;

            Assert.InRange(box.ActualWidth, 199.5, 200.5);
            Assert.InRange(box.Location.X, 99.5, 100.5);
        }

        [Fact]
        public async Task AutoWidth_MinWidthReWidensPastMaxWidth_CentersOnMinWidth()
        {
            // Degenerate min-width > max-width: min wins (CSS 2.1 §10.4), so the used width is 300pt,
            // not the clamped 200pt — the auto margins must center on 300pt: (400 - 300)/2 = 50pt.
            var html = Wrap("<div id='c' style='max-width:200pt; min-width:300pt; margin:0 auto'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "c")!;

            Assert.InRange(box.ActualWidth, 299.5, 300.5);
            Assert.InRange(box.Location.X, 49.5, 50.5);
        }

        [Fact]
        public async Task AutoWidth_MinWidthExceedsContainingBlock_MarginsCollapseToZero()
        {
            // A min-width wider than the containing block overflows: no free space, auto margins 0,
            // box pinned at the containing-block left edge.
            var html = Wrap("<div id='c' style='min-width:600pt; margin:0 auto'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "c")!;

            Assert.InRange(box.ActualWidth, 599.5, 600.5);
            Assert.InRange(box.Location.X, -0.5, 0.5);
        }

        [Fact]
        public async Task AutoWidth_MarginZero_LeftAligns_Fills()
        {
            // Sanity: plain margin:0 (no auto) fills and left-aligns, matching the auto-margin fill.
            var html = Wrap("<div id='c' style='max-width:960px; margin:0'>x</div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "c")!;

            Assert.InRange(box.Location.X, -0.5, 0.5);
            Assert.InRange(box.ActualWidth, 399.5, 400.5);
        }

        // Zero the body's UA-default 8px margin (as the invoice does with `body,html { margin:0 }`)
        // so the body content box is exactly the 400pt page and expected values read literally.
        [Fact]
        public async Task Table_MarginZeroAuto_BothMarginGetters_CenterAgainstResolvedWidth()
        {
            // A `margin: 0 auto` table is centered by CssLayoutEngineTable, which passes the resolved
            // table width back into GetActualMarginLeft/Right explicitly (boxWidth). Both getters must
            // then split the free space against that width (containingWidth - boxWidth) / 2.
            var html = Wrap("<div id='wrap' style='width:200pt'>"
                + "<table id='t' style='width:100pt; margin:0 auto'><tr><td>A</td></tr></table></div>");
            var (root, _) = await BuildAndLayout(html);
            var wrap = FindById(root, "wrap")!;
            var table = FindById(root, "t")!;

            var containing = wrap.ClientRight - wrap.ClientLeft; // 200pt content box
            var expected = (containing - 100.0) / 2.0;          // 50pt each side

            Assert.Equal(expected, CssLayoutEngine.GetActualMarginLeft(table, 100.0), 3);
            Assert.Equal(expected, CssLayoutEngine.GetActualMarginRight(table, 100.0), 3);
        }

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head><style>body,html{{margin:0}}</style></head><body>{body}</body></html>";

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            container.MarginTop = 0;
            container.MarginLeft = 0;
            container.MarginRight = 0;
            container.MarginBottom = 0;
            await container.SetHtml(html, null);

            var size = new XSize(400, 1000);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
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

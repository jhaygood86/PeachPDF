using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Layout tests for <c>aspect-ratio</c> (CSS Box Sizing Level 4): with a definite width and an auto
    /// height, the used (border-box) height is computed from the width via the ratio. This is what gives a
    /// Charts.css <c>tbody { aspect-ratio: … }</c> its height. Uses pt fixtures so the expected values read
    /// 1:1 (px would resolve at 0.75pt).
    /// </summary>
    public class AspectRatioLayoutIntegrationTests
    {
        [Theory]
        [InlineData("aspect-ratio: 2", 100, 50)]        // 100 / 2
        [InlineData("aspect-ratio: 1", 100, 100)]       // square
        [InlineData("aspect-ratio: 1 / 2", 100, 200)]   // taller than wide
        [InlineData("aspect-ratio: 21 / 9", 210, 90)]   // 210 * 9 / 21
        public async Task DefiniteWidth_AutoHeight_HeightFromRatio(string aspectDecl, double width, double expectedHeight)
        {
            var box = await LayoutDiv($"width: {width}pt; {aspectDecl}");
            Assert.Equal(expectedHeight, box.ActualHeight, 1);
        }

        [Fact]
        public async Task DefiniteHeight_OverridesAspectRatio()
        {
            // A definite height wins; the ratio does not override an explicit height.
            var box = await LayoutDiv("width: 100pt; height: 33pt; aspect-ratio: 2");
            Assert.Equal(33, box.ActualHeight, 1);
        }

        [Fact]
        public async Task AspectRatioAuto_DoesNotSizeHeight()
        {
            // aspect-ratio: auto has no ratio, so the box keeps its content-driven height — identical to a
            // box with no aspect-ratio at all (and NOT the width-derived 50pt a ratio of 2 would give).
            var baseline = (await LayoutDiv("width: 100pt")).ActualHeight;
            var box = await LayoutDiv("width: 100pt; aspect-ratio: auto");
            Assert.Equal(baseline, box.ActualHeight, 1);
        }

        [Fact]
        public async Task ZeroTermRatio_DoesNotSizeHeight()
        {
            // A ratio with a zero term is valid but yields no preferred ratio, so the box keeps its
            // content-driven height, unchanged from no aspect-ratio.
            var baseline = (await LayoutDiv("width: 100pt")).ActualHeight;
            var box = await LayoutDiv("width: 100pt; aspect-ratio: 1 / 0");
            Assert.Equal(baseline, box.ActualHeight, 1);
        }

        [Fact]
        public async Task WithPadding_ContentBox_RatioAppliesToContentBox()
        {
            // Default box-sizing is content-box: the ratio maps content-width to content-height, then padding
            // is added. width:100pt content, aspect-ratio:2 => 50pt content height, + 2*10pt padding = 70pt.
            var box = await LayoutDiv("width: 100pt; padding: 10pt; aspect-ratio: 2");
            Assert.Equal(70, box.ActualHeight, 1);
        }

        [Fact]
        public async Task WithPadding_BorderBox_RatioAppliesToBorderBox()
        {
            // With box-sizing: border-box, width:100pt IS the border-box width, so the ratio maps border-box
            // width to border-box height directly: 100 / 2 = 50pt (padding is inside, not added on top).
            var box = await LayoutDiv("box-sizing: border-box; width: 100pt; padding: 10pt; aspect-ratio: 2");
            Assert.Equal(50, box.ActualHeight, 1);
        }

        [Fact]
        public async Task RatioDerivedHeight_ResolvesPercentageHeightChild()
        {
            // The Charts.css chain: the parent's height comes from aspect-ratio, and a child's percentage
            // height resolves against that ratio-derived height — this is how the bars take their height from
            // the tbody. Parent 200pt wide, aspect-ratio 2 => 100pt tall; child height:40% => 40pt.
            var html = """
                <!DOCTYPE html><html><head><style>
                  #parent { width: 200pt; aspect-ratio: 2; position: relative; }
                  #child { height: 40%; width: 20pt; }
                </style></head><body>
                <div id="parent"><div id="child"></div></div>
                </body></html>
                """;
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);
            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            var parent = FindById(container.Root!, "parent")!;
            var child = FindById(container.Root!, "child")!;
            Assert.Equal(100, parent.ActualHeight, 1);   // ratio-derived
            Assert.Equal(40, child.ActualHeight, 1);      // 40% of the ratio-derived parent height
        }

        [Fact]
        public async Task FlexRow_AspectRatio_GivesDefiniteCrossSize_StretchChildFills()
        {
            // A row flex container with a definite width and aspect-ratio (no explicit height) has a definite
            // cross size derived from the ratio, so an align-items:stretch child fills it. Container 210pt
            // wide, aspect-ratio 21/9 => 90pt tall; the stretched child fills the 90pt cross size, and a
            // percentage-height grandchild (height:50%) resolves against the child's now-definite height.
            var html = """
                <!DOCTYPE html><html><head><style>
                  #flex { display: flex; width: 210pt; aspect-ratio: 21 / 9; align-items: stretch; }
                  #item { width: 40pt; }
                  #gc { height: 50%; width: 10pt; }
                </style></head><body>
                <div id="flex"><div id="item"><div id="gc"></div></div></div>
                </body></html>
                """;
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);
            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            var item = FindById(container.Root!, "item")!;
            var gc = FindById(container.Root!, "gc")!;
            Assert.Equal(90, item.ActualHeight, 1.5);  // stretched to the ratio-derived cross size
            Assert.Equal(45, gc.ActualHeight, 1.5);    // 50% of the stretched item height
        }

        [Fact]
        public async Task IndefinitePercentHeight_WithAspectRatio_SizedByRatio()
        {
            // A percentage height against an indefinite (auto-height) containing block behaves as automatic
            // (CSS Box Sizing 4 §5), so the aspect ratio sizes the height from the definite width instead of
            // the percentage collapsing to auto. #wrapper is auto-height (indefinite), so #parent's
            // height:50% is indefinite; width 200pt + aspect-ratio 2 => 100pt tall, and a child's height:40%
            // resolves against that ratio-derived height => 40pt.
            var container = await LayoutContainer("""
                <!DOCTYPE html><html><head><style>
                  #parent { width: 200pt; height: 50%; aspect-ratio: 2; position: relative; }
                  #child { height: 40%; width: 20pt; }
                </style></head><body>
                <div id="wrapper"><div id="parent"><div id="child"></div></div></div>
                </body></html>
                """);

            var parent = FindById(container.Root!, "parent")!;
            var child = FindById(container.Root!, "child")!;
            Assert.Equal(100, parent.ActualHeight, 1);   // ratio-derived, not collapsed to auto
            Assert.Equal(40, child.ActualHeight, 1);      // 40% of the ratio-derived parent height
        }

        [Fact]
        public async Task IndefinitePercentHeight_WithoutAspectRatio_StaysAuto()
        {
            // Contrast to the above: the same indefinite percentage height with NO aspect-ratio stays
            // automatic (content-driven), so the box does not take the 100pt a ratio would give.
            var container = await LayoutContainer("""
                <!DOCTYPE html><html><head><style>
                  #parent { width: 200pt; height: 50%; position: relative; }
                </style></head><body>
                <div id="wrapper"><div id="parent">x</div></div>
                </body></html>
                """);

            var parent = FindById(container.Root!, "parent")!;
            Assert.True(parent.ActualHeight < 100, $"expected content-driven height, got {parent.ActualHeight}");
        }

        private static async Task<HtmlContainerInt> LayoutContainer(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static async Task<CssBox> LayoutDiv(string style)
        {
            var html = $"<!DOCTYPE html><html><head></head><body><div id=\"el\" style=\"{style}\">x</div></body></html>";
            var container = await LayoutContainer(html);
            return FindById(container.Root!, "el")!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}

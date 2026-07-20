using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class BackgroundLayerResolverTests
    {
        [Fact]
        public async Task ResolveSize_Cover_ScalesUpToFillWiderContainer()
        {
            var box = await FindDivBox();

            // Intrinsic ratio 2:1 (e.g. 200x100), container is a 100x100 square - cover must scale
            // by height (the constraining axis) and overflow width.
            var (width, height) = BackgroundLayerResolver.ResolveSize(
                "cover", 100, 100, 200, 100, 2.0, box);

            Assert.Equal(200, width, 3);
            Assert.Equal(100, height, 3);
        }

        [Fact]
        public async Task ResolveSize_Contain_ScalesDownToFitWiderContainer()
        {
            var box = await FindDivBox();

            var (width, height) = BackgroundLayerResolver.ResolveSize(
                "contain", 100, 100, 200, 100, 2.0, box);

            Assert.Equal(100, width, 3);
            Assert.Equal(50, height, 3);
        }

        [Fact]
        public async Task ResolveSize_CoverContainAuto_WithNoIntrinsicRatio_ResolveToContainerSize()
        {
            var box = await FindDivBox();

            // Gradients have no intrinsic size/ratio - cover, contain, and auto-auto must all
            // collapse to exactly the container (background positioning area) size.
            Assert.Equal((150.0, 75.0), BackgroundLayerResolver.ResolveSize("cover", 150, 75, null, null, null, box));
            Assert.Equal((150.0, 75.0), BackgroundLayerResolver.ResolveSize("contain", 150, 75, null, null, null, box));
            Assert.Equal((150.0, 75.0), BackgroundLayerResolver.ResolveSize("auto", 150, 75, null, null, null, box));
        }

        [Fact]
        public async Task ResolveSize_AutoAuto_WithIntrinsicSize_UsesIntrinsicSize()
        {
            var box = await FindDivBox();

            var result = BackgroundLayerResolver.ResolveSize("auto auto", 500, 500, 40, 20, 2.0, box);

            Assert.Equal((40.0, 20.0), result);
        }

        [Fact]
        public async Task ResolveSize_OneLengthOneAuto_WithRatio_ComputesProportionally()
        {
            var box = await FindDivBox();

            var (width, height) = BackgroundLayerResolver.ResolveSize(
                "50% auto", 200, 200, 40, 20, 2.0, box);

            Assert.Equal(100, width, 3);
            Assert.Equal(50, height, 3);
        }

        [Fact]
        public async Task ResolveSize_TwoLiteralLengths_ReturnedVerbatim()
        {
            var box = await FindDivBox();

            var result = BackgroundLayerResolver.ResolveSize("100pt 50pt", 500, 500, 40, 20, 2.0, box);

            Assert.Equal((100.0, 50.0), result);
        }

        [Fact]
        public async Task ResolveSize_ZeroOrNegative_ClampsToZero()
        {
            var box = await FindDivBox();

            var result = BackgroundLayerResolver.ResolveSize("0px 0px", 500, 500, 40, 20, 2.0, box);

            Assert.Equal((0.0, 0.0), result);
        }

        [Fact]
        public async Task ResolvePosition_SingleKeyword_CentersOtherAxis()
        {
            var box = await FindDivBox();

            // "right" alone: X flush against the right edge, Y implicitly centered.
            var (x, y) = BackgroundLayerResolver.ResolvePosition("right", 200, 100, 50, 20, box);

            Assert.Equal(150, x, 3); // 200 - 50
            Assert.Equal(40, y, 3);  // (100 - 20) / 2
        }

        [Fact]
        public async Task ResolvePosition_TwoPercentValues_ResolveAgainstAvailableSpace()
        {
            var box = await FindDivBox();

            // Regression test: "25% 75%" must NOT be centered (a historical bug treated any
            // string without a literal "0" character as "center it").
            var (x, y) = BackgroundLayerResolver.ResolvePosition("25% 75%", 200, 100, 50, 20, box);

            Assert.Equal(37.5, x, 3);  // 25% of (200 - 50)
            Assert.Equal(60.0, y, 3);  // 75% of (100 - 20)
        }

        [Fact]
        public async Task ResolvePosition_ReversedKeywordOrder_AssignsAxesCorrectly()
        {
            var box = await FindDivBox();

            // "bottom center" - vertical keyword first, horizontal second.
            var (x, y) = BackgroundLayerResolver.ResolvePosition("bottom center", 200, 100, 50, 20, box);

            Assert.Equal(75, x, 3);  // (200 - 50) / 2
            Assert.Equal(80, y, 3);  // 100 - 20
        }

        [Fact]
        public async Task ResolvePosition_FourValueEdgeOffset_MeasuresInwardFromFarEdge()
        {
            var box = await FindDivBox();

            var (x, y) = BackgroundLayerResolver.ResolvePosition(
                "right 20pt bottom 10pt", 200, 100, 50, 20, box);

            Assert.Equal(130, x, 3); // (200 - 50) - 20
            Assert.Equal(70, y, 3);  // (100 - 20) - 10
        }

        [Fact]
        public async Task ResolvePosition_ThreeValueEdgeOffset_HorizontalOnly()
        {
            var box = await FindDivBox();

            var (x, y) = BackgroundLayerResolver.ResolvePosition(
                "right 20pt bottom", 200, 100, 50, 20, box);

            Assert.Equal(130, x, 3); // (200 - 50) - 20
            Assert.Equal(80, y, 3);  // 100 - 20, no offset
        }

        [Fact]
        public async Task ResolvePosition_MixedKeywordAndBareLength_ReversedOrder()
        {
            var box = await FindDivBox();

            // "bottom 10pt" (2 tokens): the bare length is NOT an offset on "bottom" - per the
            // simple 2-value grammar this is Y=bottom(keyword), X=10pt(bare, horizontal).
            var (x, y) = BackgroundLayerResolver.ResolvePosition("bottom 10pt", 200, 100, 50, 20, box);

            Assert.Equal(10, x, 3);
            Assert.Equal(80, y, 3); // 100 - 20
        }

        [Fact]
        public async Task ResolvePosition_SupportsCalc()
        {
            var box = await FindDivBox();

            var (x, y) = BackgroundLayerResolver.ResolvePosition("calc(50% - 10pt) center", 200, 100, 50, 20, box);

            Assert.Equal(65, x, 3); // 50% of (200-50)=75, minus 10pt = 65
            Assert.Equal(40, y, 3); // (100 - 20) / 2
        }

        [Fact]
        public async Task ResolveSize_SupportsCalc()
        {
            var box = await FindDivBox();

            var result = BackgroundLayerResolver.ResolveSize("calc(50% + 10pt) auto", 200, 200, 40, 20, 2.0, box);

            Assert.Equal(110, result.Width, 3);  // 50% of 200 = 100, plus 10pt = 110
            Assert.Equal(55, result.Height, 3);  // proportional via ratio: 110 / 2.0
        }

        [Theory]
        [InlineData("10px 20px, center", 0, "10px 20px")]
        [InlineData("10px 20px, center", 1, "center")]
        [InlineData("10px 20px, center", 2, "10px 20px")] // cycles back to layer 0
        [InlineData("center", 3, "center")]               // single value cycles for every layer
        public void LayerAt_CyclesAgainstLayerCount(string value, int layerIndex, string expected)
        {
            var layers = BackgroundLayerResolver.SplitLayers(value);
            Assert.Equal(expected, BackgroundLayerResolver.LayerAt(layers, layerIndex));
        }

        // --- Helpers ---

        private static async Task<CssBox> FindDivBox()
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(
                "<!DOCTYPE html><html><head><style>div { width: 200px; height: 100px; }</style></head><body><div>Text</div></body></html>",
                null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new PeachPDF.Adapters.GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return FindByTag(container.Root!, "div")!;
        }

        private static CssBox? FindByTag(CssBox box, string tag)
        {
            if (box.HtmlTag?.Name.Equals(tag, StringComparison.OrdinalIgnoreCase) == true)
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found != null) return found;
            }
            return null;
        }
    }
}

using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Html.Core.Utils
{
    /// <summary>
    /// Coverage for <see cref="BorderImageLayerResolver"/>'s own pixel arithmetic against a
    /// <c>border-image-source</c>'s natural size and a box's real border widths/border-box - the paint-time
    /// half of <c>border-image-slice</c>/<c>-width</c>/<c>-outset</c>/<c>-repeat</c> (see
    /// <c>BorderImagePaintIntegrationTests</c> for the actual 9-slice paint output these values drive).
    /// </summary>
    public class BorderImageLayerResolverTests
    {
        // ─── border-image-slice ─────────────────────────────────────────────────

        [Fact]
        public void ResolveSlice_PercentagesResolveAgainstNaturalSize()
        {
            var slice = BorderImageLayerResolver.ResolveSlice("25%", naturalWidth: 40, naturalHeight: 20);

            Assert.Equal(5, slice.Top);
            Assert.Equal(10, slice.Right);
            Assert.Equal(5, slice.Bottom);
            Assert.Equal(10, slice.Left);
            Assert.False(slice.Fill);
        }

        [Fact]
        public void ResolveSlice_BareNumbersAreSourcePixelsDirectly()
        {
            var slice = BorderImageLayerResolver.ResolveSlice("3", naturalWidth: 40, naturalHeight: 20);

            Assert.Equal(3, slice.Top);
            Assert.Equal(3, slice.Right);
            Assert.Equal(3, slice.Bottom);
            Assert.Equal(3, slice.Left);
        }

        [Fact]
        public void ResolveSlice_BareNumbersOnAVectorSource_AreCssPixelsOfItsPointSize()
        {
            // A raster's natural size is counted in the same device pixels the spec's <number> counts, so
            // the two agree. A vector or generated source is rendered into a tile measured in layout
            // points, while a number there is still a vector coordinate / CSS pixel - 0.75 of one.
            var slice = BorderImageLayerResolver.ResolveSlice("20", naturalWidth: 30, naturalHeight: 30,
                numberUnit: Length.PointsPerPx);

            Assert.Equal(15, slice.Top);
            Assert.Equal(15, slice.Right);
            Assert.Equal(15, slice.Bottom);
            Assert.Equal(15, slice.Left);
        }

        [Fact]
        public void ResolveSlice_PercentagesIgnoreTheNumberUnit()
        {
            // A percentage is relative to the natural size either way, so the unit a bare number is
            // counted in must not touch it.
            var slice = BorderImageLayerResolver.ResolveSlice("50%", naturalWidth: 30, naturalHeight: 30,
                numberUnit: Length.PointsPerPx);

            Assert.Equal(15, slice.Top);
            Assert.Equal(15, slice.Left);
        }

        [Fact]
        public void ResolveSlice_FillKeywordDetectedRegardlessOfPosition()
        {
            Assert.True(BorderImageLayerResolver.ResolveSlice("fill 10", 40, 40).Fill);
            Assert.True(BorderImageLayerResolver.ResolveSlice("10 fill", 40, 40).Fill);
            Assert.False(BorderImageLayerResolver.ResolveSlice("10", 40, 40).Fill);
        }

        [Fact]
        public void ResolveSlice_FourValues_AssignTopRightBottomLeftInOrder()
        {
            var slice = BorderImageLayerResolver.ResolveSlice("1 2 3 4", naturalWidth: 100, naturalHeight: 100);

            Assert.Equal(1, slice.Top);
            Assert.Equal(2, slice.Right);
            Assert.Equal(3, slice.Bottom);
            Assert.Equal(4, slice.Left);
        }

        [Fact]
        public void ResolveSlice_ThreeValues_TopHorizontalBottom()
        {
            var slice = BorderImageLayerResolver.ResolveSlice("1 2 3", naturalWidth: 100, naturalHeight: 100);

            Assert.Equal(1, slice.Top);
            Assert.Equal(2, slice.Right);
            Assert.Equal(3, slice.Bottom);
            Assert.Equal(2, slice.Left);
        }

        [Fact]
        public void ResolveSlice_TwoValues_CycleVerticalThenHorizontal()
        {
            var slice = BorderImageLayerResolver.ResolveSlice("10 20", naturalWidth: 100, naturalHeight: 100);

            Assert.Equal(10, slice.Top);
            Assert.Equal(20, slice.Right);
            Assert.Equal(10, slice.Bottom);
            Assert.Equal(20, slice.Left);
        }

        [Fact]
        public void ResolveSlice_OppositeValuesLargerThanNaturalSize_AreProportionallyReduced()
        {
            // top(30) + bottom(30) = 60 > naturalHeight(40) -> scaled by 40/60, giving 20 each.
            var slice = BorderImageLayerResolver.ResolveSlice("30", naturalWidth: 100, naturalHeight: 40);

            Assert.Equal(20, slice.Top, 3);
            Assert.Equal(20, slice.Bottom, 3);
            // left/right (30 each, sum 60) fit within naturalWidth(100) untouched.
            Assert.Equal(30, slice.Left, 3);
            Assert.Equal(30, slice.Right, 3);
        }

        [Fact]
        public void ResolveSlice_OppositeHorizontalValuesLargerThanNaturalWidth_AreProportionallyReduced()
        {
            // left(30) + right(30) = 60 > naturalWidth(40) -> scaled by 40/60, giving 20 each.
            var slice = BorderImageLayerResolver.ResolveSlice("30 30", naturalWidth: 40, naturalHeight: 100);

            Assert.Equal(20, slice.Left, 3);
            Assert.Equal(20, slice.Right, 3);
            Assert.Equal(30, slice.Top, 3);
            Assert.Equal(30, slice.Bottom, 3);
        }

        // ─── border-image-width ─────────────────────────────────────────────────

        [Fact]
        public async Task ResolveWidth_AutoFallsBackToTheRealBorderWidth()
        {
            var box = await FindDivBox();

            var width = BorderImageLayerResolver.ResolveWidth("auto",
                borderTop: 5, borderRight: 6, borderBottom: 7, borderLeft: 8,
                areaWidth: 200, areaHeight: 100, box);

            Assert.Equal(5, width.Top);
            Assert.Equal(6, width.Right);
            Assert.Equal(7, width.Bottom);
            Assert.Equal(8, width.Left);
        }

        [Fact]
        public async Task ResolveWidth_BareNumberIsAMultipleOfTheRealBorderWidth()
        {
            var box = await FindDivBox();

            var width = BorderImageLayerResolver.ResolveWidth("2",
                borderTop: 5, borderRight: 5, borderBottom: 5, borderLeft: 5,
                areaWidth: 200, areaHeight: 100, box);

            Assert.Equal(10, width.Top);
            Assert.Equal(10, width.Right);
            Assert.Equal(10, width.Bottom);
            Assert.Equal(10, width.Left);
        }

        [Fact]
        public async Task ResolveWidth_PercentageResolvesAgainstTheBorderImageArea()
        {
            var box = await FindDivBox();

            var width = BorderImageLayerResolver.ResolveWidth("10% 20%",
                borderTop: 1, borderRight: 1, borderBottom: 1, borderLeft: 1,
                areaWidth: 200, areaHeight: 100, box);

            Assert.Equal(10, width.Top, 3); // 10% of areaHeight (100)
            Assert.Equal(40, width.Right, 3); // 20% of areaWidth (200)
        }

        [Fact]
        public async Task ResolveWidth_LengthResolvesAbsolutely()
        {
            var box = await FindDivBox();

            var width = BorderImageLayerResolver.ResolveWidth("12pt",
                borderTop: 1, borderRight: 1, borderBottom: 1, borderLeft: 1,
                areaWidth: 200, areaHeight: 100, box);

            Assert.Equal(12, width.Top, 3);
        }

        [Fact]
        public async Task ResolveWidth_OppositeValuesLargerThanTheArea_AreProportionallyReduced()
        {
            var box = await FindDivBox();

            // top+bottom (150pt each -> 300) > areaHeight (100) -> scaled down to 50 each.
            var width = BorderImageLayerResolver.ResolveWidth("150pt",
                borderTop: 1, borderRight: 1, borderBottom: 1, borderLeft: 1,
                areaWidth: 400, areaHeight: 100, box);

            Assert.Equal(50, width.Top, 3);
            Assert.Equal(50, width.Bottom, 3);
        }

        [Fact]
        public async Task ResolveWidth_OppositeHorizontalValuesLargerThanTheArea_AreProportionallyReduced()
        {
            var box = await FindDivBox();

            // left+right (150pt each -> 300) > areaWidth (100) -> scaled down to 50 each; top/bottom
            // (150pt each -> 300) fit within the much larger areaHeight (400) untouched.
            var width = BorderImageLayerResolver.ResolveWidth("150pt",
                borderTop: 1, borderRight: 1, borderBottom: 1, borderLeft: 1,
                areaWidth: 100, areaHeight: 400, box);

            Assert.Equal(50, width.Left, 3);
            Assert.Equal(50, width.Right, 3);
            Assert.Equal(150, width.Top, 3);
        }

        // ─── border-image-outset ────────────────────────────────────────────────

        [Fact]
        public async Task ResolveOutset_BareNumberIsAMultipleOfTheRealBorderWidth()
        {
            var box = await FindDivBox();

            var outset = BorderImageLayerResolver.ResolveOutset("1.5",
                borderTop: 4, borderRight: 4, borderBottom: 4, borderLeft: 4, box);

            Assert.Equal(6, outset.Top, 3);
        }

        [Fact]
        public async Task ResolveOutset_PercentageResolvesAgainstTheSameAxisBorderWidth()
        {
            // The CSS-OM's own BorderImageOutsetProperty converter accepts a percentage - broader than
            // the spec's own length|number grammar (a pre-existing, already-tested deviation - see the
            // accepted-gap note) - resolved here against the same side's real border width.
            var box = await FindDivBox();

            var outset = BorderImageLayerResolver.ResolveOutset("50%",
                borderTop: 10, borderRight: 10, borderBottom: 10, borderLeft: 10, box);

            Assert.Equal(5, outset.Top, 3);
        }

        [Fact]
        public async Task ResolveOutset_LengthResolvesAbsolutely()
        {
            var box = await FindDivBox();

            var outset = BorderImageLayerResolver.ResolveOutset("3pt",
                borderTop: 4, borderRight: 4, borderBottom: 4, borderLeft: 4, box);

            Assert.Equal(3, outset.Top, 3);
        }

        [Fact]
        public async Task ResolveOutset_DefaultsToZeroOnAllSides()
        {
            var box = await FindDivBox();

            var outset = BorderImageLayerResolver.ResolveOutset("0",
                borderTop: 4, borderRight: 4, borderBottom: 4, borderLeft: 4, box);

            Assert.Equal(0, outset.Top);
            Assert.Equal(0, outset.Right);
            Assert.Equal(0, outset.Bottom);
            Assert.Equal(0, outset.Left);
        }

        // ─── border-image-repeat ────────────────────────────────────────────────

        [Fact]
        public void ResolveRepeat_OneKeyword_AppliesToBothAxes()
        {
            var (horizontal, vertical) = BorderImageLayerResolver.ResolveRepeat("repeat");

            Assert.Equal(BorderRepeat.Repeat, horizontal);
            Assert.Equal(BorderRepeat.Repeat, vertical);
        }

        [Fact]
        public void ResolveRepeat_TwoKeywords_HorizontalThenVertical()
        {
            var (horizontal, vertical) = BorderImageLayerResolver.ResolveRepeat("round stretch");

            Assert.Equal(BorderRepeat.Round, horizontal);
            Assert.Equal(BorderRepeat.Stretch, vertical);
        }

        [Fact]
        public void ResolveRepeat_SpaceKeyword_Recognized()
        {
            var (horizontal, vertical) = BorderImageLayerResolver.ResolveRepeat("space");

            Assert.Equal(BorderRepeat.Space, horizontal);
            Assert.Equal(BorderRepeat.Space, vertical);
        }

        [Fact]
        public void ResolveRepeat_UnrecognizedComponent_DefaultsToStretch()
        {
            // This repo's own BorderImageRepeatProperty converter should already reject anything not in
            // Map.BorderRepeatModes, so this component can never actually reach here in practice - the
            // resolver still degrades gracefully rather than throwing if one ever did.
            var (horizontal, vertical) = BorderImageLayerResolver.ResolveRepeat("bogus");

            Assert.Equal(BorderRepeat.Stretch, horizontal);
            Assert.Equal(BorderRepeat.Stretch, vertical);
        }

        // ─── Helpers (mirrors BackgroundLayerResolverTests) ────────────────────────

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
            if (string.Equals(box.HtmlTag?.Name, tag, System.StringComparison.OrdinalIgnoreCase)) return box;
            foreach (var child in box.Boxes)
            {
                var found = FindByTag(child, tag);
                if (found is not null) return found;
            }
            return null;
        }
    }
}

using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    public class BorderRadiusIntegrationTests
    {
        // --- Circular radii (symmetric X = Y) ---

        [Fact]
        public async Task BorderRadius_Shorthand_SetsAllCorners()
        {
            var divBox = await FindDivBox("border-radius: 10px; border: 1px solid black;");

            Assert.Equal(10.0, divBox.ActualBorderTopLeftRadiusX);
            Assert.Equal(10.0, divBox.ActualBorderTopLeftRadiusY);
            Assert.Equal(10.0, divBox.ActualBorderTopRightRadiusX);
            Assert.Equal(10.0, divBox.ActualBorderTopRightRadiusY);
            Assert.Equal(10.0, divBox.ActualBorderBottomRightRadiusX);
            Assert.Equal(10.0, divBox.ActualBorderBottomRightRadiusY);
            Assert.Equal(10.0, divBox.ActualBorderBottomLeftRadiusX);
            Assert.Equal(10.0, divBox.ActualBorderBottomLeftRadiusY);
            Assert.True(divBox.IsRounded);
        }

        [Fact]
        public async Task BorderRadius_TwoValues_SetsOpposingCorners()
        {
            var divBox = await FindDivBox("border-radius: 10px 20px;");

            Assert.Equal(10.0, divBox.ActualBorderTopLeftRadiusX);
            Assert.Equal(20.0, divBox.ActualBorderTopRightRadiusX);
            Assert.Equal(10.0, divBox.ActualBorderBottomRightRadiusX);
            Assert.Equal(20.0, divBox.ActualBorderBottomLeftRadiusX);
        }

        [Fact]
        public async Task BorderRadius_FourValues_SetsAllCornersIndividually()
        {
            var divBox = await FindDivBox("border-radius: 5px 10px 15px 20px;");

            Assert.Equal(5.0, divBox.ActualBorderTopLeftRadiusX);
            Assert.Equal(10.0, divBox.ActualBorderTopRightRadiusX);
            Assert.Equal(15.0, divBox.ActualBorderBottomRightRadiusX);
            Assert.Equal(20.0, divBox.ActualBorderBottomLeftRadiusX);
        }

        [Fact]
        public async Task BorderTopLeftRadius_Longhand_SetsOnlyTopLeft()
        {
            var divBox = await FindDivBox("border-top-left-radius: 12px;");

            Assert.Equal(12.0, divBox.ActualBorderTopLeftRadiusX);
            Assert.Equal(12.0, divBox.ActualBorderTopLeftRadiusY);
            Assert.Equal(0.0, divBox.ActualBorderTopRightRadiusX);
            Assert.Equal(0.0, divBox.ActualBorderBottomRightRadiusX);
            Assert.Equal(0.0, divBox.ActualBorderBottomLeftRadiusX);
            Assert.True(divBox.IsRounded);
        }

        [Fact]
        public async Task BorderRadius_Zero_IsNotRounded()
        {
            var divBox = await FindDivBox("");
            Assert.False(divBox.IsRounded);
        }

        // --- Elliptical radii (X ≠ Y) ---

        [Fact]
        public async Task BorderRadius_EllipticalShorthand_SetsAllCornersXAndY()
        {
            // border-radius: 40px / 15px → each corner: X=40, Y=15
            var divBox = await FindDivBox("border-radius: 40px / 15px;");

            Assert.Equal(40.0, divBox.ActualBorderTopLeftRadiusX);
            Assert.Equal(15.0, divBox.ActualBorderTopLeftRadiusY);
            Assert.Equal(40.0, divBox.ActualBorderTopRightRadiusX);
            Assert.Equal(15.0, divBox.ActualBorderTopRightRadiusY);
            Assert.Equal(40.0, divBox.ActualBorderBottomRightRadiusX);
            Assert.Equal(15.0, divBox.ActualBorderBottomRightRadiusY);
            Assert.Equal(40.0, divBox.ActualBorderBottomLeftRadiusX);
            Assert.Equal(15.0, divBox.ActualBorderBottomLeftRadiusY);
        }

        [Fact]
        public async Task BorderTopLeftRadius_Longhand_EllipticalValues()
        {
            // border-top-left-radius: 15px 25px → X=15, Y=25
            var divBox = await FindDivBox("border-top-left-radius: 15px 25px;");

            Assert.Equal(15.0, divBox.ActualBorderTopLeftRadiusX);
            Assert.Equal(25.0, divBox.ActualBorderTopLeftRadiusY);
            Assert.Equal(0.0, divBox.ActualBorderTopRightRadiusX);
        }

        // --- Percentage values ---

        [Fact]
        public async Task BorderRadius_Percentage_ResolvesRelativeToDimensions()
        {
            // 200px × 100px box; border-radius: 50% → X = 50% of 200 = 100, Y = 50% of 100 = 50
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 200px; height: 100px; border-radius: 50%; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);

            Assert.Equal(100.0, divBox.ActualBorderTopLeftRadiusX, 1);
            Assert.Equal(50.0, divBox.ActualBorderTopLeftRadiusY, 1);
        }

        // --- Overlapping radii reduction ---

        [Fact]
        public async Task BorderRadius_OverlappingRadii_AreReducedProportionally()
        {
            // 100px × 100px box; border-radius: 60px — adjacent radii sum to 120 > 100,
            // so all must scale by 100/120 ≈ 0.833, giving ~50px at the boundary.
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 100px; height: 100px; border-radius: 60px; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);
            var radii = divBox.ComputeRadii(new PeachPDF.Html.Adapters.Entities.RRect(0, 0, 100, 100));

            // After reduction TLX + TRX must equal 100 (the width), so each ≈ 50.
            Assert.Equal(100.0, radii.TLX + radii.TRX, 2);
            Assert.Equal(100.0, radii.BLX + radii.BRX, 2);
            Assert.Equal(100.0, radii.TLY + radii.BLY, 2);
            Assert.Equal(100.0, radii.TRY + radii.BRY, 2);
        }

        [Fact]
        public async Task BorderRadius_NonOverlappingRadii_AreNotChanged()
        {
            // 200px × 200px box; border-radius: 30px — 30+30=60 < 200, no reduction.
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 200px; height: 200px; border-radius: 30px; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);
            var radii = divBox.ComputeRadii(new PeachPDF.Html.Adapters.Entities.RRect(0, 0, 200, 200));

            Assert.Equal(30.0, radii.TLX, 2);
            Assert.Equal(30.0, radii.TLY, 2);
        }

        // --- Helpers ---

        private Task<CssBox> FindDivBox(string css)
        {
            var html = $@"<!DOCTYPE html><html><head><style>
div {{ width: 200px; height: 100px; {css} }}
</style></head><body><div></div></body></html>";
            return FindDivBoxFromHtml(html);
        }

        private async Task<CssBox> FindDivBoxFromHtml(string html)
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

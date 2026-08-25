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
            var divBox = await FindDivBox("border-radius: 10pt; border: 1pt solid black;");

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
            var divBox = await FindDivBox("border-radius: 10pt 20pt;");

            Assert.Equal(10.0, divBox.ActualBorderTopLeftRadiusX);
            Assert.Equal(20.0, divBox.ActualBorderTopRightRadiusX);
            Assert.Equal(10.0, divBox.ActualBorderBottomRightRadiusX);
            Assert.Equal(20.0, divBox.ActualBorderBottomLeftRadiusX);
        }

        [Fact]
        public async Task BorderRadius_FourValues_SetsAllCornersIndividually()
        {
            var divBox = await FindDivBox("border-radius: 5pt 10pt 15pt 20pt;");

            Assert.Equal(5.0, divBox.ActualBorderTopLeftRadiusX);
            Assert.Equal(10.0, divBox.ActualBorderTopRightRadiusX);
            Assert.Equal(15.0, divBox.ActualBorderBottomRightRadiusX);
            Assert.Equal(20.0, divBox.ActualBorderBottomLeftRadiusX);
        }

        [Fact]
        public async Task BorderTopLeftRadius_Longhand_SetsOnlyTopLeft()
        {
            var divBox = await FindDivBox("border-top-left-radius: 12pt;");

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
            // border-radius: 40pt / 15pt → each corner: X=40, Y=15
            var divBox = await FindDivBox("border-radius: 40pt / 15pt;");

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
            // border-top-left-radius: 15pt 25pt → X=15, Y=25
            var divBox = await FindDivBox("border-top-left-radius: 15pt 25pt;");

            Assert.Equal(15.0, divBox.ActualBorderTopLeftRadiusX);
            Assert.Equal(25.0, divBox.ActualBorderTopLeftRadiusY);
            Assert.Equal(0.0, divBox.ActualBorderTopRightRadiusX);
        }

        // --- Percentage values ---

        [Fact]
        public async Task BorderRadius_Percentage_ResolvesRelativeToDimensions()
        {
            // 200pt × 100pt box; border-radius: 50% → X = 50% of 200 = 100, Y = 50% of 100 = 50
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 200pt; height: 100pt; border-radius: 50%; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);

            Assert.Equal(100.0, divBox.ActualBorderTopLeftRadiusX, 1);
            Assert.Equal(50.0, divBox.ActualBorderTopLeftRadiusY, 1);
        }

        // --- Overlapping radii reduction ---

        [Fact]
        public async Task BorderRadius_OverlappingRadii_AreReducedProportionally()
        {
            // 100pt × 100pt box; border-radius: 60pt — adjacent radii sum to 120 > 100,
            // so all must scale by 100/120 ≈ 0.833, giving ~50pt at the boundary.
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 100pt; height: 100pt; border-radius: 60pt; }
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
        public async Task BorderRadius_AsymmetricOverconstraint_ReducesXAndYByOneJointFactor()
        {
            // 200pt x 20pt box; border-radius: 300pt - overconstrained on both axes, but far more so
            // on height (600 vs H=20) than width (600 vs W=200). Per the CSS spec's single joint
            // factor f = min across all four edges, BOTH axes must shrink by the same (height-driven)
            // factor, landing every radius at H/2=10 - a true semicircular cap. Reducing X and Y
            // independently (the bug: two separate factors, one per axis) would instead leave X near
            // W/2=100 while Y shrinks to 10, stretching the corner into a near-degenerate ellipse -
            // exactly what issue #812 reported as a "pointed" pill cap.
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 200pt; height: 20pt; border-radius: 300pt; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);
            var radii = divBox.ComputeRadii(new PeachPDF.Html.Adapters.Entities.RRect(0, 0, 200, 20));

            Assert.Equal(10.0, radii.TLX, 2);
            Assert.Equal(10.0, radii.TLY, 2);
        }

        [Fact]
        public async Task BorderRadius_NonOverlappingRadii_AreNotChanged()
        {
            // 200pt × 200pt box; border-radius: 30pt — 30+30=60 < 200, no reduction.
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 200pt; height: 200pt; border-radius: 30pt; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);
            var radii = divBox.ComputeRadii(new PeachPDF.Html.Adapters.Entities.RRect(0, 0, 200, 200));

            Assert.Equal(30.0, radii.TLX, 2);
            Assert.Equal(30.0, radii.TLY, 2);
        }

        // --- Inner (padding-/content-edge) radius reduction: CSS Backgrounds and Borders Level 3 §5.5 ---

        [Fact]
        public async Task ComputeInnerRadii_PaddingEdge_SubtractsBorderWidth()
        {
            // border-radius: 14pt, border: 6pt solid — padding-edge radius must be 14-6=8pt, per
            // "the padding edge (inner border) radius is the outer border radius minus the
            // corresponding border thickness" (CSS Backgrounds and Borders Level 3 §5.5).
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 200pt; height: 200pt; border: 6pt solid black; border-radius: 14pt; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);
            var borderBoxRect = new PeachPDF.Html.Adapters.Entities.RRect(0, 0, 200, 200);
            var paddingRect = new PeachPDF.Html.Adapters.Entities.RRect(6, 6, 188, 188);

            var radii = divBox.ComputeInnerRadii(borderBoxRect, paddingRect, 6, 6, 6, 6);

            Assert.Equal(8.0, radii.TLX, 2);
            Assert.Equal(8.0, radii.TLY, 2);
            Assert.Equal(8.0, radii.TRX, 2);
            Assert.Equal(8.0, radii.BRX, 2);
            Assert.Equal(8.0, radii.BLX, 2);
        }

        [Fact]
        public async Task ComputeInnerRadii_BorderWiderThanRadius_ClampsToZero()
        {
            // border-radius: 4pt, border: 10pt solid — 4-10 is negative, so the inner radius clamps
            // to zero rather than going negative.
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 200pt; height: 200pt; border: 10pt solid black; border-radius: 4pt; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);
            var borderBoxRect = new PeachPDF.Html.Adapters.Entities.RRect(0, 0, 200, 200);
            var paddingRect = new PeachPDF.Html.Adapters.Entities.RRect(10, 10, 180, 180);

            var radii = divBox.ComputeInnerRadii(borderBoxRect, paddingRect, 10, 10, 10, 10);

            Assert.Equal(0.0, radii.TLX);
            Assert.Equal(0.0, radii.TLY);
            Assert.False(radii.IsRounded);
        }

        [Fact]
        public async Task ComputeInnerRadii_ContentEdge_SubtractsBorderAndPadding()
        {
            // border-radius: 30pt, border: 5pt solid, padding: 10pt — content-edge radius must be
            // 30-5-10=15pt (border AND padding both count toward the content edge's inset).
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 200pt; height: 200pt; border: 5pt solid black; padding: 10pt; border-radius: 30pt; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);
            var borderBoxRect = new PeachPDF.Html.Adapters.Entities.RRect(0, 0, 200, 200);
            var contentRect = new PeachPDF.Html.Adapters.Entities.RRect(15, 15, 170, 170);

            var radii = divBox.ComputeInnerRadii(borderBoxRect, contentRect, 15, 15, 15, 15);

            Assert.Equal(15.0, radii.TLX, 2);
            Assert.Equal(15.0, radii.TLY, 2);
        }

        [Fact]
        public async Task ComputeInnerRadii_ReducedRadiusStillOverlapping_AppliesCornerOverlapAgain()
        {
            // 100pt x 20pt box, border-radius: 40pt, border: 4pt solid — outer (border-box) radius
            // is already overlap-clamped to height/2=10 (40+40=80 > width=100 is fine, but
            // 40+40=80 > height=20 forces f=20/80=0.25, giving an outer radius of 10 per corner).
            // After subtracting the 4pt border, the inner radius is 6pt - well within the smaller
            // padding rect (100-8=92 wide, 20-8=12 tall), so no further overlap reduction should
            // kick in and 6pt should survive unchanged.
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 100pt; height: 20pt; border: 4pt solid black; border-radius: 40pt; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);
            var borderBoxRect = new PeachPDF.Html.Adapters.Entities.RRect(0, 0, 100, 20);
            var paddingRect = new PeachPDF.Html.Adapters.Entities.RRect(4, 4, 92, 12);

            var radii = divBox.ComputeInnerRadii(borderBoxRect, paddingRect, 4, 4, 4, 4);

            Assert.Equal(6.0, radii.TLX, 2);
            Assert.Equal(6.0, radii.TLY, 2);
        }

        [Fact]
        public async Task ComputeInnerRadii_AsymmetricBorderWidths_ReduceEachCornerByItsOwnAdjacentEdges()
        {
            // Non-uniform border widths: each corner's X component reduces by its own adjacent
            // vertical edge's border width, and Y component by its own adjacent horizontal edge's -
            // not a single shared inset.
            var html = @"<!DOCTYPE html><html><head><style>
div { width: 200pt; height: 200pt; border-style: solid; border-width: 2pt 4pt 6pt 8pt; border-radius: 20pt; }
</style></head><body><div></div></body></html>";

            var divBox = await FindDivBoxFromHtml(html);
            // border-width: top right bottom left = 2 4 6 8
            var borderBoxRect = new PeachPDF.Html.Adapters.Entities.RRect(0, 0, 200, 200);
            var paddingRect = new PeachPDF.Html.Adapters.Entities.RRect(8, 2, 200 - 8 - 4, 200 - 2 - 6);

            var radii = divBox.ComputeInnerRadii(borderBoxRect, paddingRect, 8, 2, 4, 6);

            // TL: X reduces by left(8) -> 12, Y reduces by top(2) -> 18
            Assert.Equal(12.0, radii.TLX, 2);
            Assert.Equal(18.0, radii.TLY, 2);
            // TR: X reduces by right(4) -> 16, Y reduces by top(2) -> 18
            Assert.Equal(16.0, radii.TRX, 2);
            Assert.Equal(18.0, radii.TRY, 2);
            // BR: X reduces by right(4) -> 16, Y reduces by bottom(6) -> 14
            Assert.Equal(16.0, radii.BRX, 2);
            Assert.Equal(14.0, radii.BRY, 2);
            // BL: X reduces by left(8) -> 12, Y reduces by bottom(6) -> 14
            Assert.Equal(12.0, radii.BLX, 2);
            Assert.Equal(14.0, radii.BLY, 2);
        }

        // --- Helpers ---

        private Task<CssBox> FindDivBox(string css)
        {
            var html = $@"<!DOCTYPE html><html><head><style>
div {{ width: 200pt; height: 100pt; {css} }}
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

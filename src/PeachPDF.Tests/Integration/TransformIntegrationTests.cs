using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    public class TransformIntegrationTests
    {
        // --- Baseline / identity ---

        [Fact]
        public async Task NoTransform_IsIdentity()
        {
            var divBox = await FindDivBox("");

            Assert.False(divBox.IsTransformed);
            var m = divBox.ActualTransformMatrix;
            Assert.Equal(1, m.M11);
            Assert.Equal(0, m.M12);
            Assert.Equal(0, m.M21);
            Assert.Equal(1, m.M22);
            Assert.Equal(0, m.OffsetX);
            Assert.Equal(0, m.OffsetY);
        }

        [Fact]
        public async Task TransformNone_IsIdentity()
        {
            var divBox = await FindDivBox("transform: none;");
            Assert.False(divBox.IsTransformed);
        }

        [Fact]
        public async Task InvalidTransform_FallsBackToIdentity()
        {
            var divBox = await FindDivBox("transform: not-a-function(1,2,3);");
            Assert.False(divBox.IsTransformed);
        }

        // --- 2D basics ---

        [Fact]
        public async Task Translate_SetsOffsetsOnly()
        {
            var divBox = await FindDivBox("transform: translate(50px, 20px);");
            var m = divBox.ActualTransformMatrix;

            Assert.True(divBox.IsTransformed);
            Assert.Equal(1, m.M11, 3);
            Assert.Equal(0, m.M12, 3);
            Assert.Equal(0, m.M21, 3);
            Assert.Equal(1, m.M22, 3);
            Assert.Equal(50, m.OffsetX, 3);
            Assert.Equal(20, m.OffsetY, 3);
        }

        [Fact]
        public async Task Scale_SetsLinearPart()
        {
            var divBox = await FindDivBox("transform: scale(2, 3); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(2, m.M11, 3);
            Assert.Equal(0, m.M12, 3);
            Assert.Equal(0, m.M21, 3);
            Assert.Equal(3, m.M22, 3);
            Assert.Equal(0, m.OffsetX, 2);
            Assert.Equal(0, m.OffsetY, 2);
        }

        [Fact]
        public async Task Rotate90Deg_MatchesClockwiseScreenConvention()
        {
            // In this codebase's y-down coordinate system, CSS's rotate(90deg) must appear
            // clockwise on screen (matching real browsers): local (1,0) -> (0,1).
            var divBox = await FindDivBox("transform: rotate(90deg); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(0, m.M11, 3);
            Assert.Equal(1, m.M12, 3);
            Assert.Equal(-1, m.M21, 3);
            Assert.Equal(0, m.M22, 3);
        }

        [Fact]
        public async Task MatrixPassthrough_MapsDirectly()
        {
            var divBox = await FindDivBox("transform: matrix(1, 0, 0, 1, 10, 20);");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(1, m.M11, 3);
            Assert.Equal(0, m.M12, 3);
            Assert.Equal(0, m.M21, 3);
            Assert.Equal(1, m.M22, 3);
            Assert.Equal(10, m.OffsetX, 3);
            Assert.Equal(20, m.OffsetY, 3);
        }

        // --- transform-origin ---

        [Fact]
        public async Task TransformOrigin_DefaultCenter_IsFixedPointOfRotation()
        {
            // 200x100 box, default transform-origin (50% 50% -> 100,50): rotating around the
            // box's own center must leave the center point itself unmoved.
            var divBox = await FindDivBox("transform: rotate(37deg);");
            var m = divBox.ActualTransformMatrix;

            var mappedX = 100 * m.M11 + 50 * m.M21 + m.OffsetX;
            var mappedY = 100 * m.M12 + 50 * m.M22 + m.OffsetY;

            Assert.Equal(100.0, mappedX, 1);
            Assert.Equal(50.0, mappedY, 1);
        }

        [Fact]
        public async Task TransformOrigin_TopLeft_IsFixedPointOfRotation()
        {
            var divBox = await FindDivBox("transform: rotate(50deg); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            var mappedX = 0 * m.M11 + 0 * m.M21 + m.OffsetX;
            var mappedY = 0 * m.M12 + 0 * m.M22 + m.OffsetY;

            Assert.Equal(0.0, mappedX, 1);
            Assert.Equal(0.0, mappedY, 1);
        }

        // --- Composition order (the critical regression guard) ---

        [Fact]
        public async Task CompositionOrder_TranslateThenRotate_ShiftsAlongOriginalXAxis()
        {
            // Hand-verified: with transform-origin 0 0, "translate(50,0) rotate(90deg)" means
            // rotate is applied first (fixes the origin, no visible effect there), translate applied
            // last, shifting the origin point by exactly (50, 0).
            var divBox = await FindDivBox("transform: translate(50px, 0) rotate(90deg); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(50.0, m.OffsetX, 2);
            Assert.Equal(0.0, m.OffsetY, 2);
        }

        [Fact]
        public async Task CompositionOrder_RotateThenTranslate_OrbitsAroundOriginalOrigin()
        {
            // Hand-verified: with transform-origin 0 0, "rotate(90deg) translate(50,0)" means
            // translate is applied first (moves origin point to (50,0)), rotate applied last,
            // swinging that point 90deg clockwise around the original origin to (0, 50).
            var divBox = await FindDivBox("transform: rotate(90deg) translate(50px, 0); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(0.0, m.OffsetX, 2);
            Assert.Equal(50.0, m.OffsetY, 2);
        }

        // --- 3D exactness (no perspective involved) ---

        [Fact]
        public async Task RotateY_WithoutPerspective_ProjectsToExactCosineXScale()
        {
            var divBox = await FindDivBox("transform: rotateY(60deg); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(Math.Cos(60.0 * Math.PI / 180.0), m.M11, 3);
            Assert.Equal(1.0, m.M22, 3);
            Assert.Equal(0.0, m.OffsetX, 2);
            Assert.Equal(0.0, m.OffsetY, 2);
        }

        [Fact]
        public async Task RotateX_WithoutPerspective_ProjectsToExactCosineYScale()
        {
            var divBox = await FindDivBox("transform: rotateX(60deg); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(1.0, m.M11, 3);
            Assert.Equal(Math.Cos(60.0 * Math.PI / 180.0), m.M22, 3);
        }

        [Fact]
        public async Task Rotate3d_AroundZAxis_MatchesPlainRotate2D()
        {
            var rotate2d = await FindDivBox("transform: rotate(45deg); transform-origin: 0 0;");
            var rotate3d = await FindDivBox("transform: rotate3d(0, 0, 1, 45deg); transform-origin: 0 0;");

            var a = rotate2d.ActualTransformMatrix;
            var b = rotate3d.ActualTransformMatrix;

            Assert.Equal(a.M11, b.M11, 3);
            Assert.Equal(a.M12, b.M12, 3);
            Assert.Equal(a.M21, b.M21, 3);
            Assert.Equal(a.M22, b.M22, 3);
        }

        [Fact]
        public async Task TranslateZ_WithoutPerspective_IsNoOpOnProjectedMatrix()
        {
            var divBox = await FindDivBox("transform: translateZ(500px); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.False(divBox.IsTransformed);
            Assert.Equal(1, m.M11, 3);
            Assert.Equal(1, m.M22, 3);
            Assert.Equal(0, m.OffsetX, 2);
            Assert.Equal(0, m.OffsetY, 2);
        }

        [Fact]
        public async Task Translate3d_ZComponent_DroppedWithoutPerspective()
        {
            var divBox = await FindDivBox("transform: translate3d(10px, 20px, 500px); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(10, m.OffsetX, 2);
            Assert.Equal(20, m.OffsetY, 2);
        }

        [Fact]
        public async Task Matrix3d_PureTranslation_MatchesTranslate2D()
        {
            var divBox = await FindDivBox(
                "transform: matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,0, 30,40,0,1); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(1, m.M11, 3);
            Assert.Equal(0, m.M12, 3);
            Assert.Equal(0, m.M21, 3);
            Assert.Equal(1, m.M22, 3);
            Assert.Equal(30, m.OffsetX, 2);
            Assert.Equal(40, m.OffsetY, 2);
        }

        // --- perspective() approximation ---

        [Fact]
        public async Task Perspective_ForeshortensRotateY_AndConvergesAtLargeDistance()
        {
            var noPerspective = await FindDivBox("transform: rotateY(45deg); transform-origin: 0 0;");
            var nearPerspective = await FindDivBox("transform: perspective(300px) rotateY(45deg); transform-origin: 0 0;");
            var farPerspective = await FindDivBox("transform: perspective(100000px) rotateY(45deg); transform-origin: 0 0;");

            var baseline = noPerspective.ActualTransformMatrix.M11;
            var near = nearPerspective.ActualTransformMatrix.M11;
            var far = farPerspective.ActualTransformMatrix.M11;

            Assert.NotEqual(baseline, near, 3);
            Assert.Equal(baseline, far, 3);
        }

        [Fact]
        public async Task Perspective_NonPositiveDistance_IsIgnored()
        {
            var divBox = await FindDivBox("transform: perspective(0px) rotate(30deg); transform-origin: 0 0;");
            var plain = await FindDivBox("transform: rotate(30deg); transform-origin: 0 0;");

            var m = divBox.ActualTransformMatrix;
            var p = plain.ActualTransformMatrix;

            Assert.Equal(p.M11, m.M11, 3);
            Assert.Equal(p.M12, m.M12, 3);
        }

        // --- Non-inheritance ---

        [Fact]
        public async Task Transform_IsNotInherited()
        {
            var html = @"<!DOCTYPE html><html><head><style>
#parent { transform: rotate(45deg); }
#child { width: 50px; height: 50px; }
</style></head><body><div id=""parent""><div id=""child""></div></div></body></html>";

            var container = await LayoutHtml(html);
            var child = FindById(container.Root!, "child")!;

            Assert.False(child.IsTransformed);
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
            var container = await LayoutHtml(html);
            Assert.NotNull(container.Root);
            return FindByTag(container.Root!, "div")!;
        }

        private async Task<HtmlContainerInt> LayoutHtml(string html)
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

            return container;
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

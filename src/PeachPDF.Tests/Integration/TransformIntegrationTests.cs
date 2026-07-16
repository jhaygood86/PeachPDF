using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
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
            // ActualTransformMatrix treats the box's own top-left corner as local (0, 0) - it is
            // cached and computed once, independent of the box's actual page position (see
            // CssBox.Paint / RMatrix.RebaseOrigin for how the page position is re-applied at paint time).
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
            // 200x100 box, default transform-origin (50% 50% -> local 100,50): rotating around the
            // box's own center must leave the center point itself unmoved.
            var divBox = await FindDivBox("transform: rotate(37deg);");
            var m = divBox.ActualTransformMatrix;

            var (mappedX, mappedY) = MapPoint(m, 100, 50);

            Assert.Equal(100.0, mappedX, 1);
            Assert.Equal(50.0, mappedY, 1);
        }

        [Fact]
        public async Task TransformOrigin_TopLeft_IsFixedPointOfRotation()
        {
            var divBox = await FindDivBox("transform: rotate(50deg); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            var (mappedX, mappedY) = MapPoint(m, 0, 0);

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

        // --- perspective() is unsupported ---

        [Fact]
        public async Task Perspective_IsUnsupported_TreatedAsIdentity()
        {
            // perspective() is not supported (see docs/html-css-support.md) - it's ignored like any
            // other unrecognized function name, contributing identity to the composed transform.
            var withPerspective = await FindDivBox("transform: perspective(300px) rotateY(45deg); transform-origin: 0 0;");
            var plain = await FindDivBox("transform: rotateY(45deg); transform-origin: 0 0;");

            var m = withPerspective.ActualTransformMatrix;
            var p = plain.ActualTransformMatrix;

            Assert.Equal(p.M11, m.M11, 6);
            Assert.Equal(p.M22, m.M22, 6);
            Assert.Equal(p.OffsetX, m.OffsetX, 6);
            Assert.Equal(p.OffsetY, m.OffsetY, 6);
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

        // --- RMatrix.RebaseOrigin (page-position re-anchoring at paint time) ---

        [Fact]
        public void RebaseOrigin_Identity_StaysIdentityAnywhere()
        {
            var rebased = RMatrix.Identity.RebaseOrigin(1234, -567);
            Assert.True(rebased.IsIdentity);
        }

        [Fact]
        public void RebaseOrigin_MakesGivenPointAFixedPoint()
        {
            // A rotation built as if the box's own top-left were local (0,0) (transform-origin: 0 0),
            // when rebased to an arbitrary absolute page point, must leave that exact point unmoved -
            // this is the property that CssBox.Paint relies on to pivot correctly regardless of where
            // the box actually sits on the page.
            var local = new RMatrix(0, 1, -1, 0, 0, 0); // rotate(90deg) around local (0,0)
            var rebased = local.RebaseOrigin(347.5, -12.25);

            var mappedX = 347.5 * rebased.M11 + -12.25 * rebased.M21 + rebased.OffsetX;
            var mappedY = 347.5 * rebased.M12 + -12.25 * rebased.M22 + rebased.OffsetY;

            Assert.Equal(347.5, mappedX, 6);
            Assert.Equal(-12.25, mappedY, 6);
        }

        [Fact]
        public void RebaseOrigin_PureTranslation_IsUnaffectedByPagePosition()
        {
            // Translation commutes with the origin re-anchoring, so it must come out unchanged
            // regardless of what absolute point it's rebased to.
            var local = new RMatrix(1, 0, 0, 1, 50, 20);
            var rebased = local.RebaseOrigin(999, -333);

            Assert.Equal(50, rebased.OffsetX, 6);
            Assert.Equal(20, rebased.OffsetY, 6);
        }

        // --- Regression: paint-time pivot must use the box's actual page position ---
        //
        // ActualTransformMatrix is cached treating the box's own top-left as local (0, 0) - painting
        // draws in absolute page coordinates, and a box's page position can vary across repeated
        // paint passes (e.g. pagination), so CssBox.Paint re-anchors the pivot via RebaseOrigin right
        // before pushing it. This regression test drives the real Paint() pipeline (not just
        // ActualTransformMatrix) for a box positioned well away from the page's top-left corner, and
        // inspects the matrix actually handed to RGraphics.PushTransform.

        [Fact]
        public async Task Paint_RotationAroundOwnTopLeft_PivotsAroundActualPagePosition()
        {
            // The box sits at (150, 80)+ on the page (via margin), nowhere near the page origin.
            var html = """
                <!DOCTYPE html><html><head><style>
                div { width: 200px; height: 100px; margin: 80px 0 0 150px;
                      transform: rotate(30deg); transform-origin: 0 0; }
                </style></head><body><div></div></body></html>
                """;

            var container = await LayoutHtml(html);
            var divBox = FindByTag(container.Root!, "div")!;

            var spy = new SpyGraphics();
            await divBox.Paint(spy);

            Assert.NotNull(spy.LastPushedTransform);
            var pushed = spy.LastPushedTransform!.Value;

            // The box's own actual top-left corner on the page must be a fixed point of the
            // matrix that was really pushed to the graphics context.
            var mappedX = divBox.Bounds.X * pushed.M11 + divBox.Bounds.Y * pushed.M21 + pushed.OffsetX;
            var mappedY = divBox.Bounds.X * pushed.M12 + divBox.Bounds.Y * pushed.M22 + pushed.OffsetY;

            Assert.Equal(divBox.Bounds.X, mappedX, 1);
            Assert.Equal(divBox.Bounds.Y, mappedY, 1);
        }

        private sealed class SpyGraphics : RGraphics
        {
            public RMatrix? LastPushedTransform { get; private set; }

            public SpyGraphics() : base(new PdfSharpAdapter(), new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void PushTransform(RMatrix matrix) => LastPushedTransform = matrix;
            public override void PopTransform() { }
            public override void PushClip(RRect rect) => _clipStack.Push(rect);
            public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => null!;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override RSize MeasureString(string str, RFont font) => new(0, 12);
            public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = 0;
            }
            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl) { }
            public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2) { }
            public override void DrawRectangle(RPen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) { }
            public override void DrawImage(RImage image, RRect destRect, RRect srcRect) { }
            public override void DrawImage(RImage image, RRect destRect) { }
            public override void DrawPath(RPen pen, RGraphicsPath path) { }
            public override void DrawPath(RBrush brush, RGraphicsPath path) { }
            public override void DrawPolygon(RBrush brush, RPoint[] points) { }
            public override void Dispose() { }
        }

        // --- Helpers ---

        // ActualTransformMatrix treats the box's own top-left corner as local (0, 0), so probe
        // points here are box-local, not absolute page coordinates (see RebaseOrigin tests below
        // for the page-space behavior applied at paint time).
        private static (double X, double Y) MapPoint(RMatrix m, double x, double y) =>
            (x * m.M11 + y * m.M21 + m.OffsetX, x * m.M12 + y * m.M22 + m.OffsetY);

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

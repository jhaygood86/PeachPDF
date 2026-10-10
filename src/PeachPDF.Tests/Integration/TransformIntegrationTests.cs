using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System.Numerics;
using PeachPDF.Tests.TestSupport;

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
            Assert.Equal(0, m.M31);
            Assert.Equal(0, m.M32);
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
            var divBox = await FindDivBox("transform: translate(50pt, 20pt);");
            var m = divBox.ActualTransformMatrix;

            Assert.True(divBox.IsTransformed);
            Assert.Equal(1, m.M11, 3);
            Assert.Equal(0, m.M12, 3);
            Assert.Equal(0, m.M21, 3);
            Assert.Equal(1, m.M22, 3);
            Assert.Equal(50, m.M31, 3);
            Assert.Equal(20, m.M32, 3);
        }

        [Fact]
        public async Task Scale_SetsLinearPart()
        {
            // ActualTransformMatrix treats the box's own top-left corner as local (0, 0) - it is
            // cached and computed once, independent of the box's actual page position (see
            // CssBox.Paint / Matrix3x2Extensions.RebaseOrigin for how the page position is re-applied at paint time).
            var divBox = await FindDivBox("transform: scale(2, 3); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(2, m.M11, 3);
            Assert.Equal(0, m.M12, 3);
            Assert.Equal(0, m.M21, 3);
            Assert.Equal(3, m.M22, 3);
            Assert.Equal(0, m.M31, 2);
            Assert.Equal(0, m.M32, 2);
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

        [Theory]
        [InlineData("rotate(0)")]
        [InlineData("rotateX(0)")]
        [InlineData("rotateY(0)")]
        [InlineData("rotateZ(0)")]
        [InlineData("rotate3d(1, 0, 0, 0)")]
        [InlineData("skew(0)")]
        [InlineData("skew(0, 0)")]
        [InlineData("skewX(0)")]
        [InlineData("skewY(0)")]
        public async Task UnitlessZeroAngle_IsIdentity(string function)
        {
            var divBox = await FindDivBox($"transform: {function}; transform-origin: 0 0;");

            Assert.False(divBox.IsTransformed);
        }

        [Fact]
        public async Task UnitlessZeroAngle_DoesNotDiscardTheRestOfTheList()
        {
            var divBox = await FindDivBox("transform: rotate(0) translate(50pt, 20pt) skewX(0);");
            var m = divBox.ActualTransformMatrix;

            Assert.True(divBox.IsTransformed);
            Assert.Equal(1, m.M11, 3);
            Assert.Equal(0, m.M12, 3);
            Assert.Equal(0, m.M21, 3);
            Assert.Equal(1, m.M22, 3);
            Assert.Equal(50, m.M31, 3);
            Assert.Equal(20, m.M32, 3);
        }

        [Fact]
        public async Task MatrixPassthrough_MapsLinearPartDirectly_AndTranslationIsInCssPixels()
        {
            var divBox = await FindDivBox("transform: matrix(1, 0, 0, 1, 40, 80);");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(1, m.M11, 3);
            Assert.Equal(0, m.M12, 3);
            Assert.Equal(0, m.M21, 3);
            Assert.Equal(1, m.M22, 3);
            // e and f are CSS pixels (1px = 0.75pt), like translate()'s.
            Assert.Equal(30, m.M31, 3);
            Assert.Equal(60, m.M32, 3);
        }

        [Fact]
        public async Task Matrix_TranslationMatchesTheEquivalentTranslate()
        {
            var viaMatrix = (await FindDivBox("transform: matrix(1, 0, 0, 1, 10, 5); transform-origin: 0 0;")).ActualTransformMatrix;
            var viaTranslate = (await FindDivBox("transform: translate(10px, 5px); transform-origin: 0 0;")).ActualTransformMatrix;

            Assert.Equal(viaTranslate.M31, viaMatrix.M31, 3);
            Assert.Equal(viaTranslate.M32, viaMatrix.M32, 3);
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
            var divBox = await FindDivBox("transform: translate(50pt, 0) rotate(90deg); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(50.0, m.M31, 2);
            Assert.Equal(0.0, m.M32, 2);
        }

        [Fact]
        public async Task CompositionOrder_RotateThenTranslate_OrbitsAroundOriginalOrigin()
        {
            // Hand-verified: with transform-origin 0 0, "rotate(90deg) translate(50,0)" means
            // translate is applied first (moves origin point to (50,0)), rotate applied last,
            // swinging that point 90deg clockwise around the original origin to (0, 50).
            var divBox = await FindDivBox("transform: rotate(90deg) translate(50pt, 0); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(0.0, m.M31, 2);
            Assert.Equal(50.0, m.M32, 2);
        }

        // --- 3D exactness (no perspective involved) ---

        [Fact]
        public async Task RotateY_WithoutPerspective_ProjectsToExactCosineXScale()
        {
            var divBox = await FindDivBox("transform: rotateY(60deg); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(Math.Cos(60.0 * Math.PI / 180.0), m.M11, 3);
            Assert.Equal(1.0, m.M22, 3);
            Assert.Equal(0.0, m.M31, 2);
            Assert.Equal(0.0, m.M32, 2);
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
            Assert.Equal(0, m.M31, 2);
            Assert.Equal(0, m.M32, 2);
        }

        [Fact]
        public async Task Translate3d_ZComponent_DroppedWithoutPerspective()
        {
            var divBox = await FindDivBox("transform: translate3d(10pt, 20pt, 500pt); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;

            Assert.Equal(10, m.M31, 2);
            Assert.Equal(20, m.M32, 2);
        }

        [Fact]
        public async Task Matrix_WithALinearPart_KeepsItUnscaledAndConvertsOnlyTheTranslation()
        {
            // The conversion is a change of coordinate system, not a scaling of the whole matrix.
            var viaMatrix = (await FindDivBox("transform: matrix(2, .2, .3, 1.5, 40, 20); transform-origin: 0 0;")).ActualTransformMatrix;
            var viaTranslate = (await FindDivBox("transform: translate(40px, 20px) matrix(2, .2, .3, 1.5, 0, 0); transform-origin: 0 0;")).ActualTransformMatrix;

            Assert.Equal(2, viaMatrix.M11, 3);
            Assert.Equal(0.2, viaMatrix.M12, 3);
            Assert.Equal(0.3, viaMatrix.M21, 3);
            Assert.Equal(1.5, viaMatrix.M22, 3);
            Assert.Equal(30, viaMatrix.M31, 3);
            Assert.Equal(15, viaMatrix.M32, 3);
            Assert.Equal(viaTranslate.M31, viaMatrix.M31, 3);
            Assert.Equal(viaTranslate.M32, viaMatrix.M32, 3);
        }

        [Fact]
        public async Task Matrix3d_PerspectiveOnly_MatchesPerspectiveFunction_AtTheDefaultOrigin()
        {
            // With the default (centre) origin the box-local origin translations are baked into the 4x4 too.
            var viaMatrix = (await FindDivBox("transform: matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,-0.0033333333, 0,0,0,1);")).ActualTransform4;
            var viaPerspective = (await FindDivBox("transform: perspective(300px);")).ActualTransform4;

            Assert.NotNull(viaMatrix);
            Assert.NotNull(viaPerspective);
            var a = viaMatrix!.Value;
            var b = viaPerspective!.Value;
            Assert.Equal(b.M11, a.M11, 4); Assert.Equal(b.M12, a.M12, 4); Assert.Equal(b.M13, a.M13, 4); Assert.Equal(b.M14, a.M14, 4);
            Assert.Equal(b.M21, a.M21, 4); Assert.Equal(b.M22, a.M22, 4); Assert.Equal(b.M23, a.M23, 4); Assert.Equal(b.M24, a.M24, 4);
            Assert.Equal(b.M31, a.M31, 4); Assert.Equal(b.M32, a.M32, 4); Assert.Equal(b.M33, a.M33, 4); Assert.Equal(b.M34, a.M34, 4);
            Assert.Equal(b.M41, a.M41, 3); Assert.Equal(b.M42, a.M42, 3); Assert.Equal(b.M43, a.M43, 3); Assert.Equal(b.M44, a.M44, 4);
        }

        [Fact]
        public async Task Matrix3d_PureTranslation_MatchesTranslate2D()
        {
            var divBox = await FindDivBox(
                "transform: matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,0, 40,80,0,1); transform-origin: 0 0;");
            var m = divBox.ActualTransformMatrix;
            var viaTranslate = (await FindDivBox("transform: translate(40px, 80px); transform-origin: 0 0;")).ActualTransformMatrix;

            Assert.Equal(1, m.M11, 3);
            Assert.Equal(0, m.M12, 3);
            Assert.Equal(0, m.M21, 3);
            Assert.Equal(1, m.M22, 3);
            // The 13th and 14th values are CSS pixels (1px = 0.75pt), like translate()'s.
            Assert.Equal(30, m.M31, 2);
            Assert.Equal(60, m.M32, 2);
            Assert.Equal(viaTranslate.M31, m.M31, 3);
            Assert.Equal(viaTranslate.M32, m.M32, 3);
        }

        [Fact]
        public async Task Matrix3d_ZTranslation_IsInCssPixels()
        {
            // The flat Matrix3x2 drops z, so read the 4x4 the box keeps for the projection.
            var viaMatrix = (await FindDivBox("transform: matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,40,1); transform-origin: 0 0;")).ActualTransform4;
            var viaTranslate = (await FindDivBox("transform: translate3d(0, 0, 40px); transform-origin: 0 0;")).ActualTransform4;

            Assert.NotNull(viaMatrix);
            Assert.NotNull(viaTranslate);
            Assert.Equal(30, viaMatrix!.Value.M43, 3);
            Assert.Equal(viaTranslate!.Value.M43, viaMatrix.Value.M43, 3);
        }

        [Fact]
        public async Task Matrix3d_PerspectiveTerms_ArePerCssPixel_AndMatchPerspectiveFunction()
        {
            // perspective(300px) is spec-defined as matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,-1/300, 0,0,0,1).
            var viaMatrix = (await FindDivBox(
                $"transform: matrix3d(1,0,0,0.001, 0,1,0,0.002, 0,0,1,{(-1.0 / 300).ToString(System.Globalization.CultureInfo.InvariantCulture)}, 0,0,0,1); transform-origin: 0 0;")).ActualTransform4;
            var viaPerspective = (await FindDivBox("transform: perspective(300px); transform-origin: 0 0;")).ActualTransform4;
            var viaPerspectiveOnly = (await FindDivBox(
                $"transform: matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,{(-1.0 / 300).ToString(System.Globalization.CultureInfo.InvariantCulture)}, 0,0,0,1); transform-origin: 0 0;")).ActualTransform4;

            Assert.NotNull(viaMatrix);
            Assert.NotNull(viaPerspective);
            Assert.NotNull(viaPerspectiveOnly);
            Assert.Equal(viaPerspective!.Value.M34, viaPerspectiveOnly!.Value.M34, 6);
            Assert.Equal(-1.0 / 225, viaPerspectiveOnly.Value.M34, 6);
            // The x and y terms scale the same way (per px -> per pt).
            Assert.Equal(0.001 / 0.75, viaMatrix!.Value.M14, 6);
            Assert.Equal(0.002 / 0.75, viaMatrix.Value.M24, 6);
            Assert.Equal(-1.0 / 225, viaMatrix.Value.M34, 6);
        }

        // --- perspective() ---

        [Fact]
        public async Task Perspective_ContributesADivisorToTheBoxsTransform()
        {
            var withPerspective = await FindDivBox("transform: perspective(300px) rotateY(45deg); transform-origin: 0 0;");
            var plain = await FindDivBox("transform: rotateY(45deg); transform-origin: 0 0;");

            var m4 = withPerspective.ActualTransform4;
            Assert.NotNull(m4);

            // rotateY(45deg) on its own keeps the plane affine (no divisor); perspective() is what makes it projective.
            Assert.Equal(0, plain.ActualTransform4!.Value.M14, 6);
            Assert.NotEqual(0, m4!.Value.M14, 6);
        }

        [Fact]
        public async Task PerspectiveOfZero_ContributesNothing()
        {
            var zero = await FindDivBox("transform: perspective(0px) rotateY(45deg);");
            var plain = await FindDivBox("transform: rotateY(45deg);");

            Assert.Equal(plain.ActualTransform4!.Value.M14, zero.ActualTransform4!.Value.M14, 6);
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

        // --- Matrix3x2Extensions.RebaseOrigin (page-position re-anchoring at paint time) ---

        [Fact]
        public void RebaseOrigin_Identity_StaysIdentityAnywhere()
        {
            var rebased = Matrix3x2.Identity.RebaseOrigin(1234, -567);
            Assert.True(rebased.IsIdentity);
        }

        [Fact]
        public void RebaseOrigin_MakesGivenPointAFixedPoint()
        {
            // A rotation built as if the box's own top-left were local (0,0) (transform-origin: 0 0),
            // when rebased to an arbitrary absolute page point, must leave that exact point unmoved -
            // this is the property that CssBox.Paint relies on to pivot correctly regardless of where
            // the box actually sits on the page.
            var local = new Matrix3x2(0, 1, -1, 0, 0, 0); // rotate(90deg) around local (0,0)
            var rebased = local.RebaseOrigin(347.5, -12.25);

            var mappedX = 347.5 * rebased.M11 + -12.25 * rebased.M21 + rebased.M31;
            var mappedY = 347.5 * rebased.M12 + -12.25 * rebased.M22 + rebased.M32;

            Assert.Equal(347.5, mappedX, 6);
            Assert.Equal(-12.25, mappedY, 6);
        }

        [Fact]
        public void RebaseOrigin_PureTranslation_IsUnaffectedByPagePosition()
        {
            // Translation commutes with the origin re-anchoring, so it must come out unchanged
            // regardless of what absolute point it's rebased to.
            var local = new Matrix3x2(1, 0, 0, 1, 50, 20);
            var rebased = local.RebaseOrigin(999, -333);

            Assert.Equal(50, rebased.M31, 6);
            Assert.Equal(20, rebased.M32, 6);
        }

        // --- Regression: paint-time pivot must use the box's actual page position ---
        //
        // ActualTransformMatrix is cached treating the box's own top-left as local (0, 0) - painting
        // draws in absolute page coordinates, and a box's page position can vary across repeated
        // paint passes (e.g. pagination), so CssBox.Paint re-anchors the pivot via RebaseOrigin right
        // before pushing it. This regression test drives the real Paint() pipeline (not just
        // ActualTransformMatrix) for a box positioned well away from the page's top-left corner, and
        // inspects the matrix actually handed to Canvas.PushTransform.

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
            FragmentPaintHarness.PaintBox(container, divBox, spy);

            Assert.NotNull(spy.LastPushedTransform);
            var pushed = spy.LastPushedTransform!.Value;

            // The box's own actual top-left corner on the page must be a fixed point of the
            // matrix that was really pushed to the graphics context.
            var mappedX = divBox.Bounds.X * pushed.M11 + divBox.Bounds.Y * pushed.M21 + pushed.M31;
            var mappedY = divBox.Bounds.X * pushed.M12 + divBox.Bounds.Y * pushed.M22 + pushed.M32;

            Assert.Equal(divBox.Bounds.X, mappedX, 1);
            Assert.Equal(divBox.Bounds.Y, mappedY, 1);
        }

        // --- Regression: a transformed inline-level box is pivoted where it sits in its line ---
        //
        // An `inline-block` that fits on its line (and an inline replaced element) is flowed into the
        // line rather than laid out as a box, so its CssBox.Location is never assigned and reads (0, 0).
        // WholeBoxRect - what the transform pivot is re-anchored to - was built from that, so the box was
        // pivoted around the page origin and rotated/translated off the page: it was laid out, and
        // nothing was painted where it belongs.

        private const string OnePixelGif = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

        [Theory]
        [InlineData("<span style='display:inline-block;width:40px;height:40px;transform:rotate(45deg)'></span>", "span")]
        [InlineData("<span style='display:inline-block;width:40px;height:40px;transform:rotate(45deg)'></span> text after", "span")]
        [InlineData("<img alt='' src='" + OnePixelGif + "' style='display:inline-block;width:40px;height:40px;transform:rotate(45deg)'>", "img")]
        [InlineData("<span style='display:inline-block;width:40px;height:40px;transform:scale(2)'></span>", "span")]
        [InlineData("<span style='display:inline-block;padding:4px 8px;transform:rotate(-20deg)'>Label</span>", "span")]   // sized by its content
        [InlineData("<img alt='' src='" + OnePixelGif + "' style='width:40px;height:40px;transform:rotate(45deg)'>", "img")]   // default display: inline
        public async Task Paint_TransformedInlineBlock_PivotsAroundItsPositionInTheLine(string content, string tag)
        {
            // Well away from the page origin, so a pivot taken from an unassigned (0, 0) Location is wrong.
            var html = "<!DOCTYPE html><html><body style='margin:0'>" +
                       "<div style='margin:90px 0 0 120px;height:100px'>" + content + "</div></body></html>";

            var container = await LayoutHtml(html);
            var box = FindByTag(container.Root!, tag)!;
            var fragment = FragmentPaintHarness.FragmentOf(container, box);

            // Its whole border box is the rectangle it was flowed into, not a box at (0, 0).
            Assert.Equal(fragment.Rect.X, fragment.WholeBoxRect.X, 1);
            Assert.Equal(fragment.Rect.Y, fragment.WholeBoxRect.Y, 1);
            Assert.True(fragment.WholeBoxRect.Width > 10, $"has the width the line gave it: {fragment.WholeBoxRect.Width}");
            Assert.True(fragment.WholeBoxRect.X >= 90, $"left edge on the page, not at the origin: {fragment.WholeBoxRect.X}");

            var spy = new SpyGraphics();
            FragmentPaintHarness.PaintBox(container, box, spy);

            Assert.NotNull(spy.LastPushedTransform);
            var pushed = spy.LastPushedTransform!.Value;

            // A rotation or scale about the centre leaves the box's own centre where it is.
            var cx = fragment.WholeBoxRect.X + fragment.WholeBoxRect.Width / 2;
            var cy = fragment.WholeBoxRect.Y + fragment.WholeBoxRect.Height / 2;
            Assert.Equal(cx, cx * pushed.M11 + cy * pushed.M21 + pushed.M31, 1);
            Assert.Equal(cy, cx * pushed.M12 + cy * pushed.M22 + pushed.M32, 1);
        }

        [Theory]
        [InlineData("transform-origin:left top;transform:rotate(30deg)", 0.0, 0.0)]       // a keyword origin: the box's own top-left is the fixed point
        [InlineData("transform-origin:right bottom;transform:rotate(30deg)", 1.0, 1.0)]
        [InlineData("transform-origin:25% 75%;transform:scale(2)", 0.25, 0.75)]           // percentages of the box's real size
        [InlineData("transform-origin:center center;transform:rotate(30deg)", 0.5, 0.5)]
        [InlineData("transform-origin:top left;transform:rotate(30deg)", 0.0, 0.0)]       // the vertical keyword written first
        [InlineData("transform-origin:top center;transform:rotate(30deg)", 0.5, 0.0)]
        [InlineData("transform-origin:bottom center;transform:rotate(30deg)", 0.5, 1.0)]
        public async Task Paint_TransformedContentSizedInlineBlock_ResolvesTransformOriginAgainstItsLineRectangle(
            string style, double fractionX, double fractionY)
        {
            var html = "<!DOCTYPE html><html><body style='margin:0'><div style='margin:90px 0 0 120px;height:100px'>" +
                       $"<span style='display:inline-block;padding:4px 8px;{style}'>Label</span></div></body></html>";

            var container = await LayoutHtml(html);
            var box = FindByTag(container.Root!, "span")!;
            var rect = FragmentPaintHarness.FragmentOf(container, box).WholeBoxRect;

            var spy = new SpyGraphics();
            FragmentPaintHarness.PaintBox(container, box, spy);
            var m = spy.LastPushedTransform!.Value;

            // The transform-origin point is the one point the matrix leaves where it is.
            var px = rect.X + fractionX * rect.Width;
            var py = rect.Y + fractionY * rect.Height;
            Assert.Equal(px, px * m.M11 + py * m.M21 + m.M31, 1);
            Assert.Equal(py, px * m.M12 + py * m.M22 + m.M32, 1);
        }

        [Theory]
        [InlineData("translate(50%, 100%)", 0.5, 1.0)]
        [InlineData("translateX(50%)", 0.5, 0.0)]
        [InlineData("translateY(100%)", 0.0, 1.0)]
        [InlineData("translate3d(50%, 100%, 0)", 0.5, 1.0)]
        public async Task Paint_TransformedContentSizedInlineBlock_ResolvesTranslatePercentageAgainstItsLineRectangle(
            string transform, double fractionX, double fractionY)
        {
            var html = "<!DOCTYPE html><html><body style='margin:0'><div style='margin:90px 0 0 120px;height:100px'>" +
                       $"<span style='display:inline-block;padding:4px 8px;transform:{transform}'>Label</span></div></body></html>";

            var container = await LayoutHtml(html);
            var box = FindByTag(container.Root!, "span")!;
            var rect = FragmentPaintHarness.FragmentOf(container, box).WholeBoxRect;

            var spy = new SpyGraphics();
            FragmentPaintHarness.PaintBox(container, box, spy);
            var m = spy.LastPushedTransform!.Value;

            Assert.Equal(fractionX * rect.Width, m.M31, 1);
            Assert.Equal(fractionY * rect.Height, m.M32, 1);
        }

        [Fact]
        public async Task ParseTransformOrigin_WithNoValue_IsTheCentreOfAFlowedInlineBlocksLineRectangle()
        {
            var container = await LayoutHtml(
                "<!DOCTYPE html><html><body style='margin:0'><div style='margin:90px 0 0 120px'>" +
                "<span style='display:inline-block;padding:4px 8px'>Label</span></div></body></html>");
            var box = FindByTag(container.Root!, "span")!;
            var rect = FragmentPaintHarness.FragmentOf(container, box).WholeBoxRect;

            var (x, y, z) = PeachPDF.Html.Core.Parse.CssValueParser.ParseTransformOriginPublic("", box);

            Assert.Equal(rect.Width / 2, x, 1);
            Assert.Equal(rect.Height / 2, y, 1);
            Assert.Equal(0, z);
        }

        [Theory]
        [InlineData("top left", 0.0, 0.0)]          // the vertical keyword first, as the CSS grammar allows
        [InlineData("top center", 0.5, 0.0)]
        [InlineData("bottom center", 0.5, 1.0)]
        [InlineData("bottom right", 1.0, 1.0)]
        [InlineData("left", 0.0, 0.5)]
        [InlineData("top", 0.5, 0.0)]               // a lone vertical keyword leaves x at the centre
        [InlineData("bottom", 0.5, 1.0)]
        [InlineData("right", 1.0, 0.5)]
        public async Task ParseTransformOrigin_Keywords_ResolveAgainstAFlowedInlineBlocksLineRectangle(
            string origin, double fractionX, double fractionY)
        {
            var container = await LayoutHtml(
                "<!DOCTYPE html><html><body style='margin:0'><div style='margin:90px 0 0 120px'>" +
                "<span style='display:inline-block;padding:4px 8px'>Label</span></div></body></html>");
            var box = FindByTag(container.Root!, "span")!;
            var rect = FragmentPaintHarness.FragmentOf(container, box).WholeBoxRect;

            var (x, y, _) = PeachPDF.Html.Core.Parse.CssValueParser.ParseTransformOriginPublic(origin, box);

            Assert.Equal(fractionX * rect.Width, x, 1);
            Assert.Equal(fractionY * rect.Height, y, 1);
        }

        // --- Regression: a non-invertible transform paints nothing instead of throwing NotInvertible ---
        //
        // css-transforms-1: an element whose matrix has no inverse has no visible area and is not rendered,
        // its layout box unchanged. The PDF writer inverts the CTM it realizes, so pushing the matrix threw
        // out of FragmentPainter.PaintFragment and no PDF was written at all.

        [Theory]
        [InlineData("matrix(0,0,0,0,0,0)")]
        [InlineData("scale(0)")]
        [InlineData("scaleX(0)")]
        [InlineData("matrix(1,2,2,4,0,0)")]    // rank 1: collapses onto a line
        [InlineData("rotateX(90deg)")]         // an edge-on plane projects to a line
        public async Task Paint_NonInvertibleTransform_PaintsNothingForTheBoxOrItsSubtree(string transform)
        {
            var container = await LayoutHtml(
                $"<!DOCTYPE html><html><body style='margin:0'><div id='t' style='transform:{transform}; width:50pt; height:50pt; background:#c33'>" +
                "<p style='background:#33c'>x</p></div><p>after</p></body></html>");
            var box = FindByTag(container.Root!, "div")!;

            var spy = new SpyGraphics();
            FragmentPaintHarness.PaintBox(container, box, spy);

            Assert.Null(spy.LastPushedTransform);
            Assert.Equal(0, spy.FilledRectangles);
            Assert.Equal(0, spy.StringsDrawn);
        }

        [Fact]
        public async Task Paint_NonInvertibleTransform_LeavesTheLayoutBoxUnchanged()
        {
            var plain = await LayoutHtml("<!DOCTYPE html><html><body style='margin:0'><div style='width:50pt;height:50pt'>x</div><p>after</p></body></html>");
            var squashed = await LayoutHtml("<!DOCTYPE html><html><body style='margin:0'><div style='transform:scale(0);width:50pt;height:50pt'>x</div><p>after</p></body></html>");

            var expected = FindByTag(plain.Root!, "p")!.Bounds;
            var actual = FindByTag(squashed.Root!, "p")!.Bounds;

            Assert.Equal(expected.Y, actual.Y, 3);
            Assert.Equal(expected.Height, actual.Height, 3);
        }

        [Fact]
        public async Task Paint_InvertibleTransform_StillPaintsTheBox()
        {
            // The control: the guard must not swallow a transform that merely shrinks the box.
            var container = await LayoutHtml(
                "<!DOCTYPE html><html><body style='margin:0'><div style='transform:scale(0.5); width:50pt; height:50pt; background:#c33'></div></body></html>");
            var box = FindByTag(container.Root!, "div")!;

            var spy = new SpyGraphics();
            FragmentPaintHarness.PaintBox(container, box, spy);

            Assert.NotNull(spy.LastPushedTransform);
            Assert.True(spy.FilledRectangles > 0);
        }

        [Theory]
        [InlineData("matrix(0,0,0,0,0,0)")]
        [InlineData("scale(0)")]
        [InlineData("rotateX(90deg)")]
        public async Task GeneratePdf_NonInvertibleTransform_StillWritesTheDocument(string transform)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };

            var doc = await generator.GeneratePdf(
                $"<!DOCTYPE html><html><body><div style='transform:{transform}; width:50pt; height:50pt; background:#c33'>x</div><p>after</p></body></html>",
                config);

            using var stream = new MemoryStream();
            doc.Save(stream);
            Assert.True(stream.Length > 0);
            Assert.Equal(1, doc.PageCount);
        }

        [Theory]
        // Each scale is invertible alone; the cumulative CTM the writer inverts is not.
        [InlineData("<div style='transform:scale(0.00001);width:50pt;height:50pt'><div style='transform:scale(0.00001);background:#c33;width:50pt;height:50pt'>x</div></div>")]
        // An edge-on plane seen through its parent's perspective.
        [InlineData("<div style='perspective:200pt'><div style='transform:rotateY(90deg);width:50pt;height:50pt;background:#c33'>x</div></div>")]
        [InlineData("<div style='perspective:200pt'><div style='transform:perspective(100pt) rotateX(90deg);width:50pt;height:50pt;background:#c33'>x</div></div>")]
        // SVG pushes its own transforms, outside the painter's guard: the writer's tolerance is all that stands between it and an abort.
        [InlineData("<svg width='100' height='100'><rect width='50' height='50' fill='#c33' transform='scale(0)'/><g transform='matrix(0 0 0 0 0 0)'><circle r='20' cx='50' cy='50'/></g></svg>")]
        // A 3D rendering context with an edge-on member.
        [InlineData("<div style='transform-style:preserve-3d;transform:rotateX(20deg);width:80pt;height:80pt'><div style='transform:rotateY(90deg);width:50pt;height:50pt;background:#c33'>x</div><div style='transform:translateZ(10pt);width:50pt;height:50pt;background:#33c'>y</div></div>")]
        public async Task GeneratePdf_DegenerateCompoundTransforms_StillWriteTheDocument(string body)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };

            var doc = await generator.GeneratePdf($"<!DOCTYPE html><html><body>{body}<p>after</p></body></html>", config);

            using var stream = new MemoryStream();
            doc.Save(stream);
            Assert.True(stream.Length > 0);
        }

        // The guard every extra transform the painter pushes (the affine-after-perspective one, the no-raster fallback, the text
        // supplied over a warped bitmap) goes through.

        public static TheoryData<Matrix3x2> PushableMatrices => new()
        {
            Matrix3x2.Identity,
            Matrix3x2.CreateRotation(0.5f),
            Matrix3x2.CreateScale(0.001f),                // small but visible; its float determinant (1e-6) is still well clear
            Matrix3x2.CreateScale(-1f, 1f),               // a mirror
            Matrix3x2.CreateTranslation(1e6f, -1e6f),
        };

        public static TheoryData<Matrix3x2> UnpushableMatrices => new()
        {
            new Matrix3x2(),                              // all zero
            Matrix3x2.CreateScale(0f),
            Matrix3x2.CreateScale(1f, 0f),
            new Matrix3x2(1, 2, 2, 4, 0, 0),              // rank 1
            Matrix3x2.CreateScale(1e-8f),                 // determinant 1e-16: nothing visible
            new Matrix3x2(1, 0, 0, 1, float.NaN, 0),
            new Matrix3x2(1, 0, 0, 1, 0, float.PositiveInfinity),
            new Matrix3x2(float.NaN, 0, 0, 1, 0, 0),
            new Matrix3x2(float.MaxValue, 0, 0, float.PositiveInfinity, 0, 0),
        };

        [Theory]
        [MemberData(nameof(PushableMatrices))]
        public void TryPushTransform_InvertibleMatrix_IsPushed(Matrix3x2 matrix)
        {
            var spy = new SpyGraphics();

            Assert.True(PeachPDF.Html.Core.Paint.FragmentPainter.TryPushTransform(spy, matrix));
            Assert.Equal(matrix, spy.LastPushedTransform);
        }

        [Theory]
        [MemberData(nameof(UnpushableMatrices))]
        public void TryPushTransform_NonInvertibleOrNonFiniteMatrix_IsNotPushed(Matrix3x2 matrix)
        {
            var spy = new SpyGraphics();

            Assert.False(PeachPDF.Html.Core.Paint.FragmentPainter.TryPushTransform(spy, matrix));
            Assert.Null(spy.LastPushedTransform);
        }

        private const string PaintedBoxHtml =
            "<!DOCTYPE html><html><body style='margin:0'><div style='width:50pt;height:50pt;background:#c33'>x</div></body></html>";

        [Fact]
        public async Task PaintUnderTransform_InvertibleMatrix_PushesItAndPaintsTheFragment()
        {
            var container = await LayoutHtml(PaintedBoxHtml);
            var fragment = FragmentPaintHarness.FragmentOf(container, FindByTag(container.Root!, "div")!);
            var matrix = Matrix3x2.CreateScale(0.5f);

            var spy = new SpyGraphics();
            new PeachPDF.Html.Core.Paint.FragmentPainter(container).PaintUnderTransform(spy, fragment, matrix);

            Assert.Equal(matrix, spy.LastPushedTransform);
            Assert.True(spy.FilledRectangles > 0);
            Assert.Equal(1, spy.PopTransformCount);
        }

        [Fact]
        public async Task PaintUnderTransform_SingularMatrix_PaintsNothing()
        {
            var container = await LayoutHtml(PaintedBoxHtml);
            var fragment = FragmentPaintHarness.FragmentOf(container, FindByTag(container.Root!, "div")!);

            var spy = new SpyGraphics();
            new PeachPDF.Html.Core.Paint.FragmentPainter(container).PaintUnderTransform(spy, fragment, Matrix3x2.CreateScale(0f));

            Assert.Null(spy.LastPushedTransform);
            Assert.Equal(0, spy.FilledRectangles);
            Assert.Equal(0, spy.PopTransformCount);
        }

        [Fact]
        public async Task SupplyWarpedText_InvertibleLinearisation_PushesItAndPopsAfterwards()
        {
            var container = await LayoutHtml(PaintedBoxHtml);
            var fragment = FragmentPaintHarness.FragmentOf(container, FindByTag(container.Root!, "div")!);

            var spy = new SpyGraphics();
            new PeachPDF.Html.Core.Paint.FragmentPainter(container).SupplyWarpedText(spy, fragment, PeachDrawing.Homography.Identity);

            Assert.NotNull(spy.LastPushedTransform);
            Assert.Equal(1, spy.PopTransformCount);
        }

        [Fact]
        public async Task SupplyWarpedText_EdgeOnPlane_SuppliesNoText()
        {
            var container = await LayoutHtml(PaintedBoxHtml);
            var fragment = FragmentPaintHarness.FragmentOf(container, FindByTag(container.Root!, "div")!);

            // The plane collapsed onto the x axis: its linearisation has no inverse.
            var edgeOn = new PeachDrawing.Homography(1, 0, 0, 0, 0, 0, 0, 0, 1);

            var spy = new SpyGraphics();
            new PeachPDF.Html.Core.Paint.FragmentPainter(container).SupplyWarpedText(spy, fragment, edgeOn);

            Assert.Null(spy.LastPushedTransform);
            Assert.Equal(0, spy.PopTransformCount);
        }

        private sealed class SpyGraphics : Canvas
        {
            public int PopTransformCount { get; private set; }

            public Matrix3x2? LastPushedTransform { get; private set; }
            public int FilledRectangles { get; private set; }
            public int StringsDrawn { get; private set; }

            public SpyGraphics() : base(new PdfSharpAdapter(), new Rect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void PushTransform(Matrix3x2 matrix) => LastPushedTransform = matrix;
            public override void PopTransform() => PopTransformCount++;
            public override void PushBlendMode(PaintBlendMode mode) { }
            public override void PopBlendMode() { }
            public override void PushClip(Rect rect) => _clipStack.Push(rect);
            public override void PushClip(GraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(Rect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override GraphicsPath GetGraphicsPath() => null!;

            public override GraphicsPath? GetTextOutline(string str, Font font, PaintPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null) => null;
            public override (Canvas Graphics, Image Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(Image image, Image maskImage, Rect destRect) { }
            public override void DrawImageWithOpacity(Image image, Rect destRect, double opacity, PaintBlendMode blendMode = PaintBlendMode.Normal) { }
            public override void DrawImageWithColorMatrix(Image image, Rect destRect, ColorMatrix matrix) { }
            public override void DrawImageAlphaMasked(Image image, Image maskImage, Rect destRect, bool invert = false) { }
            public override void DrawImageBlendedOver(Image top, Image bottom, Rect destRect, PaintBlendMode blendMode) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override void BeginVariableText() { }
            public override void EndVariableText() { }
            public override Size MeasureString(string str, Font font, ShapeSettings? features = null) => new(0, 12);
            public override int CountShapedGlyphs(string str, Font font, ShapeSettings? features = null) => str?.Length ?? 0;
            public override void MeasureString(string str, Font font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = 0;
            }
            public override void DrawString(string str, Font font, PaintColor color, PaintPoint point, Size size, double letterSpacing = 0, FontPalette? fontPalette = null, ShapeSettings? features = null) => StringsDrawn++;
            public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, Font font, PaintColor color) { }
            public override void DrawLine(Pen pen, double x1, double y1, double x2, double y2) { }
            public override void DrawRectangle(Pen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(Brush brush, double x, double y, double width, double height) => FilledRectangles++;
            public override void DrawImage(Image image, Rect destRect, Rect srcRect) { }
            public override void DrawImage(Image image, Rect destRect) { }
            public override void DrawPath(Pen pen, GraphicsPath path) { }
            public override void DrawPath(Brush brush, GraphicsPath path) { }
            public override void DrawPolygon(Brush brush, PaintPoint[] points) { }
            public override void Dispose() { }
        }

        // --- Helpers ---

        // ActualTransformMatrix treats the box's own top-left corner as local (0, 0), so probe
        // points here are box-local, not absolute page coordinates (see RebaseOrigin tests below
        // for the page-space behavior applied at paint time).
        private static (double X, double Y) MapPoint(Matrix3x2 m, double x, double y) =>
            (x * m.M11 + y * m.M21 + m.M31, x * m.M12 + y * m.M22 + m.M32);

        private Task<CssBox> FindDivBox(string css)
        {
            var html = $@"<!DOCTYPE html><html><head><style>
div {{ width: 200pt; height: 100pt; {css} }}
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

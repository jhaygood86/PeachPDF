using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Raster;

namespace PeachPDF.Tests.Raster
{
    /// <summary>Gradients, pen styles, clips and images drawn through <see cref="RasterGraphics"/>.</summary>
    public class RasterPaintTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static RasterGraphics NewGraphics(int width, int height)
        {
            var surface = new RasterSurface(width, height, 0, 0, 1, 1);
            return new RasterGraphics(Adapter, surface, 1);
        }

        private static byte[] Pixel(RasterGraphics g, int x, int y) => g.Surface.Row(y).Slice(x * 4, 4).ToArray();

        private static XColor Red => XColor.FromArgb(255, 255, 0, 0);

        private static XColor Blue => XColor.FromArgb(255, 0, 0, 255);

        // ---- linear gradients, the four rectangle modes ----

        [Theory]
        [InlineData(10.0, true, false)]   // ForwardDiagonal-ish: red at the top-left corner
        [InlineData(60.0, false, false)]  // Vertical: red at the top
        [InlineData(100.0, false, true)]  // BackwardDiagonal: red at the top-right corner
        [InlineData(170.0, true, false)]  // Horizontal: red at the left
        public void RectangleGradient_RunsInTheDirectionOfItsMode(double angle, bool redOnLeft, bool redOnRight)
        {
            var g = NewGraphics(20, 20);
            var brush = g.GetLinearGradientBrush(new RRect(0, 0, 20, 20), RColor.FromArgb(255, 255, 0, 0), RColor.FromArgb(255, 0, 0, 255), angle);

            g.DrawRectangle(brush, 0, 0, 20, 20);

            var topLeft = Pixel(g, 1, 1);
            var topRight = Pixel(g, 18, 1);
            var bottom = Pixel(g, 10, 18);
            if (redOnLeft)
                Assert.True(topLeft[0] > topRight[0] || topLeft[0] > bottom[0]);
            if (redOnRight)
                Assert.True(topRight[0] > topLeft[0]);
            // Whatever the mode, the gradient is not a single flat colour.
            var distinct = Enumerable.Range(0, 20).SelectMany(y => Enumerable.Range(0, 20).Select(x => Convert.ToHexString(Pixel(g, x, y)))).Distinct().Count();
            Assert.True(distinct > 8);
        }

        [Fact]
        public void RepeatingLinearGradient_RepeatsItsColourStops()
        {
            var g = NewGraphics(40, 1);
            var brush = g.GetLinearGradientBrush(new RPoint(0, 0), new RPoint(10, 0),
                [(RColor.FromArgb(255, 255, 0, 0), 0.0), (RColor.FromArgb(255, 0, 0, 255), 1.0)], isRepeating: true);

            g.DrawRectangle(brush, 0, 0, 40, 1);

            // Red at the start of every 10-pixel period.
            Assert.True(Pixel(g, 0, 0)[0] > 220);
            Assert.True(Pixel(g, 10, 0)[0] > 220);
            Assert.True(Pixel(g, 20, 0)[0] > 220);
            Assert.True(Pixel(g, 9, 0)[2] > 200);
        }

        [Fact]
        public void GradientWithTransparentStops_IsPremultiplied()
        {
            var g = NewGraphics(10, 1);
            var brush = g.GetLinearGradientBrush(new RPoint(0, 0), new RPoint(10, 0),
                [(RColor.FromArgb(0, 255, 0, 0), 0.0), (RColor.FromArgb(255, 255, 0, 0), 1.0)]);

            g.DrawRectangle(brush, 0, 0, 10, 1);

            var start = Pixel(g, 0, 0);
            var end = Pixel(g, 9, 0);
            Assert.True(start[3] < 40);
            Assert.True(end[3] > 220);
            Assert.True(start[0] <= start[3]);
        }

        [Fact]
        public void SingleStopGradient_IsThatColourEverywhere()
        {
            var g = NewGraphics(4, 1);
            var brush = new BrushAdapter(new XLinearGradientBrush(new XPoint(0, 0), new XPoint(4, 0), [Red], [0.0]));

            g.DrawRectangle(brush, 0, 0, 4, 1);

            Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(g, 2, 0));
        }

        [Fact]
        public void GradientBrushTransform_MovesTheGradient()
        {
            var g = NewGraphics(20, 1);
            var xBrush = new XLinearGradientBrush(new XPoint(0, 0), new XPoint(10, 0), [Red, Blue], [0.0, 1.0]);
            xBrush.TranslateTransform(10, 0);

            g.DrawRectangle(new BrushAdapter(xBrush), 0, 0, 20, 1);

            // Translated by 10: the red end now sits at x = 10 and the middle of the ramp at x = 15
            // (untranslated, the ramp would already be finished by x = 10).
            Assert.True(Pixel(g, 10, 0)[0] > 200);
            Assert.InRange(Pixel(g, 15, 0)[0], 100, 160);
            Assert.True(Pixel(g, 19, 0)[2] > 200);
        }

        // ---- radial ----

        [Fact]
        public void RadialGradient_WithAFocalPoint_ShiftsTheBrightestPoint()
        {
            var g = NewGraphics(40, 40);
            var brush = g.GetRadialGradientBrush(new RPoint(20, 20), 18, 18,
                [(RColor.FromArgb(255, 255, 255, 255), 0.0), (RColor.FromArgb(255, 0, 0, 0), 1.0)],
                focalCenter: new RPoint(10, 20));

            g.DrawRectangle(brush, 0, 0, 40, 40);

            Assert.True(Pixel(g, 10, 20)[0] > Pixel(g, 30, 20)[0]);
        }

        [Fact]
        public void RepeatingRadialGradient_RepeatsOutward()
        {
            var g = NewGraphics(60, 60);
            var brush = g.GetRadialGradientBrush(new RPoint(30, 30), 10, 10,
                [(RColor.FromArgb(255, 255, 255, 255), 0.0), (RColor.FromArgb(255, 0, 0, 0), 1.0)], isRepeating: true);

            g.DrawRectangle(brush, 0, 0, 60, 60);

            // Bright again one radius further out than the dark rim.
            Assert.True(Pixel(g, 30 + 11, 30)[0] > Pixel(g, 30 + 9, 30)[0]);
        }

        [Fact]
        public void EllipticalRadialGradient_IsWiderThanItIsTall()
        {
            var g = NewGraphics(60, 40);
            var brush = g.GetRadialGradientBrush(new RPoint(30, 20), 28, 12,
                [(RColor.FromArgb(255, 255, 255, 255), 0.0), (RColor.FromArgb(255, 0, 0, 0), 1.0)]);

            g.DrawRectangle(brush, 0, 0, 60, 40);

            Assert.True(Pixel(g, 30 + 10, 20)[0] > Pixel(g, 30, 20 + 10)[0]);
        }

        [Fact]
        public void TwoCircleRadialBrush_WithoutStops_InterpolatesFromInnerToOuterRadius()
        {
            var g = NewGraphics(40, 40);
            var xBrush = new XRadialGradientBrush(new XPoint(20, 20), 5, 18, Red, Blue);

            g.DrawRectangle(new BrushAdapter(xBrush), 0, 0, 40, 40);

            Assert.True(Pixel(g, 20, 20)[0] > 200);
            Assert.True(Pixel(g, 20 + 17, 20)[2] > 150);
        }

        // ---- pens ----

        private static RPen Pen(RasterGraphics g, double width, RLineCap cap = RLineCap.Butt, RLineJoin join = RLineJoin.Miter, RDashStyle dash = RDashStyle.Solid)
        {
            var pen = g.GetPen(RColor.FromArgb(255, 0, 0, 0));
            pen.Width = width;
            pen.LineCap = cap;
            pen.LineJoin = join;
            pen.DashStyle = dash;
            return pen;
        }

        [Theory]
        [InlineData("Dash")]
        [InlineData("Dot")]
        [InlineData("DashDot")]
        [InlineData("DashDotDot")]
        public void PresetDashStyles_LeaveGaps(string styleName)
        {
            var style = Enum.Parse<RDashStyle>(styleName);
            var g = NewGraphics(120, 10);

            g.DrawLine(Pen(g, 2, dash: style), 0, 5, 120, 5);

            var covered = Enumerable.Range(0, 120).Count(x => Pixel(g, x, 5)[3] > 128);
            Assert.InRange(covered, 10, 100);
        }

        [Fact]
        public void CustomDashPattern_IsInUserUnits_AndOddCountsArePadded()
        {
            var g = NewGraphics(60, 10);
            var pen = Pen(g, 2);
            pen.SetDashPattern([6, 6, 6], 0);

            g.DrawLine(pen, 0, 5, 60, 5);

            Assert.True(Pixel(g, 2, 5)[3] > 200);
            Assert.True(Pixel(g, 9, 5)[3] < 50);
        }

        [Fact]
        public void HairlinePen_DrawsAtLeastOnePixel()
        {
            var g = NewGraphics(10, 10);

            g.DrawLine(Pen(g, 0), 0, 5.5, 10, 5.5);

            Assert.Contains(Enumerable.Range(0, 10), x => Pixel(g, x, 5)[3] > 100);
        }

        [Theory]
        [InlineData("Round")]
        [InlineData("Square")]
        public void LineCaps_ExtendBeyondTheEndpoints(string capName)
        {
            var cap = Enum.Parse<RLineCap>(capName);
            var g = NewGraphics(30, 20);

            g.DrawLine(Pen(g, 8, cap), 10, 10, 20, 10);

            Assert.True(Pixel(g, 8, 10)[3] > 100);
            Assert.True(Pixel(g, 21, 10)[3] > 100);
        }

        [Theory]
        [InlineData("Miter")]
        [InlineData("Round")]
        [InlineData("Bevel")]
        public void PathStroke_JoinsBendsWithoutGaps(string joinName)
        {
            var join = Enum.Parse<RLineJoin>(joinName);
            var g = NewGraphics(30, 30);
            var path = g.GetGraphicsPath();
            path.Start(5, 25);
            path.LineTo(5, 5);
            path.LineTo(25, 5);

            g.DrawPath(Pen(g, 6, join: join), path);

            Assert.True(Pixel(g, 5, 15)[3] > 200);
            Assert.True(Pixel(g, 15, 5)[3] > 200);
            Assert.True(Pixel(g, 5, 5)[3] > 100);
        }

        [Fact]
        public void PenWithAGradientBrush_StrokesWithTheGradient()
        {
            var g = NewGraphics(40, 10);
            var brush = g.GetLinearGradientBrush(new RPoint(0, 0), new RPoint(40, 0),
                [(RColor.FromArgb(255, 255, 0, 0), 0.0), (RColor.FromArgb(255, 0, 0, 255), 1.0)]);
            var pen = g.GetPen(brush);
            pen.Width = 4;

            g.DrawLine(pen, 0, 5, 40, 5);

            Assert.True(Pixel(g, 2, 5)[0] > Pixel(g, 37, 5)[0]);
            Assert.True(Pixel(g, 37, 5)[2] > Pixel(g, 2, 5)[2]);
        }

        [Fact]
        public void RectangleOutline_IsStrokedAroundTheEdges()
        {
            var g = NewGraphics(20, 20);

            g.DrawRectangle(Pen(g, 2), 4, 4, 12, 12);

            Assert.True(Pixel(g, 4, 10)[3] > 200);
            Assert.True(Pixel(g, 10, 4)[3] > 200);
            Assert.Equal(0, Pixel(g, 10, 10)[3]);
        }

        // ---- transforms and clips ----

        [Fact]
        public void RotatedTransform_RotatesTheDrawing()
        {
            var g = NewGraphics(40, 40);
            var a = Math.PI / 4;

            g.PushTransform(new RMatrix(Math.Cos(a), Math.Sin(a), -Math.Sin(a), Math.Cos(a), 20, 5));
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 0, 0, 10, 10);
            g.PopTransform();

            // A square turned 45 degrees is a diamond: its top corner is at (20, 5) and it is centred on (20, 5 + 7.07).
            Assert.True(Pixel(g, 20, 12)[3] > 200);
            Assert.Equal(0, Pixel(g, 10, 12)[3]);
        }

        [Fact]
        public void RotatedClip_AndFractionalClip_ProduceAntiAliasedMasks()
        {
            var g = NewGraphics(40, 40);

            g.PushTransform(new RMatrix(0.7071, 0.7071, -0.7071, 0.7071, 20, 0));
            g.PushClip(new RRect(0, 0, 20, 20));
            g.PopTransform();
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 0, 0, 40, 40);
            g.PopClip();

            Assert.True(Pixel(g, 20, 14)[3] > 200);
            Assert.Equal(0, Pixel(g, 2, 2)[3]);

            var h = NewGraphics(10, 10);
            h.PushClip(new RRect(1.5, 1.5, 4, 4));
            h.DrawRectangle(h.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 0, 0, 10, 10);
            h.PopClip();
            Assert.InRange(Pixel(h, 1, 3)[3], 100, 156);
        }

        [Fact]
        public void NestedClips_Intersect_AndACroppedMaskStaysCorrect()
        {
            var g = NewGraphics(20, 20);
            var path = g.GetGraphicsPath();
            path.Start(0, 0);
            path.LineTo(20, 0);
            path.LineTo(20, 20);
            path.LineTo(0, 20);
            path.CloseFigure();

            g.PushClip(path);
            g.PushClip(new RRect(5, 5, 6, 6));
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 0, 0, 20, 20);
            g.PopClip();
            g.PopClip();

            Assert.Equal(255, Pixel(g, 7, 7)[3]);
            Assert.Equal(0, Pixel(g, 3, 7)[3]);
            Assert.Equal(0, Pixel(g, 12, 7)[3]);
        }

        [Fact]
        public void EmptyClip_HidesEverything()
        {
            var g = NewGraphics(10, 10);

            g.PushClip(new RRect(50, 50, 5, 5));
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 0, 0, 10, 10);
            g.PopClip();

            Assert.Equal(0, Pixel(g, 5, 5)[3]);
        }

        [Fact]
        public void SuspendedClipping_DrawsAtTheFullSurfaceUntilResumed()
        {
            var g = NewGraphics(10, 10);
            g.PushClip(new RRect(0, 0, 2, 2));

            g.SuspendClipping();
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 5, 5, 2, 2);
            g.ResumeClipping();
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), 8, 8, 2, 2);

            Assert.Equal(255, Pixel(g, 5, 5)[3]);
            Assert.Equal(0, Pixel(g, 8, 8)[3]);
        }

        [Fact]
        public void PolygonFill_AndSmoothingModeHooks_Work()
        {
            var g = NewGraphics(10, 10);
            var previous = g.SetAntiAliasSmoothingMode();

            g.DrawPolygon(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), [new RPoint(0, 0), new RPoint(10, 0), new RPoint(0, 10)]);
            g.ReturnPreviousSmoothingMode(previous);
            g.DrawPolygon(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 0)), []);

            Assert.Equal(255, Pixel(g, 1, 1)[3]);
            Assert.Equal(0, Pixel(g, 9, 9)[3]);
        }

        [Fact]
        public void BlendModeStack_RestoresThePreviousMode()
        {
            var g = NewGraphics(2, 2);
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 255, 255, 0)), 0, 0, 2, 2);

            g.PushBlendMode(RBlendMode.Multiply);
            g.PushBlendMode(RBlendMode.Screen);
            g.PopBlendMode();
            g.PopBlendMode();
            g.PopBlendMode();
            g.DrawRectangle(g.GetSolidBrush(RColor.FromArgb(255, 0, 0, 255)), 0, 0, 2, 2);

            Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(g, 0, 0));
        }

        [Fact]
        public void MarkedContentAndDisposeHooks_AreHarmlessNoOps()
        {
            var g = NewGraphics(2, 2);

            g.BeginMarkedContent("/P", 1);
            g.EndMarkedContent();
            g.BeginArtifact();
            g.EndMarkedContent();
            g.BeginVariableText();
            g.EndVariableText();
            g.PushClipExclude(new RRect(0, 0, 1, 1));
            g.PopTransform();
            g.Dispose();

            Assert.True(g.IsOffscreenTile);
            Assert.Null(g.FormCacheOwner);
        }
    }
}

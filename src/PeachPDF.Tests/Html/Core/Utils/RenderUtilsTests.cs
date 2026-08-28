using PeachPDF.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Html.Core.Utils
{
    public class RenderUtilsTests
    {
        [Fact]
        public void GetRoundRect_ClosesTheReturnedPath()
        {
            var g = new RecordingGraphics(new PdfSharpAdapter());

            using var path = (RecordingGraphicsPath)RenderUtils.GetRoundRect(g, new RRect(0, 0, 100, 40),
                10, 10, 10, 10, 10, 10, 10, 10);

            Assert.True(path.Closed);
        }

        /// <summary>
        /// Issue #812 (reopened): <c>GetRoundRect</c> is fed raw layout-space (<c>PixelsPerInch</c>-inflated)
        /// coordinates - the same space <c>CssBox</c> geometry lives in - but neither
        /// <c>RGraphics.PushClip(RGraphicsPath)</c> nor <c>DrawPath</c> ever divides a path's coordinates
        /// by <c>PixelsPerPoint</c> before handing them to the backend, unlike every other draw primitive.
        /// At a non-default <c>PixelsPerInch</c> (<c>PixelsPerPoint != 1.0</c>), a rounded-rect path built
        /// from un-divided coordinates renders too large and mis-positioned relative to everything else on
        /// the page. <c>GetRoundRect</c> must divide by <c>g.PixelsPerPoint</c> itself.
        /// </summary>
        [Fact]
        public void GetRoundRect_DividesRectAndRadiiByPixelsPerPoint()
        {
            var g = new RecordingGraphics(new PdfSharpAdapter()) { PixelsPerPointOverride = 2.0 };

            using var path = (RecordingGraphicsPath)RenderUtils.GetRoundRect(g, new RRect(0, 0, 200, 80),
                20, 20, 20, 20, 20, 20, 20, 20);

            Assert.All(path.Arcs, arc =>
            {
                Assert.Equal(10.0, arc.RadiusX);
                Assert.Equal(10.0, arc.RadiusY);
            });
            // Rect (0,0,200,80) / 2.0 = (0,0,100,40) - every point should land within that halved rect.
            Assert.All(path.Points, p =>
            {
                Assert.InRange(p.X, 0, 100);
                Assert.InRange(p.Y, 0, 40);
            });
            Assert.Contains((100.0, 10.0), path.Points); // top-right corner's arc endpoint
        }

        /// <summary>
        /// A rounded-rect path built at a non-default <c>PixelsPerInch</c> must come out identical (in
        /// true point-space terms) to the same shape built at the library's default 72 - <c>PixelsPerInch</c>
        /// is a pure internal layout-coordinate-scale knob with zero intended visual effect.
        /// </summary>
        [Fact]
        public void GetRoundRect_IsInvariantUnderPixelsPerPoint()
        {
            var gDefault = new RecordingGraphics(new PdfSharpAdapter()) { PixelsPerPointOverride = 1.0 };
            using var pathDefault = (RecordingGraphicsPath)RenderUtils.GetRoundRect(gDefault,
                new RRect(10, 20, 100, 40), 10, 10, 10, 10, 10, 10, 10, 10);

            // Simulates the real internal-space inflation a box would have at PixelsPerInch=144
            // (PixelsPerPoint=2.0): every coordinate pre-multiplied by 2 before reaching GetRoundRect.
            var gScaled = new RecordingGraphics(new PdfSharpAdapter()) { PixelsPerPointOverride = 2.0 };
            using var pathScaled = (RecordingGraphicsPath)RenderUtils.GetRoundRect(gScaled,
                new RRect(20, 40, 200, 80), 20, 20, 20, 20, 20, 20, 20, 20);

            Assert.Equal(pathDefault.Arcs, pathScaled.Arcs);
            Assert.Equal(pathDefault.Points, pathScaled.Points);
        }
    }
}

using PeachPDF.PdfBackend;
using PeachDrawing.Core;
using PeachPDF.PDF.Drawing;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Xml.Linq;

namespace PeachPDF.Tests.Svg
{
    public class SvgPathRenderingTests
    {
        [Fact]
        public void TwoClockwiseSemicircularArcs_RenderCompleteCircle()
        {
            var root = XDocument.Parse("""
                <svg xmlns="http://www.w3.org/2000/svg"
                     width="100" height="100"
                     viewBox="0 0 100 100">
                  <path
                    d="M50 10
                       A40 40 0 0 1 50 90
                       A40 40 0 0 1 50 10"
                    fill="none"
                    stroke="black"/>
                </svg>
                """).Root!;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root), new PdfRenderContext());
            var graphics = new RealPathRecordingGraphics();

            SvgRenderer.RenderInto(graphics, document, new Rect(0, 0, 100, 100));

            var points = Assert.Single(graphics.Paths);
            Assert.Equal(10, points.Min(point => point.X), 3);
            Assert.Equal(90, points.Max(point => point.X), 3);
            Assert.Equal(10, points.Min(point => point.Y), 3);
            Assert.Equal(90, points.Max(point => point.Y), 3);
        }

        [Fact]
        public void CounterclockwiseSemicircularArc_RendersRequestedSide()
        {
            var root = XDocument.Parse("""
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
                  <path d="M50 10 A40 40 0 0 0 50 90" fill="none" stroke="black"/>
                </svg>
                """).Root!;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root), new PdfRenderContext());
            var graphics = new RealPathRecordingGraphics();

            SvgRenderer.RenderInto(graphics, document, new Rect(0, 0, 100, 100));

            var points = Assert.Single(graphics.Paths);
            Assert.Equal(10, points.Min(point => point.X), 3);
            Assert.Equal(50, points.Max(point => point.X), 3);
        }

        private sealed class RealPathRecordingGraphics : TestRecordingGraphics
        {
            public List<PaintPoint[]> Paths { get; } = [];

            public override GraphicsPath GetGraphicsPath() => new PdfGraphicsPath();

            public override void DrawPath(Pen pen, GraphicsPath path)
            {
                Paths.Add(PathPoints.Of(path));
            }
        }
    }
}

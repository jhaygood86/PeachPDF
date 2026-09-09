using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Drawing;
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
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root), new PdfSharpAdapter());
            var graphics = new RealPathRecordingGraphics();

            SvgRenderer.RenderInto(graphics, document, new RRect(0, 0, 100, 100));

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
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root), new PdfSharpAdapter());
            var graphics = new RealPathRecordingGraphics();

            SvgRenderer.RenderInto(graphics, document, new RRect(0, 0, 100, 100));

            var points = Assert.Single(graphics.Paths);
            Assert.Equal(10, points.Min(point => point.X), 3);
            Assert.Equal(50, points.Max(point => point.X), 3);
        }

        private sealed class RealPathRecordingGraphics : TestRecordingGraphics
        {
            public List<XPoint[]> Paths { get; } = [];

            public override RGraphicsPath GetGraphicsPath() => new GraphicsPathAdapter();

            public override void DrawPath(RPen pen, RGraphicsPath path)
            {
                Paths.Add(((GraphicsPathAdapter)path).GraphicsPath._corePath.PathPoints);
            }
        }
    }
}

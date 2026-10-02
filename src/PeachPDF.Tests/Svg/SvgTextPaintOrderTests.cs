using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary><c>paint-order</c> on stroked SVG text: which of fill and stroke is painted first.</summary>
    public class SvgTextPaintOrderTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        /// <summary>The stroked flag of each path the text painted, in paint order.</summary>
        private static bool[] StrokedOrder(string attrs, string svgAttrs = "")
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" {svgAttrs}><text x="10" y="50" font-size="30" fill="rgb(0,0,255)" stroke="rgb(255,0,0)" stroke-width="2" {attrs}>H</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return g.Log.OfType<TestRecordingGraphics.DrawPathCall>().Select(p => p.Stroked).ToArray();
        }

        [Theory]
        [InlineData("", false)]
        [InlineData("""paint-order="normal" """, false)]
        [InlineData("""paint-order="fill stroke" """, false)]
        [InlineData("""paint-order="stroke" """, true)]
        [InlineData("""paint-order="stroke fill" """, true)]
        [InlineData("""paint-order="markers stroke fill" """, true)]
        [InlineData("""paint-order="stroke markers" """, true)]
        [InlineData("""paint-order="fill" """, false)]
        [InlineData("""paint-order="bogus" """, false)]
        [InlineData("""style="paint-order: stroke" """, true)]
        public void Order(string attrs, bool strokeFirst)
        {
            var order = StrokedOrder(attrs);

            Assert.Equal(2, order.Length);
            Assert.Equal(strokeFirst, order[0]);
            Assert.Equal(!strokeFirst, order[1]);
        }

        [Fact]
        public void IsInherited_AndNormalResets()
        {
            Assert.True(StrokedOrder("", """paint-order="stroke" """)[0]);
            Assert.False(StrokedOrder("""paint-order="normal" """, """paint-order="stroke" """)[0]);
        }
    }
}

using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>Whitespace handling in SVG text: default collapsing, <c>xml:space="preserve"</c>, and <c>white-space</c> with <c>tab-size</c>.</summary>
    public class SvgTextWhitespaceTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static string Painted(string textAttrs, string content, string svgAttrs = "")
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" {svgAttrs}><text x="10" y="50" font-size="20" {textAttrs}>{content}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return string.Concat(g.DrawStringCalls.Select(c => c.Text));
        }

        [Fact]
        public void Default_CollapsesAndTrims() => Assert.Equal("a b c", Painted("", "  a   b\n  c  "));

        [Fact]
        public void XmlSpacePreserve_KeepsEverySpace_AndTurnsNewlinesAndTabsIntoSpaces() =>
            Assert.Equal("  a   b    c  ", Painted("""xml:space="preserve" """, "  a   b\n\t  c  ").Replace("\t", "<tab>"));

        [Fact]
        public void XmlSpaceDefault_ResetsInheritedPreserve() =>
            Assert.Equal("a b", Painted("""xml:space="default" """, "  a    b  ", """xml:space="preserve" """));

        [Fact]
        public void XmlSpace_IsInherited() => Assert.Equal(" a  b ", Painted("", " a  b ", """xml:space="preserve" """));

        [Theory]
        [InlineData("pre")]
        [InlineData("pre-wrap")]
        [InlineData("break-spaces")]
        public void WhiteSpace_PreserveValues_KeepSpaces(string value) =>
            Assert.Equal(" a  b ", Painted($"style=\"white-space: {value}\" ", " a  b "));

        [Theory]
        [InlineData("normal")]
        [InlineData("nowrap")]
        [InlineData("pre-line")]
        public void WhiteSpace_CollapsingValues_Collapse(string value) =>
            Assert.Equal("a b", Painted($"style=\"white-space: {value}\" ", " a  b "));

        [Fact]
        public void WhiteSpacePre_TabBecomesTabSizeSpaces() =>
            Assert.Equal("a" + new string(' ', 4) + "b", Painted("""style="white-space: pre; tab-size: 4" """, "a\tb"));

        [Fact]
        public void WhiteSpacePre_DefaultTabSizeIsEight() =>
            Assert.Equal("a" + new string(' ', 8) + "b", Painted("""style="white-space: pre" """, "a\tb"));

        [Fact]
        public void PreservedWhitespace_SpansTspanBoundaries() =>
            Assert.Equal("a  b", Painted("""xml:space="preserve" """, "a <tspan> </tspan>b"));
    }
}

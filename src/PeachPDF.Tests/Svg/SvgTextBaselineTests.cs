using PeachPDF.Adapters;
using PeachDrawing.Core;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary><c>dominant-baseline</c>, <c>alignment-baseline</c> and <c>baseline-shift</c> on horizontal SVG text, asserted on where each run is drawn.</summary>
    public class SvgTextBaselineTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics Render(string textAttrs, string content = "Hi", string svgAttrs = "")
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" {svgAttrs}><text x="10" y="50" font-size="20" {textAttrs}>{content}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return g;
        }

        private static double Y(TestRecordingGraphics g, int i = 0) => g.DrawStringCalls[i].PaintPoint.Y;

        [Fact]
        public void Auto_AndAlphabetic_AreTheDefault()
        {
            var plain = Y(Render(""));
            Assert.Equal(plain, Y(Render("""dominant-baseline="alphabetic" """)), 3);
            Assert.Equal(plain, Y(Render("""dominant-baseline="auto" """)), 3);
        }

        [Theory]
        [InlineData("middle")]
        [InlineData("central")]
        [InlineData("hanging")]
        [InlineData("text-top")]
        [InlineData("mathematical")]
        public void RaisedBaselines_MoveTextDown(string baseline)
        {
            Assert.True(Y(Render($"dominant-baseline=\"{baseline}\" ")) > Y(Render("")));
        }

        [Theory]
        [InlineData("text-bottom")]
        [InlineData("ideographic")]
        public void LoweredBaselines_MoveTextUp(string baseline)
        {
            Assert.True(Y(Render($"dominant-baseline=\"{baseline}\" ")) < Y(Render("")));
        }

        [Fact]
        public void Central_IsHalfwayBetweenAscentAndDescent_AndMiddleIsAboveAlphabeticByHalfXHeight()
        {
            var plain = Y(Render(""));
            var central = Y(Render("""dominant-baseline="central" """)) - plain;
            var middle = Y(Render("""dominant-baseline="middle" """)) - plain;
            var textTop = Y(Render("""dominant-baseline="text-top" """)) - plain;

            Assert.InRange(central, 5, 9);
            Assert.InRange(middle, 3, 7);
            Assert.True(textTop > central);
        }

        [Fact]
        public void DominantBaseline_IsInherited()
        {
            Assert.Equal(Y(Render("""dominant-baseline="middle" """)), Y(Render("", svgAttrs: """dominant-baseline="middle" """)), 3);
        }

        [Fact]
        public void BaselineShift_Super_RaisesText()
        {
            var plain = Y(Render(""));
            Assert.Equal(plain - 20 * 0.33, Y(Render("""baseline-shift="super" """)), 2);
            Assert.Equal(plain + 20 * 0.2, Y(Render("""baseline-shift="sub" """)), 2);
        }

        [Fact]
        public void BaselineShift_LengthAndPercentage()
        {
            var plain = Y(Render(""));
            Assert.Equal(plain - 5, Y(Render("""baseline-shift="5" """)), 3);
            Assert.Equal(plain - 4, Y(Render("""baseline-shift="20%" """)), 3);
            Assert.Equal(plain + 3, Y(Render("""baseline-shift="-3" """)), 3);
        }

        [Fact]
        public void BaselineShift_OnTspan_ShiftsOnlyThatSpan_AndAccumulatesWhenNested()
        {
            var g = Render("", "a<tspan baseline-shift=\"5\">b<tspan baseline-shift=\"3\">c</tspan></tspan>d");

            Assert.Equal(["a", "b", "c", "d"], g.DrawStringCalls.Select(c => c.Text).ToArray());
            var a = Y(g, 0);
            Assert.Equal(a - 5, Y(g, 1), 3);
            Assert.Equal(a - 8, Y(g, 2), 3);
            Assert.Equal(a, Y(g, 3), 3);
        }

        [Fact]
        public void BaselineShift_IsNotAppliedFromAContainerGroup()
        {
            var markup = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><g baseline-shift="9"><text x="10" y="50" font-size="20">Hi</text></g></svg>""";
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter), new Rect(0, 0, 200, 100));

            Assert.Equal(Y(Render("")), Y(g), 3);
        }

        [Fact]
        public void AlignmentBaseline_OnTspan_AlignsItsBaselineToTheParents()
        {
            var g = Render("", "a<tspan alignment-baseline=\"hanging\">b</tspan>");

            // hanging sits above alphabetic, so the span's alphabetic baseline drops below its parent's.
            Assert.True(Y(g, 1) > Y(g, 0));
        }

        [Fact]
        public void TspanWithLargerFont_StillSharesTheAlphabeticBaselineByDefault()
        {
            var g = Render("", "a<tspan font-size=\"40\">b</tspan>");

            // Top of the drawn cell = baseline - ascent: the larger font's cell starts higher, but baseline (y + ascent) is the same.
            var baselineA = Y(g, 0) + g.DrawStringCalls[0].Font.Ascent;
            var baselineB = Y(g, 1) + g.DrawStringCalls[1].Font.Ascent;
            Assert.Equal(baselineA, baselineB, 3);
        }
    }
}

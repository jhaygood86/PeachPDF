using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachPDF.Tests.TestSupport;
using PeachDrawing.PDF;
using PeachBrackets.Svg;
using System.Linq;
using System.Xml.Linq;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>
    /// <c>visibility: hidden</c> (and <c>collapse</c>) stops an SVG element painting but leaves it in the rendering tree: it is inherited, a descendant can set
    /// <c>visible</c> again, and the element still counts for bounding boxes and clip paths (CSS Display 3 §4, SVG 2 §13).
    /// </summary>
    public class SvgVisibilityTests
    {
        private static readonly HtmlRenderContext RenderContext = new();

        private static TestRecordingGraphics Render(string body, string style = "")
        {
            var markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 100\"><style>{style}</style>{body}</svg>";
            var root = XDocument.Parse(markup).Root!;
            var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root), new PeachPDF.Rendering.HtmlRenderContext());
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, cssData, "print"), RenderContext);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 100, 100));
            return g;
        }

        private static int Paths(TestRecordingGraphics g) => g.Log.OfType<TestRecordingGraphics.DrawPathCall>().Count();

        [Theory]
        [InlineData("<rect width=\"10\" height=\"10\" visibility=\"hidden\"/>")]
        [InlineData("<rect width=\"10\" height=\"10\" visibility=\"collapse\"/>")]
        [InlineData("<rect width=\"10\" height=\"10\" style=\"visibility: hidden\"/>")]
        public void AHiddenShape_IsNotPainted(string body)
        {
            Assert.Equal(0, Paths(Render(body)));
            Assert.Equal(1, Paths(Render(body.Replace("hidden", "visible").Replace("collapse", "visible"))));
        }

        [Fact]
        public void ARule_HidesAShape()
        {
            Assert.Equal(0, Paths(Render("<rect class=\"h\" width=\"10\" height=\"10\"/>", ".h { visibility: hidden }")));
        }

        [Fact]
        public void HiddenIsInherited_AndAChildCanShowItselfAgain()
        {
            var g = Render("<g visibility=\"hidden\"><rect width=\"10\" height=\"10\"/><circle r=\"4\" visibility=\"visible\"/></g>");
            Assert.Equal(1, Paths(g));
        }

        [Theory]
        [InlineData("initial", 1)]
        [InlineData("inherit", 0)]
        [InlineData("unset", 0)]
        [InlineData("revert", 0)]
        public void ACssWideKeyword_InsideAHiddenGroup_ResolvesAsTheSpecSays(string keyword, int expectedPaths)
        {
            // initial is visible, so the rect shows; the three inheriting keywords keep it hidden like its group.
            var asAttribute = Render($"<g visibility=\"hidden\"><rect width=\"10\" height=\"10\" visibility=\"{keyword}\"/></g>");
            var asStyle = Render($"<g visibility=\"hidden\"><rect width=\"10\" height=\"10\" style=\"visibility: {keyword}\"/></g>");

            var asRule = Render("<g visibility=\"hidden\"><rect class=\"k\" width=\"10\" height=\"10\"/></g>", $".k {{ visibility: {keyword} }}");

            Assert.Equal(expectedPaths, Paths(asAttribute));
            Assert.Equal(expectedPaths, Paths(asStyle));
            Assert.Equal(expectedPaths, Paths(asRule));
        }

        [Fact]
        public void AHiddenShape_StillSizesAClipPath()
        {
            var g = Render("<defs><clipPath id=\"c\"><rect width=\"10\" height=\"10\" visibility=\"hidden\"/></clipPath></defs><rect width=\"50\" height=\"50\" clip-path=\"url(#c)\"/>");
            var clip = Assert.Single(g.ClipPaths);
            Assert.NotEmpty(clip.Points);
            Assert.Equal(1, Paths(g));
        }

        [Fact]
        public void HiddenText_IsNotDrawn_ButAVisibleTspanIs()
        {
            var hidden = Render("<text x=\"1\" y=\"20\" visibility=\"hidden\">Hello</text>");
            Assert.Empty(hidden.DrawStringCalls);

            var mixed = Render("<text x=\"1\" y=\"20\" visibility=\"hidden\">Hello <tspan visibility=\"visible\">World</tspan></text>");
            Assert.Contains(mixed.DrawStringCalls, c => c.Text.Contains("World"));
            Assert.DoesNotContain(mixed.DrawStringCalls, c => c.Text.Contains("Hello"));
        }
    }
}

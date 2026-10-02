using PeachDrawing.Core;
using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary><c>text-shadow</c> on SVG text, asserted on the ordered <c>DrawString</c> calls.</summary>
    public class SvgTextShadowTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static TestRecordingGraphics Render(string textAttrs, string svgAttrs = "", string text = "Hi")
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" {svgAttrs}><text x="10" y="50" font-size="20" fill="rgb(0,0,255)" {textAttrs}>{text}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return g;
        }

        [Fact]
        public void NoShadow_PaintsOnlyTheText()
        {
            Assert.Single(Render("").DrawStringCalls);
        }

        [Fact]
        public void Shadow_PaintsOffsetCopyBeforeTheText()
        {
            var g = Render("""style="text-shadow: 3px 4px rgb(255,0,0)" """);

            Assert.Equal(2, g.DrawStringCalls.Count);
            var (shadow, text) = (g.DrawStringCalls[0], g.DrawStringCalls[1]);
            Assert.Equal(text.PaintPoint.X + 3, shadow.PaintPoint.X, 3);
            Assert.Equal(text.PaintPoint.Y + 4, shadow.PaintPoint.Y, 3);
            Assert.Equal(255, shadow.PaintColor.R);
            Assert.Equal(0, shadow.PaintColor.B);
            Assert.Equal(255, text.PaintColor.B);
        }

        [Fact]
        public void Shadow_WithoutColor_UsesTheTextFill()
        {
            var g = Render("""text-shadow="2px 2px" """);

            Assert.Equal(2, g.DrawStringCalls.Count);
            Assert.Equal(g.DrawStringCalls[1].PaintColor, g.DrawStringCalls[0].PaintColor);
        }

        [Fact]
        public void Shadows_PaintLastToFirst_SoTheFirstIsOnTop()
        {
            var g = Render("""style="text-shadow: 1px 1px rgb(255,0,0), 5px 5px rgb(0,255,0)" """);

            Assert.Equal(3, g.DrawStringCalls.Count);
            Assert.Equal(255, g.DrawStringCalls[0].PaintColor.G); // second-listed, underneath
            Assert.Equal(255, g.DrawStringCalls[1].PaintColor.R); // first-listed, on top of it
        }

        [Fact]
        public void Shadow_IsInherited_AndNoneResets()
        {
            Assert.Equal(2, Render("", """text-shadow="2px 2px red" """).DrawStringCalls.Count);
            Assert.Single(Render("""text-shadow="none" """, """text-shadow="2px 2px red" """).DrawStringCalls);
        }

        [Fact]
        public void Shadow_EmRelativeOffset_ResolvesAgainstFontSize()
        {
            var g = Render("""text-shadow="0.5em 0 red" """);

            Assert.Equal(g.DrawStringCalls[1].PaintPoint.X + 10, g.DrawStringCalls[0].PaintPoint.X, 3);
        }

        [Fact]
        public void Shadow_InvalidValue_IsIgnored()
        {
            Assert.Single(Render("""text-shadow="banana" """).DrawStringCalls);
        }

        [Fact]
        public void Shadow_OnRotatedGlyph_IsOffsetInUserSpace()
        {
            var g = Render("""rotate="90" style="text-shadow: 6px 0 red" """, text: "H");

            // Shadow and text each paint under a push/pop; the shadow's matrix differs from the glyph's by exactly the user-space translation.
            var pushes = g.Log.OfType<TestRecordingGraphics.PushTransformCall>().ToArray();
            Assert.True(pushes.Length >= 2);
            var shadow = pushes[^2];
            var glyph = pushes[^1];
            Assert.Equal(glyph.Matrix.M31 + 6, shadow.Matrix.M31, 2);
            Assert.Equal(glyph.Matrix.M32, shadow.Matrix.M32, 2);
        }
    }
}

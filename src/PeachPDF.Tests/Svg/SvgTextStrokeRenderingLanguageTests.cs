using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// <c>-webkit-text-stroke</c>, <c>text-rendering</c> and <c>font-language-override</c> on SVG text, asserted on what is drawn (the recorded
    /// stroke, the shaping settings the glyphs are drawn with) so a parsed-but-ignored property fails.
    /// </summary>
    public class SvgTextStrokeRenderingLanguageTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        private static (SvgDocument Document, TestRecordingGraphics Graphics) Render(string textAttrs, string content = "Hi", string svgAttrs = "")
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" {svgAttrs}><text x="10" y="50" font-size="20" {textAttrs}>{content}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return (document, g);
        }

        private static ShapeSettings Features(TestRecordingGraphics g, int i = 0) => g.DrawStringCalls[i].Features ?? ShapeSettings.Default;

        // ---- -webkit-text-stroke --------------------------------------------------------------------

        [Fact]
        public void TextStroke_StrokesTheGlyphOutlines()
        {
            var (_, plain) = Render("");
            var (_, stroked) = Render("""style="-webkit-text-stroke: 2px #ff0000" """);

            Assert.DoesNotContain(plain.Log.OfType<TestRecordingGraphics.DrawPathCall>(), c => c.Stroked);
            var stroke = Assert.Single(stroked.Log.OfType<TestRecordingGraphics.DrawPathCall>(), c => c.Stroked);
            Assert.Equal(2, stroke.StrokeWidth, 6);
            Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), stroke.PaintColor);
        }

        [Fact]
        public void TextStroke_LonghandsAndInheritance_ReachTspansAndCurrentColor()
        {
            var (_, g) = Render("""style="-webkit-text-stroke-width: 1.5px; -webkit-text-stroke-color: #0000ff" """, "a<tspan>b</tspan>");

            var strokes = g.Log.OfType<TestRecordingGraphics.DrawPathCall>().Where(c => c.Stroked).ToArray();
            Assert.Equal(2, strokes.Length);
            Assert.All(strokes, s =>
            {
                Assert.Equal(1.5, s.StrokeWidth, 6);
                Assert.Equal(PaintColor.FromArgb(255, 0, 0, 255), s.PaintColor);
            });
        }

        [Fact]
        public void TextStroke_WidthOnly_StrokesInTheCurrentColour()
        {
            var (_, g) = Render("""style="-webkit-text-stroke-width: 3px" """, "H", svgAttrs: """color="#00ff00" """);

            var stroke = Assert.Single(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), c => c.Stroked);
            Assert.Equal(3, stroke.StrokeWidth, 6);
            Assert.Equal(PaintColor.FromArgb(255, 0, 0, 0), stroke.PaintColor);
        }

        [Fact]
        public void TextStroke_ZeroWidth_DrawsNoStroke()
        {
            var (_, g) = Render("""style="-webkit-text-stroke: 0 #00ff00" """);

            Assert.DoesNotContain(g.Log.OfType<TestRecordingGraphics.DrawPathCall>(), c => c.Stroked);
        }

        // ---- text-rendering -------------------------------------------------------------------------

        [Fact]
        public void TextRendering_OptimizeSpeed_TurnsOffKerningAndOptionalLigatures()
        {
            var (_, normal) = Render("""text-rendering="auto" """);
            var (_, speed) = Render("""text-rendering="optimizeSpeed" """);

            Assert.True(Features(normal).Kerning);
            Assert.True((Features(normal).Ligatures & LigatureSet.Common) != 0);
            Assert.False(Features(speed).Kerning);
            Assert.Equal(LigatureSet.Required, Features(speed).Ligatures);
        }

        [Fact]
        public void TextRendering_OptimizeSpeed_LeavesExplicitFontPropertiesAlone()
        {
            var (_, g) = Render("""text-rendering="optimizeSpeed" font-kerning="normal" font-variant-ligatures="common-ligatures" """);

            Assert.True(Features(g).Kerning);
            Assert.True((Features(g).Ligatures & LigatureSet.Common) != 0);
        }

        [Theory]
        [InlineData("optimizeLegibility")]
        [InlineData("geometricPrecision")]
        public void TextRendering_OtherKeywords_ChangeNothingForPdfOutput(string keyword)
        {
            var (_, g) = Render($"""text-rendering="{keyword}" """);

            Assert.True(Features(g).Kerning);
            Assert.True((Features(g).Ligatures & LigatureSet.Common) != 0);
        }

        [Fact]
        public void TextRendering_Inherits_AndALaterKeywordResets()
        {
            var (_, g) = Render("", "a<tspan text-rendering=\"auto\">b</tspan>", svgAttrs: """text-rendering="optimizeSpeed" """);

            Assert.False(Features(g, 0).Kerning);
            Assert.True(Features(g, 1).Kerning);
        }

        // ---- font-language-override -----------------------------------------------------------------

        [Fact]
        public void LanguageOverride_PassesTheOpenTypeLanguageSystemTagIntoShaping()
        {
            var (_, none) = Render("");
            var (_, overridden) = Render("""font-language-override="'SRB'" lang="en" """);

            Assert.Null(Features(none).LanguageSystemTag);
            Assert.Equal("SRB ", Features(overridden).LanguageSystemTag);
            Assert.Equal("en", Features(overridden).Language);
        }

        [Fact]
        public void LanguageOverride_InheritsToTspans_NormalResets_AndInvalidIsIgnored()
        {
            var (_, g) = Render("""font-language-override="'ROM'" """,
                "a<tspan font-language-override=\"normal\">b</tspan><tspan font-language-override=\"'TOOLONG'\">c</tspan>d");

            Assert.Equal("ROM ", Features(g, 0).LanguageSystemTag);
            Assert.Null(Features(g, 1).LanguageSystemTag);
            Assert.Equal("ROM ", Features(g, 2).LanguageSystemTag);
            Assert.Equal("ROM ", Features(g, 3).LanguageSystemTag);
        }
    }
}

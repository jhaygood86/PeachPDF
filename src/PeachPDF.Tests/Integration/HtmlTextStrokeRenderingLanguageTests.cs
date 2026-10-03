using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>-webkit-text-stroke</c>, <c>text-rendering</c> and <c>font-language-override</c> on HTML text, asserted on the calls the painter makes on the
    /// <see cref="Canvas"/> (the stroke path and its pen, the shaping settings each word is drawn with) rather than on the PDF text, so a property that
    /// parses and cascades but never reaches paint fails.
    /// </summary>
    public class HtmlTextStrokeRenderingLanguageTests
    {
        private static async Task<TestRecordingGraphics> PaintAsync(string style, string content = "Hi", string wrapperStyle = "")
        {
            var html = LayoutHarness.Wrap($"<div style=\"{wrapperStyle}\"><p style=\"margin:0;font:20pt Arial;{style}\">{content}</p></div>");
            var (_, container) = await LayoutHarness.LayoutAsync(html);

            var g = new TestRecordingGraphics();
            FragmentPaintHarness.PaintPage(container, g);
            return g;
        }

        private static TestRecordingGraphics.DrawPathCall[] Strokes(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.DrawPathCall>().Where(c => c.Stroked).ToArray();

        private static ShapeSettings Features(TestRecordingGraphics g, int i = 0) => g.DrawStringCalls[i].Features ?? ShapeSettings.Default;

        // ---- -webkit-text-stroke --------------------------------------------------------------------

        [Fact]
        public async Task TextStroke_StrokesEachWordsOutline_AfterDrawingTheWord()
        {
            var plain = await PaintAsync("");
            var g = await PaintAsync("-webkit-text-stroke: 2px #ff0000");

            Assert.Empty(Strokes(plain));

            var strokes = Strokes(g);
            Assert.Single(strokes);
            Assert.Equal(1.5, strokes[0].StrokeWidth, 6); // 2px = 1.5pt
            Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), strokes[0].PaintColor);

            // The word is drawn first, the stroke goes over it.
            var drawIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawStringCall { Text: "Hi" });
            var strokeIndex = g.Log.FindIndex(c => c is TestRecordingGraphics.DrawPathCall { Stroked: true });
            Assert.InRange(drawIndex, 0, strokeIndex - 1);
        }

        [Fact]
        public async Task TextStroke_OneStrokePerWord_AndTheOutlineSitsOnTheWordsBaseline()
        {
            var g = await PaintAsync("-webkit-text-stroke: 1px #000", "alpha beta");

            var strokes = Strokes(g);
            Assert.Equal(2, strokes.Length);
            // The space glyph between the two words is drawn without a stroke - it has no outline.
            var words = g.WordDrawStringCalls;
            for (var i = 0; i < 2; i++)
            {
                // The test canvas's outline starts at the word's own x and rises from the baseline (top of the cell plus the ascent).
                Assert.Equal(words[i].PaintPoint.X, strokes[i].Bounds.X, 3);
                Assert.Equal(words[i].PaintPoint.Y + words[i].Font.Ascent, strokes[i].Bounds.Bottom, 3);
            }
        }

        [Fact]
        public async Task TextStroke_LonghandsInheritAndCurrentColorIsTheTextColour()
        {
            var g = await PaintAsync("", "x", "color:#0000ff;-webkit-text-stroke-width:thick");

            var stroke = Assert.Single(Strokes(g));
            Assert.Equal(PaintColor.FromArgb(255, 0, 0, 255), stroke.PaintColor);
            Assert.True(stroke.StrokeWidth > 1);
        }

        [Fact]
        public async Task TextStroke_NoWidthOrTransparentColour_DrawsNothing()
        {
            Assert.Empty(Strokes(await PaintAsync("-webkit-text-stroke: 0 #f00")));
            Assert.Empty(Strokes(await PaintAsync("-webkit-text-stroke: 2px transparent")));
        }

        [Fact]
        public async Task TextStroke_UnderAVerticalWritingMode_StrokesBothUprightAndSidewaysRuns()
        {
            var g = await PaintAsync("writing-mode:vertical-rl;-webkit-text-stroke: 1px #000", "ab");

            Assert.NotEmpty(Strokes(g));
        }

        // ---- text-rendering -------------------------------------------------------------------------

        [Fact]
        public async Task TextRendering_OptimizeSpeed_TurnsOffKerningAndOptionalLigatures()
        {
            var normal = await PaintAsync("text-rendering: auto");
            var speed = await PaintAsync("text-rendering: optimizeSpeed");

            Assert.True(Features(normal).Kerning);
            Assert.True((Features(normal).Ligatures & LigatureSet.Common) != 0);
            Assert.False(Features(speed).Kerning);
            Assert.Equal(LigatureSet.Required, Features(speed).Ligatures);
        }

        [Fact]
        public async Task TextRendering_OptimizeSpeed_LeavesExplicitFontPropertiesAlone()
        {
            var g = await PaintAsync("text-rendering: optimizeSpeed; font-kerning: normal; font-variant-ligatures: common-ligatures");

            Assert.True(Features(g).Kerning);
            Assert.True((Features(g).Ligatures & LigatureSet.Common) != 0);
        }

        [Theory]
        [InlineData("optimizeLegibility")]
        [InlineData("geometricPrecision")]
        public async Task TextRendering_OtherKeywords_ChangeNothingForPdfOutput(string keyword)
        {
            var g = await PaintAsync($"text-rendering: {keyword}");

            Assert.True(Features(g).Kerning);
            Assert.True((Features(g).Ligatures & LigatureSet.Common) != 0);
        }

        [Fact]
        public async Task TextRendering_IsInherited()
        {
            var g = await PaintAsync("", wrapperStyle: "text-rendering: optimizeSpeed");

            Assert.False(Features(g).Kerning);
        }

        // ---- font-language-override -----------------------------------------------------------------

        [Fact]
        public async Task LanguageOverride_PassesTheOpenTypeLanguageSystemTagIntoShaping()
        {
            var none = await PaintAsync("");
            var overridden = await PaintAsync("font-language-override: 'SRB'");

            Assert.Null(Features(none).LanguageSystemTag);
            Assert.Equal("SRB ", Features(overridden).LanguageSystemTag);
        }

        [Fact]
        public async Task LanguageOverride_IsInherited_NormalResets_AndAnInvalidValueIsIgnored()
        {
            var inherited = await PaintAsync("", wrapperStyle: "font-language-override: 'ROM'");
            var reset = await PaintAsync("font-language-override: normal", wrapperStyle: "font-language-override: 'ROM'");
            var invalid = await PaintAsync("font-language-override: 'TOOLONG'", wrapperStyle: "font-language-override: 'ROM'");

            Assert.Equal("ROM ", Features(inherited).LanguageSystemTag);
            Assert.Null(Features(reset).LanguageSystemTag);
            Assert.Equal("ROM ", Features(invalid).LanguageSystemTag);
        }
    }
}

using PeachDrawing.Core;
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
    /// The baseline properties read the font's own <c>BASE</c> table where it has one (Source Sans 3 puts the ideographic baseline at -170 of 1000
    /// units, where the metric approximation says -120; Noto Sans JP's ideographic character face spans -67..827 horizontally and 53..947 vertically),
    /// also under a vertical writing mode and on a <c>&lt;textPath&gt;</c>. Every expectation is the table's number, so an implementation that
    /// still approximates fails.
    /// </summary>
    public class SvgTextBaselineTableTests
    {
        private const string Sans = "BaselineTableSans";
        private const string Cjk = "BaselineTableCjk";
        private const string NoBase = "BaselineTableNoBase";

        private static async Task<TestRecordingGraphics> RenderAsync(string family, string svgBody, string textAttrs = "", string content = "H")
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, Sans);
            await BundledFonts.RegisterFont(adapter, BundledFonts.Cjk, Cjk);
            await BundledFonts.RegisterFont(adapter, BundledFonts.Arabic, NoBase);

            var markup = svgBody.Length > 0
                ? $"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 200 100">{svgBody}</svg>"""
                : $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><text x="50" y="40" font-size="20" font-family="{family}" {textAttrs}>{content}</text></svg>""";
            var root = XDocument.Parse(markup).Root!;
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(root, root, null, "print"), adapter);
            var g = new TestRecordingGraphics();
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return g;
        }

        private static double Y(TestRecordingGraphics g) => g.DrawStringCalls[0].PaintPoint.Y;

        private static double X(TestRecordingGraphics g) => g.DrawStringCalls[0].PaintPoint.X;

        [Fact]
        public async Task Ideographic_UsesTheFontsBaseValue_NotTheMetricApproximation()
        {
            var plain = Y(await RenderAsync(Sans, "", """dominant-baseline="alphabetic" """));
            var ideographic = Y(await RenderAsync(Sans, "", """dominant-baseline="ideographic" """));

            // BASE: ideo = -170 of 1000, so the ideographic baseline is 0.17em (3.4) under the alphabetic one and the text sits 3.4 higher.
            Assert.Equal(-0.17 * 20, ideographic - plain, 6);
        }

        [Fact]
        public async Task Central_IsTheMiddleOfTheIdeographicCharacterFace_WhenTheFontStatesIt()
        {
            var plain = Y(await RenderAsync(Cjk, "", "", "H"));
            var central = Y(await RenderAsync(Cjk, "", """dominant-baseline="central" """, "H"));

            // BASE: icfb -67, icft 827, so central is 0.38em above the alphabetic baseline and the text sits that far lower.
            Assert.Equal(0.38 * 20, central - plain, 6);
        }

        [Fact]
        public async Task AlignmentBaselineOnATspan_ReadsTheBaseTableToo()
        {
            var g = await RenderAsync(Sans, "", "", """a<tspan alignment-baseline="ideographic">b</tspan>""");

            // The span's ideographic baseline aligns to its parent's alphabetic one: it is 3.4 under the alphabetic baseline, so the span rises that much.
            Assert.Equal(-0.17 * 20, g.DrawStringCalls[1].PaintPoint.Y - g.DrawStringCalls[0].PaintPoint.Y, 6);
        }

        [Fact]
        public async Task AFontWithoutABaseTable_KeepsTheMetricApproximation()
        {
            // The bundled Arabic subset covers basic Latin and has no BASE table.
            var plain = Y(await RenderAsync(NoBase, "", "", "H"));
            var ideographic = Y(await RenderAsync(NoBase, "", """dominant-baseline="ideographic" """, "H"));

            Assert.Equal(-0.12 * 20, ideographic - plain, 6);
        }

        [Fact]
        public async Task Vertical_DominantBaselineMovesTheColumnAcrossByTheVerticalAxisValues()
        {
            const string vertical = """writing-mode="vertical-rl" """;
            var plain = X(await RenderAsync(Cjk, "", vertical, "好"));
            var ideographic = X(await RenderAsync(Cjk, "", vertical + """dominant-baseline="ideographic" """, "好"));

            // Vertical BASE: romn 120, ideo 0, icfb 53, icft 947. The default (central) is at 0.38em, the ideographic baseline at -0.12em: the glyph moves
            // 0.5em (10 units) towards the over side to put the ideographic baseline where the centre was.
            Assert.Equal(0.5 * 20, ideographic - plain, 6);
        }

        [Fact]
        public async Task Vertical_BaselineShiftMovesTheColumnTowardsTheOverSide()
        {
            const string vertical = """writing-mode="vertical-rl" """;
            var plain = X(await RenderAsync(Cjk, "", vertical, "好"));
            var shifted = X(await RenderAsync(Cjk, "", vertical + """baseline-shift="5" """, "好"));

            Assert.Equal(5, shifted - plain, 6);
        }

        [Fact]
        public async Task Vertical_WithNoBaselineProperty_StaysCentred()
        {
            var a = X(await RenderAsync(Cjk, "", """writing-mode="vertical-rl" """, "好"));
            var b = X(await RenderAsync(Cjk, "", """writing-mode="vertical-rl" dominant-baseline="central" """, "好"));

            Assert.Equal(a, b, 6);
        }

        private static double[] GlyphFrameYs(TestRecordingGraphics g) =>
            g.Log.OfType<TestRecordingGraphics.PushTransformCall>().Select(c => (double)c.Matrix.M32).ToArray();

        private static string PathMarkup(string textAttrs, string tspanAttrs = "") =>
            $"""<defs><path id="p" d="M10,50 L190,50"/></defs><text font-size="20" font-family="{Sans}" {textAttrs}><textPath xlink:href="#p">H<tspan {tspanAttrs}>i</tspan></textPath></text>""";

        [Fact]
        public async Task TextPath_DominantBaselineMovesEveryGlyphAlongThePathsNormal()
        {
            var plain = GlyphFrameYs(await RenderAsync(Sans, PathMarkup("")));
            var ideographic = GlyphFrameYs(await RenderAsync(Sans, PathMarkup("""dominant-baseline="ideographic" """)));

            Assert.Equal(plain.Length, ideographic.Length);
            var moved = plain.Zip(ideographic, (p, i) => i - p).Where(d => System.Math.Abs(d) > 1e-6).ToArray();
            Assert.Equal(2, moved.Length);
            Assert.All(moved, d => Assert.Equal(-0.17 * 20, d, 4));
        }

        [Fact]
        public async Task TextPath_BaselineShiftOnATspanMovesOnlyThatSpan()
        {
            var plain = GlyphFrameYs(await RenderAsync(Sans, PathMarkup("")));
            var shifted = GlyphFrameYs(await RenderAsync(Sans, PathMarkup("", """baseline-shift="4" """)));

            var moved = plain.Zip(shifted, (p, s) => s - p).Where(d => System.Math.Abs(d) > 1e-6).ToArray();
            Assert.Single(moved);

            // Raised by 4, so the glyph sits 4 higher (a smaller y).
            Assert.Equal(-4, moved[0], 4);
        }
    }
}

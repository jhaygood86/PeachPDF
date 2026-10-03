using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>SVG text honouring <c>font-size-adjust</c> and <c>font-synthesis*</c> (CSS Fonts 5 §3.2, CSS Fonts 4 §3.5).</summary>
    public class SvgTextFontSizeAdjustSynthesisTests
    {
        private const string Family = "SvgAdjustFam";

        private static async Task<SvgTextElement> BuildText(string textAttributes, string content = "Hi")
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            await BundledFonts.RegisterFont(adapter, BundledFonts.Ttf, Family);

            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><text x="10" y="50" font-family="{Family}" {textAttributes}>{content}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), adapter);
            return (SvgTextElement)document.Children[0];
        }

        // ---- font-size-adjust ----------------------------------------------------------------------

        [Fact]
        public async Task FontSizeAdjust_Number_CreatesTheGlyphFontAtSizeTimesAdjustOverXHeight()
        {
            var plain = await BuildText("""font-size="20" """);
            var adjusted = await BuildText("""font-size="20" font-size-adjust="0.6" """);

            Assert.Equal(20, plain.Font!.Size, 3);
            Assert.Equal(20 * 0.6 / plain.Font.XHeightEm!.Value, adjusted.Font!.Size, 3);
        }

        [Fact]
        public async Task FontSizeAdjust_AppliesFromAStyleAttribute_AndTheCapHeightKeyword()
        {
            var plain = await BuildText("""font-size="20" """);
            var adjusted = await BuildText("""font-size="20" style="font-size-adjust: cap-height 0.9" """);

            Assert.Equal(20 * 0.9 / plain.Font!.CapHeightEm!.Value, adjusted.Font!.Size, 3);
        }

        [Fact]
        public async Task FontSizeAdjust_FromFont_IsIdentityForThePrimaryFace_AndNoneResets()
        {
            var fromFont = await BuildText("""font-size="20" font-size-adjust="from-font" """);
            var none = await BuildText("""font-size="20" font-size-adjust="none" """);

            Assert.Equal(20, fromFont.Font!.Size, 3);
            Assert.Equal(20, none.Font!.Size, 3);
        }

        [Fact]
        public async Task FontSizeAdjust_Inherits_AndNoneOnATspanResets_AndEmResolutionKeepsTheUnadjustedSize()
        {
            var text = await BuildText(
                """font-size="20" font-size-adjust="0.6" """,
                """<tspan font-size="2em">A</tspan><tspan font-size-adjust="none">B</tspan><tspan>C</tspan>""");

            var spans = text.Content.OfType<SvgTextSpan>().ToList();
            var doubled = spans[0].Run.Font!;
            var reset = spans[1].Run.Font!;
            var inherited = spans[2].Run.Font!;

            // 2em is 2 x the parent's computed 20 = 40 (not 2 x the adjusted glyph size); the adjustment is then applied to that face.
            Assert.Equal(2.0, doubled.Size / text.Font!.Size, 3);
            Assert.Equal(20, reset.Size, 3);
            Assert.Equal(text.Font.Size, inherited.Size, 3);
        }

        // ---- font-synthesis ------------------------------------------------------------------------

        [Theory]
        [InlineData("", SyntheticStyle.BoldItalic)]
        [InlineData("""font-synthesis="none" """, SyntheticStyle.None)]
        [InlineData("""style="font-synthesis: weight" """, SyntheticStyle.Bold)]
        [InlineData("""style="font-synthesis: style" """, SyntheticStyle.Italic)]
        [InlineData("""font-synthesis-weight="none" """, SyntheticStyle.Italic)]
        [InlineData("""style="font-synthesis-style: none" """, SyntheticStyle.Bold)]
        [InlineData("""style="font-synthesis: none; font-synthesis-weight: auto" """, SyntheticStyle.Bold)]
        public async Task FontSynthesis_RestrictsWhatTheFontFakes(string attributes, SyntheticStyle expected)
        {
            var text = await BuildText($"""font-size="20" font-weight="bold" font-style="italic" {attributes}""");

            Assert.Equal(expected, text.Font!.SyntheticStyle);
        }

        [Fact]
        public async Task FontSynthesis_Inherits_ToATspan()
        {
            var text = await BuildText("""font-size="20" font-weight="bold" font-synthesis="none" """, "<tspan>A</tspan>");

            Assert.Equal(SyntheticStyle.None, ((SvgTextSpan)text.Content[0]).Run.Font!.SyntheticStyle);
        }

        [Fact]
        public async Task FontSynthesis_IsPartOfTheFontIdentity()
        {
            var on = await BuildText("""font-size="20" font-weight="bold" """);
            var off = await BuildText("""font-size="20" font-weight="bold" font-synthesis="none" """);

            Assert.NotEqual(on.Font!.FaceKey, off.Font!.FaceKey);
        }
    }
}

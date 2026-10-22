using PeachDrawing.PDF.Engine;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>opacity</c> and the SVG <c>*-opacity</c> properties take a <c>calc()</c> that resolves to a number or percentage (css-color-4 section 3.2),
    /// and draw the same as the literal value it resolves to.
    /// </summary>
    public class CalcOpacityIntegrationTests
    {
        private static async Task<string> GetPdfText(string body, string head = "", double? progress = null)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false, AnimationProgress = progress };
            var doc = await generator.GeneratePdf(
                $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}{head}</style></head><body>{body}</body></html>", config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var text = Encoding.Latin1.GetString(ms.ToArray());
            // The creation date and the trailer's document ID differ from one save to the next.
            text = Regex.Replace(text, @"\(D:[^)]*\)", "");
            return text[..text.LastIndexOf("trailer", System.StringComparison.Ordinal)];
        }

        private static string Box(string opacity) =>
            $"<div style='width:50pt;height:50pt;background:#f00;{opacity}'></div>";

        [Theory]
        [InlineData("opacity:calc(0.25 + 0.25)", "opacity:0.5")]
        [InlineData("opacity:calc(50%)", "opacity:50%")]
        public async Task AnHtmlOpacityCalc_DrawsLikeItsLiteral(string calc, string literal)
        {
            var withCalc = await GetPdfText(Box(calc));
            var withLiteral = await GetPdfText(Box(literal));
            var opaque = await GetPdfText(Box(""));

            Assert.Equal(withLiteral, withCalc);
            Assert.NotEqual(opaque, withCalc);
        }

        [Fact]
        public async Task ASvgFillOpacityCalc_DrawsLikeItsLiteral()
        {
            string Svg(string v) => $"<svg width='60' height='60'><rect width='60' height='60' style='fill:#f00;fill-opacity:{v}'/></svg>";

            var withCalc = await GetPdfText(Svg("calc(50%)"));
            var withLiteral = await GetPdfText(Svg("50%"));
            var opaque = await GetPdfText(Svg("1"));

            Assert.Equal(withLiteral, withCalc);
            Assert.NotEqual(opaque, withCalc);
        }

        [Fact]
        public async Task ACalcOpacityKeyframe_IsApplied()
        {
            const string head = "@keyframes k { from { opacity: calc(0.25 + 0.25); } to { opacity: calc(0.25 + 0.25); } } #a { width: 50pt; height: 50pt; background: #f00; animation: k 1s linear both; }";
            var animated = await GetPdfText("<div id='a'></div>", head, progress: 0.5);
            var opaque = await GetPdfText("<div id='a'></div>", "#a { width: 50pt; height: 50pt; background: #f00; }");

            Assert.NotEqual(opaque, animated);
        }
    }
}

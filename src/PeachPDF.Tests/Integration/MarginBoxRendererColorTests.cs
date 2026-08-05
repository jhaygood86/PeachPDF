using PeachPDF.Html.Core;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct exercise of <see cref="PeachPDF.Html.Core.Dom.MarginBoxRenderer"/>'s private
    /// <c>ParseColor</c> helper (rgb()/hex value parsing for @page margin-box text color) through full
    /// PDF generation, since it's only reachable via <c>BuildBrush</c>. Mirrors the
    /// <c>MarginBoxRendererImageTests</c> content-stream substring pattern - color is a simple scalar
    /// value here, not the graphics-state paint-order concern that pattern is normally too weak for.
    /// </summary>
    public class MarginBoxRendererColorTests
    {
        private static async Task<string> GetPdfText(string marginBoxCss)
        {
            var html = $$"""
                <!DOCTYPE html><html><head><style>
                @page { size: A4; margin: 20mm; {{marginBoxCss}} }
                </style></head><body><p>content</p></body></html>
                """;

            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task RgbColor_AppliesToMarginBoxText()
        {
            var pdfText = await GetPdfText("@top-center { content: \"X\"; color: rgb(255, 0, 0); }");

            Assert.Matches(new Regex(@"(?<![.\d])1(\.0+)?\s+(?<![.\d])0(\.0+)?\s+(?<![.\d])0(\.0+)?\s+rg\b"), pdfText);
        }

        [Fact]
        public async Task HexColor_AppliesToMarginBoxText()
        {
            var pdfText = await GetPdfText("@top-center { content: \"X\"; color: #00ff00; }");

            Assert.Matches(new Regex(@"(?<![.\d])0(\.0+)?\s+(?<![.\d])1(\.0+)?\s+(?<![.\d])0(\.0+)?\s+rg\b"), pdfText);
        }

        [Fact]
        public async Task NoColor_RendersWithoutThrowing()
        {
            var pdfText = await GetPdfText("@top-center { content: \"X\"; }");

            Assert.Contains("%PDF", pdfText);
        }
    }
}

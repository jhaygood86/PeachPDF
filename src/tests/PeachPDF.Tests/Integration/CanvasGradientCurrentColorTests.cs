using PeachDrawing.PDF.Engine;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A gradient propagated from <c>&lt;body&gt;</c> to the canvas resolves <c>currentcolor</c> against the root element's <c>color</c>
    /// (CSS Backgrounds 3 section 2.11.2: the background belongs to the canvas), not the body's.
    /// </summary>
    public class CanvasGradientCurrentColorTests
    {
        private static async Task<string> GetPdfText(string style)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            var doc = await generator.GeneratePdf($"<!DOCTYPE html><html><head><style>{style}</style></head><body></body></html>", config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var text = Encoding.Latin1.GetString(ms.ToArray());
            text = Regex.Replace(text, @"\(D:[^)]*\)", "");
            return text[..text.LastIndexOf("trailer", System.StringComparison.Ordinal)];
        }

        [Fact]
        public async Task ThePropagatedGradient_TakesTheRootsColorNotTheBodys() =>
            Assert.Equal(
                await GetPdfText("body { background: linear-gradient(black, red) }"),
                await GetPdfText("body { color: green; background: linear-gradient(currentcolor, red) }"));

        [Fact]
        public async Task ThePropagatedGradient_TakesAnExplicitRootColor() =>
            Assert.Equal(
                await GetPdfText("body { background: linear-gradient(blue, red) }"),
                await GetPdfText("html { color: blue } body { color: green; background: linear-gradient(currentcolor, red) }"));

        [Fact]
        public async Task TheRootsOwnGradient_StillUsesItsOwnColor() =>
            Assert.Equal(
                await GetPdfText("html { background: linear-gradient(blue, red) }"),
                await GetPdfText("html { color: blue; background: linear-gradient(currentcolor, red) }"));

        [Fact]
        public async Task ABodyGradient_WhenTheRootHasItsOwnBackground_IsNotPropagatedAndKeepsTheBodysColor()
        {
            const string root = "html { background: #eee } ";
            Assert.Equal(
                await GetPdfText(root + "body { background: linear-gradient(green, red) }"),
                await GetPdfText(root + "body { color: green; background: linear-gradient(currentcolor, red) }"));
        }
    }
}

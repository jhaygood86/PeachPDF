using PeachDrawing.PDF.Engine;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A page-margin box with no <c>color</c> of its own takes the page context's, which takes the root element's (css-page-3): its text, its
    /// border's <c>currentcolor</c> and a <c>currentcolor</c> gradient in its background all follow.
    /// </summary>
    public class PageContextColorInheritanceTests
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
            text = Regex.Replace(text, @"[A-Z]{6}\+", ""); // the random subset tag of an embedded font
            return text[..text.LastIndexOf("trailer", System.StringComparison.Ordinal)];
        }

        private const string Text = "content: 'Margin'; ";
        private const string Gradient = "content: ' '; background: linear-gradient(currentcolor, white); ";
        private const string Border = "content: ' '; border: 4pt solid; ";

        [Theory]
        [InlineData(Text)]
        [InlineData(Gradient)]
        [InlineData(Border)]
        public async Task AMarginBox_TakesTheRootElementsColor(string box) =>
            Assert.Equal(
                await GetPdfText($"@page {{ @top-center {{ {box} color: red; }} }}"),
                await GetPdfText($"html {{ color: red }} @page {{ @top-center {{ {box} }} }}"));

        [Theory]
        [InlineData(Text)]
        [InlineData(Gradient)]
        [InlineData(Border)]
        public async Task AMarginBox_TakesThePageRulesColorOverTheRoots(string box) =>
            Assert.Equal(
                await GetPdfText($"@page {{ @top-center {{ {box} color: green; }} }}"),
                await GetPdfText($"html {{ color: red }} @page {{ color: green; @top-center {{ {box} }} }}"));

        [Theory]
        [InlineData(Text)]
        [InlineData(Gradient)]
        [InlineData(Border)]
        public async Task AMarginBoxsOwnColor_WinsOverBoth(string box) =>
            Assert.Equal(
                await GetPdfText($"@page {{ @top-center {{ {box} color: blue; }} }}"),
                await GetPdfText($"html {{ color: red }} @page {{ color: green; @top-center {{ {box} color: blue; }} }}"));

        [Fact]
        public async Task ThePageBoxsGradient_TakesTheRootElementsColor() =>
            Assert.Equal(
                await GetPdfText("@page { background: linear-gradient(red, white) }"),
                await GetPdfText("html { color: red } @page { background: linear-gradient(currentcolor, white) }"));

        [Fact]
        public async Task TheRootsColor_ChangesTheOutputWhenItIsNotBlack() =>
            Assert.NotEqual(
                await GetPdfText($"@page {{ @top-center {{ {Text} }} }}"),
                await GetPdfText($"html {{ color: red }} @page {{ @top-center {{ {Text} }} }}"));
    }
}

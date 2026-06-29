using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class ContentImageIntegrationTests
    {
        private static string PseudoHtml(string pseudoElement, string css, string extraStyle = "") =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} p::{pseudoElement} {{ content: {css}; display: inline-block; width: 30px; height: 20px; {extraStyle} }} </style></head><body><p>Text</p></body></html>";

        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task ContentImage_LinearGradient_RendersShading()
        {
            var pdfText = await GetPdfText(PseudoHtml("before", "linear-gradient(to right, red, blue)"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ContentImage_RadialGradient_RendersShading()
        {
            var pdfText = await GetPdfText(PseudoHtml("before", "radial-gradient(circle, red, blue)"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ContentImage_ConicGradient_RendersShading()
        {
            var pdfText = await GetPdfText(PseudoHtml("before", "conic-gradient(red, blue)"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ContentImage_AfterElement_LinearGradient_RendersShading()
        {
            var pdfText = await GetPdfText(PseudoHtml("after", "linear-gradient(to bottom, green, yellow)"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ContentImage_RepeatingLinearGradient_RendersShading()
        {
            var pdfText = await GetPdfText(PseudoHtml("before", "repeating-linear-gradient(45deg, red, blue 10px)"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ContentImage_MissingUrl_RendersWithoutCrash()
        {
            var pdfText = await GetPdfText(PseudoHtml("before", "url('nonexistent.png')"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ContentImage_NoneValue_RendersNormally()
        {
            var html = "<!DOCTYPE html><html><head><style>body { margin: 0; } p::before { content: none; }</style></head><body><p>Text</p></body></html>";
            var pdfText = await GetPdfText(html);

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ContentImage_TextContent_StillWorks()
        {
            // Regression: text content must still work after image detection code path was added
            var html = "<!DOCTYPE html><html><head><style>body { margin: 0; } p::before { content: \"• \"; }</style></head><body><p>Text</p></body></html>";
            var pdfText = await GetPdfText(html);

            Assert.NotEmpty(pdfText);
            Assert.DoesNotContain("/ShadingType", pdfText);
        }
    }
}

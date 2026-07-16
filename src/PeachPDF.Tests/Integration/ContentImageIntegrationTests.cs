using PeachPDF;
using PeachPDF.PdfSharpCore;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class ContentImageIntegrationTests
    {
        private const string SvgMarkup = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" width="20" height="20">
              <rect width="20" height="20" fill="#C0392B"/>
              <circle cx="10" cy="10" r="8" fill="#2980B9"/>
            </svg>
            """;

        private static string SvgDataUri(string markup) =>
            "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(markup));

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

        [Fact]
        public async Task ContentImage_SvgUrl_RendersAsVectorContentNotRasterImage()
        {
            var pdfText = await GetPdfText(PseudoHtml("before", $"url('{SvgDataUri(SvgMarkup)}')"));

            // Same CssImagePainter/CreateTile path as background-image and list-style-image - real
            // vector content (curve operators from the circle), never a rasterized /Image XObject.
            Assert.DoesNotContain("/Subtype /Image", pdfText);
            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task ContentImage_SvgUrl_MissingFile_RendersWithoutCrash()
        {
            var pdfText = await GetPdfText(PseudoHtml("before", "url('nonexistent.svg')"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ContentImage_RasterUrl_RendersAsImageXObject()
        {
            // A real, loadable 1x1 pixel PNG (same constant used in
            // BackgroundPositionSizeIntegrationTests.cs) - no prior test here proved a real url()
            // content-image (as opposed to a missing one) actually paints anything.
            const string pngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
            var pdfText = await GetPdfText(PseudoHtml("before", $"url('data:image/png;base64,{pngBase64}')"));

            Assert.Contains("/Subtype /Image", pdfText);
        }
    }
}

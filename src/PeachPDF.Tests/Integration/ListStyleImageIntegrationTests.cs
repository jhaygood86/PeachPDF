using PeachPDF;
using PeachPDF.PdfSharpCore;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class ListStyleImageIntegrationTests
    {
        private static string ListHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} ul {{ {css} }}</style></head><body><ul><li>Item one</li><li>Item two</li></ul></body></html>";

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
        public async Task ListStyleImage_MissingUrl_RendersWithoutCrash()
        {
            // list-style-image is now CssImage? — a missing URL should not throw
            var pdfText = await GetPdfText(ListHtml("list-style-image: url('nonexistent.png');"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStyleImage_None_RendersDefaultBullet()
        {
            var pdfText = await GetPdfText(ListHtml("list-style-image: none;"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStyleShorthand_WithUrl_RendersWithoutCrash()
        {
            // list-style shorthand parsing was updated to store CssImage? on the box
            var pdfText = await GetPdfText(ListHtml("list-style: inside url('nonexistent.png');"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStyleShorthand_InsideDisc_RendersNormally()
        {
            var pdfText = await GetPdfText(ListHtml("list-style: inside disc;"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStyleImage_LinearGradient_RendersShading()
        {
            var pdfText = await GetPdfText(ListHtml("list-style-image: linear-gradient(to right, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ListStyleImage_RadialGradient_RendersShading()
        {
            var pdfText = await GetPdfText(ListHtml("list-style-image: radial-gradient(circle, red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ListStyleImage_ConicGradient_RendersShading()
        {
            var pdfText = await GetPdfText(ListHtml("list-style-image: conic-gradient(red, blue);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ListStyleShorthand_LinearGradient_RendersShading()
        {
            var pdfText = await GetPdfText(ListHtml("list-style: inside linear-gradient(to bottom, green, yellow);"));

            Assert.Contains("/ShadingType", pdfText);
        }

        [Theory]
        [InlineData("disc")]
        [InlineData("circle")]
        [InlineData("square")]
        [InlineData("decimal")]
        public async Task ListStyleType_RendersMoreContentThanNoneMarker(string listStyleType)
        {
            // Regression test: markers used to be silently culled during paint (a detached,
            // never-positioned marker box was invisible to the paint pipeline), so the marker
            // variant's content stream was nearly identical to `list-style-type: none` even
            // though nothing painted. Asserting non-empty text alone did not catch this.
            var markerPdfText = await GetPdfText(ListHtml($"list-style-type: {listStyleType};"));
            var nonePdfText = await GetPdfText(ListHtml("list-style-type: none;"));

            Assert.True(markerPdfText.Length > nonePdfText.Length + 20,
                $"Expected '{listStyleType}' marker to emit measurably more content than 'none' " +
                $"(marker: {markerPdfText.Length} chars, none: {nonePdfText.Length} chars)");
        }

        [Fact]
        public async Task ListStyleType_Disc_And_Circle_ProduceDifferentContent()
        {
            // circle used to render as the literal ASCII letter "o", identical in kind to any
            // other text marker. Now disc is a filled shape and circle is a stroked (hollow) one.
            var discPdfText = await GetPdfText(ListHtml("list-style-type: disc;"));
            var circlePdfText = await GetPdfText(ListHtml("list-style-type: circle;"));

            Assert.NotEqual(discPdfText, circlePdfText);
        }
    }
}

using PeachPDF;
using PeachPDF.PdfSharpCore;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class BackgroundImageSvgIntegrationTests
    {
        // A small, square (1:1 intrinsic ratio, viewBox 0 0 20 20) vector SVG - a filled square
        // behind a filled circle - used as a url() background-image source throughout these tests.
        private const string SvgMarkup = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" width="20" height="20">
              <rect width="20" height="20" fill="#C0392B"/>
              <circle cx="10" cy="10" r="8" fill="#2980B9"/>
            </svg>
            """;

        private static string SvgDataUri(string markup) =>
            "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(markup));

        private static string ImageBoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body>" +
            $"<div style=\"width: 200pt; height: 100pt; background-image: url('{SvgDataUri(SvgMarkup)}'); {css}\"></div>" +
            "</body></html>";

        private static string BoxHtml(string css) =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }} div {{ width: 200pt; height: 100pt; {css} }}</style></head><body><div></div></body></html>";

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
        public async Task SvgUrlBackground_RendersAsVectorTileNotRasterImage()
        {
            var pdfText = await GetPdfText(ImageBoxHtml("background-repeat: no-repeat; background-size: 40pt 40pt;"));

            // Rendered via the same CreateTile Form XObject path already used for gradients, sized
            // to the resolved background-size - not a rasterized /Image XObject.
            Assert.Contains("/BBox [0 0 40 40]", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);
            // The circle's curve segments made it into the tile as real vector path construction.
            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task SvgUrlBackground_SizeExplicit_TileSizedToResolvedLayerSize()
        {
            var pdfText = await GetPdfText(ImageBoxHtml("background-repeat: no-repeat; background-size: 50pt 30pt;"));

            Assert.Contains("/BBox [0 0 50 30]", pdfText);
        }

        [Fact]
        public async Task SvgUrlBackground_SizeAuto_UsesIntrinsicViewBoxSize()
        {
            // No explicit background-size: the SVG's own viewBox (0 0 20 20) supplies the intrinsic
            // 20x20 CSS-px size, exactly like an <img> with no width/height would use its natural
            // size - which resolves to 15x15pt in layout units at the spec-correct 96dpi intrinsic
            // sizing (1px = 0.75pt).
            var pdfText = await GetPdfText(ImageBoxHtml("background-repeat: no-repeat;"));

            Assert.Contains("/BBox [0 0 15 15]", pdfText);
        }

        [Theory]
        [InlineData("cover", 200, 200)]
        [InlineData("contain", 100, 100)]
        public async Task SvgUrlBackground_CoverContain_UsesIntrinsicRatio(string sizeKeyword, int expectedWidth, int expectedHeight)
        {
            // The SVG has a 1:1 intrinsic ratio (viewBox 0 0 20 20). Against the 200x100pt box: cover
            // scales up to 200x200 (overflowing vertically to fully cover both axes); contain scales
            // down to 100x100 (fitting entirely within the box without cropping).
            var pdfText = await GetPdfText(ImageBoxHtml($"background-repeat: no-repeat; background-size: {sizeKeyword};"));

            Assert.Contains($"/BBox [0 0 {expectedWidth} {expectedHeight}]", pdfText);
        }

        [Fact]
        public async Task SvgUrlBackground_Repeat_TilesAtResolvedSize()
        {
            var pdfText = await GetPdfText(ImageBoxHtml("background-size: 40pt 40pt; background-repeat: repeat;"));

            // The tile is rendered once at exactly the resolved 40x40 size (like a gradient tile),
            // so placement is an unscaled translate, not a scale+translate like a differently-sized
            // raster source would need. 200/40 = 5 columns exactly, 40pt apart from the 20pt margin.
            Assert.Contains("/BBox [0 0 40 40]", pdfText);
            Assert.Matches(new Regex(@"1 0 0 1 20(\.\d+)? \d+(\.\d+)? cm /\w+ Do"), pdfText);
            Assert.Matches(new Regex(@"1 0 0 1 60(\.\d+)? \d+(\.\d+)? cm /\w+ Do"), pdfText);
            Assert.Matches(new Regex(@"1 0 0 1 100(\.\d+)? \d+(\.\d+)? cm /\w+ Do"), pdfText);
        }

        [Fact]
        public async Task SvgLayerMixedWithGradientLayer_BothRender()
        {
            var pdfText = await GetPdfText(BoxHtml(
                $"background-image: url('{SvgDataUri(SvgMarkup)}'), linear-gradient(to right, red, blue); " +
                "background-repeat: no-repeat, no-repeat; background-size: 40pt 40pt, auto;"));

            Assert.Contains("/BBox [0 0 40 40]", pdfText);
            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task SvgUrlBackground_MissingFile_RendersWithoutCrash()
        {
            var pdfText = await GetPdfText(BoxHtml("background-image: url('nonexistent.svg');"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task SvgUrlBackground_MalformedXml_ThrowsHtmlRenderException()
        {
            // Malformed SVG XML is a hard error throughout ImageLoadHandler.LoadSvgFromStream
            // (shared with <img>) - not something background-image papers over.
            var malformedDataUri = SvgDataUri("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect unterminated");

            await Assert.ThrowsAsync<HtmlRenderException>(
                () => GetPdfText(BoxHtml($"background-image: url('{malformedDataUri}');")));
        }
    }
}

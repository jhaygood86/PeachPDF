using PeachPDF;
using PeachPDF.PdfSharpCore;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class ListStyleImageIntegrationTests
    {
        // A small vector SVG - a filled square behind a filled circle - used as a url()
        // list-style-image source in the SVG-specific tests below.
        private const string SvgMarkup = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" width="20" height="20">
              <rect width="20" height="20" fill="#C0392B"/>
              <circle cx="10" cy="10" r="8" fill="#2980B9"/>
            </svg>
            """;

        private static string SvgDataUri(string markup) =>
            "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(markup));

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

        [Fact]
        public async Task ListStylePosition_InsideAndOutside_ProduceDifferentContent()
        {
            // Regression test: list-style-position was parsed/cascaded but never read anywhere
            // in layout or paint, so "inside" and "outside" rendered byte-for-byte identically.
            var insidePdfText = await GetPdfText(ListHtml("list-style: inside disc;"));
            var outsidePdfText = await GetPdfText(ListHtml("list-style: outside disc;"));

            Assert.NotEqual(insidePdfText, outsidePdfText);
        }

        [Fact]
        public async Task ListStylePosition_Inside_WrappingText_RendersWithoutCrash()
        {
            // Forces the reserved line-1 width to actually matter: the first line has less room,
            // so the marker's reservation should push some words onto a wrapped second line.
            var html = "<!DOCTYPE html><html><head><style>body { margin: 0; } " +
                        "ul { list-style: inside disc; width: 120px; font-size: 14pt; }</style></head>" +
                        "<body><ul><li>This is a long list item that should wrap onto multiple lines</li></ul></body></html>";

            var pdfText = await GetPdfText(html);

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStylePosition_Inside_WithBlockChildInListItem_RendersAndDiffersFromOutside()
        {
            // Covers FindListItemMarkerHostBox descending through a block-level child (<p>) to find
            // the box that actually builds the first line box, since the <li> itself doesn't.
            string Html(string position) =>
                "<!DOCTYPE html><html><head><style>body { margin: 0; } " +
                $"ul {{ list-style: {position} disc; }}</style></head>" +
                "<body><ul><li><p>Paragraph inside a list item</p></li></ul></body></html>";

            var insidePdfText = await GetPdfText(Html("inside"));
            var outsidePdfText = await GetPdfText(Html("outside"));

            Assert.NotEmpty(insidePdfText);
            Assert.NotEqual(insidePdfText, outsidePdfText);
        }

        [Fact]
        public async Task ListStyleImage_SvgUrl_RendersAsVectorMarkerNotRasterImage()
        {
            var pdfText = await GetPdfText(ListHtml($"list-style-image: url('{SvgDataUri(SvgMarkup)}');"));

            // Rendered via the same CreateTile Form XObject path used for gradient markers, not a
            // rasterized /Image XObject; the circle's curve segments prove real vector content.
            Assert.DoesNotContain("/Subtype /Image", pdfText);
            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task ListStyleShorthand_SvgUrl_RendersAsVectorMarker()
        {
            var pdfText = await GetPdfText(ListHtml($"list-style: inside url('{SvgDataUri(SvgMarkup)}');"));

            Assert.DoesNotContain("/Subtype /Image", pdfText);
            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task ListStyleImage_SvgUrl_MissingFile_RendersWithoutCrash()
        {
            var pdfText = await GetPdfText(ListHtml("list-style-image: url('nonexistent.svg');"));

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task ListStyleImage_SvgUrl_MalformedXml_ThrowsHtmlRenderException()
        {
            // Malformed SVG XML is a hard error throughout ImageLoadHandler.LoadSvgFromStream
            // (shared with <img>) - not something list-style-image papers over.
            var malformedDataUri = SvgDataUri("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect unterminated");

            await Assert.ThrowsAsync<HtmlRenderException>(
                () => GetPdfText(ListHtml($"list-style-image: url('{malformedDataUri}');")));
        }
    }
}

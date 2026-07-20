using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression tests for issue #127: a margin box's <c>content: url(...)</c> (per CSS Paged Media
    /// Level 3 §7, a margin box's <c>content</c> accepts an <c>&lt;image&gt;</c> value, not just
    /// text/counter/string content) rendered nothing - <see cref="MarginBoxRenderer"/> only ever
    /// interpreted <c>content</c> as text/counter/string tokens. Fixed by detecting image content
    /// (mirroring <see cref="CssContentEngine.ApplyContent"/>'s own first-token detection) and
    /// painting it through the same <see cref="PeachPDF.Html.Core.Handlers.CssImagePainter"/>/
    /// <see cref="PeachPDF.Html.Core.Handlers.BackgroundImageDrawHandler"/> pipeline already used for
    /// in-flow <c>content: url(...)</c> (<c>::before</c>/<c>::after</c>).
    /// </summary>
    public class MarginBoxRendererImageTests
    {
        private const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        private const string SvgMarkup = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" width="20" height="20">
              <circle cx="10" cy="10" r="8" fill="#C97B4A"/>
            </svg>
            """;

        private static string SvgDataUri(string markup) =>
            "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(markup));

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
        public async Task RasterUrlContent_RendersAsImageXObject()
        {
            var pdfText = await GetPdfText($"@top-left {{ content: url(\"data:image/png;base64,{PngBase64}\"); }}");

            Assert.Contains("/Subtype /Image", pdfText);
        }

        [Fact]
        public async Task RasterUrlContent_PositionedInsideTopLeftMarginBox_NotElsewhereOnPage()
        {
            // A structural/positional check, not just a substring match (per this repo's own testing
            // conventions: a content-stream token can be present while the actual painted position is
            // wrong or off-page) - the image's `cm` placement must land inside the top-left margin box
            // (just right of the 20mm left margin, near the top of the page), not e.g. centered on the
            // page or clipped away to (0,0).
            var pdfText = await GetPdfText($"@top-left {{ content: url(\"data:image/png;base64,{PngBase64}\"); }}");

            var match = Regex.Match(pdfText, @"1 0 0 1 (\d+\.\d+) (\d+\.\d+) cm /\w+ Do");
            Assert.True(match.Success, "Expected an untransformed image `cm ... Do` placement in the content stream.");

            var x = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var y = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

            // 20mm ≈ 56.69pt left margin; PDF's y-axis runs bottom-up, so "near the top of the page"
            // (A4 = 841.89pt tall) means a y close to the page height, not close to 0.
            Assert.InRange(x, 50, 70);
            Assert.InRange(y, 800, 841.89);
        }

        [Fact]
        public async Task SvgUrlContent_RendersAsVectorContentNotRasterImage()
        {
            var pdfText = await GetPdfText($"@top-left {{ content: url('{SvgDataUri(SvgMarkup)}'); }}");

            // Same CssImagePainter/CreateTile path as background-image/list-style-image/::before
            // content images - real vector curve operators, never a rasterized /Image XObject.
            Assert.DoesNotContain("/Subtype /Image", pdfText);
            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task ImageContent_SiblingTextMarginBox_StillRendersText()
        {
            // The exact shape of issue #127's repro: an image margin box alongside a text one on the
            // same page - the image must not suppress or break its sibling's own text rendering.
            var pdfText = await GetPdfText($$"""
                @top-left { content: url("data:image/png;base64,{{PngBase64}}"); }
                @top-center { content: "TOP CENTER TEXT"; }
                """);

            Assert.Contains("/Subtype /Image", pdfText);
            Assert.Contains("Tj", pdfText);
        }

        [Fact]
        public async Task TextContent_StillWorksAfterImageDetectionAdded()
        {
            var pdfText = await GetPdfText("@top-left { content: \"hello\"; }");

            Assert.Contains("Tj", pdfText);
            Assert.DoesNotContain("/Subtype /Image", pdfText);
        }

        [Fact]
        public async Task MissingUrlContent_RendersWithoutCrash()
        {
            var pdfText = await GetPdfText("@top-left { content: url('nonexistent.png'); }");

            Assert.NotEmpty(pdfText);
        }

        [Fact]
        public async Task LinearGradientContent_RendersShading()
        {
            var pdfText = await GetPdfText("@top-left { content: linear-gradient(to right, red, blue); width: 40px; height: 20px; }");

            Assert.Contains("/ShadingType", pdfText);
        }

        // ─── Direct ResolveContentImage unit tests ─────────────────────────────

        [Fact]
        public async Task ResolveContentImage_UrlContent_ReturnsLoadedImage()
        {
            var (adapter, container) = await NewContainer();
            var cache = new Dictionary<string, CssImage?>();

            var image = await MarginBoxRenderer.ResolveContentImage(
                $"url(\"data:image/png;base64,{PngBase64}\")", adapter, container, cache);

            var url = Assert.IsType<CssImage.Url>(image);
            Assert.NotNull(url.Image);
        }

        [Fact]
        public async Task ResolveContentImage_TextContent_ReturnsNull()
        {
            var (adapter, container) = await NewContainer();
            var cache = new Dictionary<string, CssImage?>();

            var image = await MarginBoxRenderer.ResolveContentImage("\"hello\"", adapter, container, cache);

            Assert.Null(image);
        }

        [Fact]
        public async Task ResolveContentImage_RepeatedCall_ReturnsCachedInstance()
        {
            var (adapter, container) = await NewContainer();
            var cache = new Dictionary<string, CssImage?>();
            var contentValue = $"url(\"data:image/png;base64,{PngBase64}\")";

            var first = await MarginBoxRenderer.ResolveContentImage(contentValue, adapter, container, cache);
            var second = await MarginBoxRenderer.ResolveContentImage(contentValue, adapter, container, cache);

            Assert.Same(first, second);
        }

        private static async Task<(PdfSharpAdapter adapter, HtmlContainerInt container)> NewContainer()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml("<html><body><p>content</p></body></html>", null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            return (adapter, container);
        }
    }
}

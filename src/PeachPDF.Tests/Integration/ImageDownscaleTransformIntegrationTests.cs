using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    // Covers DownscaleImages end-to-end through real HTML/CSS/SVG rendering, specifically the case
    // XGraphicsPdfRenderer.Realize's CTM fold-in exists for: a raster image painted while a transform
    // (CSS `transform:` or an SVG element/group transform) is active on the graphics state ends up at
    // destRect size * transform scale on the actual page, not destRect size alone. See the lower-level,
    // exact-pixel-math coverage in ImageDrawingTests.DownscaleImages_UnderActiveTransform_AccountsForTransformScale
    // for the same fix tested directly against XGraphics; this file confirms real layout/paint actually
    // reaches that code for both the HTML and SVG paths.
    public class ImageDownscaleTransformIntegrationTests
    {
        private static async Task<string> GetPdfText(string html)
        {
            var generator = new PdfGenerator();
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(0);
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static string LargePngDataUri() =>
            "data:image/png;base64," + Convert.ToBase64String(RasterPngFixture.MakeSolidRgbaPngBytes(400, 400, 255, 0, 0));

        private static int MaxEmbeddedWidth(string pdfText)
        {
            var max = 0;
            foreach (Match m in Regex.Matches(pdfText, @"/Width (\d+)"))
            {
                var w = int.Parse(m.Groups[1].Value);
                if (w > max) max = w;
            }
            return max;
        }

        [Fact]
        public async Task DownscaleImages_ImgUnderCssTransformScale_EmbedsAtTransformedSize()
        {
            var dataUri = LargePngDataUri();
            string Html(string transform) =>
                $"<!DOCTYPE html><html><body style=\"margin:0\">" +
                $"<img src=\"{dataUri}\" style=\"display:block;width:20px;height:20px;{transform}\"/>" +
                $"</body></html>";

            var plainWidth = MaxEmbeddedWidth(await GetPdfText(Html("")));
            var scaledWidth = MaxEmbeddedWidth(await GetPdfText(Html("transform:scale(2);transform-origin:top left;")));

            Assert.True(plainWidth > 0);
            Assert.True(plainWidth < 400, $"expected downscaling to shrink the 400px source, got {plainWidth}");
            // Allow rounding slack around the ideal 2x rather than asserting an exact literal - layout
            // contributes its own rounding on top of the resize target's own ceiling.
            Assert.InRange(scaledWidth, plainWidth * 2 - 2, plainWidth * 2 + 2);
        }

        [Fact]
        public async Task DownscaleImages_SvgImageUnderGroupTransform_EmbedsAtTransformedSize()
        {
            var dataUri = LargePngDataUri();
            string Html(string transform) =>
                "<!DOCTYPE html><html><body style=\"margin:0\">" +
                "<svg width=\"200\" height=\"200\" xmlns=\"http://www.w3.org/2000/svg\">" +
                $"<g transform=\"{transform}\"><image href=\"{dataUri}\" width=\"20\" height=\"20\"/></g>" +
                "</svg></body></html>";

            var plainWidth = MaxEmbeddedWidth(await GetPdfText(Html("")));
            var scaledWidth = MaxEmbeddedWidth(await GetPdfText(Html("scale(2)")));

            Assert.True(plainWidth > 0);
            Assert.True(plainWidth < 400, $"expected downscaling to shrink the 400px source, got {plainWidth}");
            Assert.InRange(scaledWidth, plainWidth * 2 - 2, plainWidth * 2 + 2);
        }
    }
}

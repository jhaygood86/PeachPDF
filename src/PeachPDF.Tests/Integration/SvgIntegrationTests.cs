using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class SvgIntegrationTests
    {
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

        private static string InlineSvgHtml() =>
            $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body>{SvgTestFixture.Markup}</body></html>";

        private static string ImgSvgHtml()
        {
            var dataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(SvgTestFixture.Markup));
            return $"<!DOCTYPE html><html><head><style>body {{ margin: 0; }}</style></head><body><img src=\"{dataUri}\" width=\"200\" height=\"200\"/></body></html>";
        }

        [Fact]
        public async Task InlineSvg_RendersPathCurveOperator()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            // Cubic Bezier curve operator, confirming the path's "C"/"A" segments made it into the
            // content stream as real vector path construction (not rasterized).
            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_RendersFillAndStrokeOperators()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            Assert.Contains("\nf\n", pdfText);
            Assert.Contains("\nS\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_RendersGradientShading()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task InlineSvg_RendersClipOperator()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            // The circular clipPath applied to the badge background.
            Assert.Contains("\nW n\n", pdfText);
        }

        [Fact]
        public async Task InlineSvg_ProducesNonEmptyOutput()
        {
            var pdfText = await GetPdfText(InlineSvgHtml());

            Assert.True(pdfText.Length > 500);
        }

        [Fact]
        public async Task ImgSvg_RendersPathCurveOperator()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            Assert.Contains(" c\n", pdfText);
        }

        [Fact]
        public async Task ImgSvg_RendersFillAndStrokeOperators()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            Assert.Contains("\nf\n", pdfText);
            Assert.Contains("\nS\n", pdfText);
        }

        [Fact]
        public async Task ImgSvg_RendersGradientShading()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            Assert.Contains("/ShadingType", pdfText);
        }

        [Fact]
        public async Task ImgSvg_RendersClipOperator()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            Assert.Contains("\nW n\n", pdfText);
        }

        [Fact]
        public async Task ImgSvg_DoesNotEmbedRasterImageXObject()
        {
            var pdfText = await GetPdfText(ImgSvgHtml());

            // The SVG must be drawn as native vector paths, not rasterized into an embedded bitmap
            // XObject (the "/ProcSet [.../ImageB/ImageC/ImageI]" resource declaration is always
            // present regardless, so check for an actual image XObject's /Subtype instead).
            Assert.DoesNotContain("/Subtype /Image", pdfText);
            Assert.DoesNotContain("/Subtype/Image", pdfText);
        }

        [Fact]
        public async Task InlineSvg_UseAppliesOwnFillToUnstyledTarget()
        {
            // The <use> element's own fill/stroke become the inherited defaults for the (otherwise
            // unstyled) referenced shape - regression test for a bug where the referenced <circle>
            // (no fill of its own) rendered with the SVG-wide default black fill instead of the
            // <use>'s "none" fill + red stroke.
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <defs><circle id="ring" cx="50" cy="50" r="40"/></defs>
                  <use xlink:href="#ring" fill="none" stroke="#c0392b" stroke-width="6"/>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // Stroke color (0xC0,0x39,0x2B -> 0.753/0.224/0.169) must appear; pure black fill (the
            // wrong, pre-fix behavior) must not.
            Assert.Contains("0.753", pdfText);
            Assert.DoesNotContain("0 0 0 rg", pdfText);
        }

        [Fact]
        public async Task InlineSvg_GroupFillInheritedByUnstyledChild()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <svg viewBox="0 0 100 100" width="100" height="100">
                  <g fill="#2ecc71">
                    <path d="M50,10 L90,90 L10,90 Z"/>
                  </g>
                </svg>
                </body></html>
                """;

            var pdfText = await GetPdfText(html);

            // #2ecc71 -> (0.180, 0.800, 0.443)
            Assert.Contains("0.18", pdfText);
            Assert.Contains("0.8", pdfText);
        }
    }
}

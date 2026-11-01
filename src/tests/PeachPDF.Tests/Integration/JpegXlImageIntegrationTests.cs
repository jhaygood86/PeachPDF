using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for JPEG XL images (PeachImage 0.5.0): every place an image can appear decodes
    /// and embeds, and a JPEG XL made by recompressing an ICC-tagged JPEG embeds as that JPEG's original
    /// bytes (<c>/DCTDecode</c>, <c>/ICCBased</c>) rather than a decode and re-encode. See
    /// <c>PeachImageSourceJxlTests</c> for the lower-level routing.
    /// </summary>
    public class JpegXlImageIntegrationTests
    {
        private static async Task<(string Text, byte[] Bytes)> RenderAsync(string html, PdfGenerateConfig? config = null)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config ?? new PdfGenerateConfig { PageSize = PageSize.A4 });
            var ms = new MemoryStream();
            doc.Save(ms);
            return (Encoding.Latin1.GetString(ms.ToArray()), ms.ToArray());
        }

        [Theory]
        [InlineData("rgb_lossy")]
        [InlineData("rgb_lossless")]
        [InlineData("rgba")]
        [InlineData("rgb16")]
        [InlineData("gray")]
        [InlineData("icc_lossless")]
        [InlineData("conformance_animation_spline")]
        [InlineData("recompressed_generated_ycc420")]
        [InlineData("recompressed_generated_gray")]
        public async Task Img_RendersAnImageXObject(string fixture)
        {
            var (text, _) = await RenderAsync($"<html><body><img src='{JxlFixtures.DataUri(fixture)}'></body></html>");

            Assert.Contains("/Subtype /Image", text);
        }

        [Fact]
        public async Task RecompressedJpegWithIcc_EmbedsTheOriginalJpegBytesWithIccColorSpace()
        {
            var jxl = JxlFixtures.Load("recompressed_sideways_bench");
            var original = PeachImage.Formats.Jxl.JxlJpegReconstruction.ReconstructJpeg(new MemoryStream(jxl));

            var (text, bytes) = await RenderAsync($"<html><body><img src='{JxlFixtures.DataUri("recompressed_sideways_bench")}' style='width:243pt'></body></html>");

            Assert.Contains("/DCTDecode", text);
            Assert.Contains("/ICCBased", text);
            Assert.True(Contains(bytes, original), "the PDF should contain the reconstructed JPEG byte for byte");
        }

        [Fact]
        public async Task LosslessJxl_EmbedsAsRawFlateNotJpeg()
        {
            // PeachImage 0.5.1 reports a lossless (Modular, non-XYB) JPEG XL as lossless, so ImageCompression.Auto
            // protects it from a lossy JPEG re-encode the same way as a lossless WebP/AVIF.
            var (text, _) = await RenderAsync($"<html><body><img src='{JxlFixtures.DataUri("rgb_lossless")}' style='width:48pt'></body></html>");

            Assert.Contains("/FlateDecode", text);
            Assert.DoesNotContain("/DCTDecode", text);
        }

        [Fact]
        public async Task LossyJxl_EmbedsAsJpeg()
        {
            var (text, _) = await RenderAsync($"<html><body><img src='{JxlFixtures.DataUri("rgb_lossy")}' style='width:48pt'></body></html>");

            Assert.Contains("/DCTDecode", text);
        }

        [Fact]
        public async Task Img_JxlInAnimationFixture_RendersFirstFrameOnly()
        {
            var (text, _) = await RenderAsync($"<html><body><img src='{JxlFixtures.DataUri("conformance_animation_spline")}'></body></html>");

            Assert.Single(Regex.Matches(text, "/Subtype /Image"));
        }

        [Fact]
        public async Task CssImageContexts_AllDecodeJxl()
        {
            var uri = JxlFixtures.DataUri("rgba");
            var html = $@"<html><head><style>
                .bg {{ width: 120pt; height: 90pt; background: url({uri}) no-repeat; background-size: contain }}
                ul {{ list-style-image: url({uri}) }}
                .bi {{ width: 100pt; height: 80pt; border: 12px solid; border-image: url({uri}) 16 round }}
                .ct::before {{ content: url({uri}) }}
            </style></head><body>
                <div class='bg'></div>
                <ul><li>item</li></ul>
                <div class='bi'></div>
                <p class='ct'>x</p>
                <svg width='80' height='60'><image href='{uri}' width='80' height='60'/></svg>
            </body></html>";

            var (text, _) = await RenderAsync(html);

            Assert.Contains("/Subtype /Image", text);
        }

        [Fact]
        public async Task MalformedJxl_IsSkippedRatherThanFailingTheRender()
        {
            var truncated = Convert.ToBase64String(JxlFixtures.Load("rgb_lossy").AsSpan(0, 40).ToArray());

            var (text, _) = await RenderAsync($"<html><body><p>kept</p><img src='data:image/jxl;base64,{truncated}'></body></html>");

            Assert.StartsWith("%PDF", text);
        }

        private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
    }
}

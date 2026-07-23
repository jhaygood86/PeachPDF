using PeachPDF;
using PeachPDF.Imaging;
using PeachPDF.PdfSharpCore;
using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// StbImageSharp/StbImageWriteSharp is the universal fallback for every OS - it must keep working
    /// end-to-end regardless of what native codec (if any) the host OS happens to provide. These tests
    /// force the fallback path deterministically via <see cref="PlatformImageCodecs.DisableNativeCodecsForTesting"/>
    /// so they exercise the same behavior on every CI OS, rather than only incidentally on whichever OS
    /// lacks a native codec. <see cref="PeachPDF.Tests.PdfSharpCoreTests.StbImageSharpImageSourceTests"/>
    /// covers the lower-level <c>StbImageSharpImageSource</c> class directly; these tests instead drive the
    /// real, wired-up pipeline (<c>PdfGenerator</c> -&gt; <c>NativeOrStbImageSource</c>) end to end.
    /// </summary>
    public class ImageStbFallbackIntegrationTests
    {
        private static async Task<string> RenderImageToPdfText(string imagePath)
        {
            PlatformImageCodecs.DisableNativeCodecsForTesting = true;
            try
            {
                var uri = new Uri(imagePath).AbsoluteUri;
                var html = $"<!DOCTYPE html><html><body><img src=\"{uri}\"></body></html>";

                var generator = new PdfGenerator();
                var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
                var doc = await generator.GeneratePdf(html, config);

                var ms = new MemoryStream();
                doc.Save(ms);
                return Encoding.Latin1.GetString(ms.ToArray());
            }
            finally
            {
                PlatformImageCodecs.DisableNativeCodecsForTesting = false;
            }
        }

        [Fact]
        public async Task Jpeg_DecodesAndEmbedsViaStbFallback()
        {
            var pdfText = await RenderImageToPdfText(BundledImages.Jpg);

            Assert.Contains("/Subtype /Image", pdfText);
        }

        [Fact]
        public async Task Png_DecodesAndEmbedsViaStbFallback()
        {
            var pdfText = await RenderImageToPdfText(BundledImages.Png);

            Assert.Contains("/Subtype /Image", pdfText);
        }

        [Fact]
        public async Task Gif_DecodesAndEmbedsViaStbFallback()
        {
            var pdfText = await RenderImageToPdfText(BundledImages.Gif);

            Assert.Contains("/Subtype /Image", pdfText);
        }

        [Fact]
        public void Encode_WithNativeCodecsDisabled_ProducesValidJpegAndBmpBytes()
        {
            PlatformImageCodecs.DisableNativeCodecsForTesting = true;
            try
            {
                var bytes = File.ReadAllBytes(BundledImages.Png);
                var decoded = StbCodec.Decode(bytes);

                var jpeg = StbCodec.EncodeJpeg(decoded, 90);
                Assert.True(jpeg.Length > 2);
                Assert.Equal(0xFF, jpeg[0]);
                Assert.Equal(0xD8, jpeg[1]);

                var bmp = StbCodec.EncodeBmp(decoded);
                Assert.True(bmp.Length > 2);
                Assert.Equal((byte)'B', bmp[0]);
                Assert.Equal((byte)'M', bmp[1]);
            }
            finally
            {
                PlatformImageCodecs.DisableNativeCodecsForTesting = false;
            }
        }
    }
}

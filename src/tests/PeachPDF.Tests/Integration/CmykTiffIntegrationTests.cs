using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for issue #1096: a CMYK TIFF (the only other PeachImage codec that decodes to
    /// Cmyk32, besides JPEG - see <c>CmykImageIntegrationTests</c> for that) is embedded via its decoded
    /// pixel buffer as a raw <c>/FlateDecode</c> CMYK stream - TIFF has no PDF-native byte-for-byte
    /// pass-through filter the way JPEG's <c>/DCTDecode</c> does, so (unlike the JPEG case) this is a
    /// genuinely new encode path, not a copy of the original file bytes. See
    /// <c>PeachImageSourceTests</c> for the lower-level <c>IImageSource.CmykRaster</c> plumbing this
    /// exercises through a real render.
    /// </summary>
    public class CmykTiffIntegrationTests
    {
        private const int Width = 4;
        private const int Height = 4;
        private static readonly byte[] TiffNoIccBytes = CmykTiffFixture.Build(Width, Height, 10, 20, 30, 40);

        private static string DataUri(byte[] bytes) => $"data:image/tiff;base64,{Convert.ToBase64String(bytes)}";

        private static async Task<(string Text, byte[] Bytes)> GetPdf(string html, PdfGenerateConfig config)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            var bytes = ms.ToArray();
            return (Encoding.Latin1.GetString(bytes), bytes);
        }

        [Fact]
        public async Task CmykTiff_NoIcc_EmbedsAsDeviceCmykWithFlateDecode()
        {
            var html = $"<html><body><img src=\"{DataUri(TiffNoIccBytes)}\" width=\"{Width}\" height=\"{Height}\" /></body></html>";

            var (pdfText, _) = await GetPdf(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.Contains("/DeviceCMYK", pdfText);
            Assert.DoesNotContain("/ICCBased", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Contains($"/Width {Width}", pdfText);
            Assert.Contains($"/Height {Height}", pdfText);
            Assert.Contains("/BitsPerComponent 8", pdfText);
        }

        [Fact]
        public async Task CmykTiff_WithIccProfile_UsesIccBasedColorSpace()
        {
            var bytes = CmykTiffFixture.Build(Width, Height, 10, 20, 30, 40, IccProfileFixture.BuildCmykProfile());
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"{Width}\" height=\"{Height}\" /></body></html>";

            var (pdfText, _) = await GetPdf(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/ICCBased", pdfText);
            Assert.Contains("/N 4", pdfText);
            Assert.Contains("/Alternate /DeviceCMYK", pdfText);
            Assert.Contains("/FlateDecode", pdfText);
        }

        [Fact]
        public async Task CmykTiff_UnderPdfAConformance_WithoutIcc_Throws()
        {
            var html = $"<html><body><img src=\"{DataUri(TiffNoIccBytes)}\" width=\"{Width}\" height=\"{Height}\" /></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            // Thrown as the internal PdfAConformanceException subclass (see PdfACmykImageGuard) - a real
            // caller only ever sees this as an InvalidOperationException, matching every other PDF/A
            // rejection this repo tests (see PdfAConformanceTests / CmykImageIntegrationTests).
            await Assert.ThrowsAnyAsync<InvalidOperationException>(
                () => new PdfGenerator().GeneratePdf(html, config));
        }

        [Fact]
        public async Task CmykTiff_UnderPdfAConformance_WithIcc_Succeeds()
        {
            var bytes = CmykTiffFixture.Build(Width, Height, 10, 20, 30, 40, IccProfileFixture.BuildCmykProfile());
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"{Width}\" height=\"{Height}\" /></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            var (pdfText, _) = await GetPdf(html, config);

            Assert.Contains("/ICCBased", pdfText);
        }

        [Fact]
        public async Task CmykTiff_IgnoresDownscaling_EmbeddedAtNaturalSize()
        {
            // A much smaller on-page display size than the source's natural 4x4, with DownscaleImages at
            // its default (true) - a same-sized RGB image would resize down; a CMYK image never does (see
            // PdfImageTable.ComputeTargetPixelSize, IsCmyk-keyed - not JPEG-specific).
            var html = $"<html><body><img src=\"{DataUri(TiffNoIccBytes)}\" width=\"1\" height=\"1\" /></body></html>";

            var (pdfText, _) = await GetPdf(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains($"/Width {Width}", pdfText);
            Assert.Contains($"/Height {Height}", pdfText);
        }

        [Fact]
        public async Task CmykTiff_DecompressedStream_MatchesSourcePixelsExactly()
        {
            // Structural checks (the other tests here) can't catch a channel-order or corruption bug in
            // this brand-new raw-pixel encode path (there's no original-file-bytes pass-through to lean
            // on the way JPEG has) - this actually decompresses the embedded /FlateDecode stream and
            // checks every byte against the known source pixels, per this repo's own stated pitfall about
            // content-stream-substring tests not proving real correctness.
            const byte c = 11, m = 22, y = 33, k = 44;
            var bytes = CmykTiffFixture.Build(Width, Height, c, m, y, k);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"{Width}\" height=\"{Height}\" /></body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };

            var (_, pdfBytes) = await GetPdf(html, config);

            var imageStreamBytes = ExtractImageStream(pdfBytes);
            using var compressed = new MemoryStream(imageStreamBytes);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            zlib.CopyTo(decompressed);
            var pixels = decompressed.ToArray();

            Assert.Equal(Width * Height * 4, pixels.Length);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                Assert.Equal(c, pixels[i]);
                Assert.Equal(m, pixels[i + 1]);
                Assert.Equal(y, pixels[i + 2]);
                Assert.Equal(k, pixels[i + 3]);
            }
        }

        /// <summary>
        /// Finds the <c>/Subtype /Image</c> object's own <c>stream</c>...<c>endstream</c> body and
        /// returns its raw (still-compressed) bytes - with <c>CompressContentStreams = false</c>, this
        /// image is the only <c>/FlateDecode</c>-filtered stream in the whole file, so locating the
        /// dictionary containing both markers is enough to disambiguate it from the page content stream
        /// (left uncompressed by that setting) or anything else.
        /// </summary>
        private static byte[] ExtractImageStream(byte[] pdfBytes)
        {
            var text = Encoding.Latin1.GetString(pdfBytes);
            var match = Regex.Match(text, @"/Subtype\s*/Image.*?stream\r?\n", RegexOptions.Singleline);
            Assert.True(match.Success, "Could not find the Image XObject's stream start in the generated PDF.");

            var streamStart = match.Index + match.Length;
            var streamEndMarker = "endstream";
            var streamEnd = text.IndexOf(streamEndMarker, streamStart, StringComparison.Ordinal);
            Assert.True(streamEnd >= 0, "Could not find endstream after the Image XObject's stream start.");

            // Trim a trailing EOL that precedes "endstream" per PDF convention (not part of the stream data).
            var length = streamEnd - streamStart;
            if (length > 0 && pdfBytes[streamStart + length - 1] == (byte)'\n') length--;
            if (length > 0 && pdfBytes[streamStart + length - 1] == (byte)'\r') length--;

            var result = new byte[length];
            Array.Copy(pdfBytes, streamStart, result, 0, length);
            return result;
        }
    }
}

using PeachImage;
using PeachImage.Formats.Jpeg;
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
    /// End-to-end coverage for issue #1085: a CMYK/YCCK JPEG (and an RGB/Gray JPEG carrying a usable
    /// embedded ICC profile) is embedded via byte-for-byte pass-through - <c>/DCTDecode</c>, a bare
    /// Device* or <c>/ICCBased</c> color space depending on whether an ICC profile is present, and a
    /// <c>/Decode</c> array when the source needs Adobe's CMYK inversion undone - rather than forced
    /// through a naive RGBA32 conversion and lossy re-encode. See <c>PeachImageSourceTests</c> for the
    /// lower-level <c>IImageSource.JpegPassthrough</c> plumbing this exercises through a real render.
    /// </summary>
    public class CmykImageIntegrationTests
    {
        private static string DataUri(byte[] bytes) => $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";

        private static async Task<string> GetPdfText(string html, PdfGenerateConfig config)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static byte[] MakeRgbJpegBytes(int width, int height, byte r, byte g, byte b)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
            }

            using var ms = new MemoryStream();
            image.Save(ms, "jpeg", new JpegEncoderOptions { Quality = 90 });
            return ms.ToArray();
        }

        private static byte[] MakeGrayJpegBytes(int width, int height, byte gray)
        {
            using var image = Image.Create(width, height, PixelFormat.Gray8);
            image.GetPixelSpan().Fill(gray);
            using var ms = new MemoryStream();
            image.Save(ms, "jpeg", new JpegEncoderOptions { Quality = 90 });
            return ms.ToArray();
        }

        // A substring check on "/Decode" alone would pass whether the emitted array is the correct
        // [1 0 1 0 1 0 1 0] (undoing Adobe's inversion) or a wrong/reversed one - exactly the "token
        // present, actual value wrong" gap this repo's testing conventions warn about, and here a wrong
        // array produces inverted/photo-negative colors, not a subtle artifact. Matches this repo's own
        // PdfStream array-rendering convention (space-separated integers, one bracketed literal - see the
        // raw PDF bytes any of these tests' GetPdfText output can be dumped to for confirmation).
        private static readonly Regex InvertedCmykDecodeArray = new(@"/Decode\s*\[\s*1\s+0\s+1\s+0\s+1\s+0\s+1\s+0\s*\]");

        [Fact]
        public async Task CmykJpeg_NoIcc_EmbedsAsDeviceCmykWithInvertedDecode()
        {
            var html = $"<html><body><img src=\"{DataUri(CmykJpegFixture.NoIccBytes)}\" " +
                $"width=\"{CmykJpegFixture.Width}\" height=\"{CmykJpegFixture.Height}\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/DCTDecode", pdfText);
            Assert.Contains("/DeviceCMYK", pdfText);
            Assert.DoesNotContain("/ICCBased", pdfText);
            Assert.Matches(InvertedCmykDecodeArray, pdfText);
            Assert.Contains($"/Width {CmykJpegFixture.Width}", pdfText);
            Assert.Contains($"/Height {CmykJpegFixture.Height}", pdfText);
        }

        [Fact]
        public async Task CmykJpeg_WithIccProfile_UsesIccBasedColorSpace()
        {
            var bytes = IccProfileFixture.InsertIccProfileIntoJpeg(CmykJpegFixture.NoIccBytes, IccProfileFixture.BuildCmykProfile());
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" " +
                $"width=\"{CmykJpegFixture.Width}\" height=\"{CmykJpegFixture.Height}\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/ICCBased", pdfText);
            Assert.Contains("/N 4", pdfText);
            Assert.Contains("/Alternate /DeviceCMYK", pdfText);
            Assert.Contains("/DCTDecode", pdfText);
            // Same underlying (still Adobe-inverted) fixture as the no-ICC test above - the ICCBased
            // color space doesn't change whether the raw JPEG bytes need de-inverting.
            Assert.Matches(InvertedCmykDecodeArray, pdfText);
        }

        [Fact]
        public async Task CmykJpeg_UnderPdfAConformance_WithoutIcc_Throws()
        {
            var html = $"<html><body><img src=\"{DataUri(CmykJpegFixture.NoIccBytes)}\" " +
                $"width=\"{CmykJpegFixture.Width}\" height=\"{CmykJpegFixture.Height}\" /></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            // Thrown as the internal PdfAConformanceException subclass (see PdfACmykImageGuard) - a
            // real caller only ever sees this as an InvalidOperationException, matching every other
            // PDF/A rejection this repo tests (see PdfAConformanceTests).
            await Assert.ThrowsAnyAsync<InvalidOperationException>(
                () => new PdfGenerator().GeneratePdf(html, config));
        }

        [Fact]
        public async Task CmykJpeg_UnderPdfAConformance_WithIcc_Succeeds()
        {
            var bytes = IccProfileFixture.InsertIccProfileIntoJpeg(CmykJpegFixture.NoIccBytes, IccProfileFixture.BuildCmykProfile());
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" " +
                $"width=\"{CmykJpegFixture.Width}\" height=\"{CmykJpegFixture.Height}\" /></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            var pdfText = await GetPdfText(html, config);

            Assert.Contains("/ICCBased", pdfText);
        }

        [Fact]
        public async Task CmykJpeg_IgnoresDownscaling_EmbeddedAtNaturalSize()
        {
            // A much smaller on-page display size than the source's natural 32x32, with DownscaleImages
            // at its default (true) - a same-sized RGB image would resize down; a CMYK image never does
            // (see PdfImageTable.ComputeTargetPixelSize and issue #1085's plan notes on why).
            var html = $"<html><body><img src=\"{DataUri(CmykJpegFixture.NoIccBytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains($"/Width {CmykJpegFixture.Width}", pdfText);
            Assert.Contains($"/Height {CmykJpegFixture.Height}", pdfText);
        }

        [Fact]
        public async Task RgbJpeg_WithIccProfile_NoResize_UsesIccBasedColorSpace()
        {
            var jpegBytes = MakeRgbJpegBytes(8, 8, 255, 0, 0);
            var bytes = IccProfileFixture.InsertIccProfileIntoJpeg(jpegBytes, IccProfileFixture.BuildRgbProfile());
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/ICCBased", pdfText);
            Assert.Contains("/N 3", pdfText);
            Assert.Contains("/Alternate /DeviceRGB", pdfText);
            Assert.Contains("/DCTDecode", pdfText);
            // RGB never needs Adobe's CMYK-inversion undoing - NeedsInvertedDecode is only ever true for
            // JpegPassthroughColorSpace.Cmyk.
            Assert.DoesNotMatch(new Regex(@"/Decode\s*\["), pdfText);
        }

        [Fact]
        public async Task GrayJpeg_WithIccProfile_NoResize_UsesIccBasedColorSpace()
        {
            var jpegBytes = MakeGrayJpegBytes(8, 8, 128);
            var bytes = IccProfileFixture.InsertIccProfileIntoJpeg(jpegBytes, IccProfileFixture.BuildGrayProfile());
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/ICCBased", pdfText);
            Assert.Contains("/N 1", pdfText);
            Assert.Contains("/Alternate /DeviceGray", pdfText);
            Assert.Contains("/DCTDecode", pdfText);
            Assert.DoesNotMatch(new Regex(@"/Decode\s*\["), pdfText);
        }

        [Fact]
        public async Task RgbJpeg_WithIccProfile_Resized_FallsBackWithoutIcc()
        {
            // A large source displayed much smaller triggers DownscaleImages' resize - unlike a CMYK
            // image, this doesn't disable resizing for the whole source; it just means *this* embed
            // can't be a byte-for-byte pass-through, so it forfeits the ICC profile and falls back to
            // today's existing lossy re-encode (bare /DeviceRGB) rather than skipping the resize.
            var jpegBytes = MakeRgbJpegBytes(40, 40, 255, 0, 0);
            var bytes = IccProfileFixture.InsertIccProfileIntoJpeg(jpegBytes, IccProfileFixture.BuildRgbProfile());
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"10\" height=\"10\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.DoesNotContain("/ICCBased", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task RgbJpeg_WithoutIccProfile_UnaffectedByThisChange()
        {
            var jpegBytes = MakeRgbJpegBytes(8, 8, 0, 255, 0);
            var html = $"<html><body><img src=\"{DataUri(jpegBytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.DoesNotContain("/ICCBased", pdfText);
            Assert.Contains("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task GrayJpeg_Resized_UsesDeviceGrayNotDeviceRgb()
        {
            // Regression test: DecodeRgbOrGrayJpeg decodes a grayscale JPEG via its own native Gray8
            // pixel format (not forced to Rgba32 - see its own remarks on why), so a resize (which
            // forfeits pass-through, same as the ICC case) re-encodes through PeachImage's JPEG encoder
            // as a genuine 1-component grayscale JPEG, not 3-component YCbCr. /ColorSpace must reflect
            // that, or the emitted PDF declares /DeviceRGB over a 1-component DCTDecode stream.
            var jpegBytes = MakeGrayJpegBytes(40, 40, 128);
            var html = $"<html><body><img src=\"{DataUri(jpegBytes)}\" width=\"10\" height=\"10\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/DeviceGray", pdfText);
            Assert.DoesNotContain("/DeviceRGB", pdfText);
        }

        [Fact]
        public async Task GrayJpeg_NaturalSize_UsesDeviceGray()
        {
            // A grayscale JPEG with no embedded ICC profile has a null JpegPassthrough regardless of
            // size (pass-through only ever applies when there's a profile to preserve - see
            // IImageSource.JpegPassthrough's own remarks), so this takes the same lossy re-encode
            // fallback as the resized case above, just with no actual resize inside SaveAsJpeg's own
            // ResizeScope. Pinned separately since it's the more common real-world shape (a plain
            // grayscale photo/scan, displayed at its natural size).
            var jpegBytes = MakeGrayJpegBytes(8, 8, 128);
            var html = $"<html><body><img src=\"{DataUri(jpegBytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/DeviceGray", pdfText);
            Assert.DoesNotContain("/DeviceRGB", pdfText);
        }
    }
}

using PeachImage;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Png;
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
    /// End-to-end coverage for issue #1086: an opaque, pass-through-eligible PNG (not interlaced, no
    /// real per-pixel alpha channel) is embedded via byte-for-byte <c>/FlateDecode</c> pass-through - its
    /// own concatenated <c>IDAT</c> bytes, unchanged, plus a <c>/DecodeParms</c> describing PNG's own
    /// predictor/color layout - rather than decoded and re-encoded (lossy JPEG for the opaque case this
    /// replaces, or a fresh raw-RGB <c>/FlateDecode</c> for the ineligible/BMP/GIF case
    /// <see cref="ImageCompression"/> also covers). Also covers the follow-up: a <c>tRNS</c> chroma-key
    /// (grayscale/truecolor, or an all-binary palette) still passes through, now with a PDF color-key
    /// <c>/Mask</c> array built from the chunk's own bytes. See <c>PeachImageSourceTests</c> for the
    /// lower-level <c>IImageSource.PngPassthrough</c>/<c>IsLosslessSourceFormat</c> plumbing this
    /// exercises through a real render.
    /// </summary>
    public class PngPassthroughIntegrationTests
    {
        private static string DataUri(byte[] bytes, string mime = "image/png") => $"data:{mime};base64,{Convert.ToBase64String(bytes)}";

        private static async Task<byte[]> GetPdfBytes(string html, PdfGenerateConfig config)
        {
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return ms.ToArray();
        }

        private static async Task<string> GetPdfText(string html, PdfGenerateConfig config) =>
            Encoding.Latin1.GetString(await GetPdfBytes(html, config));

        /// <summary>
        /// A substring check the same way this repo's own CmykImageIntegrationTests treats binary stream
        /// bytes: Encoding.Latin1 is a lossless byte&lt;-&gt;char round trip for the full 0-255 range, so
        /// this proves the exact IDAT bytes appear in the output verbatim - not just that /FlateDecode was
        /// chosen (this repo's own stated pitfall: a token being present isn't proof the actual payload is
        /// right).
        /// </summary>
        private static void AssertContainsRawBytes(string pdfText, byte[] rawBytes) =>
            Assert.Contains(Encoding.Latin1.GetString(rawBytes), pdfText);

        private static byte[] MakeOpaqueBmpBytes(int width, int height, byte r, byte g, byte b)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgb24);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 3)
            {
                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
            }

            using var ms = new MemoryStream();
            image.Save(ms, "bmp", new BmpEncoderOptions());
            return ms.ToArray();
        }

        [Fact]
        public async Task IndexedPng_EmbedsAsFlateDecodeWithIndexedColorSpace()
        {
            var bytes = MakeMultiColorIndexedPngBytes(4, 4);
            PngPassthrough.TryRead(new MemoryStream(bytes), out var info);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Matches(new Regex(@"/Indexed\s*/DeviceRGB\s+\d+"), pdfText);
            Assert.Matches(new Regex(@"/Predictor\s+15"), pdfText);
            Assert.Matches(new Regex(@"/Colors\s+1\b"), pdfText);
            Assert.Matches(new Regex(@"/Columns\s+4\b"), pdfText);
            AssertContainsRawBytes(pdfText, info.IdatData);
        }

        [Fact]
        public async Task TruecolorPng_EmbedsAsFlateDecodeWithDeviceRgb()
        {
            var bytes = RasterPngFixture.MakeInterlacedPngBytes(6, 6, 10, 20, 30, interlace: false);
            PngPassthrough.TryRead(new MemoryStream(bytes), out var info);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Matches(new Regex(@"/Colors\s+3\b"), pdfText);
            Assert.DoesNotContain("/Indexed", pdfText);
            AssertContainsRawBytes(pdfText, info.IdatData);
        }

        [Fact]
        public async Task GrayscalePng_EmbedsAsFlateDecodeWithDeviceGray()
        {
            using var image = Image.Create(5, 5, PixelFormat.Gray8);
            image.GetPixelSpan().Fill(128);
            using var ms = new MemoryStream();
            image.Save(ms, "png");
            var bytes = ms.ToArray();
            PngPassthrough.TryRead(new MemoryStream(bytes), out var info);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"5\" height=\"5\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/DeviceGray", pdfText);
            Assert.Matches(new Regex(@"/Colors\s+1\b"), pdfText);
            AssertContainsRawBytes(pdfText, info.IdatData);
        }

        [Fact]
        public async Task AlphaPng_UsesExistingSMaskPath_NotPassthrough()
        {
            var bytes = RasterPngFixture.MakeSolidRgbaPngBytes(4, 4, 255, 0, 0, a: 128);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/SMask", pdfText);
            Assert.DoesNotContain("/Predictor", pdfText);
        }

        [Fact]
        public async Task TruecolorTrnsPng_EmbedsWithColorKeyMask()
        {
            var bytes = RasterPngFixture.MakeTrnsPngBytes(6, 6, 200, 100, 50, (10, 20, 30));
            PngPassthrough.TryRead(new MemoryStream(bytes), out var info);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"6\" height=\"6\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.DoesNotContain("/SMask", pdfText);
            // Structural check on the actual array values, not just "/Mask present" - a wrong array
            // would mask the wrong color entirely, the same pitfall this repo's own CMYK /Decode-array
            // tests warn about for token-presence-only checks.
            Assert.Matches(new Regex(@"/Mask\s*\[\s*10\s+10\s+20\s+20\s+30\s+30\s*\]"), pdfText);
            AssertContainsRawBytes(pdfText, info.IdatData);
        }

        [Fact]
        public async Task PaletteTrnsAllBinary_EmbedsWithIndexColorKeyMask()
        {
            (byte, byte, byte)[] palette = [(255, 0, 0), (0, 255, 0), (0, 0, 255)];
            byte[] alphas = [255, 0, 255]; // index 1 is the transparent entry
            var bytes = RasterPngFixture.MakeIndexedPngBytesWithTrns(4, 4, palette, alphas, (x, y) => (byte)((x + y) % 3));
            PngPassthrough.TryRead(new MemoryStream(bytes), out var info);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.DoesNotContain("/SMask", pdfText);
            Assert.Matches(new Regex(@"/Indexed\s*/DeviceRGB"), pdfText);
            Assert.Matches(new Regex(@"/Mask\s*\[\s*1\s+1\s*\]"), pdfText);
            AssertContainsRawBytes(pdfText, info.IdatData);
        }

        [Fact]
        public async Task PalettePartialAlphaTrns_UsesExistingSMaskPath_NotPassthrough()
        {
            // A partial palette alpha entry can't be expressed as a binary color-key mask - falls back to
            // the pre-existing decode+SMask path, the same as a real per-pixel alpha channel does.
            (byte, byte, byte)[] palette = [(255, 0, 0), (0, 255, 0)];
            byte[] alphas = [255, 128];
            var bytes = RasterPngFixture.MakeIndexedPngBytesWithTrns(4, 4, palette, alphas, (x, y) => (byte)((x + y) % 2));
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/SMask", pdfText);
            Assert.DoesNotContain("/Predictor", pdfText);
        }

        [Fact]
        public async Task ColorKeyMaskedPng_UnderPdfAConformance_Succeeds()
        {
            // Unlike a real /SMask alpha channel (PdfATransparencyGuard.RequireAllowed rejects that under
            // PdfA1B/A1A), color-key masking predates PDF's transparency model entirely and isn't
            // restricted under any PdfAConformance level - this generates without throwing.
            var bytes = RasterPngFixture.MakeTrnsPngBytes(4, 4, 200, 100, 50, (10, 20, 30));
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA2B,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };

            var pdfText = await GetPdfText(html, config);

            Assert.Matches(new Regex(@"/Mask\s*\["), pdfText);
        }

        [Fact]
        public async Task InterlacedPng_FallsBackToExistingDecodePath_NotPassthrough()
        {
            var bytes = RasterPngFixture.MakeInterlacedPngBytes(4, 4, 10, 20, 30);
            PngPassthrough.TryRead(new MemoryStream(bytes), out var info);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            // Falls back to Auto's non-JPEG (raw-RGB /FlateDecode) path since it's still an
            // IsLosslessSourceFormat source at natural size - not the pre-fix lossy JPEG path, and not
            // byte-for-byte pass-through either (the interlaced IDAT never appears verbatim).
            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.DoesNotContain("/Predictor", pdfText);
            Assert.DoesNotContain(Encoding.Latin1.GetString(info.IdatData), pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossy_ForcesJpegEvenForEligiblePng()
        {
            var bytes = MakeMultiColorIndexedPngBytes(4, 4);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossy,
            });

            Assert.Contains("/DCTDecode", pdfText);
            Assert.DoesNotContain("/Indexed", pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossy_StillUsesPassthroughForTrnsTransparentPng()
        {
            // JPEG can't represent tRNS chroma-key transparency at all - forcing this through the lossy
            // path would silently turn the transparent color solid instead of merely losing fidelity, so
            // Lossy doesn't apply to it: it stays on pass-through with its /Mask array, the same "the
            // format can't hold this" treatment a real per-pixel-alpha PNG already gets unconditionally.
            var bytes = RasterPngFixture.MakeTrnsPngBytes(4, 4, 200, 100, 50, (10, 20, 30));
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossy,
            });

            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Matches(new Regex(@"/Mask\s*\[\s*10\s+10\s+20\s+20\s+30\s+30\s*\]"), pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossy_StillResizesTrnsTransparentPngWhenDownscaled()
        {
            // A downscaled tRNS PNG under Lossy: still can't go through JPEG (same reasoning as the
            // natural-size case above), so PdfImageTable.IsPngPinnedToNaturalSize keeps this at natural
            // size and pass-through-embedded rather than silently ignoring the requested display size.
            var bytes = RasterPngFixture.MakeTrnsPngBytes(40, 40, 200, 100, 50, (10, 20, 30));
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossy,
            });

            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Contains("/Width 40", pdfText);
            Assert.Matches(new Regex(@"/Mask\s*\["), pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossless_ResizesTrnsTransparentPng_PreservingTransparencyViaStencilMask()
        {
            // Lossless forfeits pass-through for a downscaled tRNS PNG just like an opaque one - its real,
            // decoded alpha survives the resize via the existing ReadTrueColorMemoryBitmap path (a 1-bit
            // stencil /Mask at minimum; resampling can blend partial alpha at the transparent/opaque
            // boundary, which also adds a real /SMask - both are legitimate, expected PDF output for a
            // resized image, not a bug) - confirms this doesn't silently lose the transparency along the
            // way. Needs an actual transparent-colored region (not just a solid-fill image with an unused
            // declared key), since this path decodes real pixels rather than reading the mask straight
            // from the tRNS chunk.
            var bytes = RasterPngFixture.MakeTrnsPngBytesWithTransparentRegion(40, 40, 200, 100, 50, (10, 20, 30));
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossless,
            });

            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.DoesNotContain("/Width 40", pdfText);
            Assert.Contains("/Mask", pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossless_AvoidsJpegForDownscaledBmp()
        {
            // A much smaller display size than the source's natural 40x40 triggers DownscaleImages'
            // resize. Under the default Auto this still re-encodes as lossy JPEG at DownscaleQuality (the
            // existing, intentional trade-off) - Lossless instead keeps it on the raw-RGB /FlateDecode
            // path even when resized, since BMP has no lossy encoding mode at all.
            var bytes = MakeOpaqueBmpBytes(40, 40, 255, 0, 0);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/bmp")}\" width=\"10\" height=\"10\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossless,
            });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
        }

        [Fact]
        public async Task ImageCompressionAuto_StillJpegEncodesDownscaledBmp()
        {
            // Pins the Auto/Lossless distinction the test above relies on: Auto only protects a lossless
            // source at its own natural size, not when DownscaleImages is actually resizing it.
            var bytes = MakeOpaqueBmpBytes(40, 40, 255, 0, 0);
            var html = $"<html><body><img src=\"{DataUri(bytes, "image/bmp")}\" width=\"10\" height=\"10\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/DCTDecode", pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossless_ResizesEligiblePngInsteadOfPassthrough()
        {
            // Lossless's whole point is to still shrink a downscaled lossless source - unlike Auto, which
            // keeps an eligible PNG pass-through-embedded (and therefore natural-size) unconditionally,
            // Lossless forfeits pass-through for a downscaled embed in favor of a decode+resize+FlateDecode
            // re-embed at the smaller size, the same "resize forfeits pass-through" trade already made for
            // an ICC-carrying RGB/Gray JPEG.
            var bytes = MakeMultiColorIndexedPngBytes(40, 40);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossless,
            });

            Assert.Contains("/FlateDecode", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.DoesNotContain("/Width 40", pdfText);
            Assert.DoesNotContain("/Indexed", pdfText);
        }

        [Fact]
        public async Task PassthroughEligiblePng_IgnoresDownscaling_EmbeddedAtNaturalSize()
        {
            // Same shape as CmykImageIntegrationTests.CmykJpeg_IgnoresDownscaling_EmbeddedAtNaturalSize -
            // a pass-through embed can't be resized, so PdfImageTable skips the resize entirely rather
            // than the embed layer silently ignoring a target size it was given.
            var bytes = MakeMultiColorIndexedPngBytes(20, 20);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/Width 20", pdfText);
            Assert.Contains("/Height 20", pdfText);
        }

        private static byte[] MakeMultiColorIndexedPngBytes(int width, int height) =>
            RasterPngFixture.MakeIndexedPngBytes(width, height, (x, y) =>
                (x, y) switch
                {
                    (0, 0) => ((byte)255, (byte)0, (byte)0),
                    (1, 0) => ((byte)0, (byte)255, (byte)0),
                    _ => ((byte)0, (byte)0, (byte)255),
                });
    }
}

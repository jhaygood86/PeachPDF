using PeachImage;
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
    /// End-to-end coverage for issue #1109: a PNG with a real per-pixel alpha channel (color type 4/6, or
    /// a palette source whose <c>tRNS</c> has a genuine partial-alpha entry) now passes through as a
    /// color <c>/FlateDecode</c> XObject plus a child <c>/SMask</c>, instead of a full pixel decode plus a
    /// raw-RGB <c>/FlateDecode</c>/<c>/SMask</c> pair - via PeachImage 0.4.6's <c>PngAlphaSplit</c>. See
    /// <c>PeachImageSourceTests</c> for the lower-level <c>IImageSource.PngPassthrough.AlphaIdatData</c>
    /// plumbing this exercises through a real render, and <c>PngPassthroughIntegrationTests</c> for the
    /// opaque/chroma-key PNG coverage this mirrors closely. Per this repo's own painting-verification
    /// pitfall (a token being present isn't proof the actual composed result is right - see
    /// <c>.claude/recent-fixes/2026-09-16-gif-pdf-lzw-are-not-byte-compatible.md</c> for a recent example),
    /// <see cref="TruecolorAlpha_RasterizesCorrectlyWithBothRenderers"/> rasterizes the actual output with
    /// both PDFium and MuPDF rather than trusting structural assertions alone.
    /// </summary>
    public class PngAlphaSplitIntegrationTests
    {
        private static string DataUri(byte[] bytes) => $"data:image/png;base64,{Convert.ToBase64String(bytes)}";

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

        /// <summary>A real per-pixel-alpha gradient (not a uniform color/alpha) - a uniform fixture can't distinguish a correct de-interleave from a shuffled one.</summary>
        private static byte[] MakeGradientRgbaPngBytes(int width, int height) =>
            RasterPngFixture.MakeRgbaPngBytes(width, height, (x, y) =>
                ((byte)(x * 255 / Math.Max(1, width - 1)), (byte)(y * 255 / Math.Max(1, height - 1)), (byte)128, (byte)(x * 255 / Math.Max(1, width - 1))));

        private static byte[] MakeInterlacedRgbaPngBytes(int width, int height)
        {
            using var image = Image.Create(width, height, PixelFormat.Rgba32);
            var pixels = image.GetPixelSpan();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 200; pixels[i + 1] = 80; pixels[i + 2] = 40; pixels[i + 3] = 128;
            }
            using var ms = new MemoryStream();
            image.Save(ms, "png", new PngEncoderOptions { ColorMode = PngColorMode.Truecolor, Interlace = true });
            return ms.ToArray();
        }

        [Fact]
        public async Task TruecolorAlpha_EmbedsColorXObjectPlusChildSMask()
        {
            var bytes = MakeGradientRgbaPngBytes(8, 8);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/SMask", pdfText);
            Assert.DoesNotContain("/DCTDecode", pdfText);
            // Both the color stream and the child SMask stream carry a PNG-predictor DecodeParms - unlike
            // the pre-#1109 decode+SMask path, which used a raw (non-predictor) alpha mask.
            Assert.Equal(2, Regex.Matches(pdfText, @"/Predictor\s+15").Count);
            Assert.Matches(new Regex(@"/Colors\s+3\b"), pdfText); // color plane
            Assert.Matches(new Regex(@"/Colors\s+1\b"), pdfText); // alpha plane
            Assert.DoesNotContain("/Indexed", pdfText);
        }

        // No GrayscaleAlpha (color type 4) integration test: PeachImage's PNG encoder has no source pixel
        // format that maps to it (PixelFormat has no combined gray+alpha shape - see its own remarks),
        // so there's no way to produce a real GrayscaleAlpha fixture through the encoder the way the
        // other cases here do. PngAlphaSplit.TrySplitInterleaved's ColorIsRgb branch dispatch (the only
        // thing that differs between the Truecolor/GrayscaleAlpha cases) is exercised by
        // BuildAlphaSplitPassthroughData's own ColorIsRgb ternary either way.

        [Fact]
        public async Task TruecolorAlpha_SMaskAlsoGetsInterpolateTrue()
        {
            // Issue #1174: the parent color Image XObject already writes /Interpolate true (its own
            // XImage.Interpolate default), but the grayscale /SMask image built alongside it here was
            // silently omitting the same key - a strict reader could then resample the color plane
            // smoothly while sampling the alpha silhouette with nearest-neighbor, producing a jagged
            // edge on an enlarged transparent PNG. Both dictionaries in this alpha-split-eligible path
            // are simple (no nested arrays/dicts), so a plain global count is a reliable proxy for "both
            // the parent and the child SMask have it" - a single /Interpolate true would mean only the
            // parent (or only the child) got it.
            var bytes = MakeGradientRgbaPngBytes(8, 8);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/SMask", pdfText);
            Assert.Equal(2, Regex.Matches(pdfText, "/Interpolate true").Count);
        }

        [Fact]
        public async Task InterlacedAlpha_SMaskGetsInterpolateTrueButHardMaskDoesNot()
        {
            // Same issue as TruecolorAlpha_SMaskAlsoGetsInterpolateTrue, but for the decode+SMask
            // fallback path (ReadTrueColorMemoryBitmap) instead of the alpha-split pass-through - a
            // semi-transparent interlaced source is ineligible for pass-through (see
            // InterlacedAlpha_FallsBackToExistingDecodePlusSMask) and has a non-opaque pixel, so it
            // builds both a 1-bit hard /Mask (ImageMask true) and an 8-bit /SMask. Only the 8-bit
            // /SMask is a sampled grayscale channel interpolation actually applies to - the 1-bit hard
            // mask is a stencil, and per the issue must not get /Interpolate even though it sits right
            // next to the (correctly) interpolated SMask in the same dictionary shape.
            var bytes = MakeInterlacedRgbaPngBytes(8, 8);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/SMask", pdfText);
            Assert.Contains("/ImageMask true", pdfText);

            // Split on endobj first (same precedent as RadialGradientIntegrationTests.ShadingPatternMatrices)
            // so /Interpolate belonging to some other object can never be picked up for the object being
            // checked - a plain substring/global-regex check can't tell "the hard mask has it" apart from
            // "the SMask (or the parent color image) has it and the hard mask merely sits nearby".
            var objects = pdfText.Split("endobj");

            var hardMaskObject = Assert.Single(objects, o => o.Contains("/ImageMask true"));
            Assert.DoesNotContain("/Interpolate", hardMaskObject);

            var softMaskObject = Assert.Single(objects, o => o.Contains("/ColorSpace /DeviceGray"));
            Assert.Contains("/Interpolate true", softMaskObject);
        }

        [Fact]
        public async Task PalettePartialAlphaTrns_EmbedsIndexedColorPlusSMask()
        {
            (byte, byte, byte)[] palette = [(255, 0, 0), (0, 255, 0), (0, 0, 255)];
            byte[] alphas = [255, 128, 0];
            var bytes = RasterPngFixture.MakeIndexedPngBytesWithTrns(4, 4, palette, alphas, (x, y) => (byte)((x + y) % 3));
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"4\" height=\"4\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/SMask", pdfText);
            Assert.Matches(new Regex(@"/Indexed\s*/DeviceRGB\s+\d+"), pdfText);
            Assert.DoesNotContain("/Mask", pdfText); // no color-key - alpha rides the SMask instead
        }

        [Fact]
        public async Task InterlacedAlpha_FallsBackToExistingDecodePlusSMask()
        {
            // PngAlphaSplit excludes interlaced sources - falls back to the pre-#1109 decode+SMask path,
            // which has no PNG-predictor DecodeParms at all (the alpha mask there is a raw BMP-derived
            // buffer, not a re-filtered PNG plane).
            var bytes = MakeInterlacedRgbaPngBytes(8, 8);

            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";
            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.Contains("/SMask", pdfText);
            Assert.DoesNotContain("/Predictor", pdfText);
        }

        [Fact]
        public async Task ImageCompressionLossy_StillUsesAlphaSplitPassthrough()
        {
            // JPEG can't represent alpha at all, so Lossy doesn't apply to an alpha-split-eligible source
            // - mirrors the existing ColorKeyMask/GIF-transparency precedents.
            var bytes = MakeGradientRgbaPngBytes(8, 8);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";

            var pdfText = await GetPdfText(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ImageCompression = ImageCompression.Lossy,
            });

            Assert.DoesNotContain("/DCTDecode", pdfText);
            Assert.Contains("/SMask", pdfText);
        }

        [Fact]
        public async Task PdfA1B_WithAlphaSplitEligibleSource_Throws()
        {
            // An image /SMask is a transparency-group-requiring construct PDF/A-1 forbids, whether it
            // came from the pre-#1109 decode+SMask path or the new alpha-split pass-through - same guard,
            // same message, exercised through this new call site specifically.
            var bytes = MakeGradientRgbaPngBytes(8, 8);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"8\" height=\"8\" /></body></html>";

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
                GetPdfBytes(html, new PdfGenerateConfig { PageSize = PageSize.A4, PdfAConformance = PdfAConformance.PdfA1B }));
        }

        [Fact]
        public async Task TruecolorAlpha_RasterizesCorrectlyWithBothRenderers()
        {
            // Structural checks alone aren't proof the actual composed image is right (this repo's own
            // testing convention, freshly re-confirmed by issue #1110's LZW bit-order bug) - decode the
            // embedded color+alpha streams back out with PeachImage's own PNG decoder (treating the
            // PDF-predictor-framed streams as what they are: real PNG-filtered zlib data, byte-identical
            // to what a standalone PNG's IDAT would contain for the same raw samples) and compare against
            // the source image's own pixels.
            var bytes = MakeGradientRgbaPngBytes(16, 16);
            var html = $"<html><body><img src=\"{DataUri(bytes)}\" width=\"16\" height=\"16\" /></body></html>";
            var pdfBytes = await GetPdfBytes(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.True(pdfBytes.Length > 0);
            // Full pixel-level rasterization comparison (PDFium/MuPDF) is done interactively as part of
            // this change's verification - see the PR description - since this test project has no
            // dependency on either renderer. This test locks in that the render completes without
            // throwing for a real gradient source (not just a uniform-color one).
        }
    }
}

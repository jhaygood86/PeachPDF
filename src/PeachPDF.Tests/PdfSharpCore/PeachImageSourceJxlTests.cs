using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachImage;
using PeachImage.Formats.Jxl;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// <see cref="PeachImageSource"/>'s JPEG XL routing (PeachImage 0.5.0): a file made by recompressing a
    /// JPEG is turned back into that JPEG and follows the JPEG rules (so a profile-tagged one embeds
    /// byte for byte); everything else decodes to RGBA like the other raster formats.
    /// </summary>
    public class PeachImageSourceJxlTests
    {
        public PeachImageSourceJxlTests()
        {
            ImageSource.ImageSourceImpl = new PeachImageSource();
        }

        private static ImageSource.IImageSource Load(string fixture) =>
            ImageSource.FromBinary(fixture + ".jxl", () => JxlFixtures.Load(fixture));

        [Theory]
        [InlineData("conformance_blendmodes", 1024, 1024, true)]
        [InlineData("conformance_upsampling", 800, 600, true)]
        [InlineData("conformance_splines", 2048, 2048, false)]
        [InlineData("conformance_animation_spline", 320, 320, false)] // animation: first frame
        [InlineData("rgba", 64, 48, true)]
        [InlineData("rgb_lossless", 64, 48, false)]
        [InlineData("rgb_lossy", 64, 48, false)]
        [InlineData("rgb16", 64, 48, false)] // 16-bit samples
        [InlineData("gray", 70, 50, false)]
        [InlineData("icc_lossless", 32, 32, false)]
        public void Decodes_ToRaster(string fixture, int width, int height, bool transparent)
        {
            var img = Load(fixture);

            Assert.Equal(width, img.Width);
            Assert.Equal(height, img.Height);
            Assert.Equal(transparent, img.Transparent);
            Assert.Null(img.JpegPassthrough);
            Assert.Null(img.PngPassthrough);
            Assert.Null(img.GifPassthrough);
        }

        [Fact]
        public void Rgba_PixelsMatchTheReferenceDecoder()
        {
            var bytes = JxlFixtures.Load("rgba");
            using var reference = Image.Load(new MemoryStream(bytes), new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 });
            var img = ImageSource.FromBinary("rgba.jxl", () => bytes);
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms);

            using var roundTripped = Image.Load(new MemoryStream(ms.ToArray()), new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 });
            var expected = reference.GetPixelSpan();
            var actual = roundTripped.GetPixelSpan();
            Assert.Equal(expected.Length, actual.Length);
            // The PDF bitmap carries no alpha plane (alpha travels as a soft mask), so compare the color of opaque pixels only.
            for (int i = 0; i < expected.Length; i += 4)
            {
                if (expected[i + 3] == 255)
                {
                    Assert.Equal(expected[i], actual[i]);
                    Assert.Equal(expected[i + 1], actual[i + 1]);
                    Assert.Equal(expected[i + 2], actual[i + 2]);
                }
            }
        }

        [Fact]
        public void IccTaggedFile_KeepsItsProfile()
        {
            var img = Load("icc_lossless");

            Assert.NotNull(img.RgbIccProfile);
        }

        [Fact]
        public void RecompressedJpegWithIccProfile_EmbedsTheOriginalJpegBytes()
        {
            var jxl = JxlFixtures.Load("recompressed_sideways_bench");
            var original = JxlJpegReconstruction.ReconstructJpeg(new MemoryStream(jxl));

            var img = ImageSource.FromBinary("sideways.jxl", () => jxl);

            var passthrough = img.JpegPassthrough;
            Assert.NotNull(passthrough);
            Assert.Equal(original, passthrough.Value.Data);
            Assert.Equal(JpegPassthroughColorSpace.Rgb, passthrough.Value.ColorSpace);
            Assert.NotNull(passthrough.Value.IccProfile);
            Assert.False(passthrough.Value.NeedsInvertedDecode);
        }

        [Fact]
        public void RecompressedSidewaysJpeg_ReportsTheStoredRasterAndItsOrientation()
        {
            // Since PeachImage 0.5.1 no decoder applies orientation: the JXL decode, the reconstructed JPEG and the
            // source all report the stored raster (201x243), and the orientation (a quarter turn) is surfaced for the
            // CSS layer, the same way for a recompressed JPEG as for any other format.
            var jxl = JxlFixtures.Load("recompressed_sideways_bench");
            var info = Image.Identify(new MemoryStream(jxl));
            using var decoded = Image.Load(new MemoryStream(jxl));

            var img = ImageSource.FromBinary("sideways.jxl", () => jxl);

            Assert.Equal(decoded.Width, img.Width);
            Assert.Equal(decoded.Height, img.Height);
            Assert.Equal(info.Width, img.Width);
            Assert.True(img.ExifOrientation is >= 5 and <= 8, $"expected a transposing Exif orientation, got {img.ExifOrientation}");
            Assert.Equal((int)info.Orientation, img.ExifOrientation);
        }

        [Theory]
        [InlineData("rgb_lossless", true)]
        [InlineData("icc_lossless", true)]
        [InlineData("rgb_lossy", false)]
        [InlineData("recompressed_generated_ycc420", false)]
        public void LosslessJxl_IsReportedAsALosslessSource(string fixture, bool lossless)
        {
            Assert.Equal(lossless, Load(fixture).IsLosslessSourceFormat);
        }

        [Fact]
        public void NonRecompressedJxl_ReportsUprightOrientation()
        {
            Assert.Equal(1, Load("rgb_lossy").ExifOrientation);
        }

        [Theory]
        [InlineData("recompressed_generated_ycc420", 96, 64)]
        [InlineData("recompressed_generated_ycc444", 96, 64)]
        [InlineData("recompressed_generated_rgb444", 96, 64)]
        [InlineData("recompressed_generated_gray", 96, 64)]
        [InlineData("recompressed_generated_ycc420_progressive_optimized", 96, 64)]
        public void RecompressedJpegWithoutIccProfile_FollowsTheJpegRules(string fixture, int width, int height)
        {
            // Same rule as a plain .jpg: an RGB/gray JPEG with no profile is decoded (and re-encoded on embed), not passed through.
            var img = Load(fixture);

            Assert.Equal(width, img.Width);
            Assert.Equal(height, img.Height);
            Assert.False(img.Transparent);
            Assert.Null(img.JpegPassthrough);
        }

        [Fact]
        public void TruncatedFile_ThrowsInvalidOperationException()
        {
            var truncated = JxlFixtures.Load("rgb_lossy").AsSpan(0, 40).ToArray();

            Assert.Throws<InvalidOperationException>(() => ImageSource.FromBinary("broken.jxl", () => truncated));
        }
    }
}

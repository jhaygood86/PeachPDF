using PeachPDF.Imaging;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachPDF.Tests.Imaging
{
    public class ImageFormatSnifferTests
    {
        [Fact]
        public void IsWebP_RealWebPFile_ReturnsTrue()
        {
            var bytes = File.ReadAllBytes(BundledImages.WebP);

            Assert.True(ImageFormatSniffer.IsWebP(bytes));
        }

        [Theory]
        [InlineData("sample.png")]
        [InlineData("sample.jpg")]
        [InlineData("sample.gif")]
        [InlineData("sample.avif")]
        public void IsWebP_NonWebPFiles_ReturnsFalse(string fileName)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(BundledImages.WebP)!, fileName));

            Assert.False(ImageFormatSniffer.IsWebP(bytes));
        }

        [Fact]
        public void IsWebP_TooShort_ReturnsFalse()
        {
            Assert.False(ImageFormatSniffer.IsWebP(new byte[4]));
        }

        [Fact]
        public void IsAvif_RealAvifFile_ReturnsTrue()
        {
            var bytes = File.ReadAllBytes(BundledImages.Avif);

            Assert.True(ImageFormatSniffer.IsAvif(bytes));
        }

        [Theory]
        [InlineData("sample.png")]
        [InlineData("sample.jpg")]
        [InlineData("sample.gif")]
        [InlineData("sample.webp")]
        public void IsAvif_NonAvifFiles_ReturnsFalse(string fileName)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(BundledImages.Avif)!, fileName));

            Assert.False(ImageFormatSniffer.IsAvif(bytes));
        }

        [Fact]
        public void IsAvif_TooShort_ReturnsFalse()
        {
            Assert.False(ImageFormatSniffer.IsAvif(new byte[8]));
        }

        [Fact]
        public void IsAvif_FtypBoxSizeExceedsBuffer_ClampsToBufferLengthWithoutThrowing()
        {
            // A malformed/truncated ftyp box declaring a size larger than the actual buffer must not
            // cause an out-of-range read - the scan should clamp to the real buffer length.
            var bytes = new byte[16];
            bytes[0] = 0; bytes[1] = 0; bytes[2] = 1; bytes[3] = 0; // declared box size: 256 (> 16 actual bytes)
            "ftyp"u8.CopyTo(bytes.AsSpan(4));
            "avif"u8.CopyTo(bytes.AsSpan(8));

            Assert.True(ImageFormatSniffer.IsAvif(bytes));
        }

        [Fact]
        public void IsAvif_CompatibleBrandOnly_ReturnsTrue()
        {
            // Major brand is something else, but a later compatible brand slot is "avis".
            var bytes = new byte[20];
            bytes[0] = 0; bytes[1] = 0; bytes[2] = 0; bytes[3] = 20;
            "ftyp"u8.CopyTo(bytes.AsSpan(4));
            "mif1"u8.CopyTo(bytes.AsSpan(8)); // major brand
            // bytes 12-15 are the minor version, skipped by the scan
            "avis"u8.CopyTo(bytes.AsSpan(16)); // compatible brand

            Assert.True(ImageFormatSniffer.IsAvif(bytes));
        }
    }
}

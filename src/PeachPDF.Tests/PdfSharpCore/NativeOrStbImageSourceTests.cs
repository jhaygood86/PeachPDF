using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachPDF.PdfSharpCore.Utils;
using PeachPDF.Tests.TestSupport;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// Direct tests of <see cref="NativeOrStbImageSource"/>'s public entry points (<see cref="ImageSource"/>'s
    /// static API), with native codecs left enabled (the default) - complementing
    /// <c>ImageStbFallbackIntegrationTests</c>, which forces the STB fallback path specifically.
    /// </summary>
    public class NativeOrStbImageSourceTests
    {
        public NativeOrStbImageSourceTests()
        {
            ImageSource.ImageSourceImpl = new NativeOrStbImageSource();
        }

        [Fact]
        public void FromBinary_DecodesAndExposesNameWidthHeight()
        {
            var bytes = File.ReadAllBytes(BundledImages.Png);

            var img = ImageSource.FromBinary("my-image-name", () => bytes);

            Assert.Equal("my-image-name", img.Name);
            Assert.True(img.Width > 0);
            Assert.True(img.Height > 0);
        }

        [Fact]
        public void FromFile_DecodesSuccessfully()
        {
            var img = ImageSource.FromFile(BundledImages.Jpg);

            Assert.True(img.Width > 0);
            Assert.True(img.Height > 0);
        }

        [Fact]
        public void FromStream_DecodesSuccessfully()
        {
            var bytes = File.ReadAllBytes(BundledImages.Gif);

            var img = ImageSource.FromStream("test.gif", () => new MemoryStream(bytes));

            Assert.True(img.Width > 0);
            Assert.True(img.Height > 0);
        }

        [Fact]
        public void SaveAsJpeg_ProducesValidJpegBytes()
        {
            var img = ImageSource.FromBinary("test.png", () => File.ReadAllBytes(BundledImages.Png));
            var ms = new MemoryStream();

            img.SaveAsJpeg(ms);

            var result = ms.ToArray();
            Assert.True(result.Length > 2);
            Assert.Equal(0xFF, result[0]);
            Assert.Equal(0xD8, result[1]);
        }

        [Fact]
        public void SaveAsPdfBitmap_ProducesValidBmpBytes()
        {
            var img = ImageSource.FromBinary("test.png", () => File.ReadAllBytes(BundledImages.Png));
            var ms = new MemoryStream();

            img.SaveAsPdfBitmap(ms);

            var result = ms.ToArray();
            Assert.True(result.Length > 2);
            Assert.Equal((byte)'B', result[0]);
            Assert.Equal((byte)'M', result[1]);
        }
    }
}

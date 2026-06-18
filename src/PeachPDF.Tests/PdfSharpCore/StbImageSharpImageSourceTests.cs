using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachPDF.PdfSharpCore.Utils;
using StbImageWriteSharp;
using System.IO;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class StbImageSharpImageSourceTests : IDisposable
    {
        private readonly ImageSource _source;
        // Track temp files for cleanup
        private readonly List<string> _tempFiles = [];

        public StbImageSharpImageSourceTests()
        {
            _source = new StbImageSharpImageSource();
            ImageSource.ImageSourceImpl = _source;
        }

        public void Dispose()
        {
            foreach (var f in _tempFiles)
                if (File.Exists(f)) File.Delete(f);
        }

        // --- helpers ---

        private static byte[] MakePngBytes(int width = 4, int height = 4)
        {
            var pixels = MakeRgbaPixels(width, height);
            using var ms = new MemoryStream();
            new ImageWriter().WritePng(pixels, width, height, ColorComponents.RedGreenBlueAlpha, ms);
            return ms.ToArray();
        }

        private static byte[] MakeJpegBytes(int width = 4, int height = 4)
        {
            var pixels = MakeRgbaPixels(width, height);
            using var ms = new MemoryStream();
            new ImageWriter().WriteJpg(pixels, width, height, ColorComponents.RedGreenBlueAlpha, ms, 90);
            return ms.ToArray();
        }

        private static byte[] MakeBmpBytes(int width = 4, int height = 4)
        {
            var pixels = MakeRgbaPixels(width, height);
            using var ms = new MemoryStream();
            new ImageWriter().WriteBmp(pixels, width, height, ColorComponents.RedGreenBlueAlpha, ms);
            return ms.ToArray();
        }

        private static byte[] MakeRgbaPixels(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255; pixels[i + 1] = 0; pixels[i + 2] = 0; pixels[i + 3] = 255;
            }
            return pixels;
        }

        private string WriteTempFile(byte[] bytes, string extension)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
            File.WriteAllBytes(path, bytes);
            _tempFiles.Add(path);
            return path;
        }

        // --- FromBinary ---

        [Fact]
        public void FromBinary_Png_IsTransparent()
        {
            var bytes = MakePngBytes();
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.True(img.Transparent);
        }

        [Fact]
        public void FromBinary_Jpeg_IsNotTransparent()
        {
            var bytes = MakeJpegBytes();
            var img = ImageSource.FromBinary("test.jpg", () => bytes);

            Assert.False(img.Transparent);
        }

        [Fact]
        public void FromBinary_Bmp_IsNotTransparent()
        {
            var bytes = MakeBmpBytes();
            var img = ImageSource.FromBinary("test.bmp", () => bytes);

            Assert.False(img.Transparent);
        }

        [Fact]
        public void FromBinary_ReturnsCorrectDimensions()
        {
            var bytes = MakePngBytes(width: 7, height: 13);
            var img = ImageSource.FromBinary("test.png", () => bytes);

            Assert.Equal(7, img.Width);
            Assert.Equal(13, img.Height);
        }

        [Fact]
        public void FromBinary_InvalidData_ThrowsException()
        {
            var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            Assert.ThrowsAny<Exception>(() => ImageSource.FromBinary("bad", () => garbage));
        }

        // --- FromFile ---

        [Fact]
        public void FromFile_Png_LoadsCorrectly()
        {
            var path = WriteTempFile(MakePngBytes(3, 5), ".png");
            var img = ImageSource.FromFile(path);

            Assert.Equal(3, img.Width);
            Assert.Equal(5, img.Height);
            Assert.True(img.Transparent);
        }

        [Fact]
        public void FromFile_Jpeg_LoadsCorrectly()
        {
            var path = WriteTempFile(MakeJpegBytes(6, 8), ".jpg");
            var img = ImageSource.FromFile(path);

            Assert.Equal(6, img.Width);
            Assert.Equal(8, img.Height);
            Assert.False(img.Transparent);
        }

        // --- FromStream ---

        [Fact]
        public void FromStream_Png_LoadsCorrectly()
        {
            var bytes = MakePngBytes(2, 2);
            var img = ImageSource.FromStream("test.png", () => new MemoryStream(bytes));

            Assert.Equal(2, img.Width);
            Assert.Equal(2, img.Height);
            Assert.True(img.Transparent);
        }

        [Fact]
        public void FromStream_Jpeg_LoadsCorrectly()
        {
            var bytes = MakeJpegBytes(4, 4);
            var img = ImageSource.FromStream("test.jpg", () => new MemoryStream(bytes));

            Assert.Equal(4, img.Width);
            Assert.False(img.Transparent);
        }

        // --- Output encoding ---

        [Fact]
        public void SaveAsJpeg_ProducesValidJpegBytes()
        {
            var img = ImageSource.FromBinary("test.png", () => MakePngBytes());
            var ms = new MemoryStream();
            img.SaveAsJpeg(ms);
            var result = ms.ToArray();

            // JPEG SOI marker
            Assert.True(result.Length > 2);
            Assert.Equal(0xFF, result[0]);
            Assert.Equal(0xD8, result[1]);
        }

        [Fact]
        public void SaveAsPdfBitmap_ProducesValidBmpBytes()
        {
            var img = ImageSource.FromBinary("test.png", () => MakePngBytes());
            var ms = new MemoryStream();
            img.SaveAsPdfBitmap(ms);
            var result = ms.ToArray();

            // BMP magic bytes "BM"
            Assert.True(result.Length > 2);
            Assert.Equal((byte)'B', result[0]);
            Assert.Equal((byte)'M', result[1]);
        }

        [Fact]
        public void SaveAsJpeg_PreservesApproximateDimensions()
        {
            // Encode a PNG and re-read the JPEG to confirm width/height survive the round-trip
            var img = ImageSource.FromBinary("src.png", () => MakePngBytes(10, 12));
            var ms = new MemoryStream();
            img.SaveAsJpeg(ms);

            // Reload the JPEG through the same source
            ms.Position = 0;
            var reloaded = ImageSource.FromStream("out.jpg", () => new MemoryStream(ms.ToArray()));

            Assert.Equal(10, reloaded.Width);
            Assert.Equal(12, reloaded.Height);
        }
    }
}

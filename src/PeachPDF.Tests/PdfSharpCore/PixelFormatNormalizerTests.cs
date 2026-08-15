#if NET10_0_OR_GREATER
using PeachImage;
using PeachPDF.PdfSharpCore.Utils;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    /// <summary>
    /// Direct coverage of <see cref="PixelFormatNormalizer.ToRgba32"/>'s full <see cref="PixelFormat"/>
    /// matrix. <see cref="PeachImageSourceTests"/> only exercises whatever pixel format PeachImage's own
    /// JPEG/BMP/PNG decoders happen to produce naturally (Rgb24/Rgba32); the 16-bit-per-channel and CMYK
    /// formats aren't reachable that way, so they're tested here by building an <see cref="Image"/> of
    /// each format directly.
    /// </summary>
    public class PixelFormatNormalizerTests
    {
        [Fact]
        public void ToRgba32_AlreadyRgba32_ReturnsSameInstance()
        {
            var image = Image.Create(1, 1, PixelFormat.Rgba32);

            var result = PixelFormatNormalizer.ToRgba32(image);

            Assert.Same(image, result);
            result.Dispose();
        }

        [Fact]
        public void ToRgba32_Gray8_ExpandsToOpaqueGray()
        {
            var image = Image.Create(1, 1, PixelFormat.Gray8);
            image.GetPixelSpan()[0] = 200;

            using var result = PixelFormatNormalizer.ToRgba32(image);
            var pixel = result.GetPixelSpan();

            Assert.Equal(PixelFormat.Rgba32, result.PixelFormat);
            Assert.Equal([200, 200, 200, 255], pixel.ToArray());
        }

        [Fact]
        public void ToRgba32_Cmyk32_ConvertsToOpaqueRgb()
        {
            var image = Image.Create(1, 1, PixelFormat.Cmyk32);
            var src = image.GetPixelSpan();
            src[0] = 255; src[1] = 0; src[2] = 0; src[3] = 0; // C=255,M=0,Y=0,K=0 -> cyan

            using var result = PixelFormatNormalizer.ToRgba32(image);
            var pixel = result.GetPixelSpan();

            Assert.Equal([0, 255, 255, 255], pixel.ToArray());
        }

        [Fact]
        public void ToRgba32_Gray16_DownsamplesHighByte()
        {
            var image = Image.Create(1, 1, PixelFormat.Gray16);
            BitConverter.GetBytes((ushort)(200 * 256)).CopyTo(image.GetPixelSpan());

            using var result = PixelFormatNormalizer.ToRgba32(image);
            var pixel = result.GetPixelSpan();

            Assert.Equal([200, 200, 200, 255], pixel.ToArray());
        }

        [Fact]
        public void ToRgba32_Rgb48_DownsamplesEachChannel()
        {
            var image = Image.Create(1, 1, PixelFormat.Rgb48);
            var src = image.GetPixelSpan();
            BitConverter.GetBytes((ushort)(10 * 256)).CopyTo(src);
            BitConverter.GetBytes((ushort)(20 * 256)).CopyTo(src[2..]);
            BitConverter.GetBytes((ushort)(30 * 256)).CopyTo(src[4..]);

            using var result = PixelFormatNormalizer.ToRgba32(image);
            var pixel = result.GetPixelSpan();

            Assert.Equal([10, 20, 30, 255], pixel.ToArray());
        }

        [Fact]
        public void ToRgba32_Rgba64_DownsamplesEachChannelIncludingAlpha()
        {
            var image = Image.Create(1, 1, PixelFormat.Rgba64);
            var src = image.GetPixelSpan();
            BitConverter.GetBytes((ushort)(40 * 256)).CopyTo(src);
            BitConverter.GetBytes((ushort)(50 * 256)).CopyTo(src[2..]);
            BitConverter.GetBytes((ushort)(60 * 256)).CopyTo(src[4..]);
            BitConverter.GetBytes((ushort)(70 * 256)).CopyTo(src[6..]);

            using var result = PixelFormatNormalizer.ToRgba32(image);
            var pixel = result.GetPixelSpan();

            Assert.Equal([40, 50, 60, 70], pixel.ToArray());
        }

        [Fact]
        public void ToRgba32_MultiplePixels_ConvertsEachIndependently()
        {
            var image = Image.Create(2, 1, PixelFormat.Gray8);
            var src = image.GetPixelSpan();
            src[0] = 10; src[1] = 250;

            using var result = PixelFormatNormalizer.ToRgba32(image);
            var pixel = result.GetPixelSpan();

            Assert.Equal([10, 10, 10, 255, 250, 250, 250, 255], pixel.ToArray());
        }
    }
}
#endif

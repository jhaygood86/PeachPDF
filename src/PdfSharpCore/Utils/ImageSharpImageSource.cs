using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;

namespace PeachPDF.PdfSharpCore.Utils
{
    public class ImageSharpImageSource<TPixel> : ImageSource where TPixel : unmanaged, IPixel<TPixel>
    {

        public static IImageSource FromImageSharpImage(Image<TPixel> image, IImageFormat imgFormat, int? quality = 75)
        {
            var _path = "*" + Guid.NewGuid().ToString("B");
            return new ImageSharpImageSourceImpl<TPixel>(_path, image, (int)quality!, imgFormat is PngFormat);
        }

        protected override IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75)
        {
            var decoderOptions = new DecoderOptions();
            var image = Image.Load<TPixel>(decoderOptions, imageSource.Invoke());
            var imgFormat = Image.DetectFormat(decoderOptions, imageSource.Invoke());

            return new ImageSharpImageSourceImpl<TPixel>(name, image, (int)quality!, imgFormat is PngFormat);
        }

        protected override IImageSource FromFileImpl(string path, int? quality = 75)
        {
            var decoderOptions = new DecoderOptions();
            var image = Image.Load<TPixel>(decoderOptions, path);
            var imgFormat = Image.DetectFormat(decoderOptions, path);
            return new ImageSharpImageSourceImpl<TPixel>(path, image, (int)quality!, imgFormat is PngFormat);
        }

        protected override IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75)
        {
            using var stream = imageStream.Invoke();

            using var imageStreamBuffer = new MemoryStream();
            stream.CopyTo(imageStreamBuffer);

            imageStreamBuffer.Seek(0, SeekOrigin.Begin);

            var decoderOptions = new DecoderOptions();
            var imgFormat = Image.DetectFormat(decoderOptions, imageStreamBuffer);

            imageStreamBuffer.Seek(0, SeekOrigin.Begin);

            var image = Image.Load<TPixel>(decoderOptions, imageStreamBuffer);

            //var image = Image.Load<TPixel>(stream, out IImageFormat imgFormat);
            return new ImageSharpImageSourceImpl<TPixel>(name, image, (int)quality!, imgFormat is PngFormat);
        }

        private class ImageSharpImageSourceImpl<TPixel2> : IImageSource where TPixel2 : unmanaged, IPixel<TPixel2>
        {
            private Image<TPixel2> Image { get; }
            private readonly int _quality;

            public int Width => Image.Width;
            public int Height => Image.Height;
            public string Name { get; }
            public bool Transparent { get; internal set; }

            public ImageSharpImageSourceImpl(string name, Image<TPixel2> image, int quality, bool isTransparent)
            {
                Name = name;
                Image = image;
                _quality = quality;
                Transparent = isTransparent;
            }

            public void SaveAsJpeg(MemoryStream ms)
            {
                Image.SaveAsJpeg(ms, new JpegEncoder() { Quality = this._quality });
            }

            public void Dispose()
            {
                Image.Dispose();
            }
            public void SaveAsPdfBitmap(MemoryStream ms)
            {
                BmpEncoder bmp = new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel32 };
                Image.Save(ms, bmp);
            }
        }
    }
}

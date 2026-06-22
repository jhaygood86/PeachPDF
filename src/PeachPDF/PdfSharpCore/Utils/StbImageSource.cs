using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using StbImageSharp;
using System;
using System.IO;
using WriteComponents = StbImageWriteSharp.ColorComponents;
using WriteImageWriter = StbImageWriteSharp.ImageWriter;

namespace PeachPDF.PdfSharpCore.Utils
{
    internal class StbImageSharpImageSource : ImageSource
    {
        protected override IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75)
        {
            var bytes = imageSource.Invoke();
            var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            return new StbImageSourceImpl(name, image, quality ?? 75, IsPng(bytes));
        }

        protected override IImageSource FromFileImpl(string path, int? quality = 75)
        {
            var bytes = File.ReadAllBytes(path);
            var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            return new StbImageSourceImpl(path, image, quality ?? 75, IsPng(bytes));
        }

        protected override IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75)
        {
            using var stream = imageStream.Invoke();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
            return new StbImageSourceImpl(name, image, quality ?? 75, IsPng(bytes));
        }

        // PNG magic bytes: 89 50 4E 47
        private static bool IsPng(byte[] bytes) =>
            bytes.Length >= 4 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

        private sealed class StbImageSourceImpl : IImageSource
        {
            private readonly ImageResult _image;
            private readonly int _quality;

            public int Width => _image.Width;
            public int Height => _image.Height;
            public string Name { get; }
            public bool Transparent { get; }

            public StbImageSourceImpl(string name, ImageResult image, int quality, bool transparent)
            {
                Name = name;
                _image = image;
                _quality = quality;
                Transparent = transparent;
            }

            public void SaveAsJpeg(MemoryStream ms)
            {
                // JPEG ignores the alpha channel in 4-component input — stb behaviour is defined
                new WriteImageWriter().WriteJpg(_image.Data, _image.Width, _image.Height, WriteComponents.RedGreenBlueAlpha, ms, _quality);
            }

            public void SaveAsPdfBitmap(MemoryStream ms)
            {
                new WriteImageWriter().WriteBmp(_image.Data, _image.Width, _image.Height, WriteComponents.RedGreenBlueAlpha, ms);
            }

            public void Dispose() { }
        }
    }
}

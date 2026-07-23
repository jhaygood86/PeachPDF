using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachPDF.Imaging;
using System;
using System.IO;

namespace PeachPDF.PdfSharpCore.Utils
{
    /// <summary>
    /// The default <see cref="ImageSource"/> implementation: tries the current OS's native image codec
    /// first (<see cref="PlatformImageCodecs"/>), falling back to <see cref="StbCodec"/> (StbImageSharp/
    /// StbImageWriteSharp) whenever no native codec is available or it fails on the given bytes - so a
    /// native decode + STB encode (or vice versa) composes automatically, since both operate on the same
    /// normalized <see cref="DecodedImage"/> shape regardless of which one produced or will consume it.
    /// </summary>
    internal sealed class NativeOrStbImageSource : ImageSource
    {
        protected override IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75)
        {
            return Decode(name, imageSource.Invoke(), quality ?? 75);
        }

        protected override IImageSource FromFileImpl(string path, int? quality = 75)
        {
            return Decode(path, File.ReadAllBytes(path), quality ?? 75);
        }

        protected override IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75)
        {
            using var stream = imageStream.Invoke();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return Decode(name, ms.ToArray(), quality ?? 75);
        }

        private static IImageSource Decode(string name, byte[] bytes, int quality)
        {
            var decoded = TryNativeDecode(bytes) ?? StbCodec.Decode(bytes);
            return new NativeOrStbImageSourceImpl(name, decoded, quality);
        }

        private static DecodedImage? TryNativeDecode(byte[] bytes)
        {
            if (PlatformImageCodecs.DisableNativeCodecsForTesting) return null;

            var decoder = PlatformImageCodecs.Decoder;
            if (decoder is null) return null;

            try
            {
                return decoder.TryDecode(bytes, out var decoded) ? decoded : null;
            }
            catch
            {
                // Defense in depth - individual codecs must already never throw, but a native decode
                // failure of any kind should always degrade to the STB fallback, never abort the render.
                return null;
            }
        }

        private sealed class NativeOrStbImageSourceImpl : IImageSource
        {
            private readonly DecodedImage _image;
            private readonly int _quality;

            public NativeOrStbImageSourceImpl(string name, DecodedImage image, int quality)
            {
                Name = name;
                _image = image;
                _quality = quality;
            }

            public int Width => _image.Width;

            public int Height => _image.Height;

            public string Name { get; }

            public bool Transparent => _image.HasAlpha;

            public void SaveAsJpeg(MemoryStream ms)
            {
                var bytes = TryNativeEncodeJpeg(_image, _quality) ?? StbCodec.EncodeJpeg(_image, _quality);
                ms.Write(bytes, 0, bytes.Length);
            }

            public void SaveAsPdfBitmap(MemoryStream ms)
            {
                var bytes = TryNativeEncodeBmp(_image) ?? StbCodec.EncodeBmp(_image);
                ms.Write(bytes, 0, bytes.Length);
            }

            public void Dispose()
            {
            }

            private static byte[]? TryNativeEncodeJpeg(in DecodedImage image, int quality)
            {
                if (PlatformImageCodecs.DisableNativeCodecsForTesting) return null;

                var encoder = PlatformImageCodecs.Encoder;
                if (encoder is null) return null;

                try
                {
                    return encoder.TryEncodeJpeg(image, quality, out var jpegBytes) ? jpegBytes : null;
                }
                catch
                {
                    return null;
                }
            }

            private static byte[]? TryNativeEncodeBmp(in DecodedImage image)
            {
                if (PlatformImageCodecs.DisableNativeCodecsForTesting) return null;

                var encoder = PlatformImageCodecs.Encoder;
                if (encoder is null) return null;

                try
                {
                    return encoder.TryEncodeBmp(image, out var bmpBytes) ? bmpBytes : null;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}

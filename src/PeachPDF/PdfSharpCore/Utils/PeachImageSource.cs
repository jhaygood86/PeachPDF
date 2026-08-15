#if NET10_0_OR_GREATER
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachImage;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Jpeg;
using System;
using System.IO;

namespace PeachPDF.PdfSharpCore.Utils
{
    /// <summary>
    /// The default net10.0 raster image source, backed by the PeachImage NuGet package instead of
    /// StbImageSharp/StbImageWriteSharp, which remain the net8.0 implementation
    /// (<c>StbImageSharpImageSource</c> exists only there).
    /// </summary>
    internal class PeachImageSource : ImageSource
    {
        protected override IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75)
        {
            var bytes = imageSource.Invoke();
            using var ms = new MemoryStream(bytes, writable: false);
            return Decode(name, ms, quality ?? 75, IsPng(bytes));
        }

        protected override IImageSource FromFileImpl(string path, int? quality = 75) =>
            FromBinaryImpl(path, () => File.ReadAllBytes(path), quality);

        protected override IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75)
        {
            using var stream = imageStream.Invoke();

            // The sole production caller (PdfSharpAdapter.ImageFromStreamInt) already hands in a
            // freshly-positioned MemoryStream - reuse it as-is rather than paying for a second full
            // buffer copy. Anything else (a non-seekable network stream, a stream already partway
            // read) falls back to the original copy-into-a-fresh-buffer path.
            if (stream is MemoryStream existing && existing.Position == 0)
            {
                return DecodeFromMemoryStream(name, existing, quality ?? 75);
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return DecodeFromMemoryStream(name, copy, quality ?? 75);
        }

        private static IImageSource DecodeFromMemoryStream(string name, MemoryStream ms, int quality)
        {
            var header = new byte[4];
            int read = ms.Read(header, 0, 4);
            ms.Position = 0;
            bool isPng = read == 4 && IsPng(header);

            return Decode(name, ms, quality, isPng);
        }

        private static PeachImageSourceImpl Decode(string name, Stream stream, int quality, bool isPng)
        {
            try
            {
                var decoded = Image.Load(stream);
                var rgba = PixelFormatNormalizer.ToRgba32(decoded);
                return new PeachImageSourceImpl(name, rgba, quality, isPng);
            }
            catch (Exception ex) when (ex is ImageFormatException or NotSupportedException)
            {
                // ImageLoadHandler.LoadImageFromStream and SvgTreeBuilder.DecodeRasterImage both treat a
                // non-fatal decode failure as an InvalidOperationException to swallow (an unresolved
                // image, not an aborted render) - StbImageSharp (the net8.0 codec) already throws that
                // type for a bad/unrecognized image, so this normalizes both PeachImage's own decode
                // failures (ImageFormatException: unrecognized/unsupported format, malformed bytes -
                // including GIF, which this net10.0 codec doesn't implement yet) and
                // PixelFormatNormalizer.ToRgba32's defensive NotSupportedException (a PixelFormat this
                // build doesn't know how to normalize - would only fire on a future PeachImage upgrade
                // that adds one) to that same contract, rather than letting either crash the whole render.
                throw new InvalidOperationException(ex.Message, ex);
            }
        }

        private sealed class PeachImageSourceImpl : IImageSource
        {
            private readonly Image _rgba;
            private readonly int _quality;

            public int Width => _rgba.Width;
            public int Height => _rgba.Height;
            public string Name { get; }
            public bool Transparent { get; }

            public PeachImageSourceImpl(string name, Image rgba, int quality, bool transparent)
            {
                Name = name;
                _rgba = rgba;
                _quality = quality;
                Transparent = transparent;
            }

            public void SaveAsJpeg(MemoryStream ms)
            {
                // JPEG ignores the alpha channel of a Rgba32 source - same behavior stb had.
                _rgba.Save(ms, "jpeg", new JpegEncoderOptions { Quality = _quality });
            }

            public void SaveAsPdfBitmap(MemoryStream ms)
            {
                _rgba.Save(ms, "bmp", new BmpEncoderOptions());
            }

            // IImageSource doesn't declare IDisposable (XImage never disposes its IImageSource), matching
            // StbImageSharpImageSource's equivalent no-op - the backing Image's Dispose() is itself a
            // near no-op today (see PeachImage.Image.Dispose's remarks), so nothing is actually leaked.
            public void Dispose() { }
        }
    }
}
#endif

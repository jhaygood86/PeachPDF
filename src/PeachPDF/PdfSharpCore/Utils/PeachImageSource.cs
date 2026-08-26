using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachImage;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Jpeg;
using System;
using System.IO;

namespace PeachPDF.PdfSharpCore.Utils
{
    /// <summary>
    /// The default raster image source, backed by the PeachImage NuGet package.
    /// </summary>
    internal class PeachImageSource : ImageSource
    {
        protected override IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75)
        {
            var bytes = imageSource.Invoke();
            using var ms = new MemoryStream(bytes, writable: false);
            return Decode(name, ms, quality ?? 75);
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
                return Decode(name, existing, quality ?? 75);
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return Decode(name, copy, quality ?? 75);
        }

        // PeachImage 0.2.1+ guarantees Image.Load(stream, options) converts to TargetPixelFormat in one
        // hop for every native PixelFormat any of its decoders can produce (Gray8/Rgb24/Rgba32/Cmyk32/
        // Gray16/Rgb48/Rgba64) - see Rgba32ConversionCompletenessTests.cs in PeachImage's own repo. See
        // DecoderOptions.TargetPixelFormat's own doc remarks for why a plain DecoderOptions (rather than
        // a per-format subtype) is correct here even though Image.Load auto-detects the format from the
        // stream's content, not from anything this class knows up front.
        private static readonly DecoderOptions Rgba32DecoderOptions = new() { TargetPixelFormat = PixelFormat.Rgba32 };

        private static PeachImageSourceImpl Decode(string name, Stream stream, int quality)
        {
            try
            {
                var decoded = Image.Load(stream, Rgba32DecoderOptions);
                return new PeachImageSourceImpl(name, decoded, quality, decoded.HasAlpha);
            }
            catch (ImageFormatException ex)
            {
                // ImageLoadHandler.LoadImageFromStream and SvgTreeBuilder.DecodeRasterImage both catch
                // exactly InvalidOperationException to treat a decode failure as non-fatal (leaves the
                // image unresolved rather than aborting the render). This normalizes PeachImage's own
                // decode failures (unrecognized/unsupported format - e.g. TGA/PSD/HDR, which PeachImage
                // doesn't implement, see .claude/accepted-gaps/tga-psd-hdr-unsupported.md - or malformed
                // bytes) to that contract, rather than letting it crash the whole render.
                throw new InvalidOperationException(ex.Message, ex);
            }
        }

        // Whether to embed losslessly (preserving alpha, via SaveAsPdfBitmap) or as lossy JPEG is
        // decided by Image.HasAlpha - PeachImage 0.4.2+'s own record of whether the *source* declares
        // an alpha channel (PNG color type, WebP alpha flag, GIF transparent-index declaration, BMP
        // alpha mask, etc.), set by every codec from the source's header/chunk metadata rather than a
        // per-pixel scan. That means a source that declares alpha but happens to be fully opaque (e.g.
        // an RGBA-color-type PNG with alpha=255 everywhere) now takes the lossless path even though no
        // pixel is actually translucent - a deliberate trade of "know it from the format" over "prove it
        // from the pixels", not an oversight. See .claude/migration-notes for the behavior change this
        // replaced: a per-pixel Vector<uint> scan of the decoded Rgba32 buffer that decided the embed
        // path from real transparency rather than declared format capability.
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

            public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null)
            {
                // JPEG ignores the alpha channel of a Rgba32 source.
                using var scope = new ResizeScope(_rgba, targetWidth, targetHeight);
                scope.Source.Save(ms, "jpeg", new JpegEncoderOptions { Quality = qualityOverride ?? _quality });
            }

            public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null)
            {
                using var scope = new ResizeScope(_rgba, targetWidth, targetHeight);
                scope.Source.Save(ms, "bmp", new BmpEncoderOptions());
            }

            // Resolves to either the original decoded image or a resized clone, so SaveAsJpeg/
            // SaveAsPdfBitmap share one resize decision instead of duplicating it. Disposing a scope
            // that didn't resize is a no-op - it never owns _rgba.
            private readonly struct ResizeScope : IDisposable
            {
                public readonly Image Source;
                private readonly bool _owned;

                public ResizeScope(Image rgba, int? targetWidth, int? targetHeight)
                {
                    if (targetWidth is int w && targetHeight is int h && (w != rgba.Width || h != rgba.Height))
                    {
                        Source = rgba.Resize(w, h, new ResizeOptions { Filter = ResamplingFilter.Bicubic });
                        _owned = true;
                    }
                    else
                    {
                        Source = rgba;
                        _owned = false;
                    }
                }

                public void Dispose()
                {
                    if (_owned) Source.Dispose();
                }
            }

            // IImageSource doesn't declare IDisposable (XImage never disposes its IImageSource) - the
            // backing Image's Dispose() is itself a near no-op today (see PeachImage.Image.Dispose's
            // remarks), so nothing is actually leaked.
            public void Dispose() { }
        }
    }
}

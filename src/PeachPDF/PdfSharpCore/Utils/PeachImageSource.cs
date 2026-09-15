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
        protected override IImageSource FromBinaryImpl(string name, Func<byte[]> imageSource, int? quality = 75) =>
            Decode(name, imageSource.Invoke(), quality ?? 75);

        protected override IImageSource FromFileImpl(string path, int? quality = 75) =>
            FromBinaryImpl(path, () => File.ReadAllBytes(path), quality);

        protected override IImageSource FromStreamImpl(string name, Func<Stream> imageStream, int? quality = 75)
        {
            using var stream = imageStream.Invoke();

            // The sole production caller (PdfSharpAdapter.ImageFromStreamInt) already hands in a
            // freshly-positioned MemoryStream - reuse its buffer directly (ToArray()) rather than paying
            // for a second full copy via CopyTo. Anything else (a non-seekable network stream, a stream
            // already partway read) falls back to the original copy-into-a-fresh-buffer path.
            if (stream is MemoryStream existing && existing.Position == 0)
            {
                return Decode(name, existing.ToArray(), quality ?? 75);
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return Decode(name, copy.ToArray(), quality ?? 75);
        }

        // PeachImage 0.2.1+ guarantees Image.Load(stream, options) converts to TargetPixelFormat in one
        // hop for every native PixelFormat any of its decoders can produce (Gray8/Rgb24/Rgba32/Cmyk32/
        // Gray16/Rgb48/Rgba64) - see Rgba32ConversionCompletenessTests.cs in PeachImage's own repo. See
        // DecoderOptions.TargetPixelFormat's own doc remarks for why a plain DecoderOptions (rather than
        // a per-format subtype) is correct here even though Image.Load auto-detects the format from the
        // stream's content, not from anything this class knows up front. Only ever used for a non-CMYK
        // source - see Decode below, which routes a CMYK32 source away from this entirely (issue #1085:
        // a CMYK source is never forced through Rgba32, since that naive conversion destroys print
        // separations with no color management at all).
        private static readonly DecoderOptions Rgba32DecoderOptions = new() { TargetPixelFormat = PixelFormat.Rgba32 };

        private static IImageSource Decode(string name, byte[] bytes, int quality)
        {
            try
            {
                var info = Image.Identify(new MemoryStream(bytes));

                if (info.PixelFormat == PixelFormat.Cmyk32)
                {
                    return DecodeCmyk(name, bytes, info);
                }

                if (info.FormatName == "jpeg")
                {
                    return DecodeRgbOrGrayJpeg(name, bytes, quality);
                }

                // Not disposed here - PeachImageSourceImpl takes ownership of it for its whole lifetime
                // (same as before this change; see its own Dispose() remarks on why that's a safe no-op
                // to skip).
                var decoded = Image.Load(new MemoryStream(bytes), Rgba32DecoderOptions);
                return new PeachImageSourceImpl(name, decoded, quality, decoded.HasAlpha, jpegPassthrough: null);
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

        /// <summary>
        /// Routes a CMYK32-identified source to a <see cref="PeachCmykImageSourceImpl"/>. Only JPEG is
        /// supported - see .claude/accepted-gaps/cmyk-tiff-unsupported.md for why a non-JPEG CMYK source
        /// (TIFF today, the only other PeachImage codec that decodes to Cmyk32) is rejected outright
        /// rather than given a lesser, ICC-less embed.
        /// </summary>
        private static PeachCmykImageSourceImpl DecodeCmyk(string name, byte[] bytes, ImageInfo info)
        {
            if (info.FormatName != "jpeg")
            {
                throw new InvalidOperationException(
                    $"CMYK images are only supported for JPEG sources; the '{info.FormatName}' format " +
                    "does not currently expose an embedded ICC profile, so a CMYK image in that format " +
                    "can't be embedded with guaranteed color fidelity. See " +
                    ".claude/accepted-gaps/cmyk-tiff-unsupported.md.");
            }

            // Deliberately not de-inverting Adobe's inverted-CMYK convention here (KeepAdobeCmykInverted
            // = true) - the PDF reader's own /Decode [1 0 1 0 1 0 1 0] array does that (see PdfImage's
            // EmbedJpegPassthrough), not a silent decode-time normalization. This decode's only purpose
            // is reaching Image.Metadata.GetIccColorProfile() - ICC chunks are only collected by
            // PeachImage's full Decode(), not Identify() - the decoded pixel buffer itself is unused,
            // since a CMYK JPEG always embeds via the original bytes, never a re-encode.
            var cmykOptions = new JpegDecoderOptions { TargetPixelFormat = PixelFormat.Cmyk32, KeepAdobeCmykInverted = true };
            using var decoded = Image.Load(new MemoryStream(bytes), cmykOptions);
            var iccProfile = TryGetUsableIccProfileBytes(decoded, IccColorSpace.Cmyk, expectedChannelCount: 4);

            var passthrough = new JpegPassthroughData
            {
                Data = bytes,
                ColorSpace = JpegPassthroughColorSpace.Cmyk,
                NeedsInvertedDecode = info.IsAdobeInvertedCmyk || info.IsYcck,
                IccProfile = iccProfile,
            };

            return new PeachCmykImageSourceImpl(name, info.Width, info.Height, passthrough);
        }

        /// <summary>
        /// Routes an RGB/Gray JPEG source (a 1- or 3-component JPEG - anything reaching here already
        /// isn't CMYK32, see <see cref="Decode"/>). Decoded natively - <em>not</em> forced to
        /// <see cref="PixelFormat.Rgba32"/> the way every other format is (<see cref="Rgba32DecoderOptions"/>)
        /// - because PeachImage's pixel-format-conversion step (triggered whenever <c>TargetPixelFormat</c>
        /// differs from the source's own native format) returns a <em>new</em> <see cref="Image"/> instance
        /// whose <see cref="ImageMetadata.Profiles"/> does not carry over the original decode's - verified
        /// empirically, not assumed (see .claude/recent-fixes for the write-up) - so forcing Rgba32 here
        /// would silently lose any embedded ICC profile every single time, since a JPEG's native RGB/Gray
        /// pixel format is always <see cref="PixelFormat.Rgb24"/>/<see cref="PixelFormat.Gray8"/>, never
        /// Rgba32 (JPEG has no alpha channel at all). This isn't a problem for the embed path: JPEG's own
        /// encoder (<c>SaveAsJpeg</c>) already accepts Gray8/Rgb24/Rgba32 source pixel data directly, and
        /// PeachImage's resizer is pixel-format-agnostic, so nothing downstream actually needs Rgba32
        /// specifically for a JPEG source.
        /// </summary>
        private static PeachImageSourceImpl DecodeRgbOrGrayJpeg(string name, byte[] bytes, int quality)
        {
            var decoded = Image.Load(new MemoryStream(bytes));

            JpegPassthroughData? jpegPassthrough = null;
            if (TryGetUsableRgbOrGrayIccProfile(decoded, out var iccBytes, out var colorSpace))
            {
                jpegPassthrough = new JpegPassthroughData
                {
                    Data = bytes,
                    ColorSpace = colorSpace,
                    NeedsInvertedDecode = false,
                    IccProfile = iccBytes,
                };
            }

            return new PeachImageSourceImpl(name, decoded, quality, decoded.HasAlpha, jpegPassthrough);
        }

        /// <summary>
        /// Returns <paramref name="image"/>'s embedded ICC profile's raw bytes, but only when it's
        /// actually usable for <paramref name="image"/>: PeachImage's own <c>GetIccColorProfile()</c>
        /// validates that the profile parses at all, but doesn't cross-check its declared color space or
        /// channel count against the image it came from - a malformed/mismatched file (e.g. an RGB
        /// profile embedded in a CMYK image) is defended against here instead.
        /// </summary>
        private static byte[]? TryGetUsableIccProfileBytes(Image image, IccColorSpace expectedColorSpace, int expectedChannelCount)
        {
            var profile = image.Metadata.GetIccColorProfile();
            return profile is null || profile.DataColorSpace != expectedColorSpace || profile.ChannelCount != expectedChannelCount
                ? null
                : GetRawIccProfileBytes(image);
        }

        /// <summary>
        /// Same idea as <see cref="TryGetUsableIccProfileBytes"/>, for the RGB/Gray case: the profile
        /// itself declares which of the two it is, so this just tries both expected shapes in turn
        /// rather than needing a separate "expected color space" parameter - either is a legitimate
        /// embedded ICC profile for an RGB/Gray JPEG. <paramref name="colorSpace"/> is only meaningful
        /// when this returns <see langword="true"/>.
        /// </summary>
        private static bool TryGetUsableRgbOrGrayIccProfile(Image image, out byte[]? bytes, out JpegPassthroughColorSpace colorSpace)
        {
            if (TryGetUsableIccProfileBytes(image, IccColorSpace.Rgb, expectedChannelCount: 3) is { } rgbBytes)
            {
                bytes = rgbBytes;
                colorSpace = JpegPassthroughColorSpace.Rgb;
                return true;
            }

            if (TryGetUsableIccProfileBytes(image, IccColorSpace.Gray, expectedChannelCount: 1) is { } grayBytes)
            {
                bytes = grayBytes;
                colorSpace = JpegPassthroughColorSpace.Gray;
                return true;
            }

            bytes = null;
            colorSpace = default;
            return false;
        }

        /// <summary>
        /// The raw bytes behind whichever <see cref="MetadataProfileKind.Icc"/> entry
        /// <see cref="ImageMetadata.GetIccColorProfile"/> just validated - that call only checks the
        /// profile parses, not which raw entry it came from, so this re-finds it by kind rather than
        /// re-parsing.
        /// </summary>
        private static byte[]? GetRawIccProfileBytes(Image image)
        {
            foreach (var raw in image.Metadata.Profiles)
            {
                if (raw.Kind == MetadataProfileKind.Icc)
                {
                    return raw.Data;
                }
            }

            // GetIccColorProfile() only returns non-null when it found and parsed exactly this kind of
            // entry, so this is unreachable whenever a caller already checked that - kept as a safe
            // fallback rather than an assert.
            return null;
        }

        /// <summary>
        /// Embeds a CMYK/YCCK JPEG source. Always embeds via <see cref="JpegPassthroughData"/> (the
        /// original file bytes, never resized or re-encoded - see
        /// <c>PdfImageTable.ComputeTargetPixelSize</c>'s CMYK resize skip) since PeachImage has no CMYK
        /// JPEG encoder to fall back to and a naive CMYK-&gt;RGB conversion is exactly the defect issue
        /// #1085 exists to fix.
        /// </summary>
        private sealed class PeachCmykImageSourceImpl : IImageSource
        {
            private readonly JpegPassthroughData _passthrough;

            public int Width { get; }
            public int Height { get; }
            public string Name { get; }
            public bool Transparent => false;
            public bool IsCmyk => true;
            public bool IsGrayscale => false;
            public JpegPassthroughData? JpegPassthrough => _passthrough;

            public PeachCmykImageSourceImpl(string name, int width, int height, JpegPassthroughData passthrough)
            {
                Name = name;
                Width = width;
                Height = height;
                _passthrough = passthrough;
            }

            public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null) =>
                throw new InvalidOperationException("A CMYK image is always embedded via JpegPassthrough; SaveAsJpeg is never called for it.");

            public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null) =>
                throw new InvalidOperationException("A CMYK image is always embedded via JpegPassthrough; SaveAsPdfBitmap is never called for it.");
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
            private readonly JpegPassthroughData? _jpegPassthrough;

            public int Width => _rgba.Width;
            public int Height => _rgba.Height;
            public string Name { get; }
            public bool Transparent { get; }
            public bool IsCmyk => false;

            // Only ever true for a JPEG source decoded via DecodeRgbOrGrayJpeg's native (not
            // Rgba32-forced) path - every other format still forces Rgba32 (see Rgba32DecoderOptions'
            // own remarks), so _rgba.PixelFormat is never Gray8 for those. Matters because SaveAsJpeg's
            // re-encode genuinely emits a 1-component grayscale JPEG for a Gray8 source (PeachImage's
            // FrameEncoder branches on the source's own PixelFormat), and PdfImage's resize-fallback path
            // needs to write a matching /ColorSpace for that - see IImageSource.IsGrayscale's own remarks.
            public bool IsGrayscale => _rgba.PixelFormat == PixelFormat.Gray8;
            public JpegPassthroughData? JpegPassthrough => _jpegPassthrough;

            public PeachImageSourceImpl(string name, Image rgba, int quality, bool transparent, JpegPassthroughData? jpegPassthrough = null)
            {
                Name = name;
                _rgba = rgba;
                _quality = quality;
                Transparent = transparent;
                _jpegPassthrough = jpegPassthrough;
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

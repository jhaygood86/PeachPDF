using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using PeachImage;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Jpeg;
using PeachImage.Formats.Png;
using System;
using System.Collections.Generic;
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

                if (info.FormatName == "png")
                {
                    return DecodePng(name, bytes, quality);
                }

                // Not disposed here - PeachImageSourceImpl takes ownership of it for its whole lifetime
                // (same as before this change; see its own Dispose() remarks on why that's a safe no-op
                // to skip).
                var decoded = Image.Load(new MemoryStream(bytes), Rgba32DecoderOptions);
                bool isLosslessSourceFormat = info.FormatName is "bmp" or "gif";
                return new PeachImageSourceImpl(name, decoded, quality, decoded.HasAlpha, jpegPassthrough: null, isLosslessSourceFormat);
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
        /// Routes a CMYK32-identified source to a <see cref="PeachCmykImageSourceImpl"/>: JPEG embeds via
        /// byte-for-byte pass-through (<see cref="DecodeCmykJpeg"/>), TIFF (issue #1096 - the only other
        /// PeachImage codec that decodes to Cmyk32) decodes natively and embeds its raw pixel buffer
        /// instead, since TIFF has no PDF-native pass-through filter the way JPEG's <c>/DCTDecode</c> does
        /// (<see cref="DecodeCmykRaster"/>). Any other format reaching here (none exist today) is rejected
        /// outright - same as every other unsupported format (TGA/PSD/HDR) - rather than given a lesser,
        /// ICC-less embed.
        /// </summary>
        private static PeachCmykImageSourceImpl DecodeCmyk(string name, byte[] bytes, ImageInfo info)
        {
            if (info.FormatName == "jpeg")
            {
                return DecodeCmykJpeg(name, bytes, info);
            }

            if (info.FormatName == "tiff")
            {
                return DecodeCmykRaster(name, bytes, info);
            }

            throw new InvalidOperationException(
                $"CMYK images are not supported for the '{info.FormatName}' format - it doesn't currently " +
                "expose an embedded ICC profile, so a CMYK image in that format can't be embedded with " +
                "guaranteed color fidelity.");
        }

        private static PeachCmykImageSourceImpl DecodeCmykJpeg(string name, byte[] bytes, ImageInfo info)
        {
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
        /// Decodes a CMYK TIFF natively to <see cref="PixelFormat.Cmyk32"/> (never forced through
        /// Rgba32 - see <see cref="Rgba32DecoderOptions"/>'s own remarks on why that would destroy print
        /// separations) and copies its pixel buffer out for a raw <c>/FlateDecode</c> embed
        /// (<see cref="PeachPDF.PdfSharpCore.Pdf.Advanced.PdfImage"/>'s <c>InitializeCmykRaster</c>) -
        /// unlike JPEG, TIFF has no <c>IsAdobeInvertedCmyk</c>/<c>IsYcck</c> convention to account for
        /// (always <see langword="false"/> for a non-JPEG source - <see cref="ImageInfo"/>'s own remarks),
        /// so the decoded bytes are already in the standard, ready-to-embed CMYK convention.
        /// </summary>
        private static PeachCmykImageSourceImpl DecodeCmykRaster(string name, byte[] bytes, ImageInfo info)
        {
            var cmykOptions = new DecoderOptions { TargetPixelFormat = PixelFormat.Cmyk32 };
            using var decoded = Image.Load(new MemoryStream(bytes), cmykOptions);
            var iccProfile = TryGetUsableIccProfileBytes(decoded, IccColorSpace.Cmyk, expectedChannelCount: 4);

            var raster = new CmykRasterData
            {
                Data = decoded.GetPixelSpan().ToArray(),
                IccProfile = iccProfile,
            };

            return new PeachCmykImageSourceImpl(name, info.Width, info.Height, raster);
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
        /// Routes a PNG source: eligible for byte-for-byte pass-through (see
        /// <see cref="ImageSource.IImageSource.PngPassthrough"/>'s own remarks on eligibility) returns a
        /// <see cref="PeachPngPassthroughImageSourceImpl"/> that skips the full pixel decode entirely -
        /// genuinely cheaper than today's decode-and-re-encode, unlike the CMYK JPEG/TIFF precedents,
        /// which still decode for unrelated reasons (ICC-profile extraction). An ineligible PNG (alpha,
        /// <c>tRNS</c>, interlaced, or a malformed/inconsistent file <see cref="PngPassthrough.TryRead"/>
        /// itself rejects) falls through to the ordinary decode path, same as before this feature existed.
        /// </summary>
        private static IImageSource DecodePng(string name, byte[] bytes, int quality)
        {
            if (PngPassthrough.TryRead(new MemoryStream(bytes), out var pngInfo) && IsPassthroughEligible(pngInfo, out var colorKeyMask))
            {
                var colorSpace = pngInfo.ColorType switch
                {
                    PngColorType.Grayscale => PngPassthroughColorSpace.Gray,
                    PngColorType.Truecolor => PngPassthroughColorSpace.Rgb,
                    PngColorType.Palette => PngPassthroughColorSpace.Indexed,
                    _ => throw new InvalidOperationException("Unreachable - IsPassthroughEligible already restricted ColorType to these three values."),
                };

                var passthrough = new PngPassthroughData
                {
                    IdatData = pngInfo.IdatData,
                    ColorSpace = colorSpace,
                    BitDepth = pngInfo.BitDepth,
                    PaletteRgb = pngInfo.PaletteData,
                    ColorKeyMask = colorKeyMask,
                };

                return new PeachPngPassthroughImageSourceImpl(name, bytes, quality, pngInfo.Width, pngInfo.Height, passthrough);
            }

            var decoded = Image.Load(new MemoryStream(bytes), Rgba32DecoderOptions);
            return new PeachImageSourceImpl(name, decoded, quality, decoded.HasAlpha, jpegPassthrough: null, isLosslessSourceFormat: true);
        }

        /// <summary>
        /// Not interlaced (PDF's <c>/DecodeParms</c> predictor has no Adam7 concept - Adam7 data can't be
        /// used as a PDF image stream as-is) and no real per-pixel alpha channel (color type 4/6 stays on
        /// the existing decode+<c>/SMask</c> path - see
        /// <c>.claude/accepted-gaps/alpha-channel-png-not-passthrough.md</c>). A <c>tRNS</c> chunk doesn't
        /// disqualify a source by itself: Grayscale/Truecolor <c>tRNS</c> is always a single exact
        /// chroma-key value, and a Palette <c>tRNS</c> is eligible whenever every listed entry is exactly
        /// 0 or 255 (a genuine partial-alpha palette entry can't be expressed as a binary color-key mask,
        /// so that specific case still falls back to decode - see
        /// <see cref="TryBuildPaletteColorKeyMask"/>). <paramref name="colorKeyMask"/> is meaningful only
        /// when this returns <see langword="true"/>: non-null gives the PDF <c>/Mask</c> color-key array
        /// to write (see <see cref="ImageSource.IImageSource.PngPassthrough"/>'s own remarks), null means
        /// a plain opaque pass-through with no masking needed at all (either no <c>tRNS</c>, or a Palette
        /// <c>tRNS</c> with nothing actually marked transparent). Animated (APNG) sources are still
        /// eligible - <paramref name="pngInfo"/>'s <c>IdatData</c> is always the default image's data
        /// regardless, the same "decode only the first/default frame" treatment every other animated
        /// source already gets.
        /// </summary>
        private static bool IsPassthroughEligible(PngPassthroughInfo pngInfo, out int[]? colorKeyMask)
        {
            colorKeyMask = null;

            if (pngInfo.IsInterlaced) return false;
            if (pngInfo.ColorType is not (PngColorType.Grayscale or PngColorType.Truecolor or PngColorType.Palette))
                return false;
            if (!pngInfo.HasTrns) return true;

            switch (pngInfo.ColorType)
            {
                case PngColorType.Grayscale:
                    colorKeyMask = BuildChromaKeyMask(pngInfo.TrnsData!, componentCount: 1);
                    return true;
                case PngColorType.Truecolor:
                    colorKeyMask = BuildChromaKeyMask(pngInfo.TrnsData!, componentCount: 3);
                    return true;
                case PngColorType.Palette:
                    return TryBuildPaletteColorKeyMask(pngInfo.TrnsData!, out colorKeyMask);
                default:
                    // Unreachable - the type check above already restricted ColorType to these three values.
                    return false;
            }
        }

        /// <summary>
        /// Grayscale <c>tRNS</c> is 2 bytes (one big-endian sample value); Truecolor is 6 bytes (three
        /// big-endian sample values, R/G/B). Both are already range-limited to the image's own bit depth
        /// by the PNG spec (an "unscaled" sample value, per the spec's own wording), so no rescaling is
        /// needed regardless of <c>BitDepth</c> - each value is used verbatim as both the min and max of
        /// its PDF <c>/Mask</c> color-key range, since PNG's non-alpha <c>tRNS</c> for these two color
        /// types is already exactly PDF's chroma-key convention.
        /// </summary>
        private static int[] BuildChromaKeyMask(byte[] trnsData, int componentCount)
        {
            var mask = new int[componentCount * 2];
            for (int i = 0; i < componentCount; i++)
            {
                int value = (trnsData[i * 2] << 8) | trnsData[i * 2 + 1];
                mask[i * 2] = value;
                mask[i * 2 + 1] = value;
            }

            return mask;
        }

        /// <summary>
        /// <paramref name="trnsData"/>[i] is the alpha for palette entry i (an index beyond its length is
        /// implicitly fully opaque, per the PNG spec). Returns <see langword="false"/> the moment any
        /// entry is neither 0 nor 255 - a partial-alpha palette entry can't be expressed as a binary
        /// color-key mask, so the whole source falls back to the existing decode+<c>/SMask</c> path.
        /// Returns <see langword="true"/> with <paramref name="colorKeyMask"/> <see langword="null"/> when
        /// there's nothing to mask (every listed entry is opaque) - that's still a fully valid pass-through,
        /// just with no PDF <c>/Mask</c> key written at all.
        /// </summary>
        /// <remarks>
        /// PDF's colour-key masking (ISO 32000-1 §8.9.6.4) is <c>2×n</c> integers where <c>n</c> is the
        /// image's colour space's component count - for <c>/Indexed</c>, <c>n</c> is always 1, so the
        /// array is exactly one <c>[min max]</c> range, not one pair per masked value (confirmed by
        /// rasterizing a two-non-contiguous-index case with both PDFium and MuPDF: a reader only ever
        /// consults the first pair and silently ignores the rest). A single transparent index still
        /// produces the common <c>[i i]</c> case; several transparent indices only qualify when they're
        /// contiguous (so one range genuinely covers exactly them, no more) - non-contiguous indices fall
        /// back to the existing decode+<c>/SMask</c> path instead of emitting a mask that would silently
        /// under-mask some of them.
        /// </remarks>
        private static bool TryBuildPaletteColorKeyMask(byte[] trnsData, out int[]? colorKeyMask)
        {
            List<int>? transparentIndices = null;
            for (int i = 0; i < trnsData.Length; i++)
            {
                if (trnsData[i] == 0)
                {
                    (transparentIndices ??= []).Add(i);
                }
                else if (trnsData[i] != 255)
                {
                    colorKeyMask = null;
                    return false;
                }
            }

            if (transparentIndices is null)
            {
                colorKeyMask = null;
                return true;
            }

            int min = transparentIndices[0];
            int max = transparentIndices[^1];
            if (max - min + 1 != transparentIndices.Count)
            {
                // Non-contiguous - a single [min max] range would also mask an opaque index in between.
                colorKeyMask = null;
                return false;
            }

            colorKeyMask = [min, max];
            return true;
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
        /// Embeds a CMYK source - either JPEG (via <see cref="JpegPassthroughData"/>, the original file
        /// bytes, never resized or re-encoded) or TIFF (via <see cref="CmykRasterData"/>, a raw decoded
        /// pixel buffer - see <see cref="ImageSource.IImageSource.CmykRaster"/>'s own remarks on why
        /// TIFF needs a different shape). Exactly one of the two constructors is ever used for a given
        /// instance - see <c>PdfImageTable.ComputeTargetPixelSize</c>'s CMYK resize skip, which applies to
        /// both: PeachImage has no CMYK JPEG encoder, and a raw CMYK raster embed has no resize step
        /// either, so a CMYK source is never resized regardless of which shape it took.
        /// </summary>
        private sealed class PeachCmykImageSourceImpl : IImageSource
        {
            private readonly JpegPassthroughData? _passthrough;
            private readonly CmykRasterData? _raster;

            public int Width { get; }
            public int Height { get; }
            public string Name { get; }
            public bool Transparent => false;
            public bool IsCmyk => true;
            public bool IsGrayscale => false;
            public JpegPassthroughData? JpegPassthrough => _passthrough;
            public CmykRasterData? CmykRaster => _raster;
            public PngPassthroughData? PngPassthrough => null;
            public bool IsLosslessSourceFormat => false;

            public PeachCmykImageSourceImpl(string name, int width, int height, JpegPassthroughData passthrough)
            {
                Name = name;
                Width = width;
                Height = height;
                _passthrough = passthrough;
            }

            public PeachCmykImageSourceImpl(string name, int width, int height, CmykRasterData raster)
            {
                Name = name;
                Width = width;
                Height = height;
                _raster = raster;
            }

            public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null) =>
                throw new InvalidOperationException("A CMYK image is always embedded via JpegPassthrough or CmykRaster; SaveAsJpeg is never called for it.");

            public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null) =>
                throw new InvalidOperationException("A CMYK image is always embedded via JpegPassthrough or CmykRaster; SaveAsPdfBitmap is never called for it.");
        }

        /// <summary>
        /// A PNG source eligible for byte-for-byte pass-through (see <see cref="DecodePng"/>/
        /// <see cref="IsPassthroughEligible"/>). Unlike <see cref="PeachCmykImageSourceImpl"/> - which
        /// genuinely has no fallback (PeachImage has no CMYK JPEG encoder) - <see cref="SaveAsJpeg"/>/
        /// <see cref="SaveAsPdfBitmap"/> aren't unreachable here: <see cref="PeachPDF.ImageCompression.Lossy"/>
        /// explicitly asks for a lossy JPEG re-encode even of an otherwise pass-through-eligible source,
        /// and <c>PdfImageTable</c>'s resize skip only applies when compression isn't <c>Lossy</c> (see
        /// its own remarks), so a resize can legitimately reach here too under that mode. The full pixel
        /// decode is deferred until one of those is actually called rather than paid unconditionally -
        /// under the default <see cref="PeachPDF.ImageCompression.Auto"/>/<see cref="PeachPDF.ImageCompression.Lossless"/>
        /// modes, neither ever is, so this is genuinely cheaper than today for the common case.
        /// </summary>
        private sealed class PeachPngPassthroughImageSourceImpl : IImageSource
        {
            private readonly byte[] _bytes;
            private readonly int _quality;
            private readonly PngPassthroughData _passthrough;
            private PeachImageSourceImpl? _decodedFallback;

            public int Width { get; }
            public int Height { get; }
            public string Name { get; }
            public bool Transparent => false;
            public bool IsCmyk => false;
            public bool IsGrayscale => false;
            public JpegPassthroughData? JpegPassthrough => null;
            public CmykRasterData? CmykRaster => null;
            public PngPassthroughData? PngPassthrough => _passthrough;
            public bool IsLosslessSourceFormat => true;

            public PeachPngPassthroughImageSourceImpl(string name, byte[] bytes, int quality, int width, int height, PngPassthroughData passthrough)
            {
                Name = name;
                _bytes = bytes;
                _quality = quality;
                Width = width;
                Height = height;
                _passthrough = passthrough;
            }

            // Forces Rgba32 the same as any other non-JPEG format's generic decode (Decode's own
            // Rgba32DecoderOptions) - IsGrayscale stays false for this fallback regardless of the source's
            // own PNG color type, which is fine: unlike the JPEG grayscale bug this mirrors the shape of,
            // decode and re-encode are consistently RGB here, so no /ColorSpace-vs-stream mismatch results,
            // just a 3-component JPEG for what could have been a 1-component one - an acceptable size
            // trade for what's already an explicit Lossy opt-out of the lossless default.
            private PeachImageSourceImpl DecodedFallback() =>
                _decodedFallback ??= new PeachImageSourceImpl(Name, Image.Load(new MemoryStream(_bytes), Rgba32DecoderOptions), _quality, transparent: false);

            public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null) =>
                DecodedFallback().SaveAsJpeg(ms, targetWidth, targetHeight, qualityOverride);

            public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null) =>
                DecodedFallback().SaveAsPdfBitmap(ms, targetWidth, targetHeight);
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
            public CmykRasterData? CmykRaster => null;
            public PngPassthroughData? PngPassthrough => null;

            // True for BMP/GIF and for a PNG that decoded here because it wasn't pass-through-eligible
            // (see DecodePng) - both callers pass this explicitly; JPEG (DecodeRgbOrGrayJpeg) leaves it at
            // its false default, since JPEG always has a lossy encoding mode.
            public bool IsLosslessSourceFormat { get; }

            public PeachImageSourceImpl(string name, Image rgba, int quality, bool transparent, JpegPassthroughData? jpegPassthrough = null, bool isLosslessSourceFormat = false)
            {
                Name = name;
                _rgba = rgba;
                _quality = quality;
                Transparent = transparent;
                _jpegPassthrough = jpegPassthrough;
                IsLosslessSourceFormat = isLosslessSourceFormat;
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

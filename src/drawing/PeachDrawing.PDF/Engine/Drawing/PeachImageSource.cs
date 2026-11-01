using PeachDrawing.PDF.Engine.Drawing;
using PeachImage;
using PeachImage.Formats.Jxl;
using PeachImage.Formats.Bmp;
using PeachImage.Formats.Gif;
using PeachImage.Formats.Jpeg;
using PeachImage.Formats.Png;
using PeachDrawing.PDF;
using System;
using System.Collections.Generic;
using System.IO;

namespace PeachDrawing.PDF.Engine.Drawing;

/// <summary>
/// The default raster image source, backed by the PeachImage NuGet package.
/// </summary>
internal sealed class PeachImageSource : ImageSource
{
    protected override IImageSource CreateFromBinary(string name, Func<byte[]> imageSource, int? quality = 75) =>
        Decode(name, imageSource.Invoke(), quality ?? 75);

    protected override IImageSource CreateFromFile(string path, int? quality = 75) =>
        CreateFromBinary(path, () => File.ReadAllBytes(path), quality);

    protected override IImageSource CreateFromStream(string name, Func<Stream> imageStream, int? quality = 75)
    {
        using var stream = imageStream.Invoke();

        // The sole production caller (PdfRenderContext.ImageFromStreamInt) already hands in a
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
    internal static readonly DecoderOptions Rgba32DecoderOptions = new() { TargetPixelFormat = PixelFormat.Rgba32 };

    private static IImageSource Decode(string name, byte[] bytes, int quality)
    {
        var source = DecodeCore(name, bytes, quality, out var info);

        // PeachImage never applies a file's stored orientation (JPEG/TIFF Exif, PNG eXIf, WebP EXIF, AVIF irot/imir,
        // the JPEG XL header): pixels and dimensions are as stored and ImageInfo.Orientation reports it (EXIF values
        // 1-8), which is surfaced for image-orientation and applied through the draw matrix, so pass-through
        // embeds stay byte for byte. JPEG XL sets its own (DecodeJxl): a recompressed JPEG re-enters Decode as its
        // rebuilt JPEG bytes and is oriented by that inner call from the JPEG's own Exif.
        if (info.FormatName != "jxl" && info.Orientation != PeachImage.ImageOrientation.Normal)
        {
            SetOrientation(source, (int)info.Orientation);
        }

        return source;
    }

    private static void SetOrientation(IImageSource source, int orientation)
    {
        switch (source)
        {
            case DecodedImageSource impl: impl.ExifOrientation = orientation; break;
            case PngPassthroughImageSource png: png.ExifOrientation = orientation; break;
            case CmykRasterImageSource cmyk: cmyk.ExifOrientation = orientation; break;
        }
    }

    private static IImageSource DecodeCore(string name, byte[] bytes, int quality, out ImageInfo info)
    {
        info = default;
        try
        {
            var imageInfo = Image.Identify(new MemoryStream(bytes));
            info = imageInfo;

            if (imageInfo.FormatName == "jxl")
            {
                return DecodeJxl(name, bytes, quality, imageInfo);
            }

            if (imageInfo.PixelFormat == PixelFormat.Cmyk32)
            {
                return DecodeCmyk(name, bytes, imageInfo);
            }

            if (imageInfo.FormatName == "jpeg")
            {
                return DecodeRgbOrGrayJpeg(name, bytes, quality);
            }

            if (imageInfo.FormatName == "png")
            {
                return DecodePng(name, bytes, quality);
            }

            if (imageInfo.FormatName == "gif")
            {
                return DecodeGif(name, bytes, quality);
            }

            return DecodeGenericRaster(name, bytes, quality, imageInfo);
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
    /// Routes a JPEG XL source. A file made by losslessly recompressing a JPEG carries the data to
    /// rebuild that exact JPEG (<see cref="JxlJpegReconstruction"/>), so it is turned back into the
    /// original JPEG bytes without decoding any pixels and handed to <see cref="Decode"/> again as a
    /// plain JPEG - which means every JPEG rule (CMYK/YCCK and ICC-tagged RGB/gray byte-for-byte
    /// <c>/DCTDecode</c> pass-through, re-encode otherwise) applies unchanged and there is no second
    /// pass-through implementation. Any other JPEG XL file decodes to <see cref="PixelFormat.Rgba32"/>
    /// like the other raster formats; a CMYK one (which PeachImage returns as <c>Cmyk32</c> with its
    /// profile attached) is color-managed to sRGB first, since PDF has no pass-through filter for it.
    /// </summary>
    private static IImageSource DecodeJxl(string name, byte[] bytes, int quality, ImageInfo info)
    {
        if (JxlJpegReconstruction.TryReconstructJpeg(new MemoryStream(bytes), out var jpeg))
        {
            return WrapJxlPassthrough(Decode(name, jpeg, quality), bytes, info);
        }

        if (info.PixelFormat == PixelFormat.Cmyk32)
        {
            using var cmyk = Image.Load(new MemoryStream(bytes));
            var srgb = cmyk.ConvertToSrgb();
            var rgba = srgb.PixelFormat == PixelFormat.Rgba32 ? srgb : srgb.ConvertTo(PixelFormat.Rgba32);
            var cmykSource = new DecodedImageSource(name, rgba, quality, rgba.HasAlpha, jpegPassthrough: null,
                isLosslessSourceFormat: false, rgbIccProfile: null);
            SetOrientation(cmykSource, (int)info.Orientation);
            return cmykSource;
        }

        var source = DecodeGenericRaster(name, bytes, quality, info);
        SetOrientation(source, (int)info.Orientation);
        return WrapJxlPassthrough(source, bytes, info);
    }

    /// <summary>
    /// Attaches the original JPEG XL bytes to <paramref name="decoded"/> when they can be embedded as-is: no alpha
    /// (JXL alpha would need a separate <c>/SMask</c> and PeachImage has no JXL encoder to split it), no animation,
    /// an upright orientation (a PDF image cannot rotate itself), and a plain 8/16-bit gray or RGB sample format.
    /// The raster fallback stays fully intact underneath.
    /// </summary>
    private static IImageSource WrapJxlPassthrough(IImageSource decoded, byte[] bytes, ImageInfo info)
    {
        if (info.HasAlpha || info.IsAnimated || info.Orientation != ImageOrientation.Normal
            || decoded.Transparent || decoded.IsCmyk || decoded.ExifOrientation != 1)
        {
            return decoded;
        }

        var (colorSpace, bits) = info.PixelFormat switch
        {
            PixelFormat.Gray8 => (JpegPassthroughColorSpace.Gray, 8),
            PixelFormat.Gray16 => (JpegPassthroughColorSpace.Gray, 16),
            PixelFormat.Rgb24 => (JpegPassthroughColorSpace.Rgb, 8),
            PixelFormat.Rgb48 => (JpegPassthroughColorSpace.Rgb, 16),
            _ => (JpegPassthroughColorSpace.Cmyk, 0),
        };

        if (bits == 0)
        {
            return decoded;
        }

        return new JxlPassthroughImageSource(decoded, new JxlPassthroughData
        {
            Data = bytes,
            ColorSpace = colorSpace,
            BitsPerComponent = bits,
            IccProfile = colorSpace == JpegPassthroughColorSpace.Rgb ? decoded.RgbIccProfile : null,
        });
    }

    /// <summary>
    /// Routes every raster format with no dedicated pass-through mechanism of its own (BMP, WebP,
    /// AVIF, TIFF) - decodes to <see cref="PixelFormat.Rgba32"/> like every non-pass-through format
    /// does, and computes <see cref="ImageSource.IImageSource.IsLosslessSourceFormat"/> (issue #1107:
    /// unconditional for BMP/GIF, conditional on <see cref="ImageInfo.IsLosslessEncoding"/> for
    /// WebP/AVIF/TIFF/JPEG XL). For WebP/AVIF/JPEG XL specifically, also extracts a usable embedded ICC profile
    /// (issue #1106) via a second, native (no <c>TargetPixelFormat</c>) decode purely to read
    /// <c>Image.Metadata</c> - forcing <see cref="Rgba32DecoderOptions"/> in one hop, the way every
    /// other call site here does, would silently lose it for an opaque source (native format Rgb24):
    /// PeachImage's per-format pixel-format converters build a brand-new <see cref="Image"/> with an
    /// empty <c>Metadata</c> whenever a conversion actually runs (confirmed against WebP's own
    /// converter source - the same root cause as the JPEG ICC bug issue #1085 fixed, see
    /// <see cref="DecodeRgbOrGrayJpeg"/>'s own remarks). Not extended to BMP/GIF/TIFF - none of the
    /// three carry an embedded ICC profile PeachPDF has a use for here (TIFF's CMYK case already
    /// routes through <see cref="DecodeCmykRaster"/> instead, and a CMYK TIFF's RGB counterpart isn't
    /// in scope for issue #1106).
    /// </summary>
    private static IImageSource DecodeGenericRaster(string name, byte[] bytes, int quality, ImageInfo info)
    {
        byte[]? rgbIccProfile = null;
        if (info.FormatName is "webp" or "avif" or "jxl")
        {
            using var probe = Image.Load(new MemoryStream(bytes));
            rgbIccProfile = TryGetUsableIccProfileBytes(probe, IccColorSpace.Rgb, expectedChannelCount: 3);
        }

        // Not disposed here - DecodedImageSource takes ownership of it for its whole lifetime (same
        // as before this change; see its own Dispose() remarks on why that's a safe no-op to skip).
        var decoded = Image.Load(new MemoryStream(bytes), Rgba32DecoderOptions);

        bool isLosslessSourceFormat = info.FormatName is "bmp" or "gif" ||
            (info.FormatName is "webp" or "avif" or "tiff" or "jxl" && info.IsLosslessEncoding);
        return new DecodedImageSource(name, decoded, quality, decoded.HasAlpha, jpegPassthrough: null, isLosslessSourceFormat, rgbIccProfile);
    }

    /// <summary>
    /// Routes a CMYK32-identified source to a <see cref="CmykRasterImageSource"/>: JPEG embeds via
    /// byte-for-byte pass-through (<see cref="DecodeCmykJpeg"/>), TIFF (issue #1096 - the only other
    /// PeachImage codec that decodes to Cmyk32) decodes natively and embeds its raw pixel buffer
    /// instead, since TIFF has no PDF-native pass-through filter the way JPEG's <c>/DCTDecode</c> does
    /// (<see cref="DecodeCmykRaster"/>). Any other format reaching here (none exist today) is rejected
    /// outright - same as every other unsupported format (TGA/PSD/HDR) - rather than given a lesser,
    /// ICC-less embed.
    /// </summary>
    private static CmykRasterImageSource DecodeCmyk(string name, byte[] bytes, ImageInfo info)
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

    private static CmykRasterImageSource DecodeCmykJpeg(string name, byte[] bytes, ImageInfo info)
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

        return new CmykRasterImageSource(name, info.Width, info.Height, passthrough);
    }

    /// <summary>
    /// Decodes a CMYK TIFF natively to <see cref="PixelFormat.Cmyk32"/> (never forced through
    /// Rgba32 - see <see cref="Rgba32DecoderOptions"/>'s own remarks on why that would destroy print
    /// separations) and copies its pixel buffer out for a raw <c>/FlateDecode</c> embed
    /// (<see cref="PeachDrawing.PDF.Engine.Advanced.PdfImage"/>'s <c>InitializeCmykRaster</c>) -
    /// unlike JPEG, TIFF has no <c>IsAdobeInvertedCmyk</c>/<c>IsYcck</c> convention to account for
    /// (always <see langword="false"/> for a non-JPEG source - <see cref="ImageInfo"/>'s own remarks),
    /// so the decoded bytes are already in the standard, ready-to-embed CMYK convention.
    /// </summary>
    /// <summary>A CMYK image source over raw interleaved C, M, Y, K samples (255 = full ink), with no embedded profile.</summary>
    internal static IImageSource CreateCmykRaster(string name, int width, int height, byte[] cmyk) =>
        new CmykRasterImageSource(name, width, height, new CmykRasterData { Data = cmyk });

    private static CmykRasterImageSource DecodeCmykRaster(string name, byte[] bytes, ImageInfo info)
    {
        var cmykOptions = new DecoderOptions { TargetPixelFormat = PixelFormat.Cmyk32 };
        using var decoded = Image.Load(new MemoryStream(bytes), cmykOptions);
        var iccProfile = TryGetUsableIccProfileBytes(decoded, IccColorSpace.Cmyk, expectedChannelCount: 4);

        var raster = new CmykRasterData
        {
            Data = decoded.GetPixelSpan().ToArray(),
            IccProfile = iccProfile,
        };

        return new CmykRasterImageSource(name, info.Width, info.Height, raster);
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
    private static DecodedImageSource DecodeRgbOrGrayJpeg(string name, byte[] bytes, int quality)
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

        return new DecodedImageSource(name, decoded, quality, decoded.HasAlpha, jpegPassthrough);
    }

    /// <summary>
    /// Routes a PNG source: eligible for pass-through (see
    /// <see cref="ImageSource.IImageSource.PngPassthrough"/>'s own remarks on eligibility) returns a
    /// <see cref="PngPassthroughImageSource"/> that skips the full pixel decode entirely -
    /// genuinely cheaper than today's decode-and-re-encode, unlike the CMYK JPEG/TIFF precedents,
    /// which still decode for unrelated reasons (ICC-profile extraction). A source with real
    /// per-pixel alpha that <see cref="IsPassthroughEligible"/> rejects gets a second chance via
    /// <see cref="PngAlphaSplit.TrySplit(in PngPassthroughInfo, out PngAlphaSplitInfo)"/> (issue
    /// #1109) - that call has its own narrower eligibility (not interlaced, 8-bit color type 4/6, or
    /// a palette source with a genuine partial-alpha <c>tRNS</c> entry) and safely returns false for
    /// anything else, so this doesn't need to duplicate that logic here. An ineligible PNG either way
    /// (or a malformed/inconsistent file <see cref="PngPassthrough.TryRead"/> itself rejects) falls
    /// through to the ordinary decode path, same as before this feature existed.
    /// </summary>
    private static IImageSource DecodePng(string name, byte[] bytes, int quality)
    {
        if (PngPassthrough.TryRead(new MemoryStream(bytes), out var pngInfo))
        {
            if (IsPassthroughEligible(pngInfo, out var colorKeyMask))
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
                    IccProfile = GetUsablePngIccProfile(colorSpace, pngInfo.IccProfileData),
                };

                return new PngPassthroughImageSource(name, bytes, quality, pngInfo.Width, pngInfo.Height, passthrough);
            }

            if (PngAlphaSplit.TrySplit(in pngInfo, out var alphaSplit))
            {
                var passthrough = BuildAlphaSplitPassthroughData(pngInfo, alphaSplit);
                return new PngPassthroughImageSource(name, bytes, quality, pngInfo.Width, pngInfo.Height, passthrough);
            }
        }

        // An interlaced or 16-bit-per-channel-alpha PNG reaches here even when PngPassthrough.TryRead
        // succeeded (it just failed both eligibility checks above) - pngInfo.IccProfileData is
        // already the inflated iCCP bytes in that case, so this still preserves the profile with no
        // extra decode, the same way the pass-through/alpha-split branches above do. When TryRead
        // itself failed (a malformed file), pngInfo is the default struct and IccProfileData is null,
        // which TryGetUsablePngIccProfileBytes already treats as "no profile" up front.
        var decoded = Image.Load(new MemoryStream(bytes), Rgba32DecoderOptions);
        var rgbIccProfile = TryGetUsablePngIccProfileBytes(pngInfo.IccProfileData, IccColorSpace.Rgb, expectedChannelCount: 3);
        return new DecodedImageSource(name, decoded, quality, decoded.HasAlpha, jpegPassthrough: null, isLosslessSourceFormat: true, rgbIccProfile);
    }

    /// <summary>
    /// Builds the <see cref="PngPassthroughData"/> for an alpha-split-eligible source.
    /// <paramref name="alphaSplit"/>.<see cref="PngAlphaSplitInfo.ColorData"/> is non-null exactly for
    /// color type 4/6 (the de-interleaved color-only plane, replacing <paramref name="pngInfo"/>'s own
    /// interleaved <c>IdatData</c> - see <see cref="PngPassthroughData.IdatData"/>'s own remarks) and
    /// null for the palette case (the original indexed <c>IdatData</c>/<c>PaletteData</c> already need
    /// no change - only the alpha plane is new). <see cref="PngPassthroughData.ColorKeyMask"/> is
    /// always null here - a source only reaches alpha-split because it has real alpha, which
    /// <see cref="IsPassthroughEligible"/> already ruled out being chroma-key-only. An embedded ICC
    /// profile (issue #1106) rides along either way, same as the opaque/chroma-key branch.
    /// </summary>
    private static PngPassthroughData BuildAlphaSplitPassthroughData(in PngPassthroughInfo pngInfo, in PngAlphaSplitInfo alphaSplit)
    {
        if (alphaSplit.ColorData is { } colorData)
        {
            var colorSpace = alphaSplit.ColorIsRgb ? PngPassthroughColorSpace.Rgb : PngPassthroughColorSpace.Gray;
            return new PngPassthroughData
            {
                IdatData = colorData,
                ColorSpace = colorSpace,
                BitDepth = alphaSplit.ColorBitDepth,
                AlphaIdatData = alphaSplit.AlphaData,
                AlphaBitDepth = alphaSplit.AlphaBitDepth,
                IccProfile = GetUsablePngIccProfile(colorSpace, pngInfo.IccProfileData),
            };
        }

        return new PngPassthroughData
        {
            IdatData = pngInfo.IdatData,
            ColorSpace = PngPassthroughColorSpace.Indexed,
            BitDepth = pngInfo.BitDepth,
            PaletteRgb = pngInfo.PaletteData,
            AlphaIdatData = alphaSplit.AlphaData,
            AlphaBitDepth = alphaSplit.AlphaBitDepth,
            IccProfile = GetUsablePngIccProfile(PngPassthroughColorSpace.Indexed, pngInfo.IccProfileData),
        };
    }

    /// <summary>
    /// Validates <paramref name="rawIccProfile"/> against the ICC shape <paramref name="colorSpace"/>
    /// implies (Gray -&gt; 1-channel Gray, Rgb/Indexed -&gt; 3-channel Rgb - an <c>/Indexed</c> PNG's
    /// <c>iCCP</c> describes the color space its palette entries live in) via
    /// <see cref="TryGetUsablePngIccProfileBytes"/>, shared by both <see cref="DecodePng"/>'s
    /// opaque/chroma-key branch and <see cref="BuildAlphaSplitPassthroughData"/>.
    /// </summary>
    private static byte[]? GetUsablePngIccProfile(PngPassthroughColorSpace colorSpace, byte[]? rawIccProfile) =>
        colorSpace == PngPassthroughColorSpace.Gray
            ? TryGetUsablePngIccProfileBytes(rawIccProfile, IccColorSpace.Gray, expectedChannelCount: 1)
            : TryGetUsablePngIccProfileBytes(rawIccProfile, IccColorSpace.Rgb, expectedChannelCount: 3);

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
    /// Routes a GIF source: eligible for <c>/LZWDecode</c> pass-through (see
    /// <see cref="ImageSource.IImageSource.GifPassthrough"/>'s own remarks on eligibility) returns a
    /// <see cref="GifPassthroughImageSource"/> that skips the full pixel decode entirely -
    /// same shape as <see cref="DecodePng"/>. An ineligible GIF (interlaced, small palette, doesn't
    /// cover the full canvas, or a malformed/inconsistent file <see cref="GifPassthrough.TryRead"/>
    /// itself rejects) falls through to the ordinary decode path, same as before this feature existed.
    /// </summary>
    private static IImageSource DecodeGif(string name, byte[] bytes, int quality)
    {
        if (GifPassthrough.TryRead(new MemoryStream(bytes), out var gifInfo) && IsGifPassthroughEligible(gifInfo))
        {
            var passthrough = new GifPassthroughData
            {
                LzwData = gifInfo.LzwData,
                Palette = gifInfo.Palette,
                ColorKeyMask = gifInfo.TransparentColorIndex is int transparentIndex ? [transparentIndex, transparentIndex] : null,
            };

            return new GifPassthroughImageSource(name, bytes, quality, gifInfo.Width, gifInfo.Height, passthrough);
        }

        var decoded = Image.Load(new MemoryStream(bytes), Rgba32DecoderOptions);
        return new DecodedImageSource(name, decoded, quality, decoded.HasAlpha, jpegPassthrough: null, isLosslessSourceFormat: true);
    }

    /// <summary>
    /// Not interlaced (GIF's LZW compresses whatever row order it's given, so an interlaced frame's
    /// raw bytes decompress into indices in 4-pass order, not top-to-bottom - PDF has no de-interlace
    /// step for image data), <c>MinCodeSize == 8</c> (the hard byte-compatibility gate between GIF's
    /// and PDF's LZW conventions - see <see cref="ImageSource.IImageSource.GifPassthrough"/>'s own
    /// remarks), and the frame covers the full logical canvas (pass-through has no
    /// canvas-compositing step the way the existing full decode does, so a frame offset from the
    /// canvas origin or smaller than it can't be embedded as-is).
    /// </summary>
    private static bool IsGifPassthroughEligible(GifPassthroughInfo gifInfo) =>
        !gifInfo.Interlaced &&
        gifInfo.MinCodeSize == 8 &&
        gifInfo.Left == 0 && gifInfo.Top == 0 &&
        gifInfo.Width == gifInfo.CanvasWidth && gifInfo.Height == gifInfo.CanvasHeight;

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
    /// Same idea as <see cref="TryGetUsableIccProfileBytes"/>, for a PNG pass-through source (issue
    /// #1106): PNG pass-through never decodes to an <see cref="Image"/> at all (that's the point of
    /// pass-through), so there's no <c>Metadata.GetIccColorProfile()</c> to call - this validates
    /// <paramref name="rawIccProfile"/> (<see cref="PeachImage.Formats.Png.PngPassthroughInfo.IccProfileData"/>,
    /// already inflated) directly via <see cref="IccColorProfile.TryCreate"/> instead, returning it
    /// unchanged only when its declared shape actually matches the PNG's own color type - a
    /// malformed/mismatched <c>iCCP</c> chunk (e.g. an RGB profile in a grayscale PNG) is defended
    /// against the same way the JPEG/CMYK path already does.
    /// </summary>
    private static byte[]? TryGetUsablePngIccProfileBytes(byte[]? rawIccProfile, IccColorSpace expectedColorSpace, int expectedChannelCount)
    {
        if (rawIccProfile is null || !IccColorProfile.TryCreate(rawIccProfile, out var profile) || profile is null)
            return null;

        return profile.DataColorSpace != expectedColorSpace || profile.ChannelCount != expectedChannelCount
            ? null
            : rawIccProfile;
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
}

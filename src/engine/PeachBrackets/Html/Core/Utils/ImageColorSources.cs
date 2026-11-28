using PeachDrawing.Core;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PeachBrackets.Html.Utils;

/// <summary>
/// Keeps the file an image came from when the file says it is in a color space other than sRGB (a Display-P3 PNG, an Adobe RGB JPEG, an HDR AVIF). A page paints the decoded pixels as they are,
/// but a script that draws the image on a canvas of another space needs the colors the file means, which only the file still has.
/// </summary>
internal static class ImageColorSources
{
    private sealed class Source(byte[] bytes, bool otherSpace, PeachImage.ImageOrientation orientation)
    {
        public byte[] Bytes { get; } = bytes;

        public bool OtherSpace { get; } = otherSpace;

        public PeachImage.ImageOrientation Orientation { get; } = orientation;
    }

    // An image whose file records an orientation, and the same image made upright (each is the other's counterpart).
    private static readonly ConditionalWeakTable<PeachDrawing.Core.Image, PeachDrawing.Core.Image> Uprights = new();

    private static readonly ConditionalWeakTable<PeachDrawing.Core.Image, PeachDrawing.Core.Image> Raws = new();

    private static readonly ConditionalWeakTable<PeachDrawing.Core.Image, Source> Sources = new();

    private static PeachImage.NamedColorSpace? NamedSpace(string space) => space switch
    {
        "srgb" => PeachImage.NamedColorSpace.Srgb,
        "display-p3" => PeachImage.NamedColorSpace.DisplayP3,
        "srgb-linear" => PeachImage.NamedColorSpace.LinearSrgb,
        "display-p3-linear" => PeachImage.NamedColorSpace.LinearDisplayP3,
        _ => null,
    };

    /// <summary>Encodes straight pixels of a canvas as PNG, JPEG or WebP, whichever <paramref name="mimeType"/> names (PNG for any other type).</summary>
    public static byte[]? Encode(int width, int height, byte[]? bytes, float[]? floats, string space, string mimeType, double? quality, out string writtenType)
    {
        writtenType = "image/png";
        if (NamedSpace(space) is not { } source || width < 1 || height < 1 || (bytes is null && floats is null))
            return null;

        var type = mimeType.Trim().ToLowerInvariant();
        var lossy = type is "image/jpeg" or "image/webp";
        if (lossy)
            writtenType = type;

        // A transfer function that is not linear: eight bits of linear light waste most of their steps on the highlights.
        var target = source switch
        {
            PeachImage.NamedColorSpace.LinearSrgb when lossy => PeachImage.NamedColorSpace.Srgb,
            PeachImage.NamedColorSpace.LinearDisplayP3 when lossy => PeachImage.NamedColorSpace.DisplayP3,
            _ => source,
        };

        PeachImage.EncoderOptions options;
        string format;
        switch (type)
        {
            case "image/jpeg":
                format = "jpeg";
                options = quality is { } jq and >= 0 and <= 1
                    ? new PeachImage.Formats.Jpeg.JpegEncoderOptions { TargetColorSpace = target, QualityFraction = (float)jq }
                    : new PeachImage.Formats.Jpeg.JpegEncoderOptions { TargetColorSpace = target };
                break;
            case "image/webp":
                format = "webp";
                options = quality is { } wq and >= 0 and <= 1
                    ? new PeachImage.Formats.Webp.WebpEncoderOptions { TargetColorSpace = target, QualityFraction = (float)wq }
                    : new PeachImage.Formats.Webp.WebpEncoderOptions { TargetColorSpace = target };
                break;
            default:
                format = "png";
                options = new PeachImage.Formats.Png.PngEncoderOptions { TargetColorSpace = target };
                break;
        }

        try
        {
            using var output = new MemoryStream();
            if (floats is not null)
            {
                var pixels = floats;
                if (type == "image/jpeg")
                {
                    // JPEG has no alpha: the picture is shown on opaque black.
                    pixels = (float[])floats.Clone();
                    for (var i = 0; i + 3 < pixels.Length; i += 4)
                    {
                        for (var c = 0; c < 3; c++)
                            pixels[i + c] *= pixels[i + 3];

                        pixels[i + 3] = 1f;
                    }
                }

                PeachImage.ImageEncoder.Encode(MemoryMarshal.AsBytes(pixels.AsSpan()), width, height, PeachImage.PixelFormat.RgbaF32, output, format, options, 0, source, null!);
            }
            else
            {
                var pixels = bytes!;
                if (type == "image/jpeg")
                {
                    pixels = (byte[])bytes!.Clone();
                    for (var i = 0; i + 3 < pixels.Length; i += 4)
                    {
                        var a = pixels[i + 3];
                        for (var c = 0; c < 3; c++)
                            pixels[i + c] = (byte)((pixels[i + c] * a + 127) / 255);

                        pixels[i + 3] = 255;
                    }
                }

                PeachImage.ImageEncoder.Encode(pixels, width, height, PeachImage.PixelFormat.Rgba32, output, format, options, 0, source, null!);
            }

            return output.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or IOException or PeachImage.IccProfileException)
        {
            writtenType = "image/png";
            return null;
        }
    }

    /// <summary>What a file says about its colors and its orientation: whether it declares a space other than sRGB, and how it is to be turned to be upright.</summary>
    private static (bool OtherSpace, PeachImage.ImageOrientation Orientation) Inspect(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var info = PeachImage.Image.Identify(stream);
            var other = info.IsHdr || info.IsYcck || info.IsAdobeInvertedCmyk || (info.ColorSpace is { } space && space.Named != PeachImage.NamedColorSpace.Srgb);
            return (other, info.Orientation);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or InvalidOperationException or IOException or ArgumentException)
        {
            return (false, PeachImage.ImageOrientation.Normal);
        }
    }

    /// <summary>Decodes <paramref name="bytes"/> with the render context and remembers the file when it declares a space other than sRGB.</summary>
    public static PeachDrawing.Core.Image Decode(RenderContext renderContext, byte[] bytes)
    {
        var image = renderContext.ImageFromStream(new MemoryStream(bytes, writable: false));
        Remember(image, bytes);
        return image;
    }

    /// <summary>Remembers the file an image was decoded from, when it declares a space other than sRGB.</summary>
    public static void Remember(PeachDrawing.Core.Image image, byte[] bytes)
    {
        var (other, orientation) = Inspect(bytes);
        if (other || orientation != PeachImage.ImageOrientation.Normal)
            Sources.AddOrUpdate(image, new Source(bytes, other, orientation));
    }

    /// <summary>
    /// The image turned upright by the orientation its file records (a camera's EXIF rotation), which is what a script sees of an <c>img</c>; the image itself when the file records none.
    /// </summary>
    public static PeachDrawing.Core.Image Upright(RenderContext renderContext, PeachDrawing.Core.Image image)
    {
        if (Uprights.TryGetValue(image, out var known))
            return known;

        if (!Sources.TryGetValue(image, out var source) || source.Orientation == PeachImage.ImageOrientation.Normal)
            return image;

        try
        {
            using var native = PeachImage.Image.Load(source.Bytes, new PeachImage.DecoderOptions { ApplyOrientation = true });
            using var output = new MemoryStream();
            native.Save(output, "png", new PeachImage.Formats.Png.PngEncoderOptions());
            var upright = Decode(renderContext, output.ToArray());
            Uprights.AddOrUpdate(image, upright);
            Raws.AddOrUpdate(upright, image);
            return upright;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or InvalidOperationException or IOException or ArgumentException)
        {
            return image;
        }
    }

    /// <summary>The image as stored, before its recorded orientation was applied (<c>imageOrientation: "none"</c>); the image itself when it was not turned.</summary>
    public static PeachDrawing.Core.Image Unoriented(PeachDrawing.Core.Image image) => Raws.TryGetValue(image, out var raw) ? raw : image;

    /// <summary>
    /// Straight RGBA bytes in a space other than sRGB, converted into Display P3 as floats (colors outside it are kept, as values below 0 or above 1), for
    /// a picture a script draws on a canvas of any space.
    /// </summary>
    public static float[]? ToDisplayP3(byte[] straightRgba, int width, int height, PeachBrackets.Media.MediaColorSpace from)
    {
        if (width < 1 || height < 1 || straightRgba.Length < width * height * 4)
            return null;

        try
        {
            using var source = PeachImage.Image.Create(width, height, PeachImage.PixelFormat.Rgba32);
            straightRgba.AsSpan(0, width * height * 4).CopyTo(source.PixelMemory.Span);
            var named = from switch
            {
                PeachBrackets.Media.MediaColorSpace.DisplayP3 => PeachImage.NamedColorSpace.DisplayP3,
                PeachBrackets.Media.MediaColorSpace.Rec2020 => PeachImage.NamedColorSpace.Rec2020,
                _ => PeachImage.NamedColorSpace.Srgb,
            };

            using var converted = source.ConvertColorSpace(PeachImage.NamedColorSpace.DisplayP3, new PeachImage.ColorConversionOptions
            {
                SourceColorSpace = named,
                TargetPixelFormat = PeachImage.PixelFormat.RgbaF32,
                GamutHandling = PeachImage.GamutHandling.Preserve,
            });

            return MemoryMarshal.Cast<byte, float>(converted.PixelMemory.Span).ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or PeachImage.IccProfileException)
        {
            return null;
        }
    }

    /// <summary>
    /// Straight RGBA bytes in a space other than sRGB, converted into sRGB with the colors it cannot show clipped to its gamut, for a picture painted on a
    /// page or screen, which are in sRGB.
    /// </summary>
    public static byte[]? ToSrgb(byte[] straightRgba, int width, int height, PeachBrackets.Media.MediaColorSpace from)
    {
        if (width < 1 || height < 1 || straightRgba.Length < width * height * 4)
            return null;

        try
        {
            using var source = PeachImage.Image.Create(width, height, PeachImage.PixelFormat.Rgba32);
            straightRgba.AsSpan(0, width * height * 4).CopyTo(source.PixelMemory.Span);
            var named = from switch
            {
                PeachBrackets.Media.MediaColorSpace.DisplayP3 => PeachImage.NamedColorSpace.DisplayP3,
                PeachBrackets.Media.MediaColorSpace.Rec2020 => PeachImage.NamedColorSpace.Rec2020,
                _ => PeachImage.NamedColorSpace.Srgb,
            };

            using var converted = source.ConvertColorSpace(PeachImage.NamedColorSpace.Srgb, new PeachImage.ColorConversionOptions
            {
                SourceColorSpace = named,
                TargetPixelFormat = PeachImage.PixelFormat.Rgba32,
            });

            return converted.PixelMemory.Span.ToArray();
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or PeachImage.IccProfileException)
        {
            return null;
        }
    }

    // A CMYK image as Display P3 floats, by the ICC profile it carries, or null when it carries none or that cannot be done (the picture is then converted the way any other is).
    private static PeachImage.Image? CmykToDisplayP3(PeachImage.Image cmyk)
    {
        var profile = cmyk.Metadata.GetIccColorProfile();
        if (profile is null || profile.ChannelCount != 4 || PeachImage.ColorProfiles.Get(PeachImage.NamedColorSpace.DisplayP3) is not { } p3)
            return null;

        var count = cmyk.Width * cmyk.Height;
        var bytes = cmyk.PixelMemory.Span;
        if (bytes.Length < count * 4)
            return null;

        var rgb = new byte[count * 3];
        profile.ConvertTo(p3, bytes[..(count * 4)], rgb, count, null, true);
        var result = PeachImage.Image.Create(cmyk.Width, cmyk.Height, PeachImage.PixelFormat.Rgba32);
        var pixels = result.PixelMemory.Span;
        for (var i = 0; i < count; i++)
        {
            pixels[i * 4] = rgb[i * 3];
            pixels[(i * 4) + 1] = rgb[(i * 3) + 1];
            pixels[(i * 4) + 2] = rgb[(i * 3) + 2];
            pixels[(i * 4) + 3] = 255;
        }

        return result;
    }

    /// <summary>Whether the image came from a file that declares a space other than sRGB.</summary>
    public static bool Has(PeachDrawing.Core.Image image) => Sources.TryGetValue(image, out var source) && source.OtherSpace;

    /// <summary>
    /// The image's file read again, its colors converted from the space it declares into <paramref name="space"/>: eight-bit straight RGBA, or with <paramref name="wide"/> straight floats.
    /// </summary>
    /// <returns>the pixels (bytes, or floats as bytes of <c>RgbaF32</c>), or null when the image has no file of its own or the conversion failed</returns>
    public static (int Width, int Height, byte[]? Bytes, float[]? Floats)? Convert(PeachDrawing.Core.Image image, string space, bool wide)
    {
        if (!Sources.TryGetValue(image, out var source) || !source.OtherSpace)
            return null;

        PeachImage.NamedColorSpace target;
        switch (space)
        {
            case "srgb":
                target = PeachImage.NamedColorSpace.Srgb;
                break;
            case "display-p3":
                target = PeachImage.NamedColorSpace.DisplayP3;
                break;
            case "srgb-linear":
                target = PeachImage.NamedColorSpace.LinearSrgb;
                break;
            case "display-p3-linear":
                target = PeachImage.NamedColorSpace.LinearDisplayP3;
                break;
            default:
                return null;
        }

        try
        {
            // Decoded as it is stored, then converted from the space the file declares; a float canvas keeps the colors outside the target's gamut, so a later conversion can still use them.
            using var decoded = PeachImage.Image.Load(source.Bytes, new PeachImage.DecoderOptions { ApplyOrientation = true });

            // A CMYK picture has no RGB space to convert from: its ink goes through its own profile into Display P3 (wide enough to hold what the profile makes of it), and that is
            // the picture the conversion below starts from.
            using var cmykAsP3 = decoded.PixelFormat is PeachImage.PixelFormat.Cmyk32 or PeachImage.PixelFormat.Ycck32 ? CmykToDisplayP3(decoded) : null;
            var native = cmykAsP3 ?? decoded;
            var conversion = new PeachImage.ColorConversionOptions
            {
                SourceColorSpace = cmykAsP3 is null ? null : PeachImage.NamedColorSpace.DisplayP3,
                TargetPixelFormat = wide ? PeachImage.PixelFormat.RgbaF32 : PeachImage.PixelFormat.Rgba32,
                GamutHandling = wide ? PeachImage.GamutHandling.Preserve : PeachImage.GamutHandling.Clip,
            };

            using var made = native.ConvertColorSpace(target, conversion);
            var memory = made.PixelMemory.Span;
            if (wide)
                return (made.Width, made.Height, null, MemoryMarshal.Cast<byte, float>(memory).ToArray());

            return (made.Width, made.Height, memory.ToArray(), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or InvalidOperationException or IOException or ArgumentException or PeachImage.IccProfileException)
        {
            return null;
        }
    }
}

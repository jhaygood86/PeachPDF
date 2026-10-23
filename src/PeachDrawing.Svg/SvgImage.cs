using PeachDrawing.Core;
using PeachImage;
using System;
using System.IO;

namespace PeachDrawing.Svg;

/// <summary>
/// An image decoded for an SVG document: its natural size and pixels (so a caller can read them, and so a format a browser cannot show
/// can be re-encoded), together with the encoded bytes to embed. An image a browser shows (PNG, JPEG, GIF, WebP, AVIF) is embedded
/// exactly as it came, never re-encoded; any other format PeachImage reads is embedded as a PNG of its pixels.
/// </summary>
internal sealed class SvgImage : PeachDrawing.Core.Image
{
    private readonly byte[] _rgba;

    private SvgImage(int width, int height, byte[] premultipliedRgba, byte[] encoded, string mimeType, int exifOrientation = 1)
    {
        ExifOrientation = exifOrientation;
        Width = width;
        Height = height;
        _rgba = premultipliedRgba;
        Encoded = encoded;
        MimeType = mimeType;
    }

    /// <summary>The bytes to embed.</summary>
    public byte[] Encoded { get; }

    /// <summary>The media type of <see cref="Encoded"/>.</summary>
    public string MimeType { get; }

    /// <summary>The <c>data:</c> URI that embeds the image.</summary>
    public string DataUri => "data:" + MimeType + ";base64," + Convert.ToBase64String(Encoded);

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    /// <summary>
    /// The EXIF orientation (1-8) the file stores. The pixels embedded are as stored and the layout applies the orientation through its draw
    /// matrix, so an image with an orientation is always embedded as a PNG of its stored pixels: a viewer applies the orientation of a JPEG,
    /// WebP or AVIF itself, which would turn the image twice.
    /// </summary>
    public override int ExifOrientation { get; }

    public override void Dispose()
    {
    }

    public override PixelBuffer? GetPixels() => new PixelBuffer((int)Width, (int)Height, _rgba);

    /// <summary>Decodes <paramref name="stream"/>, or returns null when PeachImage does not recognise the format.</summary>
    public static SvgImage? TryDecode(Stream stream)
    {
        byte[] source;
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length && ms.Position == 0)
        {
            source = segment.Array;
        }
        else
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            source = copy.ToArray();
        }

        if (!PeachImage.Image.TryLoad(new MemoryStream(source), out var image, new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 }) || image is null)
            return null;

        using (image)
        {
            var rgba = image.PixelMemory.Span.ToArray();
            Premultiply(rgba);

            var orientation = StoredOrientation(source);
            var mime = orientation == 1 ? Sniff(source) : null;
            if (mime is null)
            {
                using var surface = new RasterSurface(image.Width, image.Height, 0, 0, 1, 1);
                rgba.CopyTo(surface.Pixels);
                return new SvgImage(image.Width, image.Height, rgba, RasterSurfaceEncoding.EncodePng(surface, opaque: false), "image/png", orientation);
            }

            return new SvgImage(image.Width, image.Height, rgba, source, mime);
        }
    }

    private static int StoredOrientation(byte[] source)
    {
        try
        {
            return (int)PeachImage.Image.Identify(new MemoryStream(source)).Orientation;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    /// <summary>An image of already decoded premultiplied pixels, embedded as a PNG.</summary>
    public static SvgImage FromPixels(PixelBuffer pixels)
    {
        using var surface = new RasterSurface(pixels.Width, pixels.Height, 0, 0, 1, 1);
        pixels.PremultipliedRgba.Span.CopyTo(surface.Pixels);
        return new SvgImage(pixels.Width, pixels.Height, pixels.PremultipliedRgba.ToArray(), RasterSurfaceEncoding.EncodePng(surface, opaque: false), "image/png");
    }

    /// <summary>The media type of the formats a browser shows inside an SVG document, by their signature; null for any other.</summary>
    internal static string? Sniff(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "image/png";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "image/jpeg";
        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
            return "image/gif";
        if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
            return "image/webp";
        if (data.Length >= 12 && data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p' && data[8] == 'a' && data[9] == 'v' && data[10] == 'i' && data[11] == 'f')
            return "image/avif";
        return null;
    }

    private static void Premultiply(byte[] rgba)
    {
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            var a = rgba[i + 3];
            if (a == 255)
                continue;

            rgba[i] = (byte)((rgba[i] * a + 127) / 255);
            rgba[i + 1] = (byte)((rgba[i + 1] * a + 127) / 255);
            rgba[i + 2] = (byte)((rgba[i + 2] * a + 127) / 255);
        }
    }
}

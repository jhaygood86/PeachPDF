using PeachDrawing.Core;
using PeachImage;
using System;
using System.IO;
using System.IO.Compression;

using static PeachDrawing.PDF.Engine.Drawing.ImageSource;

namespace PeachDrawing.PDF.Engine.Drawing;

/// <summary>
/// Builds the pieces of a PNG pass-through embed - a Flate stream of PNG-row-filtered RGB samples, and for a surface with alpha a second, independent
/// Flate stream of filtered alpha samples - straight from a finished raster surface's pixels, with no PNG file in between. What the embedder needs is
/// exactly what a PNG's own <c>IDAT</c> (and its split-out alpha plane) would give it; making them directly skips encoding the surface as a PNG, reading
/// that PNG back, and inflating and deflating the alpha plane again.
/// </summary>
internal static class RasterPlaneCodec
{
    // PNG scanline filter types this writes (Sub and Up are all a smooth, blurred raster needs, and both are one subtraction per byte).
    private const byte FilterNone = 0;
    private const byte FilterSub = 1;
    private const byte FilterUp = 2;

    /// <summary>
    /// Whether every pixel of <paramref name="premultipliedRgba"/> is fully opaque or fully transparent. A PNG of such a surface is a palette (or truecolor)
    /// image whose transparency is a color key, which a PDF viewer composites without smoothing the edge; carrying it as a soft mask instead would not look
    /// the same, so it is left to the PNG route.
    /// </summary>
    public static bool HasOnlyBinaryAlpha(ReadOnlySpan<byte> premultipliedRgba)
    {
        for (var i = 3; i < premultipliedRgba.Length; i += 4)
        {
            var a = premultipliedRgba[i];
            if (a != 0 && a != 255)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether an opaque surface has no more than <paramref name="limit"/> distinct colors. A PNG of such a surface is a palette image, which the PDF carries as an
    /// <c>/Indexed</c> color space and which MuPDF resamples differently from <c>/DeviceRGB</c> when it scales it, so it is left to the PNG route to keep
    /// the picture the same. Stops looking as soon as there are more colors than that, which for a photograph, a gradient or a blur is almost at once.
    /// </summary>
    public static bool HasFewColors(ReadOnlySpan<byte> pixels, int limit)
    {
        var seen = new System.Collections.Generic.HashSet<uint>();
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels);
        var previous = 0u;
        for (var i = 0; i < words.Length; i++)
        {
            // A run of the same color is one entry, so a flat surface costs a compare per pixel and not a hash lookup.
            var word = words[i];
            if (i > 0 && word == previous)
                continue;

            previous = word;
            if (seen.Add(word) && seen.Count > limit)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The pass-through data for <paramref name="premultipliedRgba"/>, a <paramref name="width"/> x <paramref name="height"/> surface of premultiplied pixels:
    /// the color plane (straight, 8-bit RGB), and for a surface that is not <paramref name="opaque"/> the alpha plane.
    /// </summary>
    public static PngPassthroughData Encode(ReadOnlySpan<byte> premultipliedRgba, int width, int height, bool opaque)
    {
        var rgbRow = 3 * width;
        var rgbLength = height * (1 + rgbRow);
        var alphaLength = opaque ? 0 : height * (1 + width);

        // The filtered planes are scratch (only their compressed forms are kept), so they come from the pool.
        var pool = System.Buffers.ArrayPool<byte>.Shared;
        var rgbFiltered = pool.Rent(rgbLength);
        var alphaFiltered = opaque ? null : pool.Rent(alphaLength);
        try
        {
            return EncodeCore(premultipliedRgba, width, height, opaque, rgbFiltered, alphaFiltered, rgbLength, alphaLength);
        }
        finally
        {
            pool.Return(rgbFiltered);
            if (alphaFiltered is not null)
                pool.Return(alphaFiltered);
        }
    }

    private static PngPassthroughData EncodeCore(ReadOnlySpan<byte> premultipliedRgba, int width, int height, bool opaque,
        byte[] rgbFiltered, byte[]? alphaFiltered, int rgbLength, int alphaLength)
    {
        var rgbRow = 3 * width;

        var straight = opaque ? [] : new byte[width * 4];
        var current = new byte[rgbRow];
        var previous = new byte[rgbRow];
        var currentAlpha = opaque ? [] : new byte[width];
        var previousAlpha = opaque ? [] : new byte[width];

        for (var y = 0; y < height; y++)
        {
            var row = premultipliedRgba.Slice(y * width * 4, width * 4);
            if (opaque)
            {
                // No alpha to divide out: the color is what it is.
                for (int x = 0, o = 0; x < width; x++, o += 3)
                {
                    current[o] = row[x * 4];
                    current[o + 1] = row[x * 4 + 1];
                    current[o + 2] = row[x * 4 + 2];
                }
            }
            else
            {
                // Straight color, as every PNG (and so the route this replaces) carries it.
                PixelMath.Unpremultiply(row, straight);
                for (int x = 0, o = 0; x < width; x++, o += 3)
                {
                    current[o] = straight[x * 4];
                    current[o + 1] = straight[x * 4 + 1];
                    current[o + 2] = straight[x * 4 + 2];
                    currentAlpha[x] = straight[x * 4 + 3];
                }

                FilterRow(currentAlpha, previousAlpha, 1, alphaFiltered.AsSpan(y * (1 + width), 1 + width));
                (currentAlpha, previousAlpha) = (previousAlpha, currentAlpha);
            }

            FilterRow(current, previous, 3, rgbFiltered.AsSpan(y * (1 + rgbRow), 1 + rgbRow));
            (current, previous) = (previous, current);
        }

        return new PngPassthroughData
        {
            IdatData = Compress(rgbFiltered.AsSpan(0, rgbLength)),
            ColorSpace = PngPassthroughColorSpace.Rgb,
            BitDepth = 8,
            AlphaIdatData = alphaFiltered is null ? null : Compress(alphaFiltered.AsSpan(0, alphaLength)),
            AlphaBitDepth = alphaFiltered is null ? null : (byte)8,
        };
    }

    /// <summary>A zlib stream of <paramref name="data"/>. The output buffer starts at a fraction of the input, since filtered raster rows compress many-fold.</summary>
    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream(Math.Max(256, data.Length / 8));
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    /// <summary>Writes one scanline into <paramref name="destination"/> (its filter-type byte, then the filtered samples), as Sub or Up, whichever leaves the smaller sum of magnitudes.</summary>
    private static void FilterRow(ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous, int bytesPerPixel, Span<byte> destination)
    {
        var sub = 0;
        var up = 0;
        for (var i = 0; i < current.Length; i++)
        {
            var s = (byte)(current[i] - (i >= bytesPerPixel ? current[i - bytesPerPixel] : 0));
            var u = (byte)(current[i] - previous[i]);
            sub += s < 128 ? s : 256 - s;
            up += u < 128 ? u : 256 - u;
        }

        if (up <= sub)
        {
            destination[0] = FilterUp;
            for (var i = 0; i < current.Length; i++)
                destination[1 + i] = (byte)(current[i] - previous[i]);
        }
        else
        {
            destination[0] = FilterSub;
            for (var i = 0; i < current.Length; i++)
                destination[1 + i] = (byte)(current[i] - (i >= bytesPerPixel ? current[i - bytesPerPixel] : 0));
        }
    }

    /// <summary>The straight RGBA pixels the pass-through data describes (for the rare reader that needs the pixels themselves, not the stream).</summary>
    public static byte[] ToRgba(in PngPassthroughData data, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var rgb = Unfilter(Inflate(data.IdatData), width, height, 3);
        var alpha = data.AlphaIdatData is { } alphaStream ? Unfilter(Inflate(alphaStream), width, height, 1) : null;

        for (var i = 0; i < width * height; i++)
        {
            rgba[i * 4] = rgb[i * 3];
            rgba[i * 4 + 1] = rgb[i * 3 + 1];
            rgba[i * 4 + 2] = rgb[i * 3 + 2];
            rgba[i * 4 + 3] = alpha is null ? (byte)255 : alpha[i];
        }

        return rgba;
    }

    private static byte[] Inflate(byte[] zlib)
    {
        using var input = new MemoryStream(zlib);
        using var inflater = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflater.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Reverses the row filters (None, Sub, Up) of <paramref name="filtered"/>, returning the samples without the filter-type bytes.</summary>
    internal static byte[] Unfilter(byte[] filtered, int width, int height, int bytesPerPixel)
    {
        var rowLength = width * bytesPerPixel;
        var result = new byte[rowLength * height];
        for (var y = 0; y < height; y++)
        {
            var type = filtered[y * (1 + rowLength)];
            var source = filtered.AsSpan(y * (1 + rowLength) + 1, rowLength);
            var row = result.AsSpan(y * rowLength, rowLength);
            var above = y > 0 ? result.AsSpan((y - 1) * rowLength, rowLength) : new byte[rowLength];

            for (var i = 0; i < rowLength; i++)
            {
                row[i] = type switch
                {
                    FilterNone => source[i],
                    FilterSub => (byte)(source[i] + (i >= bytesPerPixel ? row[i - bytesPerPixel] : 0)),
                    FilterUp => (byte)(source[i] + above[i]),
                    _ => throw new InvalidDataException($"Unexpected row filter {type}."),
                };
            }
        }

        return result;
    }
}

/// <summary>
/// An embedded raster surface (a blurred shadow, a filtered region) whose pixels are already encoded as the streams of a PNG pass-through embed - see
/// <see cref="RasterPlaneCodec"/>. It stands where the PNG-reading <c>PngPassthroughImageSource</c> stood for such a surface, without the PNG: the rare
/// readers that want the pixels themselves (a lossy JPEG re-encode, a bitmap rendition) get them back from the streams.
/// </summary>
internal sealed class RasterPassthroughImageSource : IImageSource, IRgbaPixelProvider
{
    private readonly PngPassthroughData _passthrough;
    private readonly int _quality;
    private DecodedImageSource? _decodedFallback;

    public int Width { get; }
    public int Height { get; }
    public string Name { get; }
    public bool Transparent => false;
    public bool IsCmyk => false;
    public bool IsGrayscale => false;
    public JpegPassthroughData? JpegPassthrough => null;
    public CmykRasterData? CmykRaster => null;
    public PngPassthroughData? PngPassthrough => _passthrough;
    public GifPassthroughData? GifPassthrough => null;
    public bool IsLosslessSourceFormat => true;
    public byte[]? RgbIccProfile => null;

    public RasterPassthroughImageSource(string name, int width, int height, PngPassthroughData passthrough, int quality = 75)
    {
        Name = name;
        Width = width;
        Height = height;
        _passthrough = passthrough;
        _quality = quality;
    }

    private DecodedImageSource DecodedFallback()
    {
        if (_decodedFallback is not null)
            return _decodedFallback;

        var image = PeachImage.Image.Create(Width, Height, PixelFormat.Rgba32);
        RasterPlaneCodec.ToRgba(_passthrough, Width, Height).CopyTo(image.GetPixelSpan());
        return _decodedFallback = new DecodedImageSource(Name, image, _quality, transparent: false);
    }

    public void SaveAsJpeg(MemoryStream ms, int? targetWidth = null, int? targetHeight = null, int? qualityOverride = null) =>
        DecodedFallback().SaveAsJpeg(ms, targetWidth, targetHeight, qualityOverride);

    public void SaveAsPdfBitmap(MemoryStream ms, int? targetWidth = null, int? targetHeight = null) =>
        DecodedFallback().SaveAsPdfBitmap(ms, targetWidth, targetHeight);

    public bool TryGetRgba(out int width, out int height, out byte[] rgba) =>
        DecodedFallback().TryGetRgba(out width, out height, out rgba);
}

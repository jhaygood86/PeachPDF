using CoreGraphics;
using Foundation;
using ImageIO;
using PeachDrawing.Core;
using PeachImage;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PeachDrawing.CoreGraphics;

/// <summary>
/// A decoded raster image. It keeps its pixels either as managed premultiplied RGBA (decoded by PeachImage, or converted from another backend's
/// image), as a <see cref="CGImage"/> (decoded by ImageIO), or as the encoded bytes ImageIO decodes on first draw, and makes the
/// <see cref="CGImage"/> a draw needs lazily.
/// </summary>
internal sealed class CoreGraphicsImage : PeachDrawing.Core.Image
{
    private readonly byte[]? _premultipliedRgba;
    private readonly byte[]? _encoded;
    private readonly CoreGraphicsResourceCounts _counts;
    private readonly object _gate = new();
    private CGImage? _image;
    private bool _disposed;

    private CoreGraphicsImage(int width, int height, byte[]? premultipliedRgba, byte[]? encoded, CGImage? image, CoreGraphicsResourceCounts counts, int exifOrientation = 1)
    {
        Width = width;
        Height = height;
        ExifOrientation = exifOrientation;
        _premultipliedRgba = premultipliedRgba;
        _encoded = encoded;
        _image = image;
        _counts = counts;
        if (image is not null)
            counts.Surface(1);
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    public override int ExifOrientation { get; }

    /// <summary>Whether the pixels are held as managed memory (false for an image ImageIO decodes itself).</summary>
    internal bool HasManagedPixels => _premultipliedRgba is not null;

    /// <summary>Whether the image is still only its encoded bytes: no decode has happened.</summary>
    internal bool IsEncodedOnly
    {
        get
        {
            lock (_gate)
                return _encoded is not null && _image is null;
        }
    }

    public override PixelBuffer? GetPixels()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_premultipliedRgba is not null)
                return new PixelBuffer((int)Width, (int)Height, _premultipliedRgba);
        }

        // A natively decoded image reads its pixels back on demand (the raster seam and conversion to another backend ask for them).
        var image = GetCgImage();
        return ReadPixels(image);
    }

    internal static PixelBuffer? ReadPixels(CGImage image)
    {
        var width = (int)image.Width;
        var height = (int)image.Height;
        var rgba = new byte[width * height * 4];
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            using var bitmap = new CGBitmapContext(handle.AddrOfPinnedObject(), width, height, 8, width * 4, CoreGraphicsCanvas.Rgb, CGImageAlphaInfo.PremultipliedLast);
            bitmap.SetBlendMode(CGBlendMode.Copy);
            bitmap.DrawImage(new CGRect(0, 0, width, height), image);
        }
        finally
        {
            handle.Free();
        }

        return new PixelBuffer(width, height, rgba);
    }

    /// <summary>The image to draw: not owned by the caller, which must not dispose it.</summary>
    public CGImage GetCgImage()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_image is not null)
                return _image;

            if (_premultipliedRgba is not null)
            {
                _image = FromPremultipliedRgba(_premultipliedRgba, (int)Width, (int)Height)
                    ?? throw new InvalidOperationException("The image's pixels could not be handed to CoreGraphics.");
            }
            else
            {
                using var data = NSData.FromArray(_encoded!);
                using var source = CGImageSource.FromData(data);
                _image = source?.CreateImage(0, new CGImageOptions { ShouldCache = false })
                    ?? throw new InvalidOperationException("ImageIO could not decode the image.");
            }

            _counts.Surface(1);
            return _image;
        }
    }

    /// <summary>A <see cref="CGImage"/> over a copy of premultiplied RGBA pixels (row 0 on top).</summary>
    internal static CGImage? FromPremultipliedRgba(ReadOnlySpan<byte> rgba, int width, int height)
    {
        using var provider = new CGDataProvider(rgba.Slice(0, width * height * 4).ToArray());
        return new CGImage(width, height, 8, 32, width * 4, CoreGraphicsCanvas.Rgb,
            CGBitmapFlags.PremultipliedLast | CGBitmapFlags.ByteOrderDefault, provider, null, true, CGColorRenderingIntent.Default);
    }

    public override void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_image is not null)
            {
                _image.Dispose();
                _image = null;
                _counts.Surface(-1);
            }
        }
    }

    // ---- decoding -----------------------------------------------------------------------------------------

    internal static byte[] ReadAll(Stream stream)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length && ms.Position == 0)
            return segment.Array;

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Decodes with PeachImage, or returns <see langword="null"/> when the data is not an image it knows.</summary>
    public static CoreGraphicsImage? TryDecodePeach(byte[] source, CoreGraphicsResourceCounts counts)
    {
        if (!PeachImage.Image.TryLoad(new MemoryStream(source), out var image, new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 }) || image is null)
            return null;

        using (image)
        {
            var rgba = image.PixelMemory.Span.ToArray();
            Premultiply(rgba);
            return new CoreGraphicsImage(image.Width, image.Height, rgba, null, null, counts, StoredOrientation(source));
        }
    }

    /// <summary>Decodes with ImageIO, or returns <see langword="null"/> when ImageIO does not recognise the data.</summary>
    public static CoreGraphicsImage? TryDecodeNative(byte[] source, CoreGraphicsResourceCounts counts)
    {
        if (!TryOpen(source, out var width, out var height, out var orientation))
            return null;

        using var data = NSData.FromArray(source);
        using var imageSource = CGImageSource.FromData(data);
        using var decoded = imageSource?.CreateImage(0, new CGImageOptions { ShouldCache = true, ShouldCacheImmediately = true });
        if (decoded is null)
            return null;

        // Redraw into a premultiplied RGBA bitmap so every image the backend holds is in the one form the rest of PeachDrawing exchanges.
        return PixelsOf(decoded) is { } pixels
            ? new CoreGraphicsImage(width, height, pixels, null, null, counts, orientation)
            : null;
    }

    private static byte[]? PixelsOf(CGImage decoded) => ReadPixels(decoded)?.PremultipliedRgba.ToArray();

    /// <summary>
    /// An image ImageIO can decode, kept as its encoded bytes: ImageIO decodes it on first draw, and the system may do so in hardware.
    /// <see langword="null"/> when ImageIO has no codec for the data, or the image has an orientation stored with it: ImageIO leaves that for the
    /// caller to apply, and PeachImage's decode does the same, so such an image is left to it.
    /// </summary>
    public static CoreGraphicsImage? TryCreateLazy(byte[] source, CoreGraphicsResourceCounts counts) =>
        TryOpen(source, out var width, out var height, out var orientation) && orientation == 1
            ? new CoreGraphicsImage(width, height, null, source, null, counts)
            : null;

    private static bool TryOpen(byte[] source, out int width, out int height, out int orientation)
    {
        width = height = 0;
        orientation = 1;
        using var data = NSData.FromArray(source);
        using var imageSource = CGImageSource.FromData(data);
        if (imageSource is null || imageSource.ImageCount < 1)
            return false;

        using var properties = imageSource.CopyProperties(new CGImageOptions(), 0);
        if (properties is null
            || properties[new NSString("PixelWidth")] is not NSNumber w || properties[new NSString("PixelHeight")] is not NSNumber h
            || w.Int32Value <= 0 || h.Int32Value <= 0)
        {
            return false;
        }

        width = w.Int32Value;
        height = h.Int32Value;
        orientation = properties[new NSString("Orientation")] is NSNumber o ? o.Int32Value : 1;
        return true;
    }

    public static CoreGraphicsImage FromPixels(PixelBuffer pixels, CoreGraphicsResourceCounts counts) =>
        new(pixels.Width, pixels.Height, pixels.PremultipliedRgba.ToArray(), null, null, counts);

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

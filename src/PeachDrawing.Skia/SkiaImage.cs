using PeachDrawing.Core;
using PeachImage;
using SkiaSharp;
using System;
using System.IO;

namespace PeachDrawing.Skia;

/// <summary>
/// A decoded raster image. It keeps its pixels either as managed premultiplied RGBA (decoded by PeachImage, or converted from another
/// backend's image) or as an <see cref="SKImage"/> (decoded by Skia's own codecs), and makes the <see cref="SKImage"/> a draw needs lazily:
/// on a GPU canvas a texture uploaded once for that GPU context, otherwise a raster image. The same image can therefore be drawn on two
/// GPU contexts, and survives the one it was first drawn on being abandoned.
/// </summary>
internal sealed class SkiaImage : PeachDrawing.Core.Image
{
    private readonly byte[]? _premultipliedRgba;
    private readonly SkiaResourceCounts _counts;
    private readonly object _gate = new();
    private SKImage? _raster;
    private SKImage? _texture;
    private GRContext? _textureContext;
    private bool _disposed;

    private SkiaImage(int width, int height, byte[]? premultipliedRgba, SKImage? raster, SkiaResourceCounts counts, int exifOrientation = 1)
    {
        Width = width;
        Height = height;
        ExifOrientation = exifOrientation;
        _premultipliedRgba = premultipliedRgba;
        _raster = raster;
        _counts = counts;
        if (raster is not null)
            counts.Surface(1);
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    public override int ExifOrientation { get; }

    public override PixelBuffer? GetPixels()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_premultipliedRgba is not null)
                return new PixelBuffer((int)Width, (int)Height, _premultipliedRgba);

            // A natively decoded image reads its pixels back on demand (the raster seam and conversion to another backend ask for them).
            var info = new SKImageInfo((int)Width, (int)Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            return _raster is not null && _raster.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0)
                ? new PixelBuffer(info.Width, info.Height, bitmap.GetPixelSpan().ToArray())
                : null;
        }
    }

    /// <summary>
    /// The image to draw for a canvas on <paramref name="gpu"/> (or a raster canvas when it is <see langword="null"/>): not owned by the
    /// caller, which must not dispose it.
    /// </summary>
    public SKImage GetImage(GRContext? gpu)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_raster is null)
            {
                var info = new SKImageInfo((int)Width, (int)Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                _raster = SKImage.FromPixelCopy(info, _premultipliedRgba!, info.RowBytes)
                    ?? throw new InvalidOperationException("The image's pixels could not be handed to Skia.");
                _counts.Surface(1);
            }

            if (gpu is null || gpu.IsAbandoned)
                return _raster;

            if (_texture is not null && ReferenceEquals(_textureContext, gpu))
                return _texture;

            // A texture of another (or an abandoned) GPU context is of no use; release it before making the replacement.
            ReleaseTexture();
            _texture = _raster.ToTextureImage(gpu);
            if (_texture is null)
                return _raster;

            _textureContext = gpu;
            _counts.Surface(1);
            return _texture;
        }
    }

    private void ReleaseTexture()
    {
        if (_texture is null)
            return;

        _texture.Dispose();
        _texture = null;
        _textureContext = null;
        _counts.Surface(-1);
    }

    public override void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            ReleaseTexture();
            if (_raster is not null)
            {
                _raster.Dispose();
                _raster = null;
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
    public static SkiaImage? TryDecodePeach(Stream stream, SkiaResourceCounts counts) => TryDecodePeach(ReadAll(stream), counts);

    public static SkiaImage? TryDecodePeach(byte[] source, SkiaResourceCounts counts)
    {
        if (!PeachImage.Image.TryLoad(new MemoryStream(source), out var image, new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 }) || image is null)
            return null;

        using (image)
        {
            var rgba = image.PixelMemory.Span.ToArray();
            Premultiply(rgba);
            return new SkiaImage(image.Width, image.Height, rgba, null, counts, StoredOrientation(source));
        }
    }

    /// <summary>Decodes with Skia's codecs, or returns <see langword="null"/> when Skia does not recognise the data.</summary>
    public static SkiaImage? TryDecodeNative(Stream stream, SkiaResourceCounts counts) => TryDecodeNative(ReadAll(stream), counts);

    public static SkiaImage? TryDecodeNative(byte[] source, SkiaResourceCounts counts)
    {
        using var data = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(data);
        if (codec is null)
            return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = SKBitmap.Decode(codec, info);
        if (bitmap is null)
            return null;

        var image = SKImage.FromPixelCopy(info, bitmap.GetPixelSpan(), info.RowBytes);
        return image is null ? null : new SkiaImage(info.Width, info.Height, null, image, counts, (int)codec.EncodedOrigin);
    }

    public static SkiaImage FromPixels(PixelBuffer pixels, SkiaResourceCounts counts) =>
        new(pixels.Width, pixels.Height, pixels.PremultipliedRgba.ToArray(), null, counts);

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

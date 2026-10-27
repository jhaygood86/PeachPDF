using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachImage;
using System;
using System.IO;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D;

/// <summary>
/// A decoded raster image. It keeps its pixels as managed memory and makes the <see cref="CanvasBitmap"/> a draw needs lazily, for the
/// device that draw is made on, so the same image can be drawn on two devices and survives the device it was first drawn on being lost.
/// </summary>
internal sealed class Win2dImage : PeachDrawing.Core.Image
{
    private readonly byte[] _premultipliedRgba;
    private readonly object _gate = new();
    private CanvasBitmap? _bitmap;
    private CanvasDevice? _bitmapDevice;
    private bool _disposed;

    private Win2dImage(int width, int height, byte[] premultipliedRgba, int exifOrientation = 1)
    {
        Width = width;
        Height = height;
        ExifOrientation = exifOrientation;
        _premultipliedRgba = premultipliedRgba;
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    public override int ExifOrientation { get; }

    public override PixelBuffer? GetPixels() => new PixelBuffer((int)Width, (int)Height, _premultipliedRgba);

    /// <summary>The bitmap for <paramref name="creator"/>'s device: the one already made if it is for that device, otherwise a new one.</summary>
    public CanvasBitmap GetBitmap(ICanvasResourceCreator creator)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var device = creator.Device;
            if (_bitmap is not null && ReferenceEquals(_bitmapDevice, device))
                return _bitmap;

            // A bitmap of another (or a lost) device is of no use; release it before making the replacement.
            _bitmap?.Dispose();
            _bitmap = null;
            _bitmapDevice = null;

            _bitmap = CanvasBitmap.CreateFromBytes(creator, _premultipliedRgba, (int)Width, (int)Height,
                DirectXPixelFormat.R8G8B8A8UIntNormalized, 96f, CanvasAlphaMode.Premultiplied);
            _bitmapDevice = device;
            return _bitmap;
        }
    }

    public override void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _bitmap?.Dispose();
            _bitmap = null;
            _bitmapDevice = null;
        }
    }

    public static Win2dImage? TryDecode(Stream stream)
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
            return new Win2dImage(image.Width, image.Height, rgba, StoredOrientation(source));
        }
    }

    public static Win2dImage FromPixels(PixelBuffer pixels) =>
        new(pixels.Width, pixels.Height, pixels.PremultipliedRgba.ToArray());

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

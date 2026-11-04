using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachImage;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;

namespace PeachDrawing.Win2D;

/// <summary>
/// A decoded raster image. It makes the <see cref="CanvasBitmap"/> a draw needs lazily, for the device that draw is made on, so the same image
/// can be drawn on two devices and survives the device it was first drawn on being lost. Most images keep their pixels as managed memory;
/// an image made by <see cref="TryCreateGpuResident"/> keeps only its encoded bytes, has Windows decode them straight into the device's bitmap,
/// and reads the pixels back only if something asks for them.
/// </summary>
internal sealed class Win2dImage : PeachDrawing.Core.Image
{
    private byte[]? _premultipliedRgba;
    private readonly byte[]? _encoded;
    private readonly object _gate = new();
    private CanvasBitmap? _bitmap;
    private CanvasDevice? _bitmapDevice;
    private bool _disposed;

    private Win2dImage(int width, int height, byte[]? premultipliedRgba, int exifOrientation = 1, byte[]? encoded = null)
    {
        Width = width;
        Height = height;
        ExifOrientation = exifOrientation;
        _premultipliedRgba = premultipliedRgba;
        _encoded = encoded;
    }

    public override double Width { get; }

    public override double Height { get; }

    public override bool Interpolate { get; set; } = true;

    public override int ExifOrientation { get; }

    public override PixelBuffer? GetPixels() => new PixelBuffer((int)Width, (int)Height, Pixels());

    /// <summary>Whether the pixels are held as managed memory (false for a GPU-resident image nothing has asked the pixels of).</summary>
    internal bool HasManagedPixels
    {
        get
        {
            lock (_gate)
                return _premultipliedRgba is not null;
        }
    }

    /// <summary>The pixels, decoding them now for an image that has so far lived only on the device.</summary>
    private byte[] Pixels()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _premultipliedRgba ??= DecodePixels(_encoded!, (int)Width, (int)Height);
        }
    }

    // The pixels of an image kept as encoded bytes: Windows decodes them, and PeachImage does if Windows now cannot.
    private static byte[] DecodePixels(byte[] encoded, int width, int height)
    {
        var native = Task.Run(() => DecodeNativeAsync(encoded)).GetAwaiter().GetResult();
        if (native?._premultipliedRgba is { } pixels && native.Width == width && native.Height == height)
            return pixels;

        return TryDecode(encoded)?._premultipliedRgba ?? throw new InvalidDataException("The image can no longer be decoded.");
    }

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

            _bitmap = _premultipliedRgba is null ? LoadOnDevice(device) : CreateFromPixels(creator, _premultipliedRgba);
            _bitmapDevice = device;
            return _bitmap;
        }
    }

    private CanvasBitmap CreateFromPixels(ICanvasResourceCreator creator, byte[] pixels) =>
        CanvasBitmap.CreateFromBytes(creator, pixels, (int)Width, (int)Height, DirectXPixelFormat.R8G8B8A8UIntNormalized, 96f, CanvasAlphaMode.Premultiplied);

    // Windows decodes the encoded bytes straight into the device's bitmap: no managed copy of the pixels is made. If it cannot (the
    // image opened but does not decode, or the device refuses the load) the pixels come from the fallback decoder.
    private CanvasBitmap LoadOnDevice(CanvasDevice device)
    {
        try
        {
            return Task.Run(async () =>
            {
                using var stream = new MemoryStream(_encoded!).AsRandomAccessStream();
                return await CanvasBitmap.LoadAsync(device, stream, 96f, CanvasAlphaMode.Premultiplied);
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or IOException)
        {
            _premultipliedRgba = DecodePixels(_encoded!, (int)Width, (int)Height);
            return CreateFromPixels(device, _premultipliedRgba);
        }
    }

    /// <summary>
    /// An image Windows can decode, kept as its encoded bytes and decoded by Windows directly into the bitmap of each device it is drawn on,
    /// or <see langword="null"/> when Windows has no codec for the data. Only the size is read here; the orientation stored with the image is
    /// read by PeachImage, as <see cref="TryDecodeNative"/> does.
    /// </summary>
    public static Win2dImage? TryCreateGpuResident(byte[] source) => Task.Run(() => OpenAsync(source)).GetAwaiter().GetResult();

    private static async Task<Win2dImage?> OpenAsync(byte[] source)
    {
        try
        {
            // Loading into a bitmap applies the orientation stored with the image and cannot be told not to, while the rest of PeachDrawing
            // applies it itself from ExifOrientation; an image that has one is decoded the other way, which can leave it unapplied.
            var orientation = StoredOrientation(source);
            if (orientation != 1)
                return null;

            using var stream = new MemoryStream(source).AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var frame = await decoder.GetFrameAsync(0);
            return new Win2dImage((int)frame.PixelWidth, (int)frame.PixelHeight, null, orientation, source);
        }
        catch (Exception)
        {
            return null;
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

    public static byte[] ReadAll(Stream stream)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length && ms.Position == 0)
            return segment.Array;

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    public static Win2dImage? TryDecode(Stream stream) => TryDecode(ReadAll(stream));

    /// <summary>
    /// Decodes with the Windows Imaging Component, or returns <see langword="null"/> when Windows has no codec for the data. The decode runs on a
    /// thread-pool thread and is waited for, so calling it from a UI thread cannot deadlock. The orientation stored with the image is read by
    /// PeachImage, because WIC applies or hides it rather than reporting it the way the rest of PeachDrawing expects.
    /// </summary>
    public static Win2dImage? TryDecodeNative(byte[] source) => Task.Run(() => DecodeNativeAsync(source)).GetAwaiter().GetResult();

    private static async Task<Win2dImage?> DecodeNativeAsync(byte[] source)
    {
        try
        {
            using var stream = new MemoryStream(source).AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied, new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return new Win2dImage((int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData(), StoredOrientation(source));
        }
        catch (Exception)
        {
            // No codec for the data (or one that failed): the caller decides whether PeachImage gets a turn.
            return null;
        }
    }

    public static Win2dImage? TryDecode(byte[] source)
    {
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

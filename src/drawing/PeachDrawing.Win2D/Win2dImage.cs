using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachImage;
using System;
using System.Threading;
using System.Collections.Generic;
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
    private readonly Lock _gate = new();
    private CanvasBitmap? _bitmap;
    private CanvasDevice? _bitmapDevice;
    private double _bitmapScale = 1;
    private bool _disposed;

    // An animated image holds every frame composited, ready to upload, and the frame that is drawn.
    private (byte[] Pixels, TimeSpan Duration)[]? _frames;
    private int _loopCount = 1;
    private int _currentFrame;
    private int _bitmapFrame;

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

    public override int FrameCount => _frames?.Length ?? 1;

    public override TimeSpan GetFrameDuration(int index) => _frames is { } frames && (uint)index < (uint)frames.Length ? frames[index].Duration : TimeSpan.Zero;

    public override int LoopCount => _loopCount;

    public override int CurrentFrame
    {
        get
        {
            lock (_gate)
                return _currentFrame;
        }

        set
        {
            lock (_gate)
            {
                if (_frames is { } frames)
                    _currentFrame = Math.Clamp(value, 0, frames.Length - 1);
            }
        }
    }

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
            if (_frames is { } frames)
                return frames[_currentFrame].Pixels;

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

    /// <summary>
    /// The smallest scale, in quarter-octave steps (at most 1, at least 1/64), at which a bitmap still has at least the resolution a draw at
    /// <paramref name="needed"/> device pixels per source pixel reads, so a picture shown far smaller than it is decoded keeps only what it shows.
    /// The steps are fine (the bitmap is at most 19% larger across) so the draw that follows is close to one to one and does not blur a thin line
    /// a second time; they are coarse enough that a zoom does not decode the image again for every notch.
    /// </summary>
    internal static double ScaleFor(double needed)
    {
        if (!(needed < 0.84))
            return 1;

        const double floor = 1.0 / 64;
        if (needed <= floor)
            return floor;

        return Math.Min(1, Math.Pow(2, Math.Ceiling(Math.Log2(needed) * 4) / 4));
    }

    /// <summary>
    /// The bitmap for <paramref name="creator"/>'s device: the one already made if it is for that device (and, for an image that can be decoded
    /// smaller, has at least <paramref name="scale"/> of the image's resolution), otherwise a new one. The bitmap is <see cref="BitmapScale"/> of the
    /// image's size, which is what a caller drawing from it divides by.
    /// </summary>
    public CanvasBitmap GetBitmap(ICanvasResourceCreator creator, double scale = 1)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var device = creator.Device;
            if (_bitmap is not null && ReferenceEquals(_bitmapDevice, device) && _bitmapScale >= scale)
            {
                // The next frame goes into the texture that is there: no new allocation on the device.
                if (_frames is { } playing && _bitmapFrame != _currentFrame)
                {
                    _bitmap.SetPixelBytes(playing[_currentFrame].Pixels);
                    _bitmapFrame = _currentFrame;
                }

                return _bitmap;
            }

            // A bitmap of another (or a lost) device is of no use; release it before making the replacement.
            _bitmap?.Dispose();
            _bitmap = null;
            _bitmapDevice = null;

            var made = 1.0;
            var loadStarted = Win2dPaintStatistics.Start();
            if (_frames is { } frames)
            {
                _bitmap = CreateFromPixels(creator, frames[_currentFrame].Pixels);
            }
            else if (_premultipliedRgba is not null)
            {
                _bitmap = CreateFromPixels(creator, _premultipliedRgba);
            }
            else
            {
                var want = Math.Clamp(scale, 1.0 / 64, 1.0);
                _bitmap = want < 1 ? LoadScaledOnDevice(device, want, out made) : LoadOnDevice(device);
            }

            _bitmapFrame = _currentFrame;
            _bitmapDevice = device;
            _bitmapScale = made;
            Win2dPaintStatistics.Stop(Win2dPaintStatistics.Counter.ImageLoad, loadStarted);
            return _bitmap;
        }
    }

    /// <summary>How much of the image's size the bitmap <see cref="GetBitmap"/> returned has (1 for the full size).</summary>
    internal double BitmapScale
    {
        get
        {
            lock (_gate)
                return _bitmapScale;
        }
    }

    // A JPEG is the common large picture, and Windows decodes it at a reduced size by scaling inside the decode (the full picture is never made), with a
    // box-like Fant filter. Anything else (a PNG screenshot with thin lines, a logo) is decoded whole and reduced on the device with the same cubic filter a
    // draw would use, because Windows' own scaler measurably dims a one-pixel line in a big reduction; only the reduced bitmap is kept. A failure falls
    // back to the full size.
    private CanvasBitmap LoadScaledOnDevice(CanvasDevice device, double scale, out double made)
    {
        var width = Math.Max(1, (int)Math.Ceiling(Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Height * scale));
        try
        {
            made = (double)width / Width;
            if (_encoded![0] == 0xFF && _encoded[1] == 0xD8)
            {
                var pixels = Task.Run(async () =>
                {
                    using var stream = new MemoryStream(_encoded).AsRandomAccessStream();
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    var transform = new BitmapTransform { ScaledWidth = (uint)width, ScaledHeight = (uint)height, InterpolationMode = BitmapInterpolationMode.Fant };
                    var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Premultiplied, transform,
                        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                    return data.DetachPixelData();
                }).GetAwaiter().GetResult();

                return CanvasBitmap.CreateFromBytes(device, pixels, width, height, DirectXPixelFormat.R8G8B8A8UIntNormalized, 96f, CanvasAlphaMode.Premultiplied);
            }

            using var full = LoadOnDevice(device);
            var reduced = new CanvasRenderTarget(device, width, height, 96f, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
            using (var session = reduced.CreateDrawingSession())
            {
                session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                session.Blend = CanvasBlend.Copy;
                session.DrawImage(full, new Windows.Foundation.Rect(0, 0, width, height), new Windows.Foundation.Rect(0, 0, Width, Height), 1f, CanvasImageInterpolation.HighQualityCubic);
            }

            return reduced;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or IOException)
        {
            made = 1;
            return LoadOnDevice(device);
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
        var options = new DecoderOptions { TargetPixelFormat = PixelFormat.Rgba32 };
        if (!PeachImage.Image.TryLoad(new MemoryStream(source), out var image, options) || image is null)
            return null;

        using (image)
        {
            if (image.IsAnimated && TryDecodeAnimation(source, options, image.Width, image.Height) is { } animated)
                return animated;

            var rgba = image.PixelMemory.Span.ToArray();
            Premultiply(rgba);
            return new Win2dImage(image.Width, image.Height, rgba, StoredOrientation(source));
        }
    }

    // Every frame of an animated GIF or WebP, composited, with how long each is shown. Null when the file turns out to hold a single frame.
    private const long MaxAnimationBytes = 256L * 1024 * 1024;

    private static Win2dImage? TryDecodeAnimation(byte[] source, DecoderOptions options, int width, int height)
    {
        try
        {
            var animation = AnimatedImage.Load(new MemoryStream(source), options);
            var frames = new List<(byte[] Pixels, TimeSpan Duration)>();
            foreach (var frame in animation.Frames)
            {
                if (frame.Image.Width != width || frame.Image.Height != height)
                    return null;

                var rgba = frame.Image.PixelMemory.Span.ToArray();
                Premultiply(rgba);
                frames.Add((rgba, frame.Duration));
            }

            if (frames.Count < 2)
                return null;

            // Every frame is kept composited; a huge animation stays a still image rather than take gigabytes.
            if ((long)width * height * 4 * frames.Count > MaxAnimationBytes)
                return null;

            return new Win2dImage(width, height, frames[0].Pixels, StoredOrientation(source))
            {
                _frames = [.. frames],
                _loopCount = animation.LoopCount,
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="source"/> looks like a format that can hold an animation (GIF, WebP or PNG), so it is read by PeachImage whatever codec is set.</summary>
    internal static bool MayBeAnimated(byte[] source)
    {
        if (source.Length < 16)
            return false;

        // GIF87a / GIF89a
        if (source[0] == 'G' && source[1] == 'I' && source[2] == 'F')
            return true;

        // RIFF....WEBP
        if (source[0] == 'R' && source[1] == 'I' && source[2] == 'F' && source[3] == 'F' && source[8] == 'W' && source[9] == 'E' && source[10] == 'B' && source[11] == 'P')
            return true;

        // An APNG has an acTL chunk before its first image data.
        if (source[0] == 0x89 && source[1] == 'P' && source[2] == 'N' && source[3] == 'G')
        {
            var limit = Math.Min(source.Length - 4, 2048);
            for (var i = 8; i < limit; i++)
            {
                if (source[i] == 'a' && source[i + 1] == 'c' && source[i + 2] == 'T' && source[i + 3] == 'L')
                    return true;
            }
        }

        return false;
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

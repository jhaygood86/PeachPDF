using PeachDrawing.Core;
using SkiaSharp;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PeachDrawing.Skia;

/// <summary>
/// The Skia backend's <see cref="OffscreenSurface"/>: an <see cref="SKSurface"/> of premultiplied RGBA8 (or half-float RGBA, for a wide one) and the <see cref="SkiaCanvas"/> over it. It is a raster
/// surface, drawn on the thread that draws into it: a surface on the graphics hardware may only be drawn on the thread that owns its GL context, and a canvas is
/// drawn into by a script on the thread that runs it. A snapshot is a copy-on-write <see cref="SKImage"/>, and where the target canvas has a
/// <see cref="SkiaRenderContext.GpuContext"/> it is uploaded as a texture when it is drawn (on the drawing thread), once for each snapshot taken.
/// </summary>
internal sealed class SkiaOffscreenSurface : OffscreenSurface
{
    private readonly SkiaRenderContext _context;
    private SKSurface? _surface;
    private SkiaCanvas? _canvas;
    private readonly bool _wide;

    public SkiaOffscreenSurface(SkiaRenderContext context, SKSurface surface, SkiaCanvas canvas, int width, int height, bool wide = false)
        : base(width, height)
    {
        _wide = wide;
        _context = context;
        _surface = surface;
        _canvas = canvas;
        context.Counts.Surface(1);
    }

    public override Canvas Canvas => _canvas ?? throw new ObjectDisposedException(nameof(SkiaOffscreenSurface));

    public override bool CanDraw(Canvas target) => target is SkiaCanvas skia && ReferenceEquals(skia.Context, _context);

    public override Image CreateSnapshot()
    {
        ObjectDisposedException.ThrowIf(_surface is null, this);
        var snapshot = _surface.Snapshot();
        return SkiaImage.FromSnapshot(snapshot, Width, Height, _context.Counts);
    }

    public override PixelBuffer ReadPixels()
    {
        ObjectDisposedException.ThrowIf(_surface is null, this);
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bytes = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (!_surface.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0))
                throw new InvalidOperationException("The surface's pixels could not be read.");
        }
        finally
        {
            handle.Free();
        }

        return new PixelBuffer(Width, Height, bytes);
    }

    public override bool IsWide => _wide;

    public override float[]? ReadWidePixels()
    {
        if (!_wide)
            return null;

        ObjectDisposedException.ThrowIf(_surface is null, this);
        var info = new SKImageInfo(Width, Height, SKColorType.RgbaF16, SKAlphaType.Premul);
        var bytes = new byte[info.BytesSize];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            if (!_surface.ReadPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, 0, 0))
                throw new InvalidOperationException("The surface's pixels could not be read.");
        }
        finally
        {
            handle.Free();
        }

        // Half floats, premultiplied: straight floats out.
        var halves = MemoryMarshal.Cast<byte, Half>(bytes);
        var result = new float[halves.Length];
        for (var i = 0; i + 3 < result.Length; i += 4)
        {
            var a = (float)halves[i + 3];
            if (a <= 0f)
                continue;

            result[i] = (float)halves[i] / a;
            result[i + 1] = (float)halves[i + 1] / a;
            result[i + 2] = (float)halves[i + 2] / a;
            result[i + 3] = a;
        }

        return result;
    }

    public override void Clear()
    {
        ObjectDisposedException.ThrowIf(_surface is null, this);
        _surface.Canvas.Clear(SKColors.Transparent);
    }

    public override void Dispose()
    {
        if (_surface is null)
            return;

        _canvas!.Dispose();
        _canvas = null;
        _surface.Dispose();
        _surface = null;
        _context.Counts.Surface(-1);
    }
}

public sealed partial class SkiaCanvas
{
    /// <inheritdoc />
    public override OffscreenSurface? CreateOffscreenSurface(int widthPixels, int heightPixels) => CreateOffscreenSurface(widthPixels, heightPixels, wide: false);

    /// <inheritdoc />
    public override OffscreenSurface? CreateOffscreenSurface(int widthPixels, int heightPixels, bool wide)
    {
        Guard();
        if (widthPixels <= 0 || heightPixels <= 0 || (long)widthPixels * heightPixels > _context.MaxRasterPixels / (wide ? 2 : 1))
            return null;

        // A wide surface holds half-float pixels with no color space, so what is painted on it is kept as the numbers it was given, outside 0 to 1 too.
        var surface = SKSurface.Create(new SKImageInfo(widthPixels, heightPixels, wide ? SKColorType.RgbaF16 : SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            return null;

        try
        {
            surface.Canvas.Clear(SKColors.Transparent);

            // One unit is one pixel, whatever scale this canvas draws at. The canvas counts as a tile: disposing it takes that count back.
            _context.Counts.Tile(1);
            var canvas = new SkiaCanvas(_context, surface.Canvas, new Rect(0, 0, widthPixels, heightPixels), 1.0, true, true, Matrix3x2.Identity, null, _smooth);
            return new SkiaOffscreenSurface(_context, surface, canvas, widthPixels, heightPixels, wide);
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }
}

public sealed partial class SkiaCanvas
{
    /// <inheritdoc />
    public override PeachDrawing.Core.Image? CreateWideImage(int widthPixels, int heightPixels, ReadOnlySpan<float> straightRgba)
    {
        Guard();
        if (widthPixels <= 0 || heightPixels <= 0 || (long)widthPixels * heightPixels > _context.MaxRasterPixels / 2 || straightRgba.Length < widthPixels * heightPixels * 4)
            return null;

        // Premultiplied half floats, which Skia keeps as they are.
        var halves = new Half[widthPixels * heightPixels * 4];
        for (var i = 0; i + 3 < halves.Length; i += 4)
        {
            var a = Math.Clamp(float.IsNaN(straightRgba[i + 3]) ? 0f : straightRgba[i + 3], 0f, 1f);
            halves[i] = (Half)((float.IsNaN(straightRgba[i]) ? 0f : straightRgba[i]) * a);
            halves[i + 1] = (Half)((float.IsNaN(straightRgba[i + 1]) ? 0f : straightRgba[i + 1]) * a);
            halves[i + 2] = (Half)((float.IsNaN(straightRgba[i + 2]) ? 0f : straightRgba[i + 2]) * a);
            halves[i + 3] = (Half)a;
        }

        var info = new SKImageInfo(widthPixels, heightPixels, SKColorType.RgbaF16, SKAlphaType.Premul);
        var image = SKImage.FromPixelCopy(info, MemoryMarshal.AsBytes(halves.AsSpan()), info.RowBytes);
        return image is null ? null : SkiaImage.FromSnapshot(image, widthPixels, heightPixels, _context.Counts);
    }
}

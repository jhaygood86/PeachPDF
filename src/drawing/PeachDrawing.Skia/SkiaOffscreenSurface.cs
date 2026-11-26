using PeachDrawing.Core;
using SkiaSharp;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PeachDrawing.Skia;

/// <summary>
/// The Skia backend's <see cref="OffscreenSurface"/>: an <see cref="SKSurface"/> of premultiplied RGBA8 and the <see cref="SkiaCanvas"/> over it. It is a raster
/// surface, drawn on the thread that draws into it: a surface on the graphics hardware may only be drawn on the thread that owns its GL context, and a canvas is
/// drawn into by a script on the thread that runs it. A snapshot is a copy-on-write <see cref="SKImage"/>, and where the target canvas has a
/// <see cref="SkiaRenderContext.GpuContext"/> it is uploaded as a texture when it is drawn (on the drawing thread), once for each snapshot taken.
/// </summary>
internal sealed class SkiaOffscreenSurface : OffscreenSurface
{
    private readonly SkiaRenderContext _context;
    private SKSurface? _surface;
    private SkiaCanvas? _canvas;

    public SkiaOffscreenSurface(SkiaRenderContext context, SKSurface surface, SkiaCanvas canvas, int width, int height)
        : base(width, height)
    {
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
    public override OffscreenSurface? CreateOffscreenSurface(int widthPixels, int heightPixels)
    {
        Guard();
        if (widthPixels <= 0 || heightPixels <= 0 || (long)widthPixels * heightPixels > _context.MaxRasterPixels)
            return null;

        var surface = SKSurface.Create(new SKImageInfo(widthPixels, heightPixels, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            return null;

        try
        {
            surface.Canvas.Clear(SKColors.Transparent);

            // One unit is one pixel, whatever scale this canvas draws at. The canvas counts as a tile: disposing it takes that count back.
            _context.Counts.Tile(1);
            var canvas = new SkiaCanvas(_context, surface.Canvas, new Rect(0, 0, widthPixels, heightPixels), 1.0, true, true, Matrix3x2.Identity, null, _smooth);
            return new SkiaOffscreenSurface(_context, surface, canvas, widthPixels, heightPixels);
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }
}

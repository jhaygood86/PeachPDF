using PeachDrawing.Core;
using SkiaSharp;
using System;

namespace PeachDrawing.Skia;

/// <summary>
/// A <see cref="RetainedLayer"/> kept as a texture: the group is drawn once into a bitmap at the density the host draws at, and its snapshot is kept as
/// an <see cref="SKImage"/> - uploaded to the graphics hardware when the render context has been given a <see cref="SkiaRenderContext.GpuContext"/>. Every
/// <see cref="Draw"/> is a single textured quad under the target's matrix, so a frame that only moves or fades the group costs a composite and not a
/// repaint. A GPU texture belongs to the <see cref="GRContext"/> it was made on; <see cref="CanDraw"/> turns false when the target draws with another
/// (a recreated GL context, say).
/// </summary>
internal sealed class SkiaRetainedLayer : RetainedLayer
{
    private readonly SkiaRenderContext _context;
    private readonly GRContext? _gpu;
    private SKSurface? _surface;
    private SkiaCanvas? _recorder;
    private SKImage? _image;
    private bool _sealed;
    private bool _disposed;

    internal SkiaRetainedLayer(SkiaRenderContext context, GRContext? gpu, Rect bounds, SKSurface surface, SkiaCanvas recorder)
        : base(bounds)
    {
        _context = context;
        _gpu = gpu;
        _surface = surface;
        _recorder = recorder;
        _context.Counts.RetainedLayer(1);
    }

    public override Canvas Recorder => _sealed ? throw new InvalidOperationException("The layer is sealed.") : _recorder!;

    public override bool IsSealed => _sealed;

    public override void Seal()
    {
        if (_sealed || _disposed)
            return;

        _sealed = true;

        // The recording canvas puts the surface's canvas back as it found it; only then is the surface's content final.
        _recorder!.Dispose();
        _recorder = null;

        _image = _surface!.Snapshot();

        // Drawn once into memory, then kept as a texture on the graphics hardware: every draw after this is one textured quad. (Drawing the group into a
        // render target of the hardware instead was measured on an Android emulator to take down the frame it was made in, whatever else was flushed:
        // the group is small and made once, so it is the upload that is kept.)
        if (_gpu is not null && _image.ToTextureImage(_gpu) is { } texture)
        {
            _image.Dispose();
            _image = texture;
        }
        _context.Counts.Surface(1);

        // The snapshot owns the pixels (or the texture) from here: the surface is not needed to draw them.
        _surface.Dispose();
        _surface = null;
    }

    public override bool CanDraw(Canvas target) =>
        !_disposed && _sealed && _image is not null && target is SkiaCanvas canvas && ReferenceEquals(canvas.Context.GpuContext, _gpu);

    public override void Draw(Canvas target, double opacity = 1.0)
    {
        if (!_sealed)
            throw new InvalidOperationException("The layer is not sealed.");

        ObjectDisposedException.ThrowIf(_disposed, this);
        if (target is not SkiaCanvas canvas)
            throw new ArgumentException("A Skia retained layer can only be drawn on a Skia canvas.", nameof(target));

        canvas.DrawRetained(_image!, Bounds, opacity);
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _recorder?.Dispose();
        _recorder = null;
        _surface?.Dispose();
        _surface = null;

        if (_image is not null)
        {
            _image.Dispose();
            _image = null;
            _context.Counts.Surface(-1);
        }

        _context.Counts.RetainedLayer(-1);
    }
}

public sealed partial class SkiaCanvas
{
    // Never more device pixels than this either way: a layer that large costs more as a texture than repainting it.
    private const double MaxRetainedPixels = 8192;

    // And never more in all (64 MB of pixels, held again as a texture): a tall box is cheaper repainted.
    private const long MaxRetainedArea = 16L * 1024 * 1024;

    // The scale of the matrix the host's canvas had when this one was made (a DPI scale, a zoom): the density a retained layer is kept at.
    private double _deviceScale = 1.0;

    /// <inheritdoc />
    public override RetainedLayer? CreateRetainedLayer(Rect bounds)
    {
        Guard();
        if (!(bounds.Width > 0) || !(bounds.Height > 0) || double.IsInfinity(bounds.Width) || double.IsInfinity(bounds.Height))
            return null;

        var density = Math.Clamp(_deviceScale, 0.5, 4.0);
        var width = (int)Math.Ceiling(bounds.Width * density);
        var height = (int)Math.Ceiling(bounds.Height * density);
        if (width > MaxRetainedPixels || height > MaxRetainedPixels || width < 1 || height < 1 || (long)width * height > MaxRetainedArea)
            return null;

        var gpu = _context.GpuContext;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        SKSurface? surface = SKSurface.Create(info);
        if (surface is null)
            return null;

        try
        {
            var host = surface.Canvas;
            host.Clear(SKColors.Transparent);

            // The recording draws in this canvas's coordinates: the surface's origin is the layer's corner, at the layer's density.
            host.Scale((float)(width / bounds.Width), (float)(height / bounds.Height));
            host.Translate((float)-bounds.X, (float)-bounds.Y);

            var recorder = new SkiaCanvas(_context, host, bounds, _ppp, true, true, _ctm, null, _smooth);
            _context.Counts.Tile(1);
            return new SkiaRetainedLayer(_context, gpu, bounds, surface, recorder);
        }
        catch
        {
            surface.Dispose();
            throw;
        }
    }

    /// <summary>Draws a retained layer's image over <paramref name="bounds"/> under the current matrix, at <paramref name="opacity"/>.</summary>
    internal void DrawRetained(SKImage image, Rect bounds, double opacity)
    {
        Guard();
        using var paint = new SKPaint
        {
            IsAntialias = _smooth,
            Color = new SKColor(0, 0, 0, (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255)),
        };

        var drawn = WithBlend(paint, out var owned);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        _canvas.DrawImage(image, SKRect.Create((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height), sampling, drawn);
        owned?.Dispose();
    }
}

using PeachDrawing.Core;

namespace PeachDrawing;

/// <summary>The raster backend's <see cref="OffscreenSurface"/>: a pixel buffer and the <see cref="RasterCanvas"/> over it. It is the portable reference the other backends are held to.</summary>
internal sealed class RasterOffscreenSurface : OffscreenSurface
{
    private readonly RasterRenderContext? _owner;
    private readonly RasterSurface _surface;
    private readonly RasterCanvas _canvas;

    public RasterOffscreenSurface(RenderContext context, int width, int height, bool wide = false)
        : base(width, height)
    {
        _owner = context as RasterRenderContext;
        _surface = new RasterSurface(width, height, 0, 0, 1, 1, wide);
        _canvas = new RasterCanvas(context, _surface, pixelsPerPoint: 1.0);
    }

    public override Canvas Canvas => _canvas;

    public override bool CanDraw(Canvas target) => target is RasterCanvas;

    public override Image CreateSnapshot()
    {
        var copy = new RasterSurface(Width, Height, 0, 0, 1, 1, _surface.IsWide);
        _surface.PixelMemory.Span.CopyTo(copy.PixelMemory.Span);
        if (_surface.IsWide)
            _surface.WidePixels.CopyTo(copy.WidePixels);
        return new RasterImage(copy, Width, Height);
    }

    public override PixelBuffer ReadPixels()
    {
        var live = _canvas.ToPixelBuffer();
        return new PixelBuffer(live.Width, live.Height, live.PremultipliedRgba.ToArray());
    }

    public override void Clear() => _surface.Clear();

    public override bool IsWide => _surface.IsWide;

    public override float[]? ReadWidePixels()
    {
        if (!_surface.IsWide)
            return null;

        // Straight alpha: the premultiplied floats over their alpha.
        var source = _surface.WidePixels;
        var result = new float[source.Length];
        for (var i = 0; i + 3 < source.Length; i += 4)
        {
            var a = source[i + 3];
            if (a <= 0)
                continue;

            result[i] = source[i] / a;
            result[i + 1] = source[i + 1] / a;
            result[i + 2] = source[i + 2] / a;
            result[i + 3] = a;
        }

        return result;
    }

    public override void Dispose()
    {
        _canvas.Dispose();
        _surface.Dispose();
    }
}

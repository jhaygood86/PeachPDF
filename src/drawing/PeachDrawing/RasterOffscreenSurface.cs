using PeachDrawing.Core;

namespace PeachDrawing;

/// <summary>The raster backend's <see cref="OffscreenSurface"/>: a pixel buffer and the <see cref="RasterCanvas"/> over it. It is the portable reference the other backends are held to.</summary>
internal sealed class RasterOffscreenSurface : OffscreenSurface
{
    private readonly RasterRenderContext? _owner;
    private readonly RasterSurface _surface;
    private readonly RasterCanvas _canvas;

    public RasterOffscreenSurface(RenderContext context, int width, int height)
        : base(width, height)
    {
        _owner = context as RasterRenderContext;
        _surface = new RasterSurface(width, height, 0, 0, 1, 1);
        _canvas = new RasterCanvas(context, _surface, pixelsPerPoint: 1.0);
    }

    public override Canvas Canvas => _canvas;

    public override bool CanDraw(Canvas target) => target is RasterCanvas;

    public override Image CreateSnapshot()
    {
        var copy = new RasterSurface(Width, Height, 0, 0, 1, 1);
        _surface.PixelMemory.Span.CopyTo(copy.PixelMemory.Span);
        return new RasterImage(copy, Width, Height);
    }

    public override PixelBuffer ReadPixels()
    {
        var live = _canvas.ToPixelBuffer();
        return new PixelBuffer(live.Width, live.Height, live.PremultipliedRgba.ToArray());
    }

    public override void Clear() => _surface.Clear();

    public override void Dispose()
    {
        _canvas.Dispose();
        _surface.Dispose();
    }
}

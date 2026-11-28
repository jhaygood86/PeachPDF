using CoreGraphics;
using PeachDrawing.Core;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PeachDrawing.CoreGraphics;

/// <summary>
/// The CoreGraphics backend's <see cref="OffscreenSurface"/>: a <see cref="CGBitmapContext"/> of premultiplied RGBA8 and the <see cref="CoreGraphicsCanvas"/> over it,
/// one unit a pixel. CoreGraphics draws into the bitmap as each call is made, so a read needs no flush; a snapshot is the context's <see cref="CGImage"/>, which
/// CoreGraphics keeps separate from later drawing.
/// </summary>
internal sealed class CoreGraphicsOffscreenSurface : OffscreenSurface
{
    private readonly CoreGraphicsRenderContext _context;
    private CoreGraphicsOffscreen? _offscreen;
    private CoreGraphicsCanvas? _canvas;

    public CoreGraphicsOffscreenSurface(CoreGraphicsRenderContext context, CoreGraphicsOffscreen offscreen, CoreGraphicsCanvas canvas, int width, int height)
        : base(width, height)
    {
        _context = context;
        _offscreen = offscreen;
        _canvas = canvas;
    }

    public override Canvas Canvas => _canvas ?? throw new ObjectDisposedException(nameof(CoreGraphicsOffscreenSurface));

    public override bool CanDraw(Canvas target) => target is CoreGraphicsCanvas cg && ReferenceEquals(cg.Context, _context);

    public override PeachDrawing.Core.Image CreateSnapshot()
    {
        ObjectDisposedException.ThrowIf(_offscreen is null, this);
        var image = _offscreen.Bitmap.ToImage() ?? throw new InvalidOperationException("The bitmap could not be read back.");
        return CoreGraphicsImage.FromCgImage(image, Width, Height, _context.Counts);
    }

    public override PixelBuffer ReadPixels()
    {
        ObjectDisposedException.ThrowIf(_offscreen is null, this);
        var bitmap = _offscreen.Bitmap;
        var stride = (int)bitmap.BytesPerRow;
        var rgba = new byte[Width * Height * 4];
        for (var row = 0; row < Height; row++)
            Marshal.Copy(bitmap.Data + row * stride, rgba, row * Width * 4, Width * 4);

        return new PixelBuffer(Width, Height, rgba);
    }

    public override void Clear()
    {
        ObjectDisposedException.ThrowIf(_offscreen is null, this);

        // The bitmap's own pixels, whatever clip, transform or blend the canvas has in force.
        _offscreen.Bitmap.ClearRect(new CGRect(-1e6, -1e6, 2e6, 2e6));
    }

    public override void Dispose()
    {
        if (_offscreen is null)
            return;

        _canvas!.Dispose();
        _canvas = null;
        _offscreen.Dispose();
        _offscreen = null;
    }
}

public sealed partial class CoreGraphicsCanvas
{
    /// <inheritdoc />
    public override OffscreenSurface? CreateOffscreenSurface(int widthPixels, int heightPixels)
    {
        Guard();
        if (widthPixels <= 0 || heightPixels <= 0 || widthPixels > 16384 || heightPixels > 16384 || (long)widthPixels * heightPixels > _context.MaxRasterPixels)
            return null;

        // One unit is one pixel: the region is the bitmap's own size at a pixel a unit.
        var region = new Rect(0, 0, widthPixels, heightPixels);
        var offscreen = new CoreGraphicsOffscreen(region, 1.0, _context.MaxRasterPixels, _context.Counts);
        try
        {
            _context.Counts.Tile(1);
            var canvas = new CoreGraphicsCanvas(_context, offscreen.Bitmap, region, 1.0, true, true, Matrix3x2.Identity, null, _smooth, true);
            return new CoreGraphicsOffscreenSurface(_context, offscreen, canvas, widthPixels, heightPixels);
        }
        catch
        {
            offscreen.Dispose();
            throw;
        }
    }
}

using CoreGraphics;
using PeachDrawing.Core;
using System;

namespace PeachDrawing.CoreGraphics;

/// <summary>
/// A bitmap context a canvas draws a layer, a tile or a mask into, with the canvas that draws on it. The canvas works in the same layout units
/// as the one that made it: <see cref="Region"/> of those units is what the bitmap covers, at <see cref="PixelsPerUnitX"/> by
/// <see cref="PixelsPerUnitY"/> pixels per unit.
/// </summary>
internal sealed class CoreGraphicsOffscreen : IDisposable
{
    private readonly CoreGraphicsResourceCounts _counts;
    private CGBitmapContext? _bitmap;
    private CGImage? _image;

    public CoreGraphicsOffscreen(Rect region, double pixelsPerUnit, long maxPixels, CoreGraphicsResourceCounts counts)
    {
        _counts = counts;
        Region = region;

        // Whole pixels, and no more of them than the context allows; a larger region is drawn at a lower resolution.
        var width = Math.Max(1.0, Math.Ceiling(region.Width * pixelsPerUnit));
        var height = Math.Max(1.0, Math.Ceiling(region.Height * pixelsPerUnit));
        var cap = Math.Sqrt(maxPixels / Math.Max(1.0, width * height));
        if (cap < 1)
        {
            width = Math.Max(1.0, Math.Floor(width * cap));
            height = Math.Max(1.0, Math.Floor(height * cap));
        }

        PixelWidth = (int)Math.Min(width, 16384);
        PixelHeight = (int)Math.Min(height, 16384);
        PixelsPerUnitX = PixelWidth / Math.Max(region.Width, 1e-9);
        PixelsPerUnitY = PixelHeight / Math.Max(region.Height, 1e-9);

        _bitmap = new CGBitmapContext(IntPtr.Zero, PixelWidth, PixelHeight, 8, 0, CoreGraphicsCanvas.Rgb, CGImageAlphaInfo.PremultipliedLast)
            ?? throw new InvalidOperationException("A bitmap context of that size could not be made.");
        counts.Surface(1);

        // Layout units to pixels: the region's corner at the bitmap's origin, scaled. The canvas on top flips y about the region.
        _bitmap.ScaleCTM((nfloat)PixelsPerUnitX, (nfloat)PixelsPerUnitY);
        _bitmap.TranslateCTM((nfloat)(-region.X), (nfloat)(-region.Y));
    }

    public Rect Region { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public double PixelsPerUnitX { get; }

    public double PixelsPerUnitY { get; }

    public CGBitmapContext Bitmap => _bitmap ?? throw new ObjectDisposedException(nameof(CoreGraphicsOffscreen));

    /// <summary>The drawing so far as an image (a snapshot, so drawing may go on). Owned by this object.</summary>
    public CGImage Snapshot()
    {
        _image?.Dispose();
        _image = Bitmap.ToImage() ?? throw new InvalidOperationException("The bitmap could not be read back.");
        return _image;
    }

    public void Dispose()
    {
        if (_bitmap is null)
            return;

        _image?.Dispose();
        _image = null;
        _bitmap.Dispose();
        _bitmap = null;
        _counts.Surface(-1);
    }
}

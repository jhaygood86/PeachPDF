using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System;

namespace PeachPDF.Raster;

/// <summary>
/// A raster surface handed out by <see cref="RGraphics.BeginRasterSurface"/>: paint into
/// <see cref="Graphics"/>, post-process <see cref="Surface"/> if needed, then give the surface back to the graphics
/// that produced it with <see cref="RGraphics.DrawRaster"/>. Disposing releases the pixel buffer.
/// </summary>
internal sealed class RasterSurfaceScope : IDisposable
{
    public RasterSurfaceScope(RasterGraphics graphics, RasterSurface surface)
    {
        Graphics = graphics;
        Surface = surface;
    }

    /// <summary>A graphics whose coordinate system is the requesting graphics' own, so paint code needs no translation.</summary>
    public RasterGraphics Graphics { get; }

    public RasterSurface Surface { get; }

    public void Dispose()
    {
        Graphics.Dispose();
        Surface.Dispose();
    }
}

/// <summary>Creates raster surfaces on the shared pixel grid, at an exact physical resolution.</summary>
internal static class RasterSurfaceFactory
{
    /// <summary>The largest side, in pixels, of any raster surface.</summary>
    internal const int MaxDimension = 16384;

    /// <summary>
    /// Creates a surface covering <paramref name="layoutBounds"/> (layout units) at <paramref name="dpi"/> pixels
    /// per inch of paper. Layout units are <c>1/72 / pixelsPerPoint</c> inch each, so one layout unit is backed by
    /// <c>dpi / 72 / pixelsPerPoint</c> pixels regardless of how the page was scaled.
    /// </summary>
    /// <remarks>
    /// The bounds are snapped <em>outwards</em> to whole pixels on a grid anchored at the local origin, and the
    /// surface records the snapped rectangle (<see cref="RasterSurface.LayoutRect"/>): that is what a caller must
    /// place the bitmap at, so every pixel is exactly <c>1/dpi</c> inch and neighbouring surfaces abut with no
    /// seam. If the area or a side would exceed the limits, the resolution is lowered just enough to fit; the
    /// placed size is never changed.
    /// </remarks>
    /// <returns>null when the bounds are empty, non-finite or the surface could not be allocated.</returns>
    public static RasterSurfaceScope? Create(RAdapter adapter, double pixelsPerPoint, RRect layoutBounds, double dpi, long maxPixels,
        (double X, double Y) transformScale = default)
    {
        if (transformScale.X <= 0 || transformScale.Y <= 0)
            transformScale = (1.0, 1.0);

        if (!(layoutBounds.Width > 0) || !(layoutBounds.Height > 0) ||
            double.IsNaN(layoutBounds.X + layoutBounds.Y + layoutBounds.Width + layoutBounds.Height) ||
            double.IsInfinity(layoutBounds.X + layoutBounds.Y + layoutBounds.Width + layoutBounds.Height) ||
            !(dpi > 0) || !(pixelsPerPoint > 0))
        {
            return null;
        }

        // Pixels per layout unit after the pushed transforms magnify it: a bitmap that is going to be drawn three times
        // larger needs three times the pixels to still be `dpi` pixels per inch of paper.
        var ppuX = dpi / 72.0 / pixelsPerPoint * transformScale.X;
        var ppuY = dpi / 72.0 / pixelsPerPoint * transformScale.Y;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var gx0 = (long)Math.Floor(layoutBounds.Left * ppuX + 1e-9);
            var gy0 = (long)Math.Floor(layoutBounds.Top * ppuY + 1e-9);
            var gx1 = (long)Math.Ceiling(layoutBounds.Right * ppuX - 1e-9);
            var gy1 = (long)Math.Ceiling(layoutBounds.Bottom * ppuY - 1e-9);
            var w = gx1 - gx0;
            var h = gy1 - gy0;
            if (w <= 0 || h <= 0 || Math.Abs(gx0) > int.MaxValue / 2 || Math.Abs(gy0) > int.MaxValue / 2)
                return null;

            if (w * h <= maxPixels && w <= MaxDimension && h <= MaxDimension)
            {
                var surface = new RasterSurface((int)w, (int)h, (int)gx0, (int)gy0, ppuX, ppuY);
                return new RasterSurfaceScope(new RasterGraphics(adapter, surface, pixelsPerPoint), surface);
            }

            var factor = Math.Min(Math.Sqrt((double)maxPixels / (w * h)), (double)MaxDimension / Math.Max(w, h));
            ppuX *= Math.Min(factor, 1.0) * 0.999;
            ppuY *= Math.Min(factor, 1.0) * 0.999;
        }

        return null;
    }
}

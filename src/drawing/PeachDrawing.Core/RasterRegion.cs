using System;

namespace PeachDrawing.Core;

/// <summary>
/// A pixel surface handed out by <see cref="Canvas.BeginRasterSurface"/>: paint into <see cref="Graphics"/>,
/// post-process <see cref="Surface"/> if needed, then give the surface back to the canvas that produced it with
/// <see cref="Canvas.DrawRaster"/>. Disposing releases the graphics and the pixel buffer.
/// </summary>
public sealed class RasterRegion : IDisposable
{
    /// <summary>Wraps a raster <paramref name="graphics"/> that paints into <paramref name="surface"/>.</summary>
    /// <param name="graphics">a canvas whose coordinate system is the requesting canvas's own, so paint code needs no translation</param>
    /// <param name="surface">the pixels <paramref name="graphics"/> paints into</param>
    public RasterRegion(Canvas graphics, RasterSurface surface)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(surface);
        Graphics = graphics;
        Surface = surface;
    }

    /// <summary>A canvas whose coordinate system is the requesting canvas's own, so paint code needs no translation.</summary>
    public Canvas Graphics { get; }

    /// <summary>The pixels <see cref="Graphics"/> paints into.</summary>
    public RasterSurface Surface { get; }

    /// <summary>Disposes <see cref="Graphics"/> and releases <see cref="Surface"/>'s pixel buffer.</summary>
    public void Dispose()
    {
        Graphics.Dispose();
        Surface.Dispose();
    }
}

using PeachPDF.Html.Adapters.Entities;
using System;
using System.Buffers;

namespace PeachPDF.Raster;

/// <summary>
/// A premultiplied RGBA8 pixel buffer together with where it sits in the coordinate space of the graphics
/// that created it. Pixel (0, 0) is the top-left; row-major, four bytes per pixel in R, G, B, A order.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GridX"/>/<see cref="GridY"/> place the surface on a pixel grid whose pitch is
/// <see cref="PixelsPerUnitX"/> pixels per layout unit and whose origin is the local (page) origin. Keeping
/// every surface on that one grid is what makes the physical size of the embedded bitmap exact (each pixel
/// is exactly <c>1/dpi</c> inch) and lets neighbouring surfaces abut without a seam.
/// </para>
/// </remarks>
internal sealed class RasterSurface : IDisposable
{
    private byte[]? _buffer;

    public RasterSurface(int width, int height, int gridX, int gridY, double pixelsPerUnitX, double pixelsPerUnitY)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A raster surface needs a positive size.");

        Width = width;
        Height = height;
        GridX = gridX;
        GridY = gridY;
        PixelsPerUnitX = pixelsPerUnitX;
        PixelsPerUnitY = pixelsPerUnitY;

        var length = checked(width * height * 4);
        _buffer = ArrayPool<byte>.Shared.Rent(length);
        Array.Clear(_buffer, 0, length);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The surface's left edge, in whole pixels of the grid.</summary>
    public int GridX { get; }

    /// <summary>The surface's top edge, in whole pixels of the grid.</summary>
    public int GridY { get; }

    /// <summary>Pixels per layout unit, horizontally. Equal to <see cref="PixelsPerUnitY"/> except for a tile, whose pitch is
    /// stretched by less than a pixel across its whole width so it covers its requested size exactly.</summary>
    public double PixelsPerUnitX { get; }

    /// <summary>Pixels per layout unit, vertically.</summary>
    public double PixelsPerUnitY { get; }

    public int Stride => Width * 4;

    public Span<byte> Pixels => _buffer is null
        ? throw new ObjectDisposedException(nameof(RasterSurface))
        : _buffer.AsSpan(0, Width * Height * 4);

    /// <summary>The underlying (possibly larger than needed) rented array; only the first <c>Width * Height * 4</c> bytes are pixels.</summary>
    internal byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(RasterSurface));

    public Span<byte> Row(int y) => Pixels.Slice(y * Stride, Stride);

    /// <summary>The surface's rectangle in layout units (the rectangle a bitmap of it must be placed at).</summary>
    public RRect LayoutRect => new(GridX / PixelsPerUnitX, GridY / PixelsPerUnitY, Width / PixelsPerUnitX, Height / PixelsPerUnitY);

    public IntRect Bounds => new(0, 0, Width, Height);

    public void Clear() => Pixels.Clear();

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = null;
        if (buffer is not null)
            ArrayPool<byte>.Shared.Return(buffer);
    }
}

using System;
using System.Buffers;

namespace PeachPDF.Raster;

/// <summary>
/// One depth value per pixel of a <see cref="RasterSurface"/> of the same size, for composing the planes of a 3D rendering context: larger is
/// nearer the viewer, and a fresh buffer holds negative infinity (nothing drawn). Rented from the shared array pool, so a context costs one
/// object and no garbage.
/// </summary>
internal sealed class DepthBuffer : IDisposable
{
    private float[]? _buffer;

    public DepthBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A depth buffer needs a positive size.");

        Width = width;
        Height = height;
        _buffer = ArrayPool<float>.Shared.Rent(checked(width * height));
        _buffer.AsSpan(0, width * height).Fill(float.NegativeInfinity);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The depths of row <paramref name="y"/>.</summary>
    public Span<float> Row(int y) => _buffer is null
        ? throw new ObjectDisposedException(nameof(DepthBuffer))
        : _buffer.AsSpan(y * Width, Width);

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = null;
        if (buffer is not null)
            ArrayPool<float>.Shared.Return(buffer);
    }
}

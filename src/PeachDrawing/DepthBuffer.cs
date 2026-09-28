using PeachDrawing.Core;
using System;
using System.Buffers;

namespace PeachDrawing;

/// <summary>
/// One depth value per pixel of a <see cref="RasterSurface"/> of the same size, for composing the planes of a 3D rendering context: larger is
/// nearer the viewer, and a fresh buffer holds negative infinity (nothing drawn). Rented from the shared array pool, so a context costs one
/// object and no garbage.
/// </summary>
public sealed class DepthBuffer : IDisposable
{
    private float[]? _buffer;

    /// <summary>Creates a buffer of <paramref name="width"/> x <paramref name="height"/> depths, all negative infinity (nothing drawn).</summary>
    /// <param name="width">the width, in pixels, of the surface it pairs with</param>
    /// <param name="height">the height, in pixels, of the surface it pairs with</param>
    public DepthBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A depth buffer needs a positive size.");

        Width = width;
        Height = height;
        _buffer = ArrayPool<float>.Shared.Rent(checked(width * height));
        _buffer.AsSpan(0, width * height).Fill(float.NegativeInfinity);
    }

    /// <summary>The buffer's width, in pixels.</summary>
    public int Width { get; }

    /// <summary>The buffer's height, in pixels.</summary>
    public int Height { get; }

    /// <summary>The depths of row <paramref name="y"/>.</summary>
    public Span<float> Row(int y) => _buffer is null
        ? throw new ObjectDisposedException(nameof(DepthBuffer))
        : _buffer.AsSpan(y * Width, Width);

    /// <summary>Returns the buffer to the shared pool.</summary>
    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = null;
        if (buffer is not null)
            ArrayPool<float>.Shared.Return(buffer);
    }
}

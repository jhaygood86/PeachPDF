using System;

namespace PeachPDF.Raster;

/// <summary>
/// One entry of the raster clip stack: an integer pixel rectangle plus, when the clip is not exactly that
/// rectangle, an 8-bit coverage mask covering <see cref="Bounds"/> (anti-aliased edges, arbitrary shapes).
/// </summary>
internal sealed class ClipState
{
    public ClipState(IntRect bounds, byte[]? mask)
    {
        Bounds = bounds;
        Mask = mask;
    }

    public IntRect Bounds { get; }

    /// <summary><c>Bounds.Width * Bounds.Height</c> coverage bytes, or null for a plain rectangle.</summary>
    public byte[]? Mask { get; }

    /// <summary>Intersects this clip with the region <paramref name="polygons"/> covers under <paramref name="evenOdd"/>.</summary>
    public ClipState Intersect(PolygonSet polygons, bool evenOdd)
    {
        var polyBounds = polygons.GetBounds();
        if (polyBounds is not { } b || Bounds.IsEmpty)
            return new ClipState(IntRect.Empty, null);

        var candidate = new IntRect(
            (int)Math.Floor(Math.Clamp(b.MinX, -1e9, 1e9)), (int)Math.Floor(Math.Clamp(b.MinY, -1e9, 1e9)),
            (int)Math.Ceiling(Math.Clamp(b.MaxX, -1e9, 1e9)), (int)Math.Ceiling(Math.Clamp(b.MaxY, -1e9, 1e9)));
        var bounds = Bounds.Intersect(candidate);
        if (bounds.IsEmpty)
            return new ClipState(IntRect.Empty, null);

        var mask = new byte[bounds.Width * bounds.Height];
        var sink = new MaskSink(mask, bounds);
        ScanlineRasterizer.Fill(polygons, evenOdd, bounds, ref sink);

        if (Mask is not null)
        {
            for (var y = bounds.Top; y < bounds.Bottom; y++)
            {
                var row = mask.AsSpan((y - bounds.Top) * bounds.Width, bounds.Width);
                var old = Mask.AsSpan((y - Bounds.Top) * Bounds.Width + (bounds.Left - Bounds.Left), bounds.Width);
                PixelKernels.MultiplyCoverage(row, old);
            }
        }

        return new ClipState(bounds, mask);
    }

    /// <summary>Intersects with an axis-aligned pixel rectangle (no mask needed).</summary>
    public ClipState Intersect(IntRect rect)
    {
        var bounds = Bounds.Intersect(rect);
        if (bounds.IsEmpty)
            return new ClipState(IntRect.Empty, null);

        if (Mask is null)
            return new ClipState(bounds, null);

        // Crop the existing mask to the smaller rectangle.
        var mask = new byte[bounds.Width * bounds.Height];
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            Mask.AsSpan((y - Bounds.Top) * Bounds.Width + (bounds.Left - Bounds.Left), bounds.Width)
                .CopyTo(mask.AsSpan((y - bounds.Top) * bounds.Width, bounds.Width));
        }

        return new ClipState(bounds, mask);
    }

    private struct MaskSink(byte[] mask, IntRect bounds) : ICoverageSink
    {
        public void Span(int y, int x0, ReadOnlySpan<byte> coverage)
        {
            coverage.CopyTo(mask.AsSpan((y - bounds.Top) * bounds.Width + (x0 - bounds.Left), coverage.Length));
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachPDF.Raster.Filters;

internal static partial class FilterOps
{
    /// <summary>
    /// Erode/dilate with sixteen bytes at a time. Where the whole window lies inside the surface the per-channel minimum (or maximum) over
    /// the window is a running <c>Vector128.Min</c>/<c>Max</c> of shifted loads; the border, where the window meets the edge (and what is
    /// outside counts as transparent), is done by the scalar reference so the two agree byte for byte.
    /// </summary>
    internal static void MorphologyVector(RasterSurface source, RasterSurface destination, int radiusX, int radiusY, bool dilate)
    {
        var w = source.Width;
        var h = source.Height;
        var stride = w * 4;
        var temp = new byte[w * h * 4];
        var ps = source.Pixels;
        var pd = destination.Pixels;

        // Horizontal: output byte i is the extreme of the bytes i - 4r .. i + 4r that share its channel (a stride of 4).
        for (var y = 0; y < h; y++)
        {
            var row = y * stride;
            // Pixels x in [r, w - 1 - r] have their whole window inside the row; a row narrower than the window has none.
            var hasInterior = w > 2 * radiusX;
            var interiorStart = hasInterior ? radiusX * 4 : 0;
            var interiorEnd = hasInterior ? (w - radiusX) * 4 : 0;

            for (var x = 0; x < w; x++)
            {
                if (x * 4 >= interiorStart && x * 4 < interiorEnd)
                    continue;

                for (var c = 0; c < 4; c++)
                    temp[row + x * 4 + c] = Extreme(dilate, x - radiusX, x + radiusX, w, ps, row + c, 4);
            }

            HorizontalInterior(ps.Slice(row, stride), temp.AsSpan(row, stride), interiorStart, interiorEnd, radiusX * 4, dilate);
        }

        // Vertical: an output row is the extreme of the rows y - r .. y + r, taken sixteen columns at a time.
        for (var y = 0; y < h; y++)
        {
            var out0 = y * stride;
            if (y < radiusY || y >= h - radiusY)
            {
                for (var x = 0; x < stride; x++)
                    pd[out0 + x] = Extreme(dilate, y - radiusY, y + radiusY, h, temp, x, stride);

                continue;
            }

            ref var tempRef = ref MemoryMarshal.GetArrayDataReference(temp);
            ref var outRef = ref MemoryMarshal.GetReference(pd);
            var i = 0;
            for (; i + 16 <= stride; i += 16)
            {
                var acc = Vector128.LoadUnsafe(ref tempRef, (nuint)((y - radiusY) * stride + i));
                for (var k = -radiusY + 1; k <= radiusY; k++)
                {
                    var next = Vector128.LoadUnsafe(ref tempRef, (nuint)((y + k) * stride + i));
                    acc = dilate ? Vector128.Max(acc, next) : Vector128.Min(acc, next);
                }

                acc.StoreUnsafe(ref outRef, (nuint)(out0 + i));
            }

            for (; i < stride; i++)
                pd[out0 + i] = Extreme(dilate, y - radiusY, y + radiusY, h, temp, i, stride);
        }
    }

    /// <summary>Fills the interior bytes [<paramref name="start"/>, <paramref name="end"/>) of one row, where every window is fully inside it.</summary>
    private static void HorizontalInterior(ReadOnlySpan<byte> row, Span<byte> output, int start, int end, int reach, bool dilate)
    {
        ref var rowRef = ref MemoryMarshal.GetReference(row);
        ref var outRef = ref MemoryMarshal.GetReference(output);

        var i = start;
        for (; i + 16 <= end; i += 16)
        {
            var acc = Vector128.LoadUnsafe(ref rowRef, (nuint)(i - reach));
            for (var k = -reach + 4; k <= reach; k += 4)
            {
                var next = Vector128.LoadUnsafe(ref rowRef, (nuint)(i + k));
                acc = dilate ? Vector128.Max(acc, next) : Vector128.Min(acc, next);
            }

            acc.StoreUnsafe(ref outRef, (nuint)i);
        }

        // The last few interior bytes (fewer than sixteen).
        for (; i < end; i++)
        {
            var best = row[i - reach];
            for (var k = -reach + 4; k <= reach; k += 4)
            {
                var v = row[i + k];
                best = dilate ? Math.Max(best, v) : Math.Min(best, v);
            }

            output[i] = best;
        }
    }
    /// <summary>
    /// Clears the colour of every pixel and keeps its alpha - a premultiplied pixel of alpha <c>a</c> and no colour is opaque black at
    /// that coverage, which is what <c>SourceAlpha</c> and <c>BackgroundAlpha</c> are.
    /// </summary>
    public static void ZeroColor(RasterSurface surface)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(surface.Pixels);
        var done = 0;

        if (Vector128.IsHardwareAccelerated)
            done = ZeroColorVector128(pixels);

        ZeroColorScalar(pixels, done);
    }

    /// <summary>The per-pixel reference for <see cref="ZeroColor"/>, starting at pixel <paramref name="start"/>.</summary>
    internal static void ZeroColorScalar(Span<uint> pixels, int start = 0)
    {
        for (var i = start; i < pixels.Length; i++)
            pixels[i] &= 0xFF000000u;
    }

    /// <summary>Four pixels at a time; returns how many pixels it handled (the caller finishes the tail).</summary>
    internal static int ZeroColorVector128(Span<uint> pixels)
    {
        var count = pixels.Length / 4 * 4;
        ref var first = ref MemoryMarshal.GetReference(pixels);
        var alphaOnly = Vector128.Create(0xFF000000u);

        for (var i = 0; i < count; i += 4)
            (Vector128.LoadUnsafe(ref first, (nuint)i) & alphaOnly).StoreUnsafe(ref first, (nuint)i);

        return count;
    }
}

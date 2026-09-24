using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachPDF.Raster.Filters;

/// <summary>
/// The vector form of the box blur. The four channels of a pixel are the four lanes of a <see cref="Vector128{T}"/> of 32-bit sums, so
/// the running-sum window slides with one add and one subtract per pixel instead of eight scalar ones, and a whole row of columns
/// slides together in the vertical pass.
/// </summary>
/// <remarks>
/// The division by the window size is a correctly rounded IEEE single-precision division followed by truncation. It is exact here:
/// a sum is below 2^20 (so exact in a float), and a true quotient that is not an integer is at least 1/4095 away from the nearest
/// one, far more than the 2^-16 spacing of floats near 255, so the rounded quotient truncates to the same integer the scalar form's
/// integer reciprocal gives. No fused operations, no reciprocal estimates: the bytes are the scalar bytes.
/// </remarks>
internal static partial class GaussianBlur
{
    internal static void BoxVector(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height, int size, int before, bool horizontal)
    {
        var half = size / 2;
        var divisor = Vector128.Create((float)size);
        var halfVector = Vector128.Create(half);

        if (horizontal)
        {
            ref var srcRef = ref MemoryMarshal.GetReference(src);
            ref var dstRef = ref MemoryMarshal.GetReference(dst);
            for (var y = 0; y < height; y++)
            {
                var row = y * width * 4;
                var sums = Vector128<int>.Zero;

                var last = size - 1 - before;
                for (var k = 0; k <= last && k < width; k++)
                    sums += WidenPixel(ref srcRef, row + k * 4);

                for (var x = 0; x < width; x++)
                {
                    StorePixel(ref dstRef, row + x * 4, Divide(sums + halfVector, divisor));

                    var add = x + 1 + (size - 1 - before);
                    var remove = x - before;
                    if (add < width)
                        sums += WidenPixel(ref srcRef, row + add * 4);

                    if (remove >= 0)
                        sums -= WidenPixel(ref srcRef, row + remove * 4);
                }
            }

            return;
        }

        var stride = width * 4;
        var column = ArrayPool<int>.Shared.Rent(stride);
        try
        {
            ref var srcRef = ref MemoryMarshal.GetReference(src);
            ref var dstRef = ref MemoryMarshal.GetReference(dst);
            ref var colRef = ref MemoryMarshal.GetArrayDataReference(column);
            Array.Clear(column, 0, stride);

            var lastRow = size - 1 - before;
            for (var k = 0; k <= lastRow && k < height; k++)
                AddRow(ref colRef, ref srcRef, k * stride, stride, subtract: false);

            for (var y = 0; y < height; y++)
            {
                var outRow = y * stride;
                for (var i = 0; i < stride; i += 4)
                    StorePixel(ref dstRef, outRow + i, Divide(Vector128.LoadUnsafe(ref colRef, (nuint)i) + halfVector, divisor));

                var add = y + 1 + (size - 1 - before);
                var remove = y - before;
                if (add < height)
                    AddRow(ref colRef, ref srcRef, add * stride, stride, subtract: false);

                if (remove >= 0)
                    AddRow(ref colRef, ref srcRef, remove * stride, stride, subtract: true);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(column);
        }
    }

    /// <summary>Adds (or subtracts) one row of bytes into the per-column sums, four channels at a time.</summary>
    private static void AddRow(ref int column, ref byte src, int rowStart, int stride, bool subtract)
    {
        for (var i = 0; i < stride; i += 4)
        {
            var current = Vector128.LoadUnsafe(ref column, (nuint)i);
            var pixel = WidenPixel(ref src, rowStart + i);
            (subtract ? current - pixel : current + pixel).StoreUnsafe(ref column, (nuint)i);
        }
    }

    /// <summary>The four bytes at <paramref name="index"/> as four 32-bit lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> WidenPixel(ref byte data, int index)
    {
        var bytes = Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref data, index))).AsByte();
        return Vector128.WidenLower(Vector128.WidenLower(bytes)).AsInt32();
    }

    /// <summary><c>min(255, floor(sum / size))</c> per lane (sums already include the rounding half).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Divide(Vector128<int> sums, Vector128<float> size) =>
        Vector128.Min(Vector128.ConvertToInt32(Vector128.ConvertToSingle(sums) / size), Vector128.Create(255));

    /// <summary>Stores four lanes (each 0-255) as four bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StorePixel(ref byte data, int index, Vector128<int> lanes)
    {
        var narrowed = Vector128.Narrow(Vector128.Narrow(lanes.AsUInt32(), lanes.AsUInt32()), Vector128.Narrow(lanes.AsUInt32(), lanes.AsUInt32()));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref data, index), narrowed.AsUInt32().ToScalar());
    }

    /// <summary>
    /// The exact-kernel convolution with the four channels of a pixel as the lanes of a 32-bit accumulator. A weighted sum is at most
    /// 255 * 65536, inside an int, and pixels outside the surface contribute nothing, as in the scalar form.
    /// </summary>
    internal static void KernelVector(Span<byte> pixels, Span<byte> scratch, int width, int height, int radius, int[] weights, bool horizontal)
    {
        var stride = width * 4;
        ref var pixelRef = ref MemoryMarshal.GetReference(pixels);
        ref var outRef = ref MemoryMarshal.GetReference(scratch);
        var rounding = Vector128.Create(32768);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // The taps that land inside the surface: index i of the kernel reads offset i - radius along the blur axis.
                var position = horizontal ? x : y;
                var length = horizontal ? width : height;
                var first = Math.Max(0, radius - position);
                var last = Math.Min(2 * radius, length - 1 - position + radius);

                var sum = rounding;
                for (var i = first; i <= last; i++)
                {
                    var offset = i - radius;
                    var index = horizontal ? y * stride + (x + offset) * 4 : (y + offset) * stride + x * 4;
                    sum += WidenPixel(ref pixelRef, index) * Vector128.Create(weights[i]);
                }

                StorePixel(ref outRef, y * stride + x * 4, Vector128.Min(Vector128.ShiftRightArithmetic(sum, 16), Vector128.Create(255)));
            }
        }

        scratch.CopyTo(pixels);
    }
}

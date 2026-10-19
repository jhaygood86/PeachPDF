using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Byte-image kernels for painting through a coverage mask (text in a bitmap-only font): pulling the alpha plane out of a premultiplied
    /// RGBA surface, scaling a surface by a per-pixel coverage, and growing/shrinking a coverage map. Vectorized where the hardware allows, with
    /// a scalar path producing the same bytes.
    /// </summary>
    internal static class SvgRasterKernels
    {
        private static readonly Vector128<byte> ExpandCoverage = Vector128.Create((byte)0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3);

        /// <summary>
        /// Copies the alpha byte of each RGBA pixel of <paramref name="rgba"/> into <paramref name="alpha"/> (one byte per pixel). Plain scalar: a
        /// shuffle-based version measured no faster (the loop is memory-bound and the JIT already unrolls it).
        /// </summary>
        public static void ExtractAlpha(ReadOnlySpan<byte> rgba, Span<byte> alpha)
        {
            var count = Math.Min(alpha.Length, rgba.Length / 4);
            for (var i = 0; i < count; i++)
                alpha[i] = rgba[i * 4 + 3];
        }

        /// <summary>
        /// Multiplies every channel of each premultiplied RGBA pixel of <paramref name="rgba"/> by the matching byte of <paramref name="coverage"/>
        /// (0..255 as 0..1), rounding to nearest.
        /// </summary>
        public static void ScaleByCoverage(Span<byte> rgba, ReadOnlySpan<byte> coverage)
        {
            var count = Math.Min(coverage.Length, rgba.Length / 4);
            var i = 0;

            if (Vector128.IsHardwareAccelerated)
            {
                var rounding = Vector128.Create((ushort)128);
                for (; i + 4 <= count; i += 4)
                {
                    var four = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetReference(coverage), i));
                    if (four == uint.MaxValue)
                        continue;

                    var block = rgba.Slice(i * 4, 16);
                    if (four == 0)
                    {
                        block.Clear();
                        continue;
                    }

                    var pixels = Vector128.Create<byte>(block);
                    var scale = Vector128.Shuffle(Vector128.CreateScalar(four).AsByte(), ExpandCoverage);
                    var low = Vector128.WidenLower(pixels) * Vector128.WidenLower(scale) + rounding;
                    var high = Vector128.WidenUpper(pixels) * Vector128.WidenUpper(scale) + rounding;

                    // round(x * a / 255) as (v + (v >> 8)) >> 8 with v = x * a + 128.
                    low = Vector128.ShiftRightLogical(low + Vector128.ShiftRightLogical(low, 8), 8);
                    high = Vector128.ShiftRightLogical(high + Vector128.ShiftRightLogical(high, 8), 8);
                    Vector128.Narrow(low, high).CopyTo(block);
                }
            }

            for (; i < count; i++)
            {
                int a = coverage[i];
                if (a == 255)
                    continue;

                for (var c = 0; c < 4; c++)
                {
                    var v = rgba[i * 4 + c] * a + 128;
                    rgba[i * 4 + c] = (byte)((v + (v >> 8)) >> 8);
                }
            }
        }

        /// <summary><paramref name="grown"/> minus <paramref name="shrunk"/>, byte by byte, never below zero: the band between two versions of a coverage map.</summary>
        public static byte[] Difference(byte[] grown, byte[] shrunk)
        {
            var result = new byte[grown.Length];
            var i = 0;

            if (Vector.IsHardwareAccelerated)
            {
                var width = Vector<byte>.Count;
                for (; i + width <= result.Length; i += width)
                {
                    var s = new Vector<byte>(shrunk, i);
                    // max(g, s) - s is g - s clamped at zero.
                    (Vector.Max(new Vector<byte>(grown, i), s) - s).CopyTo(result, i);
                }
            }

            for (; i < result.Length; i++)
                result[i] = (byte)Math.Max(0, grown[i] - shrunk[i]);

            return result;
        }

        /// <summary>
        /// Grows (<paramref name="grow"/>, a maximum filter) or shrinks (a minimum filter) an 8-bit coverage map by <paramref name="radius"/> pixels
        /// with a square window. The window is separable, and each pass is van Herk/Gil-Werman, so the cost per pixel does not depend on the radius;
        /// a pass runs a row at a time, so it is vectorized across the row, and the horizontal pass is the vertical one on the transposed map.
        /// </summary>
        public static byte[] Morphology(byte[] source, int width, int height, int radius, bool grow)
        {
            if (radius <= 0 || width == 0 || height == 0)
                return (byte[])source.Clone();

            var vertical = VerticalPass(source, width, height, radius, grow);
            var transposed = Transpose(vertical, width, height);
            var across = VerticalPass(transposed, height, width, radius, grow);
            return Transpose(across, height, width);
        }

        private static byte[] VerticalPass(byte[] source, int width, int height, int radius, bool grow)
        {
            var window = 2 * radius + 1;
            var padded = height + 2 * radius;
            var identity = new byte[width];
            if (!grow)
                Array.Fill(identity, (byte)255);

            ReadOnlySpan<byte> RowAt(int p)
            {
                var y = p - radius;
                return y < 0 || y >= height ? identity : source.AsSpan(y * width, width);
            }

            var prefix = new byte[padded * width];
            var suffix = new byte[padded * width];

            for (var p = 0; p < padded; p++)
            {
                var row = prefix.AsSpan(p * width, width);
                if (p % window == 0)
                    RowAt(p).CopyTo(row);
                else
                    Combine(prefix.AsSpan((p - 1) * width, width), RowAt(p), row, grow);
            }

            for (var p = padded - 1; p >= 0; p--)
            {
                var row = suffix.AsSpan(p * width, width);
                if (p == padded - 1 || (p + 1) % window == 0)
                    RowAt(p).CopyTo(row);
                else
                    Combine(suffix.AsSpan((p + 1) * width, width), RowAt(p), row, grow);
            }

            var result = new byte[source.Length];
            for (var y = 0; y < height; y++)
                Combine(suffix.AsSpan(y * width, width), prefix.AsSpan((y + 2 * radius) * width, width), result.AsSpan(y * width, width), grow);

            return result;
        }

        private static void Combine(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, Span<byte> destination, bool max)
        {
            var i = 0;
            if (Vector.IsHardwareAccelerated)
            {
                var width = Vector<byte>.Count;
                if (max)
                {
                    for (; i + width <= destination.Length; i += width)
                        Vector.Max(new Vector<byte>(a.Slice(i)), new Vector<byte>(b.Slice(i))).CopyTo(destination.Slice(i));
                }
                else
                {
                    for (; i + width <= destination.Length; i += width)
                        Vector.Min(new Vector<byte>(a.Slice(i)), new Vector<byte>(b.Slice(i))).CopyTo(destination.Slice(i));
                }
            }

            if (max)
            {
                for (; i < destination.Length; i++)
                    destination[i] = Math.Max(a[i], b[i]);
            }
            else
            {
                for (; i < destination.Length; i++)
                    destination[i] = Math.Min(a[i], b[i]);
            }
        }

        private static byte[] Transpose(byte[] source, int width, int height)
        {
            var result = new byte[source.Length];
            const int block = 32;
            for (var by = 0; by < height; by += block)
            {
                for (var bx = 0; bx < width; bx += block)
                {
                    var yEnd = Math.Min(by + block, height);
                    var xEnd = Math.Min(bx + block, width);
                    for (var y = by; y < yEnd; y++)
                    {
                        for (var x = bx; x < xEnd; x++)
                            result[x * height + y] = source[y * width + x];
                    }
                }
            }

            return result;
        }
    }
}

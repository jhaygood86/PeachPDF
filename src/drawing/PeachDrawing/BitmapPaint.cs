using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace PeachDrawing;

/// <summary>
/// Paints from a <see cref="Bitmap"/>, mapping each device pixel centre back into bitmap pixel space through
/// an inverse transform. Uses integer (8-bit) filter weights so sampling is deterministic.
/// </summary>
internal sealed class BitmapPaint : PaintSource
{
    private readonly Bitmap _bitmap;
    private readonly Affine _deviceToBitmap;
    private readonly bool _smooth;
    private readonly bool _bicubic;

    /// <param name="bitmap">The source pixels (premultiplied).</param>
    /// <param name="deviceToBitmap">Maps a device position to a position in bitmap pixel units (pixel (i, j) covers [i, i+1) x [j, j+1)).</param>
    /// <param name="smooth">Bilinear filtering; false is nearest-neighbour (used to keep pixel art crisp).</param>
    /// <param name="bicubic">With <paramref name="smooth"/>, a Catmull-Rom filter over sixteen source pixels instead of bilinear.</param>
    public BitmapPaint(Bitmap bitmap, in Affine deviceToBitmap, bool smooth, bool bicubic = false)
    {
        _bitmap = bitmap;
        _deviceToBitmap = deviceToBitmap;
        _smooth = smooth;
        _bicubic = smooth && bicubic;

        // A strong minification aliases under bilinear sampling, so pre-average by an integer factor first.
        var scale = Math.Sqrt(Math.Abs(deviceToBitmap.Determinant));
        if (smooth && scale >= 2)
        {
            var factor = (int)Math.Floor(scale);
            _bitmap = Downsample(bitmap, factor);
            var s = 1.0 / factor;
            _deviceToBitmap = Affine.Then(deviceToBitmap, Affine.Scale(s, s));
        }

        // A draw at the bitmap's own size, moved by whole pixels, reads each destination pixel straight from one source pixel: the sampling position is
        // exactly a pixel's far-left (bilinear weights 0) or inside it (nearest), so the row is a clamped copy. Bicubic is left to its own code.
        var t = _deviceToBitmap;
        if (!_bicubic && t.M11 == 1 && t.M12 == 0 && t.M21 == 0 && t.M22 == 1
            && Math.Abs(t.OffsetX) < 1e9 && Math.Abs(t.OffsetY) < 1e9
            && Math.Floor(t.OffsetX) == t.OffsetX && Math.Floor(t.OffsetY) == t.OffsetY)
        {
            _wholePixelShift = true;
            _shiftX = (int)t.OffsetX;
            _shiftY = (int)t.OffsetY;
        }
    }

    private readonly bool _wholePixelShift;
    private readonly int _shiftX, _shiftY;

    public override void FillSpan(int x0, int y, int count, Span<byte> destination)
    {
        var pixels = _bitmap.Pixels;
        var w = _bitmap.Width;
        var h = _bitmap.Height;

        if (_wholePixelShift)
        {
            CopyShiftedRow(pixels, w, h, x0, y, count, destination);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var (u, v) = _deviceToBitmap.Apply(x0 + i + 0.5, y + 0.5);
            var d = i * 4;

            if (!_smooth)
            {
                var ix = Math.Clamp((int)Math.Floor(u), 0, w - 1);
                var iy = Math.Clamp((int)Math.Floor(v), 0, h - 1);
                var p = (iy * w + ix) * 4;
                destination[d] = pixels[p];
                destination[d + 1] = pixels[p + 1];
                destination[d + 2] = pixels[p + 2];
                destination[d + 3] = pixels[p + 3];
                continue;
            }

            var fu = u - 0.5;
            var fv = v - 0.5;
            var x0i = (int)Math.Floor(fu);
            var y0i = (int)Math.Floor(fv);

            if (_bicubic)
            {
                SampleBicubic(pixels, w, h, x0i, y0i, fu - x0i, fv - y0i, destination.Slice(d, 4));
                continue;
            }

            var wx = (int)((fu - x0i) * 256);
            var wy = (int)((fv - y0i) * 256);
            var xa = Math.Clamp(x0i, 0, w - 1);
            var xb = Math.Clamp(x0i + 1, 0, w - 1);
            var ya = Math.Clamp(y0i, 0, h - 1);
            var yb = Math.Clamp(y0i + 1, 0, h - 1);

            var p00 = (ya * w + xa) * 4;
            var p10 = (ya * w + xb) * 4;
            var p01 = (yb * w + xa) * 4;
            var p11 = (yb * w + xb) * 4;
            var w00 = (256 - wx) * (256 - wy);
            var w10 = wx * (256 - wy);
            var w01 = (256 - wx) * wy;
            var w11 = wx * wy;

            if (Vector128.IsHardwareAccelerated)
            {
                // The four channels of each texel at once: the same integer products and sum, rounded and shifted the same way.
                var sum = Texel(pixels, p00) * Vector128.Create(w00) + Texel(pixels, p10) * Vector128.Create(w10)
                    + Texel(pixels, p01) * Vector128.Create(w01) + Texel(pixels, p11) * Vector128.Create(w11);
                var rounded = Vector128.ShiftRightArithmetic(sum + Vector128.Create(32768), 16).AsUInt32();
                var bytes = Vector128.Narrow(Vector128.Narrow(rounded, rounded), Vector128.Narrow(rounded, rounded));
                MemoryMarshal.Write(destination.Slice(d, 4), bytes.AsUInt32().ToScalar());
                continue;
            }

            for (var c = 0; c < 4; c++)
            {
                var sum = pixels[p00 + c] * w00 + pixels[p10 + c] * w10 + pixels[p01 + c] * w01 + pixels[p11 + c] * w11;
                destination[d + c] = (byte)((sum + 32768) >> 16);
            }
        }
    }

    /// <summary>The four channels of the pixel starting at byte <paramref name="p"/>, widened to 32-bit lanes.</summary>
    private static Vector128<int> Texel(byte[] pixels, int p)
    {
        var packed = MemoryMarshal.Read<uint>(pixels.AsSpan(p, 4));
        return Vector128.WidenLower(Vector128.WidenLower(Vector128.CreateScalar(packed).AsByte())).AsInt32();
    }

    /// <summary>
    /// Row <paramref name="y"/> of a whole-pixel-shifted draw: destination pixel x0 + i is source pixel (x0 + i + shift), the column and row clamped
    /// to the bitmap, copied as runs.
    /// </summary>
    private void CopyShiftedRow(byte[] pixels, int w, int h, int x0, int y, int count, Span<byte> destination)
    {
        var sy = Math.Clamp(y + _shiftY, 0, h - 1);
        var row = pixels.AsSpan(sy * w * 4, w * 4);
        var first = x0 + _shiftX;
        var i = 0;

        // Columns left of the bitmap take its first pixel.
        var left = Math.Clamp(-first, 0, count);
        for (; i < left; i++)
            row[..4].CopyTo(destination.Slice(i * 4, 4));

        // Columns inside it are one copy.
        var inside = Math.Clamp(w - Math.Max(first, 0), 0, count - i);
        if (inside > 0)
        {
            row.Slice(Math.Max(first, 0) * 4, inside * 4).CopyTo(destination.Slice(i * 4, inside * 4));
            i += inside;
        }

        // Columns right of it take its last pixel.
        for (; i < count; i++)
            row.Slice((w - 1) * 4, 4).CopyTo(destination.Slice(i * 4, 4));
    }

    /// <summary>Catmull-Rom weights of the four taps around a position <paramref name="t"/> (0..1) past the second one.</summary>
    private static void CatmullRom(double t, Span<double> weights)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        weights[0] = -0.5 * t3 + t2 - 0.5 * t;
        weights[1] = 1.5 * t3 - 2.5 * t2 + 1;
        weights[2] = -1.5 * t3 + 2 * t2 + 0.5 * t;
        weights[3] = 0.5 * t3 - 0.5 * t2;
    }

    private static void SampleBicubic(byte[] pixels, int w, int h, int x0, int y0, double fx, double fy, Span<byte> result)
    {
        Span<double> wx = stackalloc double[4];
        Span<double> wy = stackalloc double[4];
        CatmullRom(fx, wx);
        CatmullRom(fy, wy);

        double r = 0, g = 0, b = 0, a = 0;
        for (var j = 0; j < 4; j++)
        {
            var yy = Math.Clamp(y0 - 1 + j, 0, h - 1);
            for (var i = 0; i < 4; i++)
            {
                var xx = Math.Clamp(x0 - 1 + i, 0, w - 1);
                var p = (yy * w + xx) * 4;
                var weight = wx[i] * wy[j];
                r += pixels[p] * weight;
                g += pixels[p + 1] * weight;
                b += pixels[p + 2] * weight;
                a += pixels[p + 3] * weight;
            }
        }

        // The curve overshoots at sharp edges; premultiplied pixels must stay within 0..alpha.
        var alpha = (byte)Math.Clamp(Math.Round(a), 0, 255);
        result[3] = alpha;
        result[0] = (byte)Math.Clamp(Math.Round(r), 0, alpha);
        result[1] = (byte)Math.Clamp(Math.Round(g), 0, alpha);
        result[2] = (byte)Math.Clamp(Math.Round(b), 0, alpha);
    }

    /// <summary>Averages <paramref name="factor"/> x <paramref name="factor"/> blocks (premultiplied, so the average is correct).</summary>
    private static Bitmap Downsample(Bitmap source, int factor)
    {
        // Round up: a trailing partial block still holds source pixels, and dropping it would skew the scale.
        var w = Math.Max(1, (source.Width + factor - 1) / factor);
        var h = Math.Max(1, (source.Height + factor - 1) / factor);
        var result = new byte[w * h * 4];

        var fullBlocksX = source.Width / factor;
        var fullBlocksY = source.Height / factor;
        var fullCount = (float)(factor * factor);

        // The single-precision division is exact only while a block's sum is below 2^24 and the quotient stays farther from the next whole number than the
        // rounding error: at a factor of 128 the sum is at most 4.2 million and that gap is at least 1 / 16384, against an error under 2e-5. Past it (a
        // 130-fold or greater reduction, which is not a draw anyone makes on purpose) the integer loop below does the averaging.
        var vectorBlocks = Vector128.IsHardwareAccelerated && factor <= 128;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (vectorBlocks && x < fullBlocksX && y < fullBlocksY)
                {
                    // A block entirely inside the source: sum its pixels' channels together, then divide as the loop below does,
                    // (sum + n / 2) / n rounded down. A single-precision quotient of whole numbers this small never lands on the wrong side of
                    // a whole number (the closest a true quotient gets to the next one is 1 / n, far above the rounding error), so truncating it
                    // gives the integer division's result.
                    var total = Vector128<int>.Zero;
                    for (var dy = 0; dy < factor; dy++)
                    {
                        var rowStart = ((y * factor + dy) * source.Width + x * factor) * 4;
                        for (var dx = 0; dx < factor; dx++)
                            total += Texel(source.Pixels, rowStart + dx * 4);
                    }

                    var quotient = Vector128.ConvertToSingle(total + Vector128.Create(factor * factor / 2)) / Vector128.Create(fullCount);
                    var averaged = Vector128.ConvertToInt32(quotient).AsUInt32();
                    var packed = Vector128.Narrow(Vector128.Narrow(averaged, averaged), Vector128.Narrow(averaged, averaged));
                    MemoryMarshal.Write(result.AsSpan((y * w + x) * 4, 4), packed.AsUInt32().ToScalar());
                    continue;
                }

                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var dy = 0; dy < factor; dy++)
                {
                    var sy = y * factor + dy;
                    if (sy >= source.Height) break;
                    for (var dx = 0; dx < factor; dx++)
                    {
                        var sx = x * factor + dx;
                        if (sx >= source.Width) break;
                        var p = (sy * source.Width + sx) * 4;
                        r += source.Pixels[p];
                        g += source.Pixels[p + 1];
                        b += source.Pixels[p + 2];
                        a += source.Pixels[p + 3];
                        n++;
                    }
                }

                var q = (y * w + x) * 4;
                if (n > 0)
                {
                    result[q] = (byte)((r + n / 2) / n);
                    result[q + 1] = (byte)((g + n / 2) / n);
                    result[q + 2] = (byte)((b + n / 2) / n);
                    result[q + 3] = (byte)((a + n / 2) / n);
                }
            }
        }

        return new Bitmap(w, h, result);
    }
}

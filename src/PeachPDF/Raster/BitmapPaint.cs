using System;

namespace PeachPDF.Raster;

/// <summary>
/// Paints from a <see cref="Bitmap"/>, mapping each device pixel centre back into bitmap pixel space through
/// an inverse transform. Uses integer (8-bit) filter weights so sampling is deterministic.
/// </summary>
internal sealed class BitmapPaint : PaintSource
{
    private readonly Bitmap _bitmap;
    private readonly Affine _deviceToBitmap;
    private readonly bool _smooth;

    /// <param name="bitmap">The source pixels (premultiplied).</param>
    /// <param name="deviceToBitmap">Maps a device position to a position in bitmap pixel units (pixel (i, j) covers [i, i+1) x [j, j+1)).</param>
    /// <param name="smooth">Bilinear filtering; false is nearest-neighbour (used to keep pixel art crisp).</param>
    public BitmapPaint(Bitmap bitmap, in Affine deviceToBitmap, bool smooth)
    {
        _bitmap = bitmap;
        _deviceToBitmap = deviceToBitmap;
        _smooth = smooth;

        // A strong minification aliases under bilinear sampling, so pre-average by an integer factor first.
        var scale = Math.Sqrt(Math.Abs(deviceToBitmap.Determinant));
        if (smooth && scale >= 2)
        {
            var factor = (int)Math.Floor(scale);
            _bitmap = Downsample(bitmap, factor);
            var s = 1.0 / factor;
            _deviceToBitmap = Affine.Then(deviceToBitmap, Affine.Scale(s, s));
        }
    }

    public override void FillSpan(int x0, int y, int count, Span<byte> destination)
    {
        var pixels = _bitmap.Pixels;
        var w = _bitmap.Width;
        var h = _bitmap.Height;

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

            for (var c = 0; c < 4; c++)
            {
                var sum = pixels[p00 + c] * w00 + pixels[p10 + c] * w10 + pixels[p01 + c] * w01 + pixels[p11 + c] * w11;
                destination[d + c] = (byte)((sum + 32768) >> 16);
            }
        }
    }

    /// <summary>Averages <paramref name="factor"/> x <paramref name="factor"/> blocks (premultiplied, so the average is correct).</summary>
    private static Bitmap Downsample(Bitmap source, int factor)
    {
        // Round up: a trailing partial block still holds source pixels, and dropping it would skew the scale.
        var w = Math.Max(1, (source.Width + factor - 1) / factor);
        var h = Math.Max(1, (source.Height + factor - 1) / factor);
        var result = new byte[w * h * 4];

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
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

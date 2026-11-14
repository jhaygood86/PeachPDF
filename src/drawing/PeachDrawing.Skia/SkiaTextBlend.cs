using PeachDrawing.Core;
using System;

namespace PeachDrawing.Skia;

/// <summary>
/// Turns the coverage of a <see cref="SkiaTextMask"/> into pixels: first the correction that makes dark and light text carry the same
/// apparent weight (blending coverage as it is makes dark text look too light and light text too thin, because the eye does not add up
/// brightness the way the arithmetic does), then, for LCD text, the filter that spreads the three stripes' coverage into the neighboring ones.
/// A port of the Win2D backend's <c>Win2dTextBlend</c> with the same fitted constants, so the same text looks the same on both.
/// </summary>
internal static class SkiaTextBlend
{
    /// <summary>The constants of <c>a' = c + c(1-c)((g0 f + g1) c + (g2 f + g3))</c>, with <c>f</c> the text color's lightness raised by <see cref="Lift"/>; fitted to the way Windows' text renderer treats the same coverage.</summary>
    private const double G0 = 0.125, G1 = 0.0, G2 = 0.755, G3 = -0.125, Lift = 3.15;

    /// <summary>The same constants for LCD text, whose per-stripe coverage the eye weighs a little differently.</summary>
    private const double L0 = 0.575, L1 = -0.175, L2 = 0.655, L3 = -0.175, LLift = 1.8;

    /// <summary>The FIR filter that spreads a stripe's coverage over its neighbors: the weight of the stripe two away and of the one next to it (the stripe itself takes the rest).</summary>
    private const double FirOuter = 0.0, FirInner = 0.36;

    /// <summary>The lightness of a text color in 0..1, raised so mid-grey text is treated as light text, which is how it looks.</summary>
    private static double Lightness(PaintColor color, double lift)
    {
        var (r, g, b) = color.ToRgb();
        var luma = (0.30 * r + 0.59 * g + 0.11 * b) / 255.0;
        return 1 - Math.Pow(1 - luma, lift);
    }

    /// <summary>A table from a coverage byte to the corrected alpha byte, for text of this color.</summary>
    internal static byte[] CorrectionTable(PaintColor color, bool lcd = false)
    {
        var (g0, g1, g2, g3, lift) = lcd ? (L0, L1, L2, L3, LLift) : (G0, G1, G2, G3, Lift);
        var f = Lightness(color, lift);
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255.0;
            var corrected = c + c * (1 - c) * ((g0 * f + g1) * c + (g2 * f + g3));
            table[i] = (byte)Math.Clamp(Math.Round(corrected * 255.0), 0, 255);
        }

        return table;
    }

    /// <summary>The text color at its corrected coverage as a premultiplied RGBA bitmap, one coverage sample per pixel (for a mask of three stripes, their average).</summary>
    internal static byte[] Grayscale(SkiaTextMask mask, PaintColor color)
    {
        var table = CorrectionTable(color);
        var (r, g, b) = color.ToRgb();
        var count = mask.Width * mask.Height;
        var pixels = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            var coverage = mask.SubPixels == 1
                ? mask.Samples[i]
                : (mask.Samples[i * 3] + mask.Samples[(i * 3) + 1] + mask.Samples[(i * 3) + 2]) / 3;
            var alpha = table[coverage] * color.A / 255;
            var o = i * 4;
            pixels[o] = (byte)(r * alpha / 255);
            pixels[o + 1] = (byte)(g * alpha / 255);
            pixels[o + 2] = (byte)(b * alpha / 255);
            pixels[o + 3] = (byte)alpha;
        }

        return pixels;
    }

    /// <summary>
    /// LCD text as two opaque RGBA bitmaps that composite without reading what is behind the text: <paramref name="keep"/> holds, per color channel, the
    /// share of the backdrop that stays (255 minus the channel's corrected, filtered coverage), and <paramref name="add"/> the text color times that coverage.
    /// The backdrop is multiplied by the first and the second is added to the result, which is <c>dst * (1 - a) + ink * a</c> for each channel with its own <c>a</c>.
    /// </summary>
    internal static void Lcd(SkiaTextMask mask, PaintColor color, bool bgrStripes, out byte[] keep, out byte[] add)
    {
        var width = mask.Width;
        var table = CorrectionTable(color, lcd: true);
        var fir = new[] { FirOuter, FirInner, 1 - 2 * FirOuter - 2 * FirInner, FirInner, FirOuter };
        var (r, g, b) = color.ToRgb();
        var rowLength = width * 3;
        var samples = mask.Samples;
        keep = new byte[width * mask.Height * 4];
        add = new byte[keep.Length];
        for (var y = 0; y < mask.Height; y++)
        {
            var row = y * rowLength;
            for (var x = 0; x < width; x++)
            {
                var o = ((y * width) + x) * 4;
                for (var stripe = 0; stripe < 3; stripe++)
                {
                    var center = (x * 3) + stripe;
                    var sum = 0.0;
                    for (var k = -2; k <= 2; k++)
                    {
                        var s = center + k;
                        if ((uint)s < (uint)rowLength)
                            sum += fir[k + 2] * samples[row + s];
                    }

                    var alpha = table[(int)Math.Min(Math.Round(sum), 255)] * color.A / 255;

                    // Left to right the stripes are red, green, blue (blue, green, red on a display built the other way).
                    var channel = bgrStripes ? 2 - stripe : stripe;
                    var ink = channel == 0 ? r : channel == 1 ? g : b;
                    keep[o + channel] = (byte)(255 - alpha);
                    add[o + channel] = (byte)((ink * alpha + 127) / 255);
                }

                keep[o + 3] = 255;
                add[o + 3] = 255;
            }
        }
    }
}

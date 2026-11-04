using PeachDrawing.Core;
using System;

namespace PeachDrawing.Win2D;

/// <summary>
/// Turns the coverage of a <see cref="Win2dTextMask"/> into pixels: first the correction that makes dark and light text carry the same
/// apparent weight (blending coverage as it is makes dark text look too light and light text too thin, because the eye does not add up
/// brightness the way the arithmetic does), then, for LCD text, the filter that spreads the three stripes' coverage into the neighbouring ones.
/// </summary>
internal static class Win2dTextBlend
{
    /// <summary>
    /// The constants of the correction, <c>a' = c + c(1-c)((g0 f + g1) c + (g2 f + g3))</c> with <c>f</c> the text colour's lightness raised
    /// by <c>Lift</c>. They were fitted to the way Windows' text renderer treats the same coverage, over dark and light text on both dark and
    /// light backgrounds.
    /// </summary>
    internal static double G0 = 0.125, G1 = 0.0, G2 = 0.755, G3 = -0.125, Lift = 3.15;

    /// <summary>The same constants for LCD text, whose per-stripe coverage the eye weighs a little differently.</summary>
    internal static double L0 = 0.575, L1 = -0.175, L2 = 0.655, L3 = -0.175, LLift = 1.8;

    /// <summary>
    /// The FIR filter that spreads one stripe's coverage over its neighbours, which keeps the colour fringes of LCD text from being harsh:
    /// the weights of the stripe two to the left, one to the left, the stripe itself, and mirrored; they sum to 1. Fitted to the LCD text Windows draws: close to a plain three-stripe average.
    /// </summary>
    internal static double FirOuter = 0.0, FirInner = 0.36;

    /// <summary>How many stripes the coverage is shifted to the right of where the glyph is, to line up with the pixel's own stripes.</summary>
    internal static int Shift = 0;

    /// <summary>The lightness of a text colour in 0..1, raised so mid-grey text is treated as light text, which is how it looks.</summary>
    private static double Lightness(PaintColor color, double lift)
    {
        var (r, g, b) = color.ToRgb();
        var luma = (0.30 * r + 0.59 * g + 0.11 * b) / 255.0;
        return 1 - Math.Pow(1 - luma, lift);
    }

    /// <summary>A table from a coverage byte to the corrected alpha byte, for text of this colour.</summary>
    public static byte[] CorrectionTable(PaintColor color, bool lcd = false)
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

    /// <summary>Straight alpha as a premultiplied RGBA bitmap of the text's colour: one coverage sample per pixel.</summary>
    public static byte[] Grayscale(Win2dTextMask mask, PaintColor color)
    {
        var table = CorrectionTable(color);
        var (r, g, b) = color.ToRgb();
        var pixels = new byte[mask.Width * mask.Height * 4];
        for (var i = 0; i < mask.Samples.Length; i++)
        {
            var alpha = table[mask.Samples[i]] * color.A / 255;
            var o = i * 4;
            pixels[o] = (byte)(r * alpha / 255);
            pixels[o + 1] = (byte)(g * alpha / 255);
            pixels[o + 2] = (byte)(b * alpha / 255);
            pixels[o + 3] = (byte)alpha;
        }

        return pixels;
    }

    /// <summary>
    /// The pixels of LCD text blended over <paramref name="backdrop"/> (opaque BGRA, the same area as <paramref name="mask"/>): every colour
    /// channel is blended with the coverage of its own stripe, left to right in red, green, blue order. The result is opaque BGRA.
    /// </summary>
    public static byte[] Lcd(Win2dTextMask mask, PaintColor color, byte[] backdrop, bool bgrStripes = false)
    {
        var table = CorrectionTable(color, lcd: true);
        var fir = new[] { FirOuter, FirInner, 1 - 2 * FirOuter - 2 * FirInner, FirInner, FirOuter };
        var (r, g, b) = color.ToRgb();
        var ink = bgrStripes ? new[] { b, g, r } : new[] { r, g, b };
        var width = mask.Width;
        var samples = mask.Samples;
        var rowLength = width * 3;
        var pixels = new byte[width * mask.Height * 4];
        for (var y = 0; y < mask.Height; y++)
        {
            var row = y * rowLength;
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                for (var stripe = 0; stripe < 3; stripe++)
                {
                    var center = x * 3 + stripe - Shift;
                    var sum = 0.0;
                    for (var k = -2; k <= 2; k++)
                    {
                        var s = center + k;
                        if ((uint)s < (uint)rowLength)
                            sum += fir[k + 2] * samples[row + s];
                    }

                    var alpha = table[(int)Math.Min(Math.Round(sum), 255)] * color.A / 255;

                    // The backdrop is BGRA, so the stripe that is red for a left-to-right RGB display is the third byte of a pixel.
                    var channel = bgrStripes ? stripe : 2 - stripe;
                    var behind = backdrop[o + channel];
                    pixels[o + channel] = (byte)((behind * (255 - alpha) + ink[stripe] * alpha + 127) / 255);
                }

                pixels[o + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>
    /// The coverage of each colour channel for the GPU blend, as an R8G8B8A8 bitmap (alpha 255): the corrected, filtered alpha of the stripe that lands on
    /// that channel in R, G and B (or the same value in all three for grayscale text). Together with the text colour this is all the blend needs.
    /// </summary>
    public static byte[] StripeAlphas(Win2dTextMask mask, PaintColor color, bool bgrStripes)
    {
        var width = mask.Width;
        var pixels = new byte[width * mask.Height * 4];
        if (mask.SubPixels == 1)
        {
            var gray = CorrectionTable(color);
            for (var i = 0; i < mask.Samples.Length; i++)
            {
                var alpha = (byte)(gray[mask.Samples[i]] * color.A / 255);
                var o = i * 4;
                pixels[o] = pixels[o + 1] = pixels[o + 2] = alpha;
                pixels[o + 3] = 255;
            }

            return pixels;
        }

        var table = CorrectionTable(color, lcd: true);
        var fir = new[] { FirOuter, FirInner, 1 - 2 * FirOuter - 2 * FirInner, FirInner, FirOuter };
        var rowLength = width * 3;
        var samples = mask.Samples;
        for (var y = 0; y < mask.Height; y++)
        {
            var row = y * rowLength;
            for (var x = 0; x < width; x++)
            {
                var o = ((y * width) + x) * 4;
                for (var stripe = 0; stripe < 3; stripe++)
                {
                    var center = (x * 3) + stripe - Shift;
                    var sum = 0.0;
                    for (var k = -2; k <= 2; k++)
                    {
                        var s = center + k;
                        if ((uint)s < (uint)rowLength)
                            sum += fir[k + 2] * samples[row + s];
                    }

                    // Left to right the stripes are red, green, blue (blue, green, red on a display built the other way).
                    pixels[o + (bgrStripes ? 2 - stripe : stripe)] = (byte)(table[(int)Math.Min(Math.Round(sum), 255)] * color.A / 255);
                }

                pixels[o + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>The text colour times each channel's coverage, from <see cref="StripeAlphas"/>: what is added to the backdrop once it has been dimmed by the coverage.</summary>
    public static byte[] InkTimesAlpha(byte[] alphas, PaintColor color)
    {
        var (r, g, b) = color.ToRgb();
        var pixels = new byte[alphas.Length];
        for (var i = 0; i < alphas.Length; i += 4)
        {
            pixels[i] = (byte)((r * alphas[i] + 127) / 255);
            pixels[i + 1] = (byte)((g * alphas[i + 1] + 127) / 255);
            pixels[i + 2] = (byte)((b * alphas[i + 2] + 127) / 255);
            pixels[i + 3] = 255;
        }

        return pixels;
    }
}

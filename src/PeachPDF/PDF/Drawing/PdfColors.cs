using PeachDrawing.Core;
using System;

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// The color conversions the PDF layer needs beyond what <see cref="PaintColor"/> offers: reading a color's
/// opacity as a fraction, going between RGB and CMYK when a document forces a color mode, and building a color
/// from the gray or fractional-alpha values an ICC conversion produces.
/// </summary>
internal static class PdfColors
{
    /// <summary>The color's opacity as a fraction, <c>0</c> (transparent) to <c>1</c> (opaque).</summary>
    public static double Opacity(this PaintColor color) => color.A / 255f;

    /// <summary>Builds a CMYK color from an opacity fraction and ink fractions, each clamped to <c>0</c> to <c>1</c>.</summary>
    public static PaintColor FromCmyk(double opacity, double cyan, double magenta, double yellow, double black)
        => PaintColor.FromCmyk(ToAlphaByte(opacity), (float)Clamp(cyan), (float)Clamp(magenta), (float)Clamp(yellow), (float)Clamp(black));

    /// <summary>Builds a color from a packed 32-bit ARGB value (alpha in the top byte).</summary>
    public static PaintColor FromPackedArgb(int argb)
        => PaintColor.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>Builds an opaque neutral gray from a fraction, <c>0</c> (black) to <c>1</c> (white).</summary>
    public static PaintColor FromGrayScale(double gray)
    {
        float level = (float)Clamp(gray);
        byte value = (byte)(level * 255);
        return PaintColor.FromArgb(255, value, value, value);
    }

    /// <summary>
    /// The color's ink fractions: a CMYK color's own, or for an RGB color the plain (profile-less) conversion
    /// where black is the darkest channel's complement.
    /// </summary>
    public static (double Cyan, double Magenta, double Yellow, double Black) ToCmyk(this PaintColor color)
    {
        if (color.IsCmyk)
            return (color.C, color.M, color.Y, color.K);

        int cyan = 255 - color.R;
        int magenta = 255 - color.G;
        int yellow = 255 - color.B;
        int black = Math.Min(cyan, Math.Min(magenta, yellow));
        if (black == 255)
            return (0, 0, 0, 1);

        float ink = 255f - black;
        float cyanFraction = (cyan - black) / ink;
        float magentaFraction = (magenta - black) / ink;
        float yellowFraction = (yellow - black) / ink;
        return (cyanFraction, magentaFraction, yellowFraction, black / 255f);
    }

    /// <summary>The color expressed in the RGB color space, keeping its opacity.</summary>
    public static PaintColor ToRgbColor(this PaintColor color)
    {
        if (!color.IsCmyk)
            return color;

        var (red, green, blue) = color.ToRgb();
        return PaintColor.FromArgb(color.A, red, green, blue);
    }

    /// <summary>The color expressed in the CMYK color space, keeping its opacity.</summary>
    public static PaintColor ToCmykColor(this PaintColor color)
    {
        if (color.IsCmyk)
            return color;

        var (cyan, magenta, yellow, black) = color.ToCmyk();
        return PaintColor.FromCmyk(color.A, (float)cyan, (float)magenta, (float)yellow, (float)black);
    }

    /// <summary>A copy of the color with its opacity replaced by the given fraction.</summary>
    public static PaintColor WithOpacity(this PaintColor color, double opacity)
    {
        int alpha = ToAlphaByte(opacity);
        if (!color.IsCmyk)
            return PaintColor.FromArgb(alpha, color.R, color.G, color.B);

        return PaintColor.FromCmyk(alpha, color.C, color.M, color.Y, color.K);
    }

    /// <summary>The color's red, green and blue (a CMYK color's converted ones) packed into 24 bits.</summary>
    public static uint PackedRgb(this PaintColor color)
    {
        var (red, green, blue) = color.ToRgb();
        return ((uint)red << 16) | ((uint)green << 8) | blue;
    }

    private static double Clamp(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private static int ToAlphaByte(double opacity) => (int)Math.Round(Clamp(opacity) * 255);
}

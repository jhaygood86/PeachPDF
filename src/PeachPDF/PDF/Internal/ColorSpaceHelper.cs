// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.Core;
using PeachPDF.PDF.Drawing;
using System;

namespace PeachPDF.PDF.Internal;

/// <summary>
/// Helper functions for RGB and CMYK colors.
/// </summary>
internal static class ColorSpaceHelper
{
    /// <summary>
    /// Checks whether a color mode and a color match.
    /// </summary>
    public static PaintColor EnsureColorMode(PdfColorMode colorMode, PaintColor color)
    {
        if (colorMode == PdfColorMode.Rgb && color.IsCmyk)
            return color.ToRgbColor();

        if (colorMode == PdfColorMode.Cmyk && !color.IsCmyk)
            return color.ToCmykColor();

        return color;
    }

    /// <summary>
    /// Checks whether the color mode of a document and a color match.
    /// </summary>
    public static PaintColor EnsureColorMode(PdfDocument document, PaintColor color)
    {
        ArgumentNullException.ThrowIfNull(document);

        return EnsureColorMode(document.Options.ColorMode, color);
    }

    /// <summary>
    /// Determines whether two colors are equal referring to their CMYK color values.
    /// </summary>
    public static bool IsEqualCmyk(PaintColor x, PaintColor y)
    {
        if (!x.IsCmyk || !y.IsCmyk)
            return false;
        return x.C == y.C && x.M == y.M && x.Y == y.Y && x.K == y.K;
    }
}

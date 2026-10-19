using PeachDrawing.Core;

namespace PeachDrawing.PDF.Engine.Drawing;

/// <summary>Builders for the plain paints the PDF layer draws with.</summary>
internal static class PdfPaints
{
    /// <summary>Whether the brush is one of the gradient kinds the PDF writer paints with a shading pattern.</summary>
    public static bool IsGradient(Brush brush) => brush is LinearGradientBrush or RadialGradientBrush or ConicGradientBrush;

    /// <summary>
    /// Rejects a dash array the PDF specification does not allow: a negative length, or lengths that are all zero.
    /// A single zero-length dash is legal, and under a round line cap it is how a dotted line is expressed.
    /// </summary>
    public static void RequireValidDashPattern(double[] pattern)
    {
        bool anyPositive = false;
        foreach (double length in pattern)
        {
            if (length < 0)
                throw new System.ArgumentException("Dash pattern value must not be negative.");
            if (length > 0)
                anyPositive = true;
        }

        if (pattern.Length > 0 && !anyPositive)
            throw new System.ArgumentException("Dash pattern values must not all be zero.");
    }

    /// <summary>A pen that strokes with one solid color.</summary>
    public static Pen CreatePen(PaintColor color, double width = 1)
        => new() { Width = width, Paint = new SolidBrush(color) };
}

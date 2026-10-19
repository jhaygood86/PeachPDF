using System;
using System.Collections.Generic;
using PeachDrawing.Core;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// One full-tile fill painted over an isolated transparency group with a separable PDF blend mode
/// (ISO 32000-1 §11.3.5.2), the same fill colour on every pixel.
/// </summary>
/// <param name="BlendMode">The PDF blend-mode name (<c>Difference</c>, <c>Multiply</c>, ...).</param>
/// <param name="R">The red component of the fill, in <c>[0, 1]</c>.</param>
/// <param name="G">The green component of the fill, in <c>[0, 1]</c>.</param>
/// <param name="B">The blue component of the fill, in <c>[0, 1]</c>.</param>
internal readonly record struct PdfBlendLayer(string BlendMode, double R, double G, double B);

/// <summary>
/// Expresses a channel-independent <see cref="ColorMatrix"/> (CSS <c>brightness()</c>,
/// <c>contrast()</c>, <c>invert()</c>, or a diagonal <c>feColorMatrix</c>/<c>feComponentTransfer</c>) as a
/// short stack of blend-mode fills, so the filter stays real vector content that every reader composites
/// itself, instead of an ExtGState <c>/TR</c> transfer function that MuPDF-based readers ignore.
/// </summary>
/// <remarks>
/// <para>
/// Every output channel is <c>clamp(m * c + b)</c> of its own input channel only. The separable blend modes
/// are exactly the building blocks that map onto that: <c>Multiply</c> (<c>c * s</c>, pivot 0),
/// <c>ColorDodge</c> (<c>min(1, c / (1 - s))</c>, pivot 0, slope above one), <c>Screen</c>
/// (<c>c * (1 - s) + s</c>, pivot 1), <c>ColorBurn</c> (<c>max(0, 1 - (1 - c) / s)</c>, pivot 1, slope above
/// one) and <c>Difference</c> against white (<c>1 - c</c>). Each has a fill colour at which it is the
/// identity (<c>Multiply</c> 1, <c>ColorDodge</c> 0, <c>Screen</c> 0, <c>ColorBurn</c> 1, <c>Difference</c> 0),
/// so a channel that needs less than the others simply gets that neutral value rather than a different
/// layer stack.
/// </para>
/// <para>
/// The decomposition, per channel: a negative slope is first turned positive by inverting the input
/// (<c>m c + b = -m (1 - c) + (m + b)</c>); then, with <c>s = m / (1 - b)</c>, the scale <c>s</c> is
/// applied by <c>Multiply</c> when <c>s &lt; 1</c> or <c>ColorDodge</c> at <c>1 - 1/s</c> when
/// <c>s &gt; 1</c>, and the offset by <c>Screen</c> at <c>b</c> when <c>b &gt; 0</c> or <c>ColorBurn</c> at
/// <c>1 / (1 - b)</c> when <c>b &lt; 0</c>. The clamp that <c>ColorDodge</c> applies at the top and
/// <c>ColorBurn</c> at the bottom land exactly where <c>m c + b</c> itself leaves <c>[0, 1]</c>, so no
/// intermediate clamp ever loses information. A constant channel (<c>m = 0</c>, or an offset of 1 or more)
/// is <c>Multiply</c> 0 followed by <c>Screen</c> at the constant. Fills are emitted in the fixed order
/// <c>Difference</c>, <c>Multiply</c>, <c>ColorDodge</c>, <c>Screen</c>, <c>ColorBurn</c>, which is the
/// order the per-channel decomposition above needs.
/// </para>
/// </remarks>
internal static class PdfColorMatrixBlendLayers
{
    // Below one 8-bit step of a fill colour a layer is the identity in every reader's integer maths.
    private const double Neutral = 1.0 / 512;

    /// <summary>
    /// Builds the layer stack for <paramref name="matrix"/> (<see cref="ColorMatrix.IsChannelIndependent"/>
    /// must already be true - this does not check). Empty when the matrix leaves R, G and B unchanged.
    /// </summary>
    public static IReadOnlyList<PdfBlendLayer> Build(ColorMatrix matrix)
    {
        var linear = matrix.Linear;
        var offset = matrix.Offset;

        Span<double> invert = stackalloc double[3];
        Span<double> multiply = stackalloc double[3];
        Span<double> dodge = stackalloc double[3];
        Span<double> screen = stackalloc double[3];
        Span<double> burn = stackalloc double[3];

        // Row = input component, column = output component (see ColorMatrix.Linear's remarks).
        Channel(linear.M11, offset.X, 0, invert, multiply, dodge, screen, burn);
        Channel(linear.M22, offset.Y, 1, invert, multiply, dodge, screen, burn);
        Channel(linear.M33, offset.Z, 2, invert, multiply, dodge, screen, burn);

        var layers = new List<PdfBlendLayer>(5);
        AddIfAny(layers, "Difference", invert, 0);
        AddIfAny(layers, "Multiply", multiply, 1);
        AddIfAny(layers, "ColorDodge", dodge, 0);
        AddIfAny(layers, "Screen", screen, 0);
        AddIfAny(layers, "ColorBurn", burn, 1);
        return layers;
    }

    private static void Channel(double m, double b, int index,
        Span<double> invert, Span<double> multiply, Span<double> dodge, Span<double> screen, Span<double> burn)
    {
        invert[index] = 0;
        multiply[index] = 1;
        dodge[index] = 0;
        screen[index] = 0;
        burn[index] = 1;

        if (Math.Abs(m) < 1e-6)
        {
            Constant(Math.Clamp(b, 0, 1), index, multiply, screen);
            return;
        }

        if (m < 0)
        {
            invert[index] = 1;
            b += m;
            m = -m;
        }

        // m c + b >= 1 for every c: the channel is pinned at white.
        if (b >= 1)
        {
            Constant(1, index, multiply, screen);
            return;
        }

        var scale = m / (1 - b);
        if (scale < 1)
            multiply[index] = scale;
        else if (scale > 1)
            dodge[index] = 1 - 1 / scale;

        if (b > 0)
            screen[index] = b;
        else if (b < 0)
            burn[index] = 1 / (1 - b);
    }

    private static void Constant(double value, int index, Span<double> multiply, Span<double> screen)
    {
        multiply[index] = 0;
        screen[index] = value;
    }

    private static void AddIfAny(List<PdfBlendLayer> layers, string blendMode, ReadOnlySpan<double> values, double neutral)
    {
        if (Math.Abs(values[0] - neutral) < Neutral && Math.Abs(values[1] - neutral) < Neutral && Math.Abs(values[2] - neutral) < Neutral)
            return;

        layers.Add(new PdfBlendLayer(blendMode, values[0], values[1], values[2]));
    }

    /// <summary>
    /// Applies <paramref name="layers"/> to one channel value with the PDF blend formulas of ISO 32000-1
    /// §11.3.5.2 against an opaque backdrop. Not used when writing a PDF - it is the reference the unit
    /// tests hold <see cref="Build"/> to, so a layer stack is proven equal to the matrix it came from.
    /// </summary>
    internal static double Apply(IReadOnlyList<PdfBlendLayer> layers, double c, int channel)
    {
        foreach (var layer in layers)
        {
            var s = channel switch { 0 => layer.R, 1 => layer.G, _ => layer.B };
            c = layer.BlendMode switch
            {
                "Difference" => Math.Abs(c - s),
                "Multiply" => c * s,
                "Screen" => c + s - c * s,
                "ColorDodge" => c <= 0 ? 0 : s >= 1 ? 1 : Math.Min(1, c / (1 - s)),
                "ColorBurn" => c >= 1 ? 1 : s <= 0 ? 0 : 1 - Math.Min(1, (1 - c) / s),
                _ => throw new InvalidOperationException($"Unexpected blend mode {layer.BlendMode}."),
            };
        }

        return c;
    }
}

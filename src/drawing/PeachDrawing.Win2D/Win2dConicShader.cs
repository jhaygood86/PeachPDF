using Microsoft.Graphics.Canvas.Effects;
using PeachDrawing.Core;
using System;
using System.IO;
using System.Numerics;

namespace PeachDrawing.Win2D;

/// <summary>The pixel shader that paints a conic gradient (see <c>Shaders/ConicGradient.hlsl</c>), which Win2D has no brush for.</summary>
internal static class Win2dConicShader
{
    /// <summary>The most stops the shader evaluates; a gradient with more is drawn as wedges instead.</summary>
    internal const int MaxStops = 16;

    private static byte[]? _code;

    /// <summary>
    /// Makes the effect for <paramref name="conic"/>. <paramref name="center"/> is the gradient's centre in the pixels the effect is
    /// evaluated in.
    /// </summary>
    internal static PixelShaderEffect Create(ConicGradientBrush conic, Vector2 center)
    {
        if (_code is null)
        {
            using var stream = typeof(Win2dConicShader).Assembly.GetManifestResourceStream("PeachDrawing.Win2D.ConicGradient.bin")
                ?? throw new InvalidOperationException("The conic gradient shader is not embedded in the assembly.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _code = memory.ToArray();
        }

        var stops = conic.Stops;
        var count = Math.Min(stops.Count, MaxStops);

        // The shader's constants are properties of the effect, one per variable of its constant buffer.
        var angleValues = new float[MaxStops];
        var colors = new Vector4[MaxStops];
        for (var i = 0; i < count; i++)
        {
            angleValues[i] = (float)stops[i].Position;
            var color = stops[i].PaintColor;
            var (r, g, b) = color.ToRgb();
            colors[i] = new Vector4(r / 255f, g / 255f, b / 255f, color.A / 255f);
        }

        // Four angles to a vector, as the shader packs them.
        var angles = new Vector4[MaxStops / 4];
        for (var v = 0; v < angles.Length; v++)
            angles[v] = new Vector4(angleValues[v * 4], angleValues[v * 4 + 1], angleValues[v * 4 + 2], angleValues[v * 4 + 3]);

        var effect = new PixelShaderEffect(_code);
        effect.Properties["center"] = center;
        effect.Properties["start"] = (float)stops[0].Position;
        effect.Properties["count"] = (float)count;
        effect.Properties["positions"] = angles;
        effect.Properties["colors"] = colors;
        return effect;
    }
}

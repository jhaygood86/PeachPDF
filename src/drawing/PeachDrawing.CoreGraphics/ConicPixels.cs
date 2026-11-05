using PeachDrawing.Core;
using System;

namespace PeachDrawing.CoreGraphics;

/// <summary>Renders a conic gradient into premultiplied RGBA pixels: CoreGraphics has no angular gradient, so it is computed per pixel.</summary>
internal static class ConicPixels
{
    /// <summary>
    /// Pixels (row 0 on top) of <paramref name="width"/> by <paramref name="height"/> covering the layout rectangle at (<paramref name="x"/>, <paramref name="y"/>)
    /// of <paramref name="boxWidth"/> by <paramref name="boxHeight"/> units.
    /// </summary>
    public static byte[] Render(ConicGradientBrush conic, double x, double y, double boxWidth, double boxHeight, int width, int height)
    {
        var stops = conic.Stops;
        var positions = new double[stops.Count];
        var colors = new (double R, double G, double B, double A)[stops.Count];
        for (var i = 0; i < stops.Count; i++)
        {
            positions[i] = i == 0 ? stops[0].Position : Math.Max(stops[i].Position, positions[i - 1]);
            var (r, g, b) = stops[i].PaintColor.ToRgb();
            var a = stops[i].PaintColor.A / 255.0;

            // Premultiplied, so blending between stops does not darken or fringe at transparent ones, as CSS specifies.
            colors[i] = (r / 255.0 * a, g / 255.0 * a, b / 255.0 * a, a);
        }

        var pixels = new byte[width * height * 4];
        var turn = 2 * Math.PI;
        var first = positions[0];
        for (var row = 0; row < height; row++)
        {
            var py = y + (row + 0.5) / height * boxHeight - conic.Center.Y;
            for (var column = 0; column < width; column++)
            {
                var px = x + (column + 0.5) / width * boxWidth - conic.Center.X;

                // Clockwise from straight up.
                var angle = Math.Atan2(px, -py);
                if (angle < first)
                    angle += turn * Math.Ceiling((first - angle) / turn);

                var (r, g, b, a) = Sample(positions, colors, angle);
                var offset = (row * width + column) * 4;
                pixels[offset] = ToByte(r);
                pixels[offset + 1] = ToByte(g);
                pixels[offset + 2] = ToByte(b);
                pixels[offset + 3] = ToByte(a);
            }
        }

        return pixels;
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    private static (double R, double G, double B, double A) Sample(double[] positions, (double R, double G, double B, double A)[] colors, double angle)
    {
        if (angle <= positions[0])
            return colors[0];

        for (var i = 1; i < positions.Length; i++)
        {
            if (angle > positions[i])
                continue;

            var span = positions[i] - positions[i - 1];
            var t = span > 1e-12 ? (angle - positions[i - 1]) / span : 1;
            var (r0, g0, b0, a0) = colors[i - 1];
            var (r1, g1, b1, a1) = colors[i];
            return (r0 + (r1 - r0) * t, g0 + (g1 - g0) * t, b0 + (b1 - b0) * t, a0 + (a1 - a0) * t);
        }

        return colors[^1];
    }
}

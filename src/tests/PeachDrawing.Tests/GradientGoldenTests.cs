using System.Security.Cryptography;
using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>
/// Pins the exact pixels gradient fills produce - linear and radial, opaque and translucent stops, hard stops, repeating, stops that start
/// after the origin and end before the far edge - so that rewriting how a color stop is evaluated cannot change one byte. The hashes were
/// recorded from the stop evaluation that preceded the per-segment precomputation. A hash cannot say what changed: compare pixel by pixel.
/// </summary>
public class GradientGoldenTests
{
    private const int Width = 211;
    private const int Height = 37;

    private static PaintColor C(int a, int r, int g, int b) => PaintColor.FromArgb(a, r, g, b);

    private static string Render(Func<RasterRenderContext, RasterCanvas, Brush> brush)
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(Width, Height);
        canvas.DrawRectangle(canvas.GetSolidBrush(C(255, 250, 240, 220)), 0, 0, Width, Height);
        canvas.DrawRectangle(brush(context, canvas), 0, 0, Width, Height);
        return Convert.ToHexString(SHA256.HashData(canvas.ToPixelBuffer().PremultipliedRgba.Span));
    }

    [Fact]
    public void TwoOpaqueStopsLinear_AreByteIdentical() => GradientGolden.Check("LINEAR_2_OPAQUE", Render((ctx, c) =>
        c.GetLinearGradientBrush(new PaintPoint(10.3, 4), new PaintPoint(190.7, 30), [(C(255, 255, 0, 0), 0d), (C(255, 0, 0, 255), 1d)])));

    [Fact]
    public void FourStopsWithAlphaLinear_AreByteIdentical() => GradientGolden.Check("LINEAR_4_ALPHA", Render((ctx, c) =>
        c.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(Width, Height),
            [(C(255, 255, 0, 0), 0d), (C(128, 255, 220, 0), 0.4), (C(0, 0, 160, 80), 0.6), (C(220, 0, 60, 220), 1d)])));

    [Fact]
    public void HardStopsAndPaddingLinear_AreByteIdentical() => GradientGolden.Check("LINEAR_HARD_PADDED", Render((ctx, c) =>
        c.GetLinearGradientBrush(new PaintPoint(40, 0), new PaintPoint(170, 0),
            [(C(255, 10, 20, 30), 0d), (C(255, 10, 20, 30), 0.3), (C(255, 200, 100, 0), 0.3), (C(255, 200, 100, 0), 0.7), (C(90, 0, 0, 0), 0.7), (C(90, 0, 0, 0), 1d)])));

    [Fact]
    public void RepeatingLinear_AreByteIdentical() => GradientGolden.Check("LINEAR_REPEATING", Render((ctx, c) =>
        c.GetLinearGradientBrush(new PaintPoint(5, 0), new PaintPoint(45, 12),
            [(C(255, 255, 255, 255), 0d), (C(200, 0, 0, 0), 0.5), (C(255, 255, 255, 255), 1d)], isRepeating: true)));

    [Fact]
    public void TwoOpaqueStopsRadial_AreByteIdentical() => GradientGolden.Check("RADIAL_2_OPAQUE", Render((ctx, c) =>
        c.GetRadialGradientBrush(new PaintPoint(100, 18), 90, 30, [(C(255, 255, 255, 255), 0d), (C(255, 40, 40, 160), 1d)])));

    [Fact]
    public void ThreeStopsWithAlphaAndFocalPointRadial_AreByteIdentical() => GradientGolden.Check("RADIAL_3_ALPHA_FOCAL", Render((ctx, c) =>
        c.GetRadialGradientBrush(new PaintPoint(110, 20), 80, 25, [(C(255, 255, 220, 0), 0d), (C(160, 255, 0, 0), 0.5), (C(40, 0, 0, 90), 1d)],
            isRepeating: false, focalCenter: new PaintPoint(80, 14))));

    [Fact]
    public void RepeatingRadial_AreByteIdentical() => GradientGolden.Check("RADIAL_REPEATING", Render((ctx, c) =>
        c.GetRadialGradientBrush(new PaintPoint(60, 18), 25, 25, [(C(255, 0, 128, 255), 0d), (C(255, 255, 255, 0), 1d)], isRepeating: true)));
}

/// <summary>The recorded hashes of <see cref="GradientGoldenTests"/>, by case name.</summary>
internal static class GradientGolden
{
    /// <summary>Fails unless <paramref name="actual"/> is the hash recorded for <paramref name="name"/>; the message carries the whole hash, to record a new case.</summary>
    public static void Check(string name, string actual) =>
        Assert.True(Hashes.TryGetValue(name, out var hash) && hash == actual, $"{name}: recorded {(Hashes.TryGetValue(name, out var h) ? h : "(none)")}, actual {actual}");

    private static readonly Dictionary<string, string> Hashes = new()
    {
        ["LINEAR_2_OPAQUE"] = "023FC0610EC5160403D662C9A8F46CCEF593A53F0D8BD61178C9C9B0EE3350FC",
        ["LINEAR_4_ALPHA"] = "F32742DCF7BDE67B2999411585C2EE2E3B49DB5388A896E09831C222B624B20D",
        ["LINEAR_HARD_PADDED"] = "ED8C8A35F3C63E13138B8337862B97D8A99EA7DBC356C2315A03F4C56A603611",
        ["LINEAR_REPEATING"] = "DC16ADFB5538817CDFC60A13912576715C1B8A50C5C9D7EDC7FCA04066A01077",
        ["RADIAL_2_OPAQUE"] = "AEF7962BD729095E23280356C2619DEEE91FB3ECE3CEEFE27D02584D57DA8B94",
        ["RADIAL_3_ALPHA_FOCAL"] = "6991B69451349456400837A6C04F0CDE36673924D1C39A1F5FB1282FA4173263",
        ["RADIAL_REPEATING"] = "D8E6672E6A293079E5642D61D967E4D513469484DFE4232398E99E3DD52040D7",
    };
}

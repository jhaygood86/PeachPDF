using System.Security.Cryptography;
using PeachDrawing.Core;

namespace PeachDrawing.Tests;

/// <summary>
/// Pins the exact pixels the scanline rasterizer produces for scenes that exercise its row resolve (wide fills with fractional edges, where the
/// interior is one long run, next to narrow shapes where there is none), both fill rules, overlapping contours, clipping and strokes. The hashes
/// were recorded from the scalar row resolve and the list-based edge building that preceded the run-writing and scratch-reuse rewrite, so a
/// rewrite that changes one byte of output fails here. A hash cannot say what changed: compare the two outputs pixel by pixel.
/// </summary>
public class ScanlineGoldenTests
{
    private static string Render(Action<RasterCanvas, RasterRenderContext> draw)
    {
        var context = new RasterRenderContext();
        using var canvas = context.CreateCanvas(203, 117);
        draw(canvas, context);
        return Convert.ToHexString(SHA256.HashData(canvas.ToPixelBuffer().PremultipliedRgba.Span));
    }

    private static Brush Solid(RasterCanvas c, int a, int r, int g, int b) => c.GetSolidBrush(PaintColor.FromArgb(a, r, g, b));

    private static GraphicsPath Star(RasterCanvas c, double cx, double cy, double r)
    {
        var path = c.GetGraphicsPath();
        for (var i = 0; i < 5; i++)
        {
            var angle = -Math.PI / 2 + i * 4 * Math.PI / 5;
            var x = cx + r * Math.Cos(angle);
            var y = cy + r * Math.Sin(angle);
            if (i == 0) path.Start(x, y); else path.LineTo(x, y);
        }

        path.CloseFigure();
        return path;
    }

    private static GraphicsPath Circle(RasterCanvas c, double cx, double cy, double r)
    {
        const double k = 0.5522847498;
        var path = c.GetGraphicsPath();
        path.Start(cx + r, cy);
        path.AddBezierTo(cx + r, cy + k * r, cx + k * r, cy + r, cx, cy + r);
        path.AddBezierTo(cx - k * r, cy + r, cx - r, cy + k * r, cx - r, cy);
        path.AddBezierTo(cx - r, cy - k * r, cx - k * r, cy - r, cx, cy - r);
        path.AddBezierTo(cx + k * r, cy - r, cx + r, cy - k * r, cx + r, cy);
        path.CloseFigure();
        return path;
    }

    [Fact]
    public void WideRectanglesWithFractionalEdges_AreByteIdentical() => ScanlineGolden.Check("SCANLINE_WIDE_RECTS", Render((c, _) =>
    {
        var brush = Solid(c, 255, 30, 144, 255);
        c.DrawRectangle(brush, 3.3, 2.6, 190.4, 40.7);
        c.DrawRectangle(Solid(c, 128, 220, 60, 40), 10.5, 20.25, 150.0, 60.5);
        // widths around the point where a run is long enough to be written in one go
        for (var w = 14; w <= 20; w++)
            c.DrawRectangle(brush, 5.5, 85 + (w - 14) * 4, w + 0.4, 3);
    }));

    [Fact]
    public void CurvedAndPolygonalPaths_AreByteIdentical() => ScanlineGolden.Check("SCANLINE_PATHS", Render((c, _) =>
    {
        c.DrawPath(Solid(c, 255, 200, 30, 30), Circle(c, 60, 55, 50));
        c.DrawPath(Solid(c, 160, 20, 160, 60), Star(c, 140, 58, 52));
        c.DrawPath(Solid(c, 255, 0, 0, 0), Circle(c, 195, 8, 3.5));
    }));

    [Fact]
    public void OverlappingContoursUnderBothFillRules_AreByteIdentical() => ScanlineGolden.Check("SCANLINE_FILL_RULES", Render((c, _) =>
    {
        var path = c.GetGraphicsPath();
        path.AddPath(Star(c, 50, 55, 50));
        path.AddPath(Circle(c, 70, 55, 40));
        c.DrawPath(Solid(c, 255, 30, 100, 200), path);
        var rect = c.GetGraphicsPath();
        rect.Start(110, 10);
        rect.LineTo(200, 10);
        rect.LineTo(200, 100);
        rect.LineTo(110, 100);
        rect.CloseFigure();
        rect.Start(130, 30);
        rect.LineTo(180, 30);
        rect.LineTo(180, 80);
        rect.LineTo(130, 80);
        rect.CloseFigure();
        c.DrawPath(Solid(c, 255, 200, 120, 0), rect);
    }));

    [Fact]
    public void ClippedFillsAndStrokes_AreByteIdentical() => ScanlineGolden.Check("SCANLINE_CLIP_STROKE", Render((c, ctx) =>
    {
        c.PushClip(Circle(c, 100, 58, 55));
        c.DrawRectangle(c.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(203, 117),
            [(PaintColor.FromArgb(255, 255, 0, 0), 0d), (PaintColor.FromArgb(255, 0, 0, 255), 1d)]), 0, 0, 203, 117);
        c.PopClip();
        var pen = c.GetPen(PaintColor.FromArgb(255, 0, 0, 0));
        pen.Width = 3.5;
        c.DrawPath(pen, Star(c, 100, 60, 45));
    }));

    [Fact]
    public void ManySmallShapes_AreByteIdentical() => ScanlineGolden.Check("SCANLINE_MANY_SMALL", Render((c, _) =>
    {
        var brush = Solid(c, 255, 10, 80, 160);
        for (var i = 0; i < 400; i++)
            c.DrawPath(brush, Circle(c, 6 + i % 20 * 9.7, 5 + i / 20 * 5.3, 2.5 + i % 3));
    }));
}

/// <summary>The recorded hashes of <see cref="ScanlineGoldenTests"/>, by case name.</summary>
internal static class ScanlineGolden
{
    /// <summary>Fails unless <paramref name="actual"/> is the hash recorded for <paramref name="name"/>; the message carries the whole hash, to record a new case.</summary>
    public static void Check(string name, string actual) =>
        Assert.True(Hashes.TryGetValue(name, out var hash) && hash == actual, $"{name}: recorded {(Hashes.TryGetValue(name, out var h) ? h : "(none)")}, actual {actual}");

    private static readonly Dictionary<string, string> Hashes = new()
    {
        ["SCANLINE_CLIP_STROKE"] = "61EA5C2FF231F3D4223FB368CEA0BE269889CDF4C9C52A8D5AF76D3CC4329129",
        ["SCANLINE_FILL_RULES"] = "060EC7B06BD8D943C3B6B5D68D262F95A0CB30C260DF72B876F640FF1728CFE1",
        ["SCANLINE_MANY_SMALL"] = "6C64EAEAAEF75AAE8501D661E23A6071DE5BCC9B80C715A66F65D77CC27012EE",
        ["SCANLINE_PATHS"] = "856342211902E0BD0FC7B47E18F1A1ED5338EB84912D9FD209555EE7A8C028A8",
        ["SCANLINE_WIDE_RECTS"] = "E57C6E0A7722F71A4F1673773FB59CA4797EDC8C8FEB746DC3CE0C5F766EBD54",
    };
}

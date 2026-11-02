using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System;

namespace PeachDrawing.Win2D;

/// <summary>
/// A run of text rasterized to coverage on the device pixel grid: <see cref="SubPixels"/> samples across each pixel (1 for grayscale, 3 for the
/// stripes of an LCD pixel), one byte of coverage per sample, rows top to bottom. The pixel at (<see cref="Left"/>, <see cref="Top"/>) is the
/// first one.
/// </summary>
internal readonly record struct Win2dTextMask(byte[] Samples, int Left, int Top, int Width, int Height, int SubPixels)
{
    /// <summary>The most samples one mask may hold; a larger run (huge text) is drawn as an outline instead.</summary>
    private const int MaxSamples = 6_000_000;

    /// <summary>
    /// Rasterizes <paramref name="text"/>, as a canvas whose layout units are <paramref name="deviceScaleX"/> by <paramref name="deviceScaleY"/>
    /// device pixels would draw it with its top-left at <paramref name="point"/> and a translation of (<paramref name="translateX"/>,
    /// <paramref name="translateY"/>) device pixels, with the font's own hinting. The mask covers only the part of the run inside
    /// <paramref name="limit"/> (the device pixels that can be seen) when one is given; it is empty when none of the run is. Null when the run
    /// is too large.
    /// </summary>
    public static Win2dTextMask? Rasterize(Win2dRenderContext context, string text, Font font, PaintPoint point, double letterSpacing,
        double layoutUnitsPerPoint, double deviceScaleX, double deviceScaleY, double translateX, double translateY,
        FontPalette? palette, ShapeSettings features, int subPixels, (int Left, int Top, int Right, int Bottom)? limit = null)
    {
        var typeface = font.Typeface!;
        var metrics = typeface.Metrics;
        var unitsPerEm = metrics.UnitsPerEm;
        if (unitsPerEm == 0)
            return null;

        var advance = TextMeasurement.Measure(text, typeface, font.Size, font.SyntheticStyle, features).Width * layoutUnitsPerPoint
            + letterSpacing * text.Length;

        // Where the run's origin falls on the device grid, and the rows its baseline and ink can reach.
        var originX = point.X * deviceScaleX + translateX;
        var originY = point.Y * deviceScaleY + translateY;
        var pixelsPerEmX = font.Size * layoutUnitsPerPoint * deviceScaleX;
        var pixelsPerEmY = font.Size * layoutUnitsPerPoint * deviceScaleY;
        var baseline = originY + pixelsPerEmY * metrics.LineSpacing / unitsPerEm * metrics.CellAscent / metrics.LineSpacing;
        var above = pixelsPerEmY * metrics.CellAscent / unitsPerEm * 1.25 + 2;
        var below = pixelsPerEmY * metrics.CellDescent / unitsPerEm * 1.25 + 2;
        var slant = (font.SyntheticStyle & SyntheticStyle.Italic) != 0 ? pixelsPerEmY * 0.4 : 0;

        var left = (int)Math.Floor(originX - 2 - pixelsPerEmX * 0.15);
        var right = (int)Math.Ceiling(originX + advance * deviceScaleX + 2 + pixelsPerEmX * 0.15 + slant);
        var top = (int)Math.Floor(baseline - above);
        var bottom = (int)Math.Ceiling(baseline + below);
        if (limit is { } visible)
        {
            left = Math.Max(left, visible.Left);
            top = Math.Max(top, visible.Top);
            right = Math.Min(right, visible.Right);
            bottom = Math.Min(bottom, visible.Bottom);
        }

        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
            return new Win2dTextMask([], left, top, 0, 0, subPixels);

        if ((long)width * height * subPixels > MaxSamples)
            return null;

        var raster = context.MaskContext;
        using var surface = new RasterSurface(width * subPixels, height, 0, 0, deviceScaleX * subPixels, deviceScaleY);
        using var canvas = new PeachDrawing.RasterCanvas(raster, surface, layoutUnitsPerPoint);
        canvas.DrawString(text, font, PaintColor.White, new PaintPoint((originX - left) / deviceScaleX, (originY - top) / deviceScaleY),
            new Size(advance, 0), letterSpacing, palette, features);

        var samples = new byte[width * subPixels * height];
        var buffer = canvas.ToPixelBuffer().PremultipliedRgba.Span;
        for (var i = 0; i < samples.Length; i++)
            samples[i] = buffer[i * 4 + 3];

        return new Win2dTextMask(samples, left, top, width, height, subPixels);
    }
}

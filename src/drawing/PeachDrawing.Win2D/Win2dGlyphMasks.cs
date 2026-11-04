using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Win2D;

/// <summary>
/// Makes the coverage mask of a word from the masks of its letters. A word seen for the first time is mostly letters seen before, and rasterizing each
/// letter again (fitting its outline to the pixel grid, then filling it) is nearly all of what a word costs; here every letter is rasterized once for each
/// font, size and position within a pixel, and a word is those masks added up at the places its letters go.
/// </summary>
/// <remarks>
/// A letter's position within its pixel is kept to 1/16 of a pixel, so a letter lands up to 1/32 of a pixel from where the word as a whole would have put
/// it; the coverage where two letters overlap is added (and capped), not united. Both differences are far below what the stripes of an LCD pixel can show.
/// </remarks>
internal sealed class Win2dGlyphMaskCache
{
    private const long Budget = 24L * 1024 * 1024;
    private const int PhaseSteps = 16;
    private const int MaxSamples = 6_000_000;

    /// <summary>Whether words are made from their letters' masks. Off only to compare with the word-at-once result.</summary>
    internal static bool Enabled { get; set; } = true;

    private readonly record struct Key(Typeface Typeface, int Glyph, double Size, double Ppp, double ScaleX, double ScaleY, int SubPixels, int PhaseX, int PhaseY,
        TextHinting Hinting, bool StemDarkening);

    // A glyph's coverage cut to its ink; the offsets are from the pixel the glyph's origin falls in.
    private sealed record Entry(Key Key, byte[] Samples, int OffsetX, int OffsetY, int Width, int Height);

    private readonly object _gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _order = new();
    private long _bytes;

    internal int Hits { get; private set; }

    internal int Misses { get; private set; }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
            _bytes = 0;
        }
    }

    /// <summary>
    /// The mask of <paramref name="text"/> with its origin at (<paramref name="translateX"/>, <paramref name="translateY"/>) device pixels (a fraction of
    /// a pixel, since the mask is placed on whole pixels afterwards), or null when this word cannot be made from letters (the caller rasterizes it whole).
    /// </summary>
    internal Win2dTextMask? TryRasterize(Win2dRenderContext context, string text, Font font, double letterSpacing, double layoutUnitsPerPoint,
        double deviceScaleX, double deviceScaleY, double translateX, double translateY, FontPalette? palette, ShapeSettings features, int subPixels)
    {
        if (!Enabled || palette is not null || font.SyntheticStyle != SyntheticStyle.None || font.Typeface is not { } typeface)
            return null;

        var metrics = typeface.Metrics;
        var unitsPerEm = metrics.UnitsPerEm;
        if (unitsPerEm == 0)
            return null;

        var glyphs = Shaper.Shape(typeface, text, features).Glyphs;
        if (glyphs.Count == 0)
            return null;

        // The extent of the word, as the word-at-once rasterizer works it out.
        var designToLayout = font.Size / unitsPerEm * layoutUnitsPerPoint;
        double advance = 0;
        foreach (var glyph in glyphs)
            advance += (typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * designToLayout + letterSpacing;

        var pixelsPerEmX = font.Size * layoutUnitsPerPoint * deviceScaleX;
        var pixelsPerEmY = font.Size * layoutUnitsPerPoint * deviceScaleY;
        var baselineLayout = metrics.CellAscent * font.Size / unitsPerEm * layoutUnitsPerPoint;
        var baseline = translateY + (baselineLayout * deviceScaleY);
        var above = pixelsPerEmY * metrics.CellAscent / unitsPerEm * 1.25 + 2;
        var below = pixelsPerEmY * metrics.CellDescent / unitsPerEm * 1.25 + 2;

        var left = (int)Math.Floor(translateX - 2 - (pixelsPerEmX * 0.15));
        var right = (int)Math.Ceiling(translateX + (advance * deviceScaleX) + 2 + (pixelsPerEmX * 0.15));
        var top = (int)Math.Floor(baseline - above);
        var bottom = (int)Math.Ceiling(baseline + below);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0 || (long)width * height * subPixels > MaxSamples)
            return null;

        var raster = context.MaskContext;
        var samples = new byte[width * subPixels * height];
        var stride = width * subPixels;
        var pen = 0.0;
        foreach (var glyph in glyphs)
        {
            var x = translateX + ((pen + (glyph.XOffset * designToLayout)) * deviceScaleX) - left;
            var y = baseline - (glyph.YOffset * designToLayout * deviceScaleY) - top;
            pen += ((typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * designToLayout) + letterSpacing;

            var ix = (int)Math.Floor(x);
            var iy = (int)Math.Floor(y);
            var phaseX = (int)Math.Round((x - ix) * PhaseSteps);

            // Vertically the outline is fitted to the pixel grid, which rounds the baseline to a whole pixel: quantizing downward never moves a baseline across
            // the half-pixel where that rounding changes, so the letters land on the row the word as a whole would have put them on.
            var phaseY = (int)Math.Floor(((y - iy) * PhaseSteps) + 1e-6);
            if (phaseX == PhaseSteps)
            {
                phaseX = 0;
                ix++;
            }

            var entry = Glyph(context, font, typeface, glyph.GlyphIndex, layoutUnitsPerPoint, deviceScaleX, deviceScaleY, subPixels, phaseX, phaseY, raster);
            if (entry.Width == 0)
                continue;

            var destX = ix + entry.OffsetX;
            var destY = iy + entry.OffsetY;
            for (var row = 0; row < entry.Height; row++)
            {
                var dy = destY + row;
                if ((uint)dy >= (uint)height)
                    continue;

                for (var col = 0; col < entry.Width; col++)
                {
                    var dx = destX + col;
                    if ((uint)dx >= (uint)width)
                        continue;

                    var from = ((row * entry.Width) + col) * subPixels;
                    var to = (dy * stride) + (dx * subPixels);
                    for (var s = 0; s < subPixels; s++)
                    {
                        var sum = samples[to + s] + entry.Samples[from + s];
                        samples[to + s] = (byte)(sum > 255 ? 255 : sum);
                    }
                }
            }
        }

        return new Win2dTextMask(samples, left, top, width, height, subPixels);
    }

    private Entry Glyph(Win2dRenderContext context, Font font, Typeface typeface, int glyph, double ppp, double scaleX, double scaleY, int subPixels,
        int phaseX, int phaseY, PeachDrawing.RasterRenderContext raster)
    {
        var key = new Key(typeface, glyph, font.Size, ppp, scaleX, scaleY, subPixels, phaseX, phaseY, raster.TextHinting, raster.TextStemDarkening);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                Hits++;
                return node.Value;
            }

            Misses++;
        }

        var made = Rasterize(font, typeface, glyph, ppp, scaleX, scaleY, subPixels, phaseX, phaseY, raster, key);
        lock (_gate)
        {
            if (!_entries.ContainsKey(key))
            {
                _entries[key] = _order.AddFirst(made);
                _bytes += made.Samples.LongLength + 64;
                while (_bytes > Budget && _order.Last is { } oldest)
                {
                    _order.RemoveLast();
                    _entries.Remove(oldest.Value.Key);
                    _bytes -= oldest.Value.Samples.LongLength + 64;
                }
            }
        }

        return made;
    }

    private static Entry Rasterize(Font font, Typeface typeface, int glyph, double ppp, double scaleX, double scaleY, int subPixels, int phaseX, int phaseY,
        PeachDrawing.RasterRenderContext raster, Key key)
    {
        var metrics = typeface.Metrics;
        var unitsPerEm = metrics.UnitsPerEm;
        var pixelsPerEmX = font.Size * ppp * scaleX;
        var pixelsPerEmY = font.Size * ppp * scaleY;

        // Room around the origin for what a letter can reach: left of it (a negative bearing, an accent), right of it (a wide or swash letter), above and below.
        var originX = (int)Math.Ceiling(pixelsPerEmX * 0.5) + 3;
        var originY = (int)Math.Ceiling(pixelsPerEmY * metrics.CellAscent / unitsPerEm * 1.25) + 3;
        var width = originX + (int)Math.Ceiling(pixelsPerEmX * 1.6) + 4;
        var height = originY + (int)Math.Ceiling(pixelsPerEmY * metrics.CellDescent / unitsPerEm * 1.25) + 4;

        using var surface = new RasterSurface(width * subPixels, height, 0, 0, scaleX * subPixels, scaleY);
        using var canvas = new PeachDrawing.RasterCanvas(raster, surface, ppp);
        var placement = new GlyphPlacement(glyph, (originX + ((double)phaseX / PhaseSteps)) / scaleX, (originY + ((double)phaseY / PhaseSteps)) / scaleY);
        canvas.DrawGlyphs(new[] { placement }, font, PaintColor.White);

        var buffer = canvas.ToPixelBuffer().PremultipliedRgba.Span;
        var stride = width * subPixels;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < stride; x++)
            {
                if (buffer[(((y * stride) + x) * 4) + 3] == 0)
                    continue;

                var pixel = x / subPixels;
                if (pixel < minX) minX = pixel;
                if (pixel > maxX) maxX = pixel;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0)
            return new Entry(key, [], 0, 0, 0, 0);

        var cutWidth = maxX - minX + 1;
        var cutHeight = maxY - minY + 1;
        var cut = new byte[cutWidth * cutHeight * subPixels];
        for (var y = 0; y < cutHeight; y++)
        {
            for (var x = 0; x < cutWidth * subPixels; x++)
                cut[(y * cutWidth * subPixels) + x] = buffer[((((minY + y) * stride) + (minX * subPixels) + x) * 4) + 3];
        }

        return new Entry(key, cut, minX - originX, minY - originY, cutWidth, cutHeight);
    }
}

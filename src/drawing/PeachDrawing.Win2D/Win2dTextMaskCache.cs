using PeachDrawing.Core;
using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace PeachDrawing.Win2D;

/// <summary>
/// Remembers rasterized text runs so a run that is drawn again - the same text, font, scale and position on the pixel grid, as when a scrolling view
/// paints the strip it just revealed - is not shaped, hinted and rasterized again. A mask holds coverage only (the colour and what is behind the text
/// are applied when it is drawn), so one entry serves every colour and background.
/// </summary>
/// <remarks>
/// A mask is stored for the run's origin at a fraction of a pixel (quantized to 1/64 unless <see cref="Win2dRenderContext.TextPositionSteps"/> says otherwise) and shifted onto the whole pixel it is wanted at, so a view
/// that scrolls by whole pixels hits the entry made for the same text a screen ago. The entries are immutable once stored.
/// </remarks>
internal sealed class Win2dTextMaskCache
{
    /// <summary>The most bytes of coverage the cache keeps.</summary>
    private const long Budget = 48L * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _order = new();
    private long _bytes;

    internal readonly record struct Key(
        string Text, Typeface Typeface, double Size, SyntheticStyle Synthetic, double LetterSpacing, double Ppp, double ScaleX, double ScaleY,
        int SubPixels, FontPalette? Palette, ShapeSettings Features, int PhaseX, int PhaseY, int PhaseSteps, TextHinting Hinting, bool StemDarkening);

    private sealed record Entry(Key Key, Win2dTextMask Mask, long Bytes);

    /// <summary>How many lookups found a mask, for a test and for <see cref="Win2dTextStatistics"/>.</summary>
    internal int Hits { get; private set; }

    /// <summary>How many lookups did not.</summary>
    internal int Misses { get; private set; }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    /// <summary>Forgets every mask.</summary>
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
    /// The mask of a run whose origin falls at (<paramref name="originX"/>, <paramref name="originY"/>) device pixels, limited to
    /// <paramref name="limit"/> when one is given; null when the run is too large to draw as a mask. The empty mask means no part of the run is in the limit.
    /// </summary>
    internal Win2dTextMask? Get(Win2dRenderContext context, string text, Font font, double letterSpacing, double ppp, double scaleX, double scaleY,
        double originX, double originY, FontPalette? palette, ShapeSettings features, int subPixels, (int Left, int Top, int Right, int Bottom)? limit,
        out Key key, out bool whole)
    {
        var ix = (int)Math.Floor(originX);
        var iy = (int)Math.Floor(originY);
        var phaseSteps = context.TextPositionSteps;
        var phaseX = (int)Math.Round((originX - ix) * phaseSteps);
        var phaseY = (int)Math.Round((originY - iy) * phaseSteps);
        if (phaseX == phaseSteps)
        {
            phaseX = 0;
            ix++;
        }

        if (phaseY == phaseSteps)
        {
            phaseY = 0;
            iy++;
        }

        var raster = context.MaskContext;
        key = new Key(text, font.Typeface!, font.Size, font.SyntheticStyle, letterSpacing, ppp, scaleX, scaleY, subPixels, palette, features,
            phaseX, phaseY, phaseSteps, raster.TextHinting, raster.TextStemDarkening);
        whole = false;

        Win2dTextMask? mask = null;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                mask = node.Value.Mask;
                Hits++;
            }
            else
            {
                Misses++;
            }
        }

        if (mask is null)
        {
            var started = Stopwatch.GetTimestamp();

            // At the origin of the grid, so the mask is the same wherever the run is drawn on whole pixels.
            // From the masks of its letters when they can be (most of a new word's letters have been drawn before), whole otherwise.
            mask = context.GlyphMasks.TryRasterize(context, text, font, letterSpacing, ppp, scaleX, scaleY,
                    phaseX / (double)phaseSteps, phaseY / (double)phaseSteps, palette, features, subPixels)
                ?? Win2dTextMask.Rasterize(context, text, font, new PaintPoint(0, 0), letterSpacing, ppp, scaleX, scaleY,
                    phaseX / (double)phaseSteps, phaseY / (double)phaseSteps, palette, features, subPixels);
            Win2dTextStatistics.Add(Win2dTextStatistics.Stage.Rasterize, Stopwatch.GetTimestamp() - started);
            if (mask is not { } rasterized)
                return null;

            var made = Trim(rasterized);

            var bytes = made.Samples.LongLength + 64;
            if (bytes <= Budget / 8)
            {
                lock (_gate)
                {
                    if (!_entries.ContainsKey(key))
                    {
                        var entry = new Entry(key, made, bytes);
                        _entries[key] = _order.AddFirst(entry);
                        _bytes += bytes;
                        while (_bytes > Budget && _order.Last is { } oldest)
                        {
                            _order.RemoveLast();
                            _entries.Remove(oldest.Value.Key);
                            _bytes -= oldest.Value.Bytes;
                        }
                    }
                }
            }

            mask = made;
        }

        var placed = Place(mask.Value, ix, iy, limit, out whole);
        return placed;
    }

    // The mask cut to the pixels that carry ink, one more all round for the filter that spreads a stripe's coverage into its neighbours: the
    // padding the rasterizer leaves around a run would otherwise overlap the words next to it.
    private static Win2dTextMask Trim(Win2dTextMask mask)
    {
        if (mask.Width == 0 || mask.Height == 0)
            return mask;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        var stride = mask.Width * mask.SubPixels;
        for (var y = 0; y < mask.Height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < stride; x++)
            {
                if (mask.Samples[row + x] == 0)
                    continue;

                var pixel = x / mask.SubPixels;
                if (pixel < minX) minX = pixel;
                if (pixel > maxX) maxX = pixel;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0)
            return new Win2dTextMask([], mask.Left, mask.Top, 0, 0, mask.SubPixels);

        minX = Math.Max(0, minX - 1);
        minY = Math.Max(0, minY - 1);
        maxX = Math.Min(mask.Width - 1, maxX + 1);
        maxY = Math.Min(mask.Height - 1, maxY + 1);
        if (minX == 0 && minY == 0 && maxX == mask.Width - 1 && maxY == mask.Height - 1)
            return mask;

        var width = maxX - minX + 1;
        var height = maxY - minY + 1;
        var rowLength = width * mask.SubPixels;
        var samples = new byte[rowLength * height];
        for (var y = 0; y < height; y++)
            Array.Copy(mask.Samples, ((minY + y) * stride) + (minX * mask.SubPixels), samples, y * rowLength, rowLength);

        return new Win2dTextMask(samples, mask.Left + minX, mask.Top + minY, width, height, mask.SubPixels);
    }

    // Moves the mask (made with its origin at 0, 0) onto the pixel it is wanted at and cuts it to the part that can be seen.
    private static Win2dTextMask Place(Win2dTextMask mask, int ix, int iy, (int Left, int Top, int Right, int Bottom)? limit, out bool whole)
    {
        var left = mask.Left + ix;
        var top = mask.Top + iy;
        whole = true;
        if (mask.Width == 0)
            return mask with { Left = left, Top = top };

        if (limit is not { } visible)
            return mask with { Left = left, Top = top };

        var cutLeft = Math.Max(left, visible.Left);
        var cutTop = Math.Max(top, visible.Top);
        var cutRight = Math.Min(left + mask.Width, visible.Right);
        var cutBottom = Math.Min(top + mask.Height, visible.Bottom);
        if (cutRight <= cutLeft || cutBottom <= cutTop)
        {
            whole = false;
            return new Win2dTextMask([], cutLeft, cutTop, 0, 0, mask.SubPixels);
        }

        if (cutLeft == left && cutTop == top && cutRight == left + mask.Width && cutBottom == top + mask.Height)
            return mask with { Left = left, Top = top };

        whole = false;
        var width = cutRight - cutLeft;
        var height = cutBottom - cutTop;
        var stride = mask.Width * mask.SubPixels;
        var rowLength = width * mask.SubPixels;
        var samples = new byte[rowLength * height];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(mask.Samples, ((cutTop - top + row) * stride) + ((cutLeft - left) * mask.SubPixels), samples, row * rowLength, rowLength);
        }

        return new Win2dTextMask(samples, cutLeft, cutTop, width, height, mask.SubPixels);
    }
}

/// <summary>
/// Optional timing of the text path, written to the debugger output every couple of seconds while text is drawn when the environment variable
/// <c>PEACH_TEXT_STATS</c> is set: how long went into rasterizing runs, reading the pixels behind them, blending and uploading, and how often the mask cache hit.
/// </summary>
internal static class Win2dTextStatistics
{
    internal enum Stage { Rasterize, Readback, Blend, Upload }

    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("PEACH_TEXT_STATS") is { Length: > 0 };
    private static readonly long[] Ticks = new long[4];
    private static readonly long[] Counts = new long[4];
    private static long _lastReport = Stopwatch.GetTimestamp();

    internal static void Add(Stage stage, long ticks)
    {
        if (!Enabled)
            return;

        Interlocked.Add(ref Ticks[(int)stage], ticks);
        Interlocked.Increment(ref Counts[(int)stage]);
    }

    internal static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    internal static void Stop(Stage stage, long started)
    {
        if (Enabled)
            Add(stage, Stopwatch.GetTimestamp() - started);
    }

    internal static void Report(Win2dTextMaskCache cache)
    {
        if (!Enabled)
            return;

        var now = Stopwatch.GetTimestamp();
        if (now - _lastReport < Stopwatch.Frequency * 2)
            return;

        _lastReport = now;
        var line = "PEACH_TEXT_STATS";
        foreach (Stage stage in Enum.GetValues<Stage>())
        {
            var ms = Interlocked.Exchange(ref Ticks[(int)stage], 0) * 1000.0 / Stopwatch.Frequency;
            var count = Interlocked.Exchange(ref Counts[(int)stage], 0);
            line += $" {stage}={ms:F1}ms/{count}";
        }

        line += $" cache hits={cache.Hits} misses={cache.Misses} entries={cache.Count}";
        Debug.WriteLine(line);
        if (Environment.GetEnvironmentVariable("PEACH_TEXT_STATS_FILE") is { Length: > 0 } file)
            System.IO.File.AppendAllText(file, line + Environment.NewLine);
    }
}

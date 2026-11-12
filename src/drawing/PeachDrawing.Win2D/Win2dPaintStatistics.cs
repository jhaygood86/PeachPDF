using System;
using System.Diagnostics;
using System.Threading;

namespace PeachDrawing.Win2D;

/// <summary>
/// Optional accounting of what one paint cost on the GPU side, written when the environment variable <c>PEACH_PAINT_STATS</c> is set: one line each
/// time a top-level canvas is disposed (a view paints one canvas per region it was asked to redraw), with how often and for how long the paint
/// flushed queued text, opened clip layers or surface clips (each a copy of the whole target), recorded command lists, and decoded an image on the
/// spot. <c>PEACH_PAINT_STATS_FILE</c> appends the lines to a file as well as the debugger output. Off, every call is a branch on a static.
/// </summary>
internal static class Win2dPaintStatistics
{
    internal enum Counter { TextFlush, RectClip, GeometryClip, SurfaceClip, SurfaceClipEnd, SurfaceClipWithText, SurfaceClipEmpty, CommandList, ImageLoad, ImageDraw, TextMask, ClipRelease }

    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("PEACH_PAINT_STATS") is { Length: > 0 };
    private static readonly long[] Ticks = new long[Enum.GetValues<Counter>().Length];
    private static readonly long[] Counts = new long[Enum.GetValues<Counter>().Length];

    internal static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0;

    internal static void Stop(Counter counter, long started)
    {
        if (!Enabled)
            return;

        Interlocked.Add(ref Ticks[(int)counter], Stopwatch.GetTimestamp() - started);
        Interlocked.Increment(ref Counts[(int)counter]);
    }

    internal static void Count(Counter counter)
    {
        if (Enabled)
            Interlocked.Increment(ref Counts[(int)counter]);
    }

    /// <summary>Writes (and clears) what has been counted since the last paint ended; nothing when nothing was counted.</summary>
    internal static void EndPaint()
    {
        if (!Enabled)
            return;

        var line = "PEACH_PAINT_STATS(win2d)";
        var any = false;
        foreach (var counter in Enum.GetValues<Counter>())
        {
            var count = Interlocked.Exchange(ref Counts[(int)counter], 0);
            var ms = Interlocked.Exchange(ref Ticks[(int)counter], 0) * 1000.0 / Stopwatch.Frequency;
            any |= count > 0;
            line += ms > 0 ? $" {counter}={count}/{ms:F1}ms" : $" {counter}={count}";
        }

        if (any)
            Write(line);
    }

    internal static void Write(string line)
    {
        Debug.WriteLine(line);
        if (Environment.GetEnvironmentVariable("PEACH_PAINT_STATS_FILE") is { Length: > 0 } file)
        {
            lock (Ticks)
                System.IO.File.AppendAllText(file, line + Environment.NewLine);
        }
    }
}

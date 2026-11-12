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

    /// <summary>Whether the statistics are kept: the environment variable, unless a test changes it.</summary>
    internal static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("PEACH_PAINT_STATS") is { Length: > 0 };

    /// <summary>Told each line instead of the debugger output and the file, when set.</summary>
    internal static Action<string>? Sink { get; set; }

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

    private static long _lastMemoryNote;

    // What the images' bitmaps on the device take, kept only to be shown in the memory line.
    private static long _imageBytes;
    private static long _imageCount;
    private static long _surfaceMade;
    private static long _surfaceMadeBytes;
    private static long _surfaceMaxBytes;

    /// <summary>A clip surface was allocated (not taken from the pool): how big.</summary>
    internal static void SurfaceMade(long bytes)
    {
        Interlocked.Increment(ref _surfaceMade);
        Interlocked.Add(ref _surfaceMadeBytes, bytes);
        long seen;
        while (bytes > (seen = Interlocked.Read(ref _surfaceMaxBytes)) && Interlocked.CompareExchange(ref _surfaceMaxBytes, bytes, seen) != seen)
        {
        }
    }

    internal static void ImageBitmap(long bytes)
    {
        Interlocked.Add(ref _imageBytes, bytes);
        Interlocked.Add(ref _imageCount, Math.Sign(bytes));
    }

    /// <summary>About once a second, one line with what the render context holds on to: the text blend's scratch targets and each cache.</summary>
    internal static void NoteMemory(Win2dRenderContext context, bool force = false)
    {
        if (!Enabled)
            return;

        var now = Stopwatch.GetTimestamp();
        if (!force && now - _lastMemoryNote < Stopwatch.Frequency)
            return;

        _lastMemoryNote = now;
        static string Mb(long bytes) => $"{bytes / 1048576.0:F1}MB";
        Write($"PEACH_PAINT_STATS(memory) lcdScratch={Mb(context.LcdScratch.ByteCount)} ({context.LcdScratch.Describe()}) textMasks={Mb(context.TextMasks.ByteCount)} " +
            $"glyphMasks={Mb(context.GlyphMasks.ByteCount)} textBitmaps={Mb(context.TextBitmaps.ByteCount)} imageBitmaps={Mb(Interlocked.Read(ref _imageBytes))}/{Interlocked.Read(ref _imageCount)} surfacesMade={Interlocked.Read(ref _surfaceMade)} ({Mb(Interlocked.Read(ref _surfaceMadeBytes))} total, largest {Mb(Interlocked.Read(ref _surfaceMaxBytes))}) managed={Mb(GC.GetTotalMemory(false))}");
    }

    internal static void Write(string line)
    {
        if (Sink is { } sink)
        {
            sink(line);
            return;
        }

        Debug.WriteLine(line);
        if (Environment.GetEnvironmentVariable("PEACH_PAINT_STATS_FILE") is { Length: > 0 } file)
        {
            lock (Ticks)
                System.IO.File.AppendAllText(file, line + Environment.NewLine);
        }
    }
}

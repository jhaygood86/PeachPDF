using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachBrackets.WinUI;

/// <summary>
/// What painting a view's invalidated regions cost, kept for the example's scroll measurement (<c>--scroll-bench</c>): how long each
/// <c>RegionsInvalidated</c> event took on the UI thread and how much it painted.
/// </summary>
internal static class HtmlViewDiagnostics
{
    private static readonly object Gate = new();
    private static readonly List<(double Milliseconds, double Pixels, int Regions, long AllocatedBytes)> Events = [];

    private static readonly List<(double IntervalMs, double AdvanceMs, int Regions, long AllocatedBytes)> AnimationFrames = [];

    internal static bool Recording { get; set; }

    /// <summary>One animation frame: the time since the previous one, what stepping the document cost, and how many regions it asked to repaint.</summary>
    internal static void AddAnimationFrame(double intervalMilliseconds, double advanceMilliseconds, int regions, long allocatedBytes)
    {
        lock (Gate)
            AnimationFrames.Add((intervalMilliseconds, advanceMilliseconds, regions, allocatedBytes));
    }

    internal static List<(double IntervalMs, double AdvanceMs, int Regions, long AllocatedBytes)> TakeAnimationFrames()
    {
        lock (Gate)
        {
            var taken = new List<(double IntervalMs, double AdvanceMs, int Regions, long AllocatedBytes)>(AnimationFrames);
            AnimationFrames.Clear();
            return taken;
        }
    }

    /// <summary>Told of anything that took long on the UI thread, when set.</summary>
    internal static Action<string>? Trace { get; set; }

    internal static double MillisecondsSince(long started) => (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

    // What the UI thread had allocated when the event began, so its end can say what the event cost in memory.
    [ThreadStatic]
    private static long _allocatedAtStart;

    internal static long Start()
    {
        if (!Recording) return 0;

        _allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
        return Stopwatch.GetTimestamp();
    }

    internal static void Stop(long started, double pixels, int regions)
    {
        if (!Recording)
            return;

        var milliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        lock (Gate)
            Events.Add((milliseconds, pixels, regions, GC.GetAllocatedBytesForCurrentThread() - _allocatedAtStart));
    }

    internal static List<(double Milliseconds, double Pixels, int Regions, long AllocatedBytes)> Take()
    {
        lock (Gate)
        {
            var taken = new List<(double Milliseconds, double Pixels, int Regions, long AllocatedBytes)>(Events);
            Events.Clear();
            return taken;
        }
    }
}

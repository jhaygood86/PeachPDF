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
    private static readonly List<(double Milliseconds, double Pixels, int Regions)> Events = [];

    internal static bool Recording { get; set; }

    /// <summary>Told of anything that took long on the UI thread, when set.</summary>
    internal static Action<string>? Trace { get; set; }

    internal static double MillisecondsSince(long started) => (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

    internal static long Start() => Recording ? Stopwatch.GetTimestamp() : 0;

    internal static void Stop(long started, double pixels, int regions)
    {
        if (!Recording)
            return;

        var milliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        lock (Gate)
            Events.Add((milliseconds, pixels, regions));
    }

    internal static List<(double Milliseconds, double Pixels, int Regions)> Take()
    {
        lock (Gate)
        {
            var taken = new List<(double, double, int)>(Events);
            Events.Clear();
            return taken;
        }
    }
}

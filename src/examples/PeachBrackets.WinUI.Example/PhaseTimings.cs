using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PeachBrackets.WinUI.Example;

/// <summary>
/// Totals the load pipeline's timing spans (the <c>PeachBrackets</c> activity source) per phase, for the status bar and, when the environment variable
/// <c>PEACH_TIMINGS_FILE</c> names a file, a report of every phase and every web request. The spans nest (an image loads while the page is laid out,
/// and each request is inside what asked for it), so the totals are not meant to add up to the load time; they say where it went.
/// </summary>
internal static class PhaseTimings
{
    private sealed record Total(int Count, double Milliseconds);

    private sealed record Request(string Url, string Cache, string Status, double Milliseconds);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Total> Totals = [];
    private static readonly List<Request> Requests = [];
    private static bool _started;

    /// <summary>Starts listening (once).</summary>
    public static void Start()
    {
        if (_started)
            return;

        _started = true;
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == "PeachBrackets",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = Record,
        });
    }

    /// <summary>Forgets what was recorded, for the next load.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Totals.Clear();
            Requests.Clear();
        }
    }

    private static void Record(Activity activity)
    {
        var milliseconds = activity.Duration.TotalMilliseconds;
        lock (Gate)
        {
            Totals[activity.OperationName] = Totals.TryGetValue(activity.OperationName, out var total)
                ? new Total(total.Count + 1, total.Milliseconds + milliseconds)
                : new Total(1, milliseconds);

            if (activity.OperationName == "resource.fetch" && Requests.Count < 5000)
            {
                Requests.Add(new Request(
                    activity.GetTagItem("url")?.ToString() ?? string.Empty,
                    activity.GetTagItem("cache")?.ToString() ?? string.Empty,
                    activity.GetTagItem("status")?.ToString() ?? "-",
                    milliseconds));
            }
        }
    }

    /// <summary>The phases that took longest, longest first, as <c>name total ms (count)</c>.</summary>
    public static string Summary(int top = 5)
    {
        lock (Gate)
        {
            return string.Join(", ", Totals.OrderByDescending(t => t.Value.Milliseconds).Take(top)
                .Select(t => $"{t.Key} {t.Value.Milliseconds:0} ms" + (t.Value.Count > 1 ? $" (x{t.Value.Count})" : string.Empty)));
        }
    }

    /// <summary>Writes every phase and every web request to <paramref name="path"/>.</summary>
    public static void WriteReport(string path)
    {
        var report = new StringBuilder();
        lock (Gate)
        {
            report.AppendLine("phase\tcount\ttotal ms");
            foreach (var total in Totals.OrderByDescending(t => t.Value.Milliseconds))
                report.AppendLine($"{total.Key}\t{total.Value.Count}\t{total.Value.Milliseconds:0.0}");

            report.AppendLine();
            report.AppendLine($"web requests: {Requests.Count} ({Requests.Count(r => r.Cache == "hit")} from the cache)");
            report.AppendLine("ms\tcache\tstatus\turl");
            foreach (var request in Requests.OrderByDescending(r => r.Milliseconds))
                report.AppendLine($"{request.Milliseconds:0.0}\t{request.Cache}\t{request.Status}\t{request.Url}");
        }

        File.WriteAllText(path, report.ToString());
    }
}

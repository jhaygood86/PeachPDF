using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace PeachBrackets.WinUI.Example;

/// <summary>
/// <c>--anim-bench &lt;result file&gt; [slug ...]</c>: plays the animation showcases (by default the keyframes, the 120-box wall and the animated GIF) in a
/// real window for a few seconds each and writes how the frames came: the time between frames (the display's 60 or 120 fps is 16.7 or 8.3 ms), what
/// stepping the document cost, and how long each repaint of the changed regions took. Below 60 fps is a defect. Run it in Release on an otherwise idle machine.
/// </summary>
internal static class AnimationBench
{
    // What started each collection of the second generation: an allocation of a large object, the runtime itself, a call to GC.Collect, memory pressure...
    // Only attached while PEACH_ANIM_GC is set, since it makes the runtime report every collection.
    private sealed class GcReasons : System.Diagnostics.Tracing.EventListener
    {
        public readonly Dictionary<string, int> Gen2 = [];

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Informational, (System.Diagnostics.Tracing.EventKeywords)1);
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (e.EventName is not { } name || !name.StartsWith("GCStart", StringComparison.Ordinal) || e.Payload is null || e.PayloadNames is null)
                return;

            var depth = e.PayloadNames.IndexOf("Depth") is var d and >= 0 ? Convert.ToInt32(e.Payload[d]) : -1;
            if (depth < 2) return;

            var reason = e.PayloadNames.IndexOf("Reason") is var r and >= 0 ? Convert.ToInt32(e.Payload[r]) : -1;
            var text = reason switch { 0 => "AllocSmall", 1 => "Induced", 2 => "LowMemory", 3 => "Empty", 4 => "AllocLarge", 5 => "OutOfSpaceSOH", 6 => "OutOfSpaceLOH", 7 => "InducedNotForced", 8 => "Internal", 9 => "InducedLowMemory", 10 => "InducedCompacting", 11 => "LowMemoryHost", 12 => "PMFullGC", 13 => "LowMemoryHostBlocking", _ => "reason " + reason };
            lock (Gen2) Gen2[text] = Gen2.GetValueOrDefault(text) + 1;
        }
    }

    private const double ViewWidth = 1200;
    private const double ViewHeight = 800;
    private const int SecondsPerShowcase = 6;

    public static async void Run(Window window, string resultPath, string[] slugs, Application app)
    {
        var report = new StringBuilder();
        try
        {
            var host = new Microsoft.UI.Xaml.Controls.Grid { Width = ViewWidth, Height = ViewHeight };
            window.Content = host;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)ViewWidth, (int)ViewHeight));
            report.AppendLine($"configuration={Configuration}");
            if (Environment.GetEnvironmentVariable("PEACH_ANIM_TRACE") is { Length: > 0 } tracePath)
                HtmlViewDiagnostics.Trace = text => File.AppendAllText(tracePath, text + Environment.NewLine);

            foreach (var slug in slugs.Length > 0 ? slugs : ["screen_animation_keyframes", "screen_animation_stress", "screen_animation_images"])
            {
                var showcase = ShowcaseCatalog.Load().First(s => s.Slug == slug);
                var view = new HtmlView { Input = PeachBrackets.InputCapabilities.Mouse, AnimationsEnabled = true };
                host.Children.Add(view);
                var loaded = new TaskCompletionSource();
                view.DocumentLoaded += (_, _) => loaded.TrySetResult();
                view.DocumentFailed += (_, e) => loaded.TrySetException(e.Exception);
                // PEACH_ANIM_HTML plays another file in place of the showcase, to compare a change to a page with the page itself.
                view.Html = File.ReadAllText(Environment.GetEnvironmentVariable("PEACH_ANIM_HTML") is { Length: > 0 } other ? other : showcase.Path);
                await loaded.Task;
                await Task.Delay(1000);

                HtmlViewDiagnostics.Recording = true;
                HtmlViewDiagnostics.Take();
                HtmlViewDiagnostics.TakeAnimationFrames();
                if (Environment.GetEnvironmentVariable("PEACH_ANIM_LATENCY") is { Length: > 0 })
                    System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;

                using var gcReasons =Environment.GetEnvironmentVariable("PEACH_ANIM_GC") is { Length: > 0 } ? new GcReasons() : null;
                var gen0 = GC.CollectionCount(0);
                var gen2 = GC.CollectionCount(2);
                var pause = GC.GetTotalPauseDuration();
                var allocated = GC.GetTotalAllocatedBytes();
                await Task.Delay(SecondsPerShowcase * 1000);
                var frames = HtmlViewDiagnostics.TakeAnimationFrames();
                var paints = HtmlViewDiagnostics.Take();
                HtmlViewDiagnostics.Recording = false;

                report.AppendLine();
                report.AppendLine($"== {slug} (running={view.Document?.HasRunningAnimations})");
                var intervals = frames.Select(f => f.IntervalMs).Where(v => v > 0).OrderBy(v => v).ToArray();
                var advance = frames.Select(f => f.AdvanceMs).OrderBy(v => v).ToArray();
                var paint = paints.Select(p => p.Milliseconds).OrderBy(v => v).ToArray();
                var fps = intervals.Length == 0 ? 0 : 1000.0 / intervals.Average();
                report.AppendLine($"frames={frames.Count} fps={fps:F1} interval p50={Percentile(intervals, 0.5):F1} p95={Percentile(intervals, 0.95):F1} p99={Percentile(intervals, 0.99):F1} max={(intervals.Length == 0 ? 0 : intervals[^1]):F1} ms; slower than 60 fps: {intervals.Count(v => v > 17.5)}");
                report.AppendLine($"advance (document step) p50={Percentile(advance, 0.5):F2} p95={Percentile(advance, 0.95):F2} max={(advance.Length == 0 ? 0 : advance[^1]):F2} ms; regions per frame avg={(frames.Count == 0 ? 0 : frames.Average(f => f.Regions)):F1}");
                report.AppendLine($"repaint events={paints.Count} p50={Percentile(paint, 0.5):F2} p95={Percentile(paint, 0.95):F2} max={(paint.Length == 0 ? 0 : paint[^1]):F2} ms");
                report.AppendLine($"GC: gen0={GC.CollectionCount(0) - gen0} gen2={GC.CollectionCount(2) - gen2} pause={(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:F0} ms, allocated={(GC.GetTotalAllocatedBytes() - allocated) / 1048576.0:F1} MB");

                if (gcReasons is not null)
                {
                    lock (gcReasons.Gen2)
                        report.AppendLine("gen2 started by: " + string.Join(", ", gcReasons.Gen2.Select(p => $"{p.Key}={p.Value}")));
                }

                host.Children.Remove(view);
            }

            report.Insert(0, "OK\n");
        }
        catch (Exception e)
        {
            report.Insert(0, "FAILED: " + e + "\n");
        }

        File.WriteAllText(resultPath, report.ToString());
        app.Exit();
        Environment.Exit(0);
    }

    private static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static double Percentile(double[] sorted, double p) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];
}

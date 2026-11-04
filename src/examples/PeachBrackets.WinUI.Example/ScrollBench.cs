using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace PeachBrackets.WinUI.Example;

/// <summary>
/// <c>--scroll-bench &lt;result file&gt; [slug]</c>: scrolls a showcase (by default the one very long page) in a real window the way a person does - a
/// steady drag, bursts of mouse-wheel notches with pauses between them, a fling - and writes how the UI thread coped: how long it took to run
/// something posted to it (a stall shows as a long wait), how long each repaint of the revealed regions took, and what the garbage collector did.
/// Run it in Release on an otherwise idle machine.
/// </summary>
internal static class ScrollBench
{
    private const double ViewWidth = 1200;
    private const double ViewHeight = 800;

    public static async void Run(Window window, string resultPath, string slug, Application app)
    {
        var report = new StringBuilder();
        LogPath = resultPath + ".log";
        HtmlViewDiagnostics.Trace = Trace;
        void Log(string text) => File.AppendAllText(resultPath + ".log", $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}");
        try
        {
            Log("start");
            var showcase = ShowcaseCatalog.Load().First(s => s.Slug == slug);
            var host = new Microsoft.UI.Xaml.Controls.Grid { Width = ViewWidth, Height = ViewHeight };
            window.Content = host;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)ViewWidth, (int)ViewHeight));

            var view = new HtmlView { Input = PeachBrackets.InputCapabilities.Mouse };
            host.Children.Add(view);
            var loaded = new TaskCompletionSource();
            view.DocumentLoaded += (_, _) => loaded.TrySetResult();
            view.DocumentFailed += (_, e) => loaded.TrySetException(e.Exception);
            var loadStarted = Stopwatch.GetTimestamp();
            if (showcase.IsWeb)
            {
                view.AllowNetworkAccess = true;
                view.Source = new Uri(showcase.Path);
            }
            else
            {
                view.Html = File.ReadAllText(showcase.Path);
            }
            Log("waiting for the document");
            await loaded.Task;
            Log("loaded");
            report.AppendLine($"slug={slug} loaded in {Elapsed(loadStarted):F0} ms, configuration={Configuration}, tiered={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}");

            var contentHeight = view.Document!.GetContentHeight(0) * 96.0 / 72.0;
            var maxY = Math.Max(0, contentHeight - ViewHeight);
            report.AppendLine($"content height={contentHeight:F0} px, max offset={maxY:F0} px");

            await Task.Delay(1500);
            Log("scenarios start");
            HtmlViewDiagnostics.Recording = true;

            await Scenario(view, report, "steady drag, 12 px per 16 ms for 3 s", 0, maxY, async (scroll) =>
            {
                for (var i = 0; i < 187; i++)
                {
                    scroll(12);
                    await Task.Delay(16);
                }
            }, settle: 1500);

            await Scenario(view, report, "wheel bursts: 8 notches of 120 px, 16 ms apart, then 500 ms still, x6", 0, maxY, async (scroll) =>
            {
                for (var burst = 0; burst < 6; burst++)
                {
                    for (var i = 0; i < 8; i++)
                    {
                        scroll(120);
                        await Task.Delay(16);
                    }

                    await Task.Delay(500);
                }
            }, settle: 1000);

            await Scenario(view, report, "fling: 60 px per 16 ms for 1.5 s", 0, maxY, async (scroll) =>
            {
                for (var i = 0; i < 94; i++)
                {
                    scroll(60);
                    await Task.Delay(16);
                }
            }, settle: 1500);

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

    internal static string? LogPath;

    private static void Trace(string text) { if (LogPath is not null) File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {text}{Environment.NewLine}"); }

    private static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static async Task Scenario(HtmlView view, StringBuilder report, string name, double startY, double maxY, Func<Action<double>, Task> drive, int settle)
    {
        var y = startY;
        Trace(name + ": begin");
        view.ScrollToY(y, animate: false);
        await Task.Delay(1000);
        Trace(name + ": at top");
        HtmlViewDiagnostics.Take();

        var steps = 0;
        void Scroll(double delta)
        {
            y = Math.Min(maxY, y + delta);
            if (++steps % 10 == 0)
                Trace($"step {steps} y={y:F0}");
            view.ScrollToY(y, animate: false);
        }

        var latencies = new List<double>();
        var gate = new object();
        var stop = false;
        var queue = DispatcherQueue.GetForCurrentThread();
        var probe = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                var posted = Stopwatch.GetTimestamp();
                queue.TryEnqueue(() =>
                {
                    var wait = Elapsed(posted);
                    lock (gate)
                        latencies.Add(wait);
                });
                Thread.Sleep(4);
            }
        }) { IsBackground = true };

        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var pause = GC.GetTotalPauseDuration();
        var allocated = GC.GetTotalAllocatedBytes();
        var started = Stopwatch.GetTimestamp();
        probe.Start();

        await drive(Scroll);
        var scrollingMs = Elapsed(started);
        var eventsWhileScrolling = HtmlViewDiagnostics.Take();

        // Then still: whatever the view does once scrolling has stopped (the sharper text) is part of what a person feels.
        await Task.Delay(settle);
        Volatile.Write(ref stop, true);
        probe.Join();
        var eventsAfter = HtmlViewDiagnostics.Take();

        report.AppendLine();
        report.AppendLine($"== {name}");
        double[] copy;
        lock (gate)
            copy = latencies.OrderBy(v => v).ToArray();

        report.AppendLine($"UI thread latency of a posted callback (n={copy.Length}): p50={Percentile(copy, 0.5):F1} p95={Percentile(copy, 0.95):F1} p99={Percentile(copy, 0.99):F1} max={copy.LastOrDefault():F1} ms; over 16 ms: {copy.Count(v => v > 16)}; over 50 ms: {copy.Count(v => v > 50)}; over 100 ms: {copy.Count(v => v > 100)}");
        Describe(report, "regions painted while scrolling", eventsWhileScrolling, scrollingMs);
        Describe(report, $"regions painted in the {settle} ms after it stopped", eventsAfter, settle);
        report.AppendLine($"GC: gen0={GC.CollectionCount(0) - gen0} gen1={GC.CollectionCount(1) - gen1} gen2={GC.CollectionCount(2) - gen2} pause={(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:F0} ms, allocated={(GC.GetTotalAllocatedBytes() - allocated) / 1048576.0:F1} MB");
    }

    private static void Describe(StringBuilder report, string what, List<(double Milliseconds, double Pixels, int Regions)> events, double windowMs)
    {
        if (events.Count == 0)
        {
            report.AppendLine($"{what}: none");
            return;
        }

        var times = events.Select(e => e.Milliseconds).OrderBy(v => v).ToArray();
        var pixels = events.Sum(e => e.Pixels);
        report.AppendLine($"{what}: events={events.Count} regions={events.Sum(e => e.Regions)} total={times.Sum():F0} ms ({times.Sum() / windowMs:P0} of {windowMs:F0} ms) p50={Percentile(times, 0.5):F1} p95={Percentile(times, 0.95):F1} max={times[^1]:F1} ms; painted {pixels / 1e6:F2} Mpx ({times.Sum() / Math.Max(pixels / 1e6, 0.001):F0} ms per Mpx)");
    }

    private static double Percentile(double[] sorted, double p) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];

    private static double Elapsed(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
}

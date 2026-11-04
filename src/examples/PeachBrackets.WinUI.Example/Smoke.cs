using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using PeachDrawing.Core;
using System.Text;

namespace PeachBrackets.WinUI.Example;

internal static class Smoke
{
    public static void Run(HtmlView view, string resultPath, Application app)
    {
        var report = new StringBuilder();
        var done = false;

        void Finish(string status)
        {
            if (done)
                return;

            done = true;
            report.Insert(0, status + "\n");
            File.WriteAllText(resultPath, report.ToString());
            app.Exit();
            Environment.Exit(status == "OK" ? 0 : 1);
        }

        view.DocumentFailed += (_, e) => Finish("FAILED: " + e.Exception);
        view.DocumentLoaded += async (_, _) =>
        {
            try
            {
                var document = view.Document!;
                report.AppendLine($"pages={document.Pages.Count}");

                // Give the first paint a moment, then ask the document what it says about a few points.
                await Task.Delay(500);
                var link = document.GetElementBounds("link")!.Value;
                var card = document.GetElementBounds("card")!.Value;
                var locked = document.GetElementBounds("locked")!.Value;
                var pinned = document.GetElementBounds("pinned")!.Value;
                report.AppendLine($"link-cursor={document.GetCursorAt(0, link.X + 2, link.Y + 2).Shape}");
                report.AppendLine($"card-cursor={document.GetCursorAt(0, card.X + 2, card.Y + 2).Shape}");
                report.AppendLine($"locked-selectable={document.GetInteractionAt(0, locked.X + 2, locked.Y + 2).CanSelectText}");
                report.AppendLine($"pinned-touch={document.GetInteractionAt(0, pinned.X + 2, pinned.Y + 2).TouchActions}");
                report.AppendLine($"hint-hover-media={document.TextRuns.Any(r => r.Text.Contains("hover capable"))}");

                // Hover the card: the :hover rule grows it, which moves what is below.
                var before = document.GetElementBounds("locked")!.Value.Y;
                using var measuring = new PeachDrawing.Win2D.Win2dComposer(new PeachDrawing.Win2D.Win2dRenderContext(Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice()), 1, 1);
                var result = await document.SetPointerStateAsync(0, card.X + 2, card.Y + 2, false, measuring.Canvas);
                var after = document.GetElementBounds("locked")!.Value.Y;
                report.AppendLine($"hover-applied={result.Applied} moved={after - before:0.#}");

                Finish("OK");
            }
            catch (Exception e)
            {
                Finish("FAILED: " + e);
            }
        };

        // A document that never loads must not hang the run.
        var timer = view.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(30);
        timer.Tick += (_, _) => Finish("FAILED: timed out");
        timer.Start();
    }

    /// <summary>Loads every showcase of the demo in a view and reports the ones that fail, and checks the screen-only ones for what they exist to show.</summary>
    public static async void RunAll(Window window, string resultPath, Application app)
    {
        var report = new StringBuilder();
        var failures = 0;
        try
        {
            var showcases = ShowcaseCatalog.Load();
            report.AppendLine($"showcases={showcases.Count}");
            var host = new Microsoft.UI.Xaml.Controls.Grid { Width = 800, Height = 600 };
            window.Content = host;

            foreach (var showcase in showcases)
            {
                var view = new HtmlView { Input = PeachBrackets.InputCapabilities.Mouse };
                host.Children.Clear();
                host.Children.Add(view);

                var finished = new TaskCompletionSource<string?>();
                view.DocumentLoaded += (_, _) => finished.TrySetResult(null);
                view.DocumentFailed += (_, e) => finished.TrySetResult(e.Exception.Message);
                view.Html = File.ReadAllText(showcase.Path);

                var winner = await Task.WhenAny(finished.Task, Task.Delay(30000));
                var error = winner == finished.Task ? await finished.Task : "timed out";
                error ??= Check(showcase, view);

                if (error is not null)
                {
                    failures++;
                    report.AppendLine($"FAIL {showcase.Slug}: {error}");
                }
            }

            report.AppendLine($"failures={failures}");
        }
        catch (Exception e)
        {
            failures++;
            report.AppendLine("FAILED: " + e);
        }

        File.WriteAllText(resultPath, (failures == 0 ? "OK" : "FAILED") + Environment.NewLine + report);
        app.Exit();
        Environment.Exit(failures == 0 ? 0 : 1);
    }

    // What a showcase has to be true of once it is loaded: it is one surface (screen media has no pages), and the screen-only ones show their feature.
    private static string? Check(Showcase showcase, HtmlView view)
    {
        var document = view.Document!;
        if (document.Pages.Count != 1)
            return $"{document.Pages.Count} pages";

        switch (showcase.Slug)
        {
            case "screen_long_page" when document.GetContentHeight(0) < 14000:
                return $"content height {document.GetContentHeight(0):0}pt is not beyond a 14000pt page";

            case "screen_sticky_header" or "screen_fixed_toolbar":
            {
                var word = showcase.Slug == "screen_sticky_header" ? "Sticky page header" : "Fixed toolbar";
                var before = document.TextRuns.FirstOrDefault(r => r.Text.Contains(word))?.Bounds.Y;
                document.SetViewport(new Rect(0, 400, 800, 600));
                var after = document.TextRuns.FirstOrDefault(r => r.Text.Contains(word))?.Bounds.Y;
                if (before is null || after is null || Math.Abs(after.Value - before.Value - 400) > 2)
                    return $"{word} did not follow the viewport ({before} -> {after})";
                break;
            }
        }

        return null;
    }
}

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
}

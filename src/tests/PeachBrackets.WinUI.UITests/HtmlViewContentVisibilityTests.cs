using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PeachBrackets.WinUI.UITests;

/// <summary><c>content-visibility: auto</c> in a view that scrolls itself: what is away from the view is skipped, and what the view comes near is shown.</summary>
public class HtmlViewContentVisibilityTests
{
    private static string Page =>
        "<style>body{margin:0;font:16px/20px sans-serif}section{content-visibility:auto;contain-intrinsic-size:80px}section div{height:600px}</style>"
        + string.Concat(Enumerable.Range(0, 8).Select(i => $"<section id=s{i}>section {i} text<div></div></section>"));

    private static async Task<HtmlView> LoadedAsync(Window window)
    {
        var view = new HtmlView { Html = Page, PageBackground = Microsoft.UI.Colors.White };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        window.Content = new Grid { Width = 400, Height = 300, Children = { view } };
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(500);
        return view;
    }

    [Fact]
    public async Task ScrollingShowsWhatCameNearAndSkipsWhatWentAway()
    {
        var (before, after, offset, top) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window);
            var start = view.Document!.TextRuns.Select(r => r.Text).ToList();

            // Down to the fifth section: the ones around it come near, the first goes away.
            var target = view.Document!.GetElementBounds("s4")!.Value.Y * 96 / 72;
            view.ScrollToY(target, animate: false);
            await Task.Delay(1500);

            var document = view.Document!;
            return (start, document.TextRuns.Select(r => r.Text).ToList(), view.ViewVerticalOffset, document.GetElementBounds("s4")!.Value.Y);
        });

        Assert.Contains(before, t => t.Contains("section 0", StringComparison.Ordinal));
        Assert.DoesNotContain(before, t => t.Contains("section 4", StringComparison.Ordinal));
        Assert.Contains(after, t => t.Contains("section 4", StringComparison.Ordinal));
        Assert.DoesNotContain(after, t => t.Contains("section 0", StringComparison.Ordinal));

        // The view was moved to where the document says it is looking, which is the section it was sent to.
        Assert.InRange(offset, top * 96 / 72 - 5, top * 96 / 72 + 5);
    }
}

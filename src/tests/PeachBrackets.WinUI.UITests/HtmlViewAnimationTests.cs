using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PeachBrackets.WinUI.UITests;

/// <summary>A view that plays animations: its frame loop stops when nothing runs and starts again when the control API asks for it.</summary>
public class HtmlViewAnimationTests
{
    private const string Page =
        "<style>@keyframes grow { from { width: 20px } to { width: 120px } } #a { height: 20px; background: red; animation: grow 0.3s linear forwards }</style>" +
        "<body style='margin:0'><div id=a></div></body>";

    private static async Task<HtmlView> LoadedAsync(Window window)
    {
        var view = new HtmlView { Html = Page, AnimationsEnabled = true, PageBackground = Microsoft.UI.Colors.White };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        window.Content = new Grid { Width = 400, Height = 300, Children = { view } };
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return view;
    }

    [Fact]
    public async Task PlayingAFinishedAnimation_StartsTheFrameLoopAgain()
    {
        var (idleFrames, afterPlay) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window);

            // A slow runner may not have made the animation yet.
            for (var waited = 0; waited < 100 && view.Document!.GetAnimations().Count == 0; waited++)
                await Task.Delay(100);

            // Let the animation run its course and the loop stop.
            await Task.Delay(1500);
            var settled = view.FrameCount;
            await Task.Delay(500);
            var idle = view.FrameCount - settled;

            view.Document!.GetAnimations()[0].CurrentTime = TimeSpan.Zero;
            await Task.Delay(600);
            return (idle, view.FrameCount - settled);
        });

        Assert.Equal(0, idleFrames);
        Assert.True(afterPlay > 3, $"{afterPlay} frames after the control API was used");
    }
}

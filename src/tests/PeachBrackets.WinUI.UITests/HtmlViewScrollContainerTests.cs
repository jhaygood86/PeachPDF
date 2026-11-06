using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PeachBrackets.WinUI.UITests;

/// <summary>A scroll container of the document (an element with <c>overflow: auto</c>) takes the wheel before the page does, and passes it on at the end of its content.</summary>
public class HtmlViewScrollContainerTests
{
    private const string Page =
        "<style>body{margin:0} #s{width:200px;height:100px;overflow:auto} .t{height:400px;background:linear-gradient(red,blue)}</style>" +
        "<body><div id=s><div class=t></div></div><div style='height:800px'></div></body>";

    private static async Task<HtmlView> LoadedAsync(Window window)
    {
        var view = new HtmlView { Html = Page, PageBackground = Microsoft.UI.Colors.White };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        window.Content = new Grid { Width = 400, Height = 300, Children = { view } };
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(300);
        return view;
    }

    [Fact]
    public async Task TheWheel_ScrollsTheContainerUnderThePointer_ThenLetsGoAtItsEnd()
    {
        var (first, atEnd, metrics, outside) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window);
            var document = view.Document!;
            var scroller = document.GetElement("s")!;

            var first = view.ScrollContainerAt(0, 50, 50, 0, 60);
            var after = document.GetScrollMetrics(scroller)!.Value;
            document.SetScrollPosition(scroller, 0, 10000);
            var atEnd = view.ScrollContainerAt(0, 50, 50, 0, 60);
            var outside = view.ScrollContainerAt(0, 300, 250, 0, 60);
            return (first, atEnd, after, outside);
        });

        Assert.True(first);
        Assert.True(metrics.Y > 0);
        Assert.False(atEnd);
        Assert.False(outside);
    }
}

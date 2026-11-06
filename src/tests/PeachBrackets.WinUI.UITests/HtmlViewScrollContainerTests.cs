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

    private const string ListPage =
        "<style>body{margin:0} #s{width:200px;height:100px;overflow:auto;margin:0;padding:0;list-style:none} li{height:40px} a{display:block;height:40px}</style>" +
        "<body><ul id=s><li>a</li><li>b</li><li>c</li><li>d</li><li>e</li><li>f</li><li>g</li><li><a id=last href='#x'>last</a></li></ul><p>after</p></body>";

    private static async Task<HtmlView> LoadedAsync(Window window, string html)
    {
        var view = new HtmlView { Html = html, PageBackground = Microsoft.UI.Colors.White };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        window.Content = new Grid { Width = 400, Height = 300, Children = { view } };
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(300);
        return view;
    }

    [Fact]
    public async Task ADraggedThumb_ScrollsTheContainer_AndATrackPressPagesIt()
    {
        var (afterDrag, afterPage, dragHandled) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window, ListPage);
            var document = view.Document!;
            var scroller = document.GetElement("s")!;
            var port = document.GetScrollMetrics(scroller)!.Value.Viewport;
            var x = port.Right - 4;

            var pressedThumb = view.PressScrollbarAt(0, x, port.Y + 10);
            var dragHandled = pressedThumb && view.DragScrollbar(x, port.Y + 10 + 30);
            var afterDrag = document.GetScrollMetrics(scroller)!.Value.Y;
            view.EndScrollbarDrag();

            document.SetScrollPosition(scroller, 0, 0);
            Assert.True(view.PressScrollbarAt(0, x, port.Bottom - 8));
            return (afterDrag, document.GetScrollMetrics(scroller)!.Value.Y, dragHandled);
        });

        Assert.True(dragHandled);
        Assert.True(afterDrag > 15, $"{afterDrag}");
        Assert.Equal(65.625, afterPage, 1);
    }

    [Fact]
    public async Task TheFocus_ScrollsTheContainerToShowIt_AndTheKeysScrollItFromThere()
    {
        var (afterFocus, afterUp, afterEnd) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window, ListPage);
            var document = view.Document!;
            var scroller = document.GetElement("s")!;

            view.StepFocus(true);
            await view.FocusIdle;
            await Task.Delay(300);
            var afterFocus = document.GetScrollMetrics(scroller)!.Value.Y;

            Assert.True(view.HandleScrollKey(Windows.System.VirtualKey.Up));
            var afterUp = document.GetScrollMetrics(scroller)!.Value.Y;
            Assert.True(view.HandleScrollKey(Windows.System.VirtualKey.End));
            return (afterFocus, afterUp, document.GetScrollMetrics(scroller)!.Value.Y);
        });

        Assert.True(afterFocus > 90, $"{afterFocus}");
        Assert.True(afterUp < afterFocus);
        Assert.True(afterEnd >= afterFocus);
    }

    [Fact]
    public async Task AContainerInTheAccessibilityTree_ScrollsFromAutomation()
    {
        var (scrollable, afterLarge, percent) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window, ListPage);
            var node = view.AccessibilityTree!.Root.ReadingChildren.First(n => n.Role == PeachBrackets.Accessibility.AccessibilityRole.List);
            var scrollable = view.ScrollMetricsOf(node) is { MaxY: > 0 };

            view.ScrollFromAutomation(node, Microsoft.UI.Xaml.Automation.ScrollAmount.NoAmount, Microsoft.UI.Xaml.Automation.ScrollAmount.LargeIncrement);
            var afterLarge = view.ScrollMetricsOf(node)!.Value.Y;

            view.ScrollToPercentFromAutomation(node, -1, 100);
            return (scrollable, afterLarge, view.ScrollMetricsOf(node)!.Value);
        });

        Assert.True(scrollable);
        Assert.True(afterLarge > 60);
        Assert.Equal(percent.MaxY, percent.Y, 1);
    }

    [Fact]
    public async Task AFingerDraggedOverAContainer_ScrollsItByTheDistanceMoved_AndNotThePage()
    {
        var (moved, after, tooSmall, pageLocked) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window, ListPage);
            var document = view.Document!;
            var scroller = document.GetElement("s")!;
            var ppp = 96.0 / 72.0;
            Windows.Foundation.Point At(double x, double y) => new(x * ppp, y * ppp);

            view.BeginTouchScroll(0, 50, 30, At(50, 30));

            // A twitch is not a drag.
            var tooSmall = view.MoveTouchScroll(At(50, 29));

            // The finger moves up by 20 points: the content follows it, so the container scrolls toward its end by 20.
            var moved = view.MoveTouchScroll(At(50, 10));
            var after = document.GetScrollMetrics(scroller)!.Value.Y;
            var pageLocked = view.IsTouchScrollingContainer;
            view.EndTouchScroll();
            return (moved, after, tooSmall, pageLocked);
        });

        Assert.False(tooSmall);
        Assert.True(moved);
        Assert.InRange(after, 17, 23);
        Assert.True(pageLocked);
    }

    [Fact]
    public async Task AFingerOutsideEveryContainer_LeavesTheGestureToThePage()
    {
        var moved = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window, ListPage);
            var ppp = 96.0 / 72.0;
            Windows.Foundation.Point At(double x, double y) => new(x * ppp, y * ppp);

            view.BeginTouchScroll(0, 150, 290, At(150, 290));
            return view.MoveTouchScroll(At(150, 200));
        });

        Assert.False(moved);
    }
}

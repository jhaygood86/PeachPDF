using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PeachDrawing.Core;

namespace PeachBrackets.WinUI.UITests;

/// <summary>Links to places in the document, in a bar that stays at the top of a view that scrolls itself.</summary>
public class HtmlViewLinkTests
{
    // A bar stuck to the top with a link in it, and tall sections to scroll through, one of them the link's target.
    private const string Page =
        "<style>body{margin:0;font:16px/20px sans-serif}nav{position:sticky;top:0;height:40px;background:#246}a{display:block;width:120px;height:40px;color:#fff}section{height:400px}</style>"
        + "<nav><a id=go href='#t'>Jump</a></nav><section></section><section></section><section id=t>target</section><section></section>";

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
    public async Task AnInPageLink_ScrollsTheViewToItsTarget()
    {
        var (offset, raised) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window);
            var clicked = new List<string?>();
            view.LinkClicked += (_, e) => clicked.Add(e.Destination);

            view.FollowLinkAt(0, 10, 10);
            await Task.Delay(1500);
            return (view.ViewVerticalOffset, clicked);
        });

        Assert.Equal("t", Assert.Single(raised));
        Assert.True(offset > 600, "offset " + offset);
    }

    [Fact]
    public async Task ALinkInTheBarIsFound_WhereverTheBarIsPinned()
    {
        var (shape, offset) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window);
            view.ScrollToY(300);
            await Task.Delay(1500);

            var document = view.Document!;
            var top = document.Viewport.Y;
            var shown = document.GetCursorAt(0, 10, top + 10).Shape;
            view.FollowLinkAt(0, 10, top + 10);
            await Task.Delay(1500);
            return (shown, view.ViewVerticalOffset);
        });

        Assert.Equal(CursorShape.Pointer, shape);
        Assert.True(offset > 600, "offset " + offset);
    }

    // The page of the demo, as it ships: inline links in a sticky bar with a z-index, and targets with a scroll margin.
    private static string DemoPage()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "examples", "ScreenShowcases", "screen_anchors.html");
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        throw new FileNotFoundException("screen_anchors.html");
    }

    [Fact]
    public async Task TheDemosJumpLinks_ScrollToTheirTargets()
    {
        var results = await UiThread.RunAsync(async window =>
        {
            var view = new HtmlView { Html = DemoPage(), PageBackground = Microsoft.UI.Colors.White };
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.DocumentLoaded += (_, _) => loaded.TrySetResult();
            window.Content = new Grid { Width = 700, Height = 520, Children = { view } };
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(500);

            var document = view.Document!;
            var run = document.TextRuns.First(r => r.Text.Contains("Jump to 3"));
            var x = run.Bounds.X + (run.Bounds.Width / 2) - (run.Bounds.Width / 4);
            var y = run.Bounds.Y + (run.Bounds.Height / 2);
            var found = document.Annotations.Links.Where(l => l.Bounds.Contains(x, y)).Select(l => l.Anchor).ToList();
            var target = document.GetElementBounds("t3")!.Value.Y * 96 / 72;

            view.FollowLinkAt(0, x, y);
            await Task.Delay(1500);
            return (found, target, view.ViewVerticalOffset);
        });

        Assert.Equal(["t3"], results.found);
        // The target is left 48px clear above it (its scroll-margin-top), for the bar stuck to the top.
        Assert.InRange(results.Item3, results.target - 50, results.target - 46);
    }

    [Fact]
    public async Task TheDemosJumpLinks_WorkOneAfterAnother()
    {
        var offsets = await UiThread.RunAsync(async window =>
        {
            var view = new HtmlView { Html = DemoPage(), PageBackground = Microsoft.UI.Colors.White };
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.DocumentLoaded += (_, _) => loaded.TrySetResult();
            window.Content = new Grid { Width = 700, Height = 520, Children = { view } };
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(500);

            var document = view.Document!;
            var targets = new Dictionary<string, double>();
            var seen = new List<(string Name, double Offset, double Viewport)>();
            foreach (var name in new[] { "7", "3", "5", "1", "2" })
            {
                // Where the link is drawn now: the bar is stuck to the top of the viewport once the page has scrolled.
                var run = document.TextRuns.First(r => r.Text.Contains("Jump to " + name));
                var x = run.Bounds.X + (run.Bounds.Width / 4);
                var y = run.Bounds.Y + (run.Bounds.Height / 2);
                using (var measuring = new PeachDrawing.Win2D.Win2dComposer(new PeachDrawing.Win2D.Win2dRenderContext(Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice()), 1, 1))
                    {
                    var down = await document.SetPointerStateAsync(0, x, y, true, measuring.Canvas);
                    view.ApplyUpdate(down);
                    var up = await document.SetPointerStateAsync(0, x, y, false, measuring.Canvas);
                    view.ApplyUpdate(up);
                }

                view.FollowLinkAt(0, x, y);
                await Task.Delay(1500);
                seen.Add((name, view.ViewVerticalOffset, document.GetElementBounds("t" + name)!.Value.Y * 96 / 72));
            }

            return seen;
        });

        foreach (var (name, offset, target) in offsets)
            Assert.True(Math.Abs(offset - Math.Max(0, target - 48)) < 4, $"jump to {name}: offset {offset}, target {target}");
    }

    [Fact]
    public async Task AClickWhileThePageIsStillScrolling_FindsTheBarWhereItIsNow()
    {
        var (found, offset, target) = await UiThread.RunAsync(async window =>
        {
            var view = new HtmlView { Html = DemoPage(), PageBackground = Microsoft.UI.Colors.White };
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.DocumentLoaded += (_, _) => loaded.TrySetResult();
            window.Content = new Grid { Width = 700, Height = 520, Children = { view } };
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(500);

            // A first jump, and a second click before the scroll it started has finished: the document has only heard about part of it, and the bar the
            // pointer is over is where the scroller is now, not where the document last was told.
            view.ScrollToY(2700, animate: false);
            await Task.Delay(60);
            var scrolledTo = view.ViewVerticalOffset;
            var x = 145.0;
            var y = (scrolledTo + 15) * 72 / 96;
            view.FollowLinkAt(0, x, y);
            await Task.Delay(1500);
            return (true, view.ViewVerticalOffset, view.Document!.GetElementBounds("t3")!.Value.Y * 96 / 72);
        });

        Assert.True(found);
        Assert.InRange(offset, target - 50, target - 46);
    }
}

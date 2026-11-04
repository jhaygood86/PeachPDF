using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PeachBrackets.WinUI.UITests;

/// <summary>A document with a fixed-attachment background is painted from the viewport, not from a canvas the scroller moves.</summary>
public class HtmlViewViewportModeTests
{
    private const string FixedBackground =
        "<style>body{margin:0;font:16px/20px sans-serif}section{height:400px;background:linear-gradient(red,blue) no-repeat fixed 0 0/100% 100%}</style>"
        + "<section><a id=go href='#t'>Jump</a></section><section></section><section id=t>target</section><section></section>";

    private const string Plain =
        "<style>body{margin:0;font:16px/20px sans-serif}section{height:400px;background:#eef}</style><section></section><section></section><section></section>";

    private static async Task<HtmlView> LoadedAsync(Window window, string html, HtmlViewScrollMode mode = HtmlViewScrollMode.Auto)
    {
        var view = new HtmlView { Html = html, ScrollMode = mode, PageBackground = Microsoft.UI.Colors.White };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        window.Content = new Grid { Width = 400, Height = 300, Children = { view } };
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(500);
        return view;
    }

    [Fact]
    public async Task AFixedBackground_PutsAViewerInViewportMode()
    {
        var (fixedMode, plainMode) = await UiThread.RunAsync(async window =>
        {
            var withBackground = (await LoadedAsync(window, FixedBackground)).IsViewportMode;
            var without = (await LoadedAsync(window, Plain)).IsViewportMode;
            return (withBackground, without);
        });

        Assert.True(fixedMode);
        Assert.False(plainMode);
    }

    [Fact]
    public async Task AnAutoHeightView_IsNeverInViewportMode()
    {
        var mode = await UiThread.RunAsync(async window => (await LoadedAsync(window, FixedBackground, HtmlViewScrollMode.Disabled)).IsViewportMode);

        Assert.False(mode);
    }

    [Fact]
    public async Task ScrollingAndLinks_StillWorkInViewportMode()
    {
        var (viewportY, offset) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window, FixedBackground);
            view.ScrollToY(300, animate: false);
            await Task.Delay(1000);
            var y = view.Document!.Viewport.Y;

            // The pointer handlers are still on the scrolled canvas, which is what the link is found through.
            view.ScrollToY(0, animate: false);
            await Task.Delay(500);
            view.FollowLinkAt(0, 10, 10);
            await Task.Delay(1000);
            return (y, view.ViewVerticalOffset);
        });

        Assert.True(viewportY > 200, "viewport " + viewportY);
        Assert.True(offset > 600, "offset " + offset);
    }
}

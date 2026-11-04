using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PeachBrackets.WinUI.UITests;

/// <summary>The settings a host gives the view: each reads back what was set, and the ones that change how the page looks load it again.</summary>
public class HtmlViewOptionsTests
{
    private const string Page = "<body style='margin:0;font:16px sans-serif'><p id=p>Some text in a paragraph.</p></body>";

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

    private static async Task<int> LoadsAfterAsync(HtmlView view, Action change)
    {
        var loads = 0;
        void Count(object? sender, EventArgs e) => loads++;
        view.DocumentLoaded += Count;
        change();
        await Task.Delay(1500);
        view.DocumentLoaded -= Count;
        return loads;
    }

    [Fact]
    public async Task EachSetting_ReadsBackWhatWasSet()
    {
        var (network, address, zoom, synchronous, color, sheet, margin, mode) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window);
            view.AllowNetworkAccess = true;
            view.DocumentBaseUri = new Uri("https://example.test/base/");
            view.IsZoomEnabled = false;
            view.IsLayoutSynchronous = true;
            view.SelectionColor = Microsoft.UI.Colors.Orange;
            view.UserStyleSheet = "p { color: red }";
            view.DocumentMargin = 12;
            view.ScrollMode = HtmlViewScrollMode.Disabled;
            await Task.Delay(500);
            return (view.AllowNetworkAccess, view.DocumentBaseUri, view.IsZoomEnabled, view.IsLayoutSynchronous, view.SelectionColor, view.UserStyleSheet, view.DocumentMargin, view.ScrollMode);
        });

        Assert.True(network);
        Assert.Equal("https://example.test/base/", address!.AbsoluteUri);
        Assert.False(zoom);
        Assert.True(synchronous);
        Assert.Equal(Microsoft.UI.Colors.Orange, color);
        Assert.Equal("p { color: red }", sheet);
        Assert.Equal(12, margin);
        Assert.Equal(HtmlViewScrollMode.Disabled, mode);
    }

    [Fact]
    public async Task ASettingThatChangesHowThePageLooks_LoadsItAgain()
    {
        var (afterSheet, afterMargin, afterBase, heightWithoutMargin, heightWithMargin) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadedAsync(window);
            var before = view.ContentHeight;
            var sheet = await LoadsAfterAsync(view, () => view.UserStyleSheet = "p { color: red }");
            var margin = await LoadsAfterAsync(view, () => view.DocumentMargin = 30);
            var baseUri = await LoadsAfterAsync(view, () => view.DocumentBaseUri = new Uri("https://example.test/"));
            return (sheet, margin, baseUri, before, view.ContentHeight);
        });

        Assert.True(afterSheet >= 1, "a user style sheet loads the page again");
        Assert.True(afterMargin >= 1, "a margin loads the page again");
        Assert.True(afterBase >= 1, "a base address loads the page again");
        Assert.True(heightWithMargin > heightWithoutMargin, $"{heightWithMargin} is taller than {heightWithoutMargin} once there is a margin");
    }

    [Fact]
    public async Task TheFontSet_IsTheSharedOneUnlessTheHostGivesTheViewItsOwn()
    {
        var (shared, own) = await UiThread.RunAsync(async window =>
        {
            var view = new HtmlView();
            var shared = view.FontSet;
            view.FontSet = new PeachDrawing.Text.FontSet();
            await Task.CompletedTask;
            return (shared, view.FontSet);
        });

        Assert.Same(HtmlViewFonts.Shared, shared ?? HtmlViewFonts.Shared);
        Assert.NotNull(own);
        Assert.NotSame(HtmlViewFonts.Shared, own);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace PeachBrackets.WinUI.UITests;

public class HtmlViewTests
{
    private const string Page = "<h1 id=h>Title</h1><p id=p>Some <a id=l href='https://example.com/'>link</a> text</p>";

    // Puts a view in the test window and waits until its document has laid out.
    private static async Task<HtmlView> LoadAsync(Window window, string html)
    {
        var view = new HtmlView { Html = html };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        view.DocumentFailed += (_, e) => loaded.TrySetException(e.Exception);
        window.Content = view;
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        return view;
    }

    [Fact]
    public async Task ADocumentLoadsAndLaysOut()
    {
        var pages = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            return view.Document!.Pages.Count;
        });

        Assert.True(pages >= 1);
    }

    [Fact]
    public async Task TheViewExposesADocumentPeerWithTheTextBlocksBelowIt()
    {
        var (type, names) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var document = DocumentPeer(view)!;
            return (document.GetAutomationControlType(), document.GetChildren().Select(c => c.GetName()).ToList());
        });

        Assert.Equal(AutomationControlType.Document, type);
        Assert.Contains("Title", names);
        Assert.Contains(names, n => n.StartsWith("Some"));
    }

    [Fact]
    public async Task HeadingsLinksAndTextAreToldApartToUiAutomation()
    {
        var (heading, level, link, text) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var children = DocumentPeer(view)!.GetChildren();
            var h = children.First(c => c.GetName() == "Title");
            var p = children.First(c => c.GetName().StartsWith("Some"));
            var a = p.GetChildren().First();
            return (h.GetAutomationControlType(), h.GetHeadingLevel(), a.GetAutomationControlType(), p.GetAutomationControlType());
        });

        Assert.Equal(AutomationControlType.Text, heading);
        Assert.Equal(AutomationHeadingLevel.Level1, level);
        Assert.Equal(AutomationControlType.Hyperlink, link);
        Assert.Equal(AutomationControlType.Text, text);
    }

    [Fact]
    public async Task InvokingALinkRaisesLinkClickedAndCanBeCancelled()
    {
        var (uri, anchor) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            string? seen = null;
            view.LinkClicked += (_, e) =>
            {
                seen = e.Uri;
                e.Cancel = true;
            };
            var link = DocumentPeer(view)!.GetChildren().First(c => c.GetName().StartsWith("Some")).GetChildren().First();
            ((IInvokeProvider)link.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            return (seen, link.GetAutomationId());
        });

        Assert.Equal("https://example.com/", uri);
        Assert.False(string.IsNullOrEmpty(anchor));
    }

    [Fact]
    public async Task EveryNodeHasABoundingRectangleAndTheFirstIsOnScreen()
    {
        var (empty, firstOffscreen) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var nodes = DocumentPeer(view)!.GetChildren();
            return (nodes.Count(n => n.GetBoundingRectangle().Width <= 0 || n.GetBoundingRectangle().Height <= 0), nodes[0].IsOffscreen());
        });

        Assert.Equal(0, empty);
        Assert.False(firstOffscreen);
    }

    [Fact]
    public async Task SelectAllSelectsTheTextAndScrollToElementFindsAnId()
    {
        var (selected, found, missing) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            view.SelectAll();
            return (view.SelectedText, view.ScrollToElement("l"), view.ScrollToElement("nope"));
        });

        Assert.Contains("Title", selected);
        Assert.True(found);
        Assert.False(missing);
    }

    [Fact]
    public async Task ADocumentCanComeFromAFile_AndAMissingOneIsReported()
    {
        var path = Path.Combine(Path.GetTempPath(), $"peachbrackets-winui-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(path, Page, TestContext.Current.CancellationToken);
        try
        {
            var (fromFile, failure) = await UiThread.RunAsync(async window =>
            {
                var view = new HtmlView { Source = new Uri(path) };
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                view.DocumentLoaded += (_, _) => loaded.TrySetResult();
                window.Content = view;
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var title = DocumentPeer(view)!.GetChildren().Any(c => c.GetName() == "Title");

                var broken = new HtmlView { Source = new Uri(path + ".missing") };
                var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
                broken.DocumentFailed += (_, e) => failed.TrySetResult(e.Exception);
                window.Content = broken;
                return (title, await failed.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            });

            Assert.True(fromFile);
            Assert.NotNull(failure);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ALinkToAnAnchorInTheDocumentIsRaisedWithoutAnAddress()
    {
        var (uri, anchor) = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<a id=go href='#end'>go</a><div style='height:3000px'></div><h2 id=end>End</h2>");
            string? seenUri = "unset";
            string? seenAnchor = null;
            view.LinkClicked += (_, e) =>
            {
                seenUri = e.Uri;
                seenAnchor = e.Destination;
            };
            var link = DocumentPeer(view)!.GetChildren()[0];
            ((IInvokeProvider)link.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            return (seenUri, seenAnchor);
        });

        Assert.Null(uri);
        Assert.Equal("end", anchor);
    }

    [Fact]
    public async Task TheDocumentAndItsNodesDescribeThemselvesToUiAutomation()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var document = DocumentPeer(view)!;
            var node = document.GetChildren().First(c => c.GetName() == "Title");
            node.SetFocus();
            node.ShowContextMenu();
            return new[]
            {
                document.GetClassName(), document.GetName(), node.GetClassName(),
                node.IsContentElement().ToString(), node.IsControlElement().ToString(), node.IsEnabled().ToString(),
                node.IsKeyboardFocusable().ToString(), node.HasKeyboardFocus().ToString(),
            };
        });

        Assert.Equal(["HtmlView", "HTML document", "HtmlNode", "True", "True", "True", "False", "False"], facts);
    }

    [Fact]
    public async Task ResizingTheViewLaysTheDocumentOutAgain()
    {
        var reloaded = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, Page);
            var again = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.DocumentLoaded += (_, _) => again.TrySetResult();
            view.Width = 320;
            window.Content = null;
            window.Content = view;
            view.Width = 480;
            await again.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return view.Document is not null;
        });

        Assert.True(reloaded);
    }

    [Fact]
    public async Task LandmarksControlsAndTablesAreToldToUiAutomationWithTheirOwnTypes()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<nav aria-label='Site'><a href='#x'>Home</a></nav><main><button disabled aria-describedby=d>Save</button><span id=d>Saves it</span>"
                + "<table><tr><th>Head</th></tr><tr><td>Cell</td></tr></table></main>");
            var top = DocumentPeer(view)!.GetChildren();
            var nav = top.First(c => c.GetLandmarkType() == AutomationLandmarkType.Navigation);
            var main = top.First(c => c.GetLandmarkType() == AutomationLandmarkType.Main);
            var button = Descend(main).First(c => c.GetAutomationControlType() == AutomationControlType.Button);
            var table = Descend(main).First(c => c.GetAutomationControlType() == AutomationControlType.Table);
            return new[]
            {
                nav.GetName(), nav.GetChildren()[0].GetAutomationControlType().ToString(),
                button.GetName(), button.IsEnabled().ToString(), button.GetHelpText(),
                Descend(table).Any(c => c.GetAutomationControlType() == AutomationControlType.HeaderItem).ToString(),
            };
        });

        Assert.Equal(["Site", "Hyperlink", "Save", "False", "Saves it", "True"], facts);
    }

    [Fact]
    public async Task TabStepsThroughTheTabStopsOfThePageAndThenLeavesIt()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<a href='#x'>first</a><button>second</button><p>text</p>");
            var log = new List<string>();

            async Task<bool> TabAsync(bool forward)
            {
                var used = view.StepFocus(forward);
                await view.FocusIdle;
                log.Add(used + ":" + (view.FocusedNode?.Name ?? "none"));
                return used;
            }

            await TabAsync(true);
            await TabAsync(true);
            await TabAsync(false);
            await TabAsync(true);
            await TabAsync(true);
            return log;
        });

        // Tab to the link, to the button, back to the link, on to the button, and past it: the page gives up the focus and the key is left to XAML.
        Assert.Equal(["True:first", "True:second", "True:first", "True:second", "False:none"], facts);
    }

    [Fact]
    public async Task AutomationCanFocusAPartOfThePageAndSeesWhereTheFocusIs()
    {
        var facts = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<a href='#x'>link</a><button>button</button><p>text</p>");
            var top = DocumentPeer(view)!.GetChildren();
            var button = top.First(c => c.GetAutomationControlType() == AutomationControlType.Button);
            var text = top.First(c => c.GetName() == "text");
            var before = button.HasKeyboardFocus();
            button.SetFocus();
            await Task.Delay(300);
            await view.FocusIdle;
            // The engine builds a new tree when the focus restyles the page; the peer for the focused part is still found.
            var found = DocumentPeer(view)!.FindPeer(view.FocusedNode!) is not null;
            return new[] { before.ToString(), button.IsKeyboardFocusable().ToString(), text.IsKeyboardFocusable().ToString(), button.HasKeyboardFocus().ToString(), view.FocusedNode!.Name, found.ToString() };
        });

        Assert.Equal(["False", "True", "False", "True", "button", "True"], facts);
    }

    [Fact]
    public async Task ListItemsKnowTheirPlaceInTheSet()
    {
        var places = await UiThread.RunAsync(async window =>
        {
            var view = await LoadAsync(window, "<ul><li>one</li><li>two</li><li>three</li></ul><p>not an item</p>");
            var top = DocumentPeer(view)!.GetChildren();
            var items = top.First(c => c.GetAutomationControlType() == AutomationControlType.List).GetChildren();
            return (items.Select(i => (i.GetPositionInSet(), i.GetSizeOfSet())).ToArray(), top.First(c => c.GetName() == "not an item").GetPositionInSet());
        });

        Assert.Equal([(1, 3), (2, 3), (3, 3)], places.Item1);
        Assert.Equal(-1, places.Item2);
    }

    private static IEnumerable<AutomationPeer> Descend(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren() ?? [])
        {
            yield return child;
            foreach (var below in Descend(child))
                yield return below;
        }
    }

    // A UserControl has no automation peer of its own: the view's is carried by an element inside it.
    private static HtmlViewAutomationPeer? DocumentPeer(DependencyObject element)
    {
        if (element is UIElement ui && FrameworkElementAutomationPeer.CreatePeerForElement(ui) is HtmlViewAutomationPeer found)
            return found;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            if (DocumentPeer(VisualTreeHelper.GetChild(element, i)) is { } peer)
                return peer;
        }

        return null;
    }
}

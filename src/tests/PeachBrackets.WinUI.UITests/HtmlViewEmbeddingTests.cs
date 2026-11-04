using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PeachDrawing.Text;
using Windows.Foundation;

namespace PeachBrackets.WinUI.UITests;

/// <summary>An <see cref="HtmlView"/> used as a block in other layout - a row of a list - rather than as a viewer.</summary>
public class HtmlViewEmbeddingTests
{
    // A box 100 CSS pixels tall (100 device-independent pixels) with nothing around it.
    private const string Box = "<style>body{margin:0}</style><div id=e style='height:100px;background:rgb(255,0,0)'></div>";

    private static string Paragraphs(int count) =>
        "<style>body{margin:0}p{margin:0;font:16px/20px sans-serif}</style>" + string.Concat(Enumerable.Range(0, count).Select(i => $"<p>Line {i} of some words that wrap when the view is narrow</p>"));

    private static HtmlView Embedded(string html, bool synchronous = true) =>
        new() { ScrollMode = HtmlViewScrollMode.Disabled, IsLayoutSynchronous = synchronous, Html = html, PageBackground = Microsoft.UI.Colors.Transparent };

    private static Task WaitForHeightAsync(HtmlView view)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        view.ContentHeightChanged += (_, _) => done.TrySetResult();
        return done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }

    [Fact]
    public async Task ASynchronousViewKnowsItsHeightFromTheLayoutPass()
    {
        var (desired, content, top) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded(Box);
            window.Content = new StackPanel { Width = 400, Children = { view } };
            view.Measure(new Size(400, double.PositiveInfinity));
            await Task.Yield();
            return (view.DesiredSize, view.ContentHeight, view.Document!.GetElementBounds("e")!.Value.Y);
        });

        Assert.Equal(400, desired.Width);
        Assert.Equal(100, desired.Height, 1);
        Assert.Equal(100, content, 1);
        Assert.Equal(0, top, 3);
    }

    [Fact]
    public async Task TheDocumentStartsAtTheTopLeftWithNothingAroundIt()
    {
        var (x, y) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded(Box);
            window.Content = new StackPanel { Width = 400, Children = { view } };
            view.Measure(new Size(400, double.PositiveInfinity));
            await Task.Yield();
            var bounds = view.Document!.GetElementBounds("e")!.Value;
            return (bounds.X, bounds.Y);
        });

        Assert.Equal(0, x, 3);
        Assert.Equal(0, y, 3);
    }

    [Fact]
    public async Task ChangingTheWidthChangesTheHeightInTheNextLayoutWithoutAStaleValue()
    {
        var (wide, narrow, back) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded(Paragraphs(3));
            window.Content = new StackPanel { Width = 600, Children = { view } };
            view.Measure(new Size(600, double.PositiveInfinity));
            var wideHeight = view.DesiredSize.Height;

            view.Measure(new Size(150, double.PositiveInfinity));
            var narrowHeight = view.DesiredSize.Height;

            view.Measure(new Size(600, double.PositiveInfinity));
            await Task.Yield();
            return (wideHeight, narrowHeight, view.DesiredSize.Height);
        });

        Assert.True(wide > 0);
        Assert.True(narrow > wide, $"narrow {narrow} should be taller than wide {wide}; back {back}");
        Assert.Equal(wide, back, 1);
    }

    [Fact]
    public async Task AnAsynchronousViewKeepsItsHeightUntilTheLayoutIsReadyThenMeasuresAgain()
    {
        var (before, after, raised) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded(Box, synchronous: false);
            var changed = 0;
            view.ContentHeightChanged += (_, _) => changed++;
            var height = WaitForHeightAsync(view);
            window.Content = new StackPanel { Width = 400, Children = { view } };
            view.Measure(new Size(400, double.PositiveInfinity));
            var first = view.DesiredSize.Height;

            await height;
            view.Measure(new Size(400, double.PositiveInfinity));
            return (first, view.DesiredSize.Height, changed);
        });

        // Everything the load awaits here has already completed, so even the asynchronous path may finish inside the first measure.
        Assert.InRange(before, 0, 100);
        Assert.Equal(100, after, 1);
        Assert.True(raised >= 1);
    }

    [Fact]
    public async Task ContentHeightChangedIsRaisedWhenTheContentChanges()
    {
        var (first, second, raised) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded(Box);
            var changed = 0;
            view.ContentHeightChanged += (_, _) => changed++;
            window.Content = new StackPanel { Width = 400, Children = { view } };
            view.Measure(new Size(400, double.PositiveInfinity));
            var firstHeight = view.ContentHeight;

            view.Html = "<style>body{margin:0}</style><div style='height:40px'></div>";
            view.Measure(new Size(400, double.PositiveInfinity));
            await Task.Delay(50);
            return (firstHeight, view.ContentHeight, changed);
        });

        Assert.Equal(100, first, 1);
        Assert.Equal(40, second, 1);
        Assert.True(raised >= 2);
    }

    [Fact]
    public async Task AViewTakenOutOfTheTreeAndPutBackLoadsItsDocumentAgain()
    {
        var (before, after) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded(Box, synchronous: false);
            var host = new StackPanel { Width = 400, Children = { view } };
            window.Content = host;
            view.Measure(new Size(400, double.PositiveInfinity));
            var first = view.ContentHeight;

            // A list takes a row out of the tree and later puts it back, perhaps with other content.
            host.Children.Clear();
            await Task.Delay(50);
            view.Html = "<style>body{margin:0}</style><div style='height:70px'></div>";
            host.Children.Add(view);
            view.Measure(new Size(400, double.PositiveInfinity));
            await Task.Delay(100);
            view.Measure(new Size(400, double.PositiveInfinity));
            return (first, view.ContentHeight);
        });

        Assert.Equal(100, before, 1);
        Assert.Equal(70, after, 1);
    }

    [Fact]
    public async Task ARecycledViewShowsNothingOfItsPreviousContent()
    {
        var (documentAfterSet, textAfter) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded("<style>body{margin:0}</style><p id=p>first</p>");
            window.Content = new StackPanel { Width = 400, Children = { view } };
            view.Measure(new Size(400, double.PositiveInfinity));

            view.Html = "<style>body{margin:0}</style><p id=p>second</p>";
            var cleared = view.Document is null;
            view.Measure(new Size(400, double.PositiveInfinity));
            await Task.Yield();
            view.SelectAll();
            return (cleared, view.SelectedText);
        });

        Assert.True(documentAfterSet);
        Assert.Contains("second", textAfter);
        Assert.DoesNotContain("first", textAfter);
    }

    [Fact]
    public async Task AnAutoHeightViewHasNoScrollerAndAViewerHasOne()
    {
        var (embedded, viewer) = await UiThread.RunAsync(async window =>
        {
            var inner = Embedded(Box);
            var outer = new HtmlView { Html = Box };
            window.Content = new StackPanel { Width = 400, Children = { inner, outer } };
            inner.Measure(new Size(400, double.PositiveInfinity));
            await Task.Delay(100);
            window.Content.UpdateLayout();
            return (Descendants<ScrollViewer>(inner).Count(), Descendants<ScrollViewer>(outer).Count());
        });

        Assert.Equal(0, embedded);
        Assert.Equal(1, viewer);
    }

    [Fact]
    public async Task SelectionAndLinksStillWorkInTheAutoHeightMode()
    {
        var (selected, uri) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded("<style>body{margin:0}</style><p>Some <a id=l href='https://example.com/'>link</a> text</p>");
            window.Content = new StackPanel { Width = 400, Children = { view } };
            view.Measure(new Size(400, double.PositiveInfinity));
            await Task.Yield();
            view.SelectAll();
            return (view.SelectedText, view.Document!.Annotations.Links.FirstOrDefault()?.Uri);
        });

        Assert.Contains("Some", selected);
        Assert.Equal("https://example.com/", uri);
    }

    [Fact]
    public async Task TheUserStyleSheetAndTheDocumentMarginReachTheDocument()
    {
        var (plain, styled, margined, x) = await UiThread.RunAsync(async window =>
        {
            var plainView = Embedded("<style>body{margin:0}</style><div class=b></div>");
            var styledView = Embedded("<style>body{margin:0}</style><div class=b></div>");
            styledView.UserStyleSheet = ".b { height: 60px }";
            var marginView = Embedded(Box);
            marginView.DocumentMargin = 10;
            window.Content = new StackPanel { Width = 400, Children = { plainView, styledView, marginView } };
            foreach (var view in new[] { plainView, styledView, marginView })
                view.Measure(new Size(400, double.PositiveInfinity));
            await Task.Yield();
            return (plainView.ContentHeight, styledView.ContentHeight, marginView.ContentHeight, marginView.Document!.GetElementBounds("e")!.Value.X * (96.0 / 72.0));
        });

        Assert.Equal(0, plain, 1);
        Assert.Equal(60, styled, 1);
        Assert.Equal(120, margined, 1);
        Assert.Equal(10, x, 1);
    }

    [Fact]
    public async Task ADocumentTallerThanAnyPageIsOneSurface()
    {
        var (pages, height) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded(Paragraphs(1250));
            window.Content = new StackPanel { Width = 400, Children = { view } };
            view.Measure(new Size(400, double.PositiveInfinity));
            await Task.Yield();
            return (view.Document!.Pages.Count, view.ContentHeight);
        });

        // Screen media has no pages: the document is not cut where an invented page would end.
        Assert.Equal(1, pages);
        Assert.True(height > 2 * 14000 * 96.0 / 72.0, "height " + height);
    }

    [Fact]
    public async Task AFontAddedOnceToTheSharedSetIsUsedByEveryView()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf");
        HtmlViewFonts.AddFile(path, new AddOptions { FamilyName = "Embedded Test Sans" });
        const string text = "<style>body{margin:0}span{white-space:nowrap}</style><span id=s style=\"font-size:24px;font-family:FAMILY\">Wide words in a face</span>";

        var (registered, fallback) = await UiThread.RunAsync(async window =>
        {
            var first = Embedded(text.Replace("FAMILY", "'Embedded Test Sans'"));
            var second = Embedded(text.Replace("FAMILY", "'No Such Family', monospace"));
            window.Content = new StackPanel { Width = 600, Children = { first, second } };
            first.Measure(new Size(600, double.PositiveInfinity));
            second.Measure(new Size(600, double.PositiveInfinity));
            await Task.Yield();
            return (first.Document!.GetElementBounds("s")!.Value.Width, second.Document!.GetElementBounds("s")!.Value.Width);
        });

        Assert.NotEqual(registered, fallback);
        Assert.Contains("Embedded Test Sans", HtmlViewFonts.Shared.AddedFamilyNames);
    }

    [Fact]
    public async Task AFontAddedAfterAViewLoadedIsSeenWhenItLoadsAgain()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf");
        var family = "Late Test Sans " + Guid.NewGuid().ToString("N")[..6];
        var html = $"<style>body{{margin:0}}span{{white-space:nowrap}}</style><span id=s style=\"font-size:24px;font-family:'{family}', monospace\">Wide words in a face</span>";

        var (before, after) = await UiThread.RunAsync(async window =>
        {
            var view = Embedded(html);
            window.Content = new StackPanel { Width = 600, Children = { view } };
            view.Measure(new Size(600, double.PositiveInfinity));
            await Task.Yield();
            var widthBefore = view.Document!.GetElementBounds("s")!.Value.Width;

            HtmlViewFonts.AddFile(path, new AddOptions { FamilyName = family });
            view.UserStyleSheet = "/* load again */";
            view.Measure(new Size(600, double.PositiveInfinity));
            await Task.Yield();
            return (widthBefore, view.Document!.GetElementBounds("s")!.Value.Width);
        });

        Assert.NotEqual(before, after);
    }
}

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using PeachBrackets.Avalonia.Tests.TestSupport;
using PeachDrawing.Text;
using SkiaSharp;

namespace PeachBrackets.Avalonia.Tests;

public class LoadingTests
{
    private const string Red = "<html><body style='margin:0;background:#cc3333'><div style='height:900px'>x</div></body></html>";

    [Fact]
    public async Task AFileSourceLoadsAndTheBaseAddressResolvesRelativeImages()
    {
        await HeadlessSession.Run(async () =>
        {
            var dir = Directory.CreateTempSubdirectory("htmlview");
            try
            {
                await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "blue.png"), Images.Png(new SKColor(0, 0, 255), 8, 8));
                var page = Path.Combine(dir.FullName, "page.html");
                await File.WriteAllTextAsync(page, "<html><body style='margin:0'><img src='blue.png' width='100' height='100' style='display:block'></body></html>");
                var (window, view, failure) = await HeadlessSession.ShowViewAsync(new HtmlView { Source = new Uri(page) });
                Assert.Null(failure);
                Assert.NotNull(view.Document);
                var frame = HeadlessSession.Capture(window);
                var (r, g, b) = HeadlessSession.PixelAt(frame, 40, 40);
                Assert.True(b > 200 && r < 60 && g < 60, $"the relative image should be drawn, got {r},{g},{b}");
                window.Close();
            }
            finally
            {
                dir.Delete(true);
            }
        });
    }

    [Fact]
    public async Task AMissingFileRaisesDocumentFailed()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view, failure) = await HeadlessSession.ShowViewAsync(new HtmlView { Source = new Uri(Path.Combine(Path.GetTempPath(), "no-such-folder", "missing.html")) });
            Assert.IsAssignableFrom<IOException>(failure);
            Assert.Null(view.Document);
            window.Close();
        });
    }

    [Fact]
    public async Task ANetworkSourceIsRefusedUntilTheHostAllowsIt()
    {
        await HeadlessSession.Run(async () =>
        {
            using var server = new LocalServer().Map("/page.html", "text/html", "<html><body style='margin:0;background:#cc3333'>net</body></html>"u8.ToArray()).Start();
            var address = new Uri(server.BaseAddress, "page.html");

            var (refused, refusedView, failure) = await HeadlessSession.ShowViewAsync(new HtmlView { Source = address });
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Null(refusedView.Document);
            Assert.Empty(server.Requests);
            refused.Close();

            var (window, view, none) = await HeadlessSession.ShowViewAsync(new HtmlView { Source = address, AllowNetworkAccess = true });
            Assert.Null(none);
            Assert.NotNull(view.Document);
            Assert.Contains("/page.html", server.Requests);
            window.Close();
        });
    }

    [Fact]
    public async Task ANetworkImageIsFetchedOnlyWhenAllowed()
    {
        await HeadlessSession.Run(async () =>
        {
            using var server = new LocalServer().Map("/i.png", "image/png", Images.Png(new SKColor(0, 200, 0), 8, 8)).Start();
            var html = $"<html><body style='margin:0'><img src='{server.BaseAddress}i.png' width='100' height='100' style='display:block'></body></html>";

            var (closed, _, _) = await HeadlessSession.ShowViewAsync(new HtmlView { Html = html });
            Assert.Empty(server.Requests);
            closed.Close();

            var (window, view, _) = await HeadlessSession.ShowViewAsync(new HtmlView { Html = html, AllowNetworkAccess = true });
            Assert.True(await HeadlessSession.PumpUntilAsync(() => server.Requests.Count > 0));
            var frame = HeadlessSession.Capture(window);
            var (r, g, b) = HeadlessSession.PixelAt(frame, 40, 40);
            Assert.True(g > 150 && r < 60 && b < 60, $"the fetched image should be drawn, got {r},{g},{b}");
            window.Close();
        });
    }

    [Fact]
    public async Task ASynchronousLayoutLoadsOnTheUiThread()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view, failure) = await HeadlessSession.ShowViewAsync(new HtmlView { Html = Red, IsLayoutSynchronous = true });
            Assert.Null(failure);
            Assert.True(view.ContentHeight > 600);
            window.Close();
        });
    }

    [Fact]
    public async Task ChangingTheHtmlLoadsTheNewDocumentAndAnOptionReloadsTheSameOne()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Red);
            var first = view.Document;
            var loaded = new TaskCompletionSource();
            view.DocumentLoaded += (_, _) => loaded.TrySetResult();
            view.Html = "<html><body style='margin:0'><div style='height:2000px'></div></body></html>";
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotSame(first, view.Document);
            Assert.True(view.ContentHeight > 1500);

            // A setting that changes how a document loads reloads it, once the control has settled.
            var again = view.Document;
            var reloaded = new TaskCompletionSource();
            view.DocumentLoaded += (_, _) => reloaded.TrySetResult();
            view.DocumentMargin = 20;
            view.UserStyleSheet = "p{color:red}";
            view.SelectionColor = global::Avalonia.Media.Colors.Yellow;
            view.Input = null;
            await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotSame(again, view.Document);
            window.Close();
        });
    }

    [Fact]
    public async Task AFontSetOfTheViewsOwnProvidesTheFontsADocumentNames()
    {
        await HeadlessSession.Run(async () =>
        {
            var set = new FontSet();
            var family = set.AddFile(Path.Combine(AppContext.BaseDirectory, "ArefRuqaaSubset.ttf"));
            Assert.NotNull(family);

            var (window, view, failure) = await HeadlessSession.ShowViewAsync(new HtmlView { Html = "<html><body style='font-family:" + family.Name + "'>text</body></html>", FontSet = set });
            Assert.Null(failure);
            Assert.Same(set, view.FontSet);

            // Switching the set loads the document again with the other one.
            var reloaded = new TaskCompletionSource();
            view.DocumentLoaded += (_, _) => reloaded.TrySetResult();
            view.FontSet = null;
            Assert.Null(view.FontSet);
            await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            window.Close();
        });
    }

    [Fact]
    public void TheSharedFontSetTakesFontsFromBytesFilesAndStreams()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ArefRuqaaSubset.ttf");
        var bytes = File.ReadAllBytes(path);
        Assert.NotNull(HtmlViewFonts.AddData(bytes));
        Assert.NotNull(HtmlViewFonts.AddFile(path));
        using var stream = File.OpenRead(path);
        var family = HtmlViewFonts.AddStream(stream);
        Assert.NotNull(family);
        Assert.NotNull(HtmlViewFonts.Shared);
    }

    [Fact]
    public async Task ADisabledScrollModeIsAsTallAsTheDocumentAndTellsWhenItChanges()
    {
        await HeadlessSession.Run(async () =>
        {
            var heights = 0;
            var view = new HtmlView { Html = Red, ScrollMode = HtmlViewScrollMode.Disabled };
            view.ContentHeightChanged += (_, _) => heights++;
            var loaded = new TaskCompletionSource();
            view.DocumentLoaded += (_, _) => loaded.TrySetResult();

            // A parent that scrolls gives the view all the height it asks for.
            var window = new global::Avalonia.Controls.Window { Width = 400, Height = 300, Content = new global::Avalonia.Controls.ScrollViewer { Content = view } };
            var shown = view;
            window.Show();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(await HeadlessSession.PumpUntilAsync(() => shown.ContentHeight > 800));
            Assert.True(heights > 0);
            Assert.True(await HeadlessSession.PumpUntilAsync(() => Math.Abs(shown.DesiredSize.Height - shown.ContentHeight) < 1), $"desired {shown.DesiredSize.Height} content {shown.ContentHeight}");

            // The parent scrolls, so the view itself neither scrolls nor zooms.
            window.MouseWheel(new Point(50, 50), new Vector(0, -1));
            Assert.Equal(0, shown.ScrollPixels.Y);
            shown.SetZoom(2);
            Assert.Equal(1, shown.Zoom);
            window.Close();
        });
    }

    [Fact]
    public async Task ZoomIsHeldBetweenItsLimitsAndTellsWhenItChanges()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Red);
            var changes = 0;
            view.ZoomChanged += (_, _) => changes++;

            view.SetZoom(10);
            Assert.Equal(4, view.Zoom);
            view.SetZoom(0.01);
            Assert.Equal(0.5, view.Zoom);
            Assert.Equal(2, changes);
            view.SetZoom(0.5);
            Assert.Equal(2, changes);

            view.IsZoomEnabled = false;
            view.SetZoom(2);
            Assert.Equal(0.5, view.Zoom);
            window.Close();
        });
    }

    [Fact]
    public async Task ScrollingToAnElementNeedsAnIdThatExists()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body style='margin:0'><div style='height:1500px'></div><h1 id='h'>x</h1><div style='height:1500px'></div></body></html>");
            Assert.False(view.ScrollToElement("nothing"));
            Assert.Throws<ArgumentNullException>(() => view.ScrollToElement(null!));
            Assert.True(view.ScrollToElement("h"));
            Assert.True(view.ScrollPixels.Y > 1000);
            window.Close();
        });
    }

    [Fact]
    public async Task AnUpdateThatWasNotAppliedChangesNothing()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Red);
            var tree = view.AccessibilityTree;
            view.ApplyUpdate(default);
            Assert.Same(tree, view.AccessibilityTree);
            window.Close();
        });
    }

    [Fact]
    public async Task SelectingAllAndCopyingWithoutAClipboardIsHarmlessAndTheTextIsReported()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body><p>alpha beta</p></body></html>");
            Assert.Equal(string.Empty, view.SelectedText);
            view.CopySelection();
            view.SelectAll();
            Assert.Contains("alpha", view.SelectedText);
            view.CopySelection();
            window.Close();
        });
    }
}

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using PeachBrackets.Avalonia.Tests.TestSupport;
using PeachDrawing.Core;
using SkiaSharp;

namespace PeachBrackets.Avalonia.Tests;

public class ImageTests
{
    [Fact]
    public async Task ADenserImageIsChosenOnceTheZoomHasSettled()
    {
        await HeadlessSession.Run(async () =>
        {
            var red = Images.DataUri(new SKColor(220, 0, 0), 8, 8);
            var blue = Images.DataUri(new SKColor(0, 0, 220), 16, 16);
            var html = $"<html><body style='margin:0'><img src='{red}' srcset='{red} 1x, {blue} 2x' width='100' height='100' style='display:block'></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 400, 300);

            var before = HeadlessSession.PixelAt(HeadlessSession.Capture(window), 40, 40);
            Assert.True(before.R > 150 && before.B < 60, $"the 1x image is drawn first, got {before}");

            view.SetZoom(2, new Point(0, 0));
            Assert.Equal(1, view.ImagesChosenForZoom);

            // Nothing is chosen again until the zoom has stayed put, then the 2x candidate replaces the first.
            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.ImagesChosenForZoom >= 2));
            Assert.True(await HeadlessSession.PumpUntilAsync(() =>
            {
                var (r, _, b) = HeadlessSession.PixelAt(HeadlessSession.Capture(window), 40, 40);
                return b > 150 && r < 60;
            }), "the denser image should be painted after the upgrade");
            window.Close();
        });
    }

    [Fact]
    public async Task ZoomingOutNeverAsksForDenserImages()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body><p>text</p></body></html>");
            view.SetZoom(0.6);
            await HeadlessSession.PumpUntilAsync(() => false, 600);
            Assert.Equal(1, view.ImagesChosenForZoom);
            window.Close();
        });
    }

    [Fact]
    public async Task AnUpgradeThatFailsIsReportedOnlyForTheDocumentOnShow()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body><p>text</p></body></html>");
            Exception? failure = null;
            view.DocumentFailed += (_, e) => failure = e.Exception;
            view.ImageUpgrader = _ => throw new InvalidOperationException("the upgrade failed");
            await view.UpgradeImagesAsync();
            Assert.Equal("the upgrade failed", failure?.Message);

            // One that fails because the document was replaced meanwhile is nothing to report.
            failure = null;
            var replaced = new TaskCompletionSource();
            view.DocumentLoaded += (_, _) => replaced.TrySetResult();
            view.ImageUpgrader = async _ =>
            {
                view.Html = "<html><body><p>Another.</p></body></html>";
                await replaced.Task;
                throw new ObjectDisposedException("the old document");
            };
            await view.UpgradeImagesAsync();
            Assert.Null(failure);
            window.Close();
        });
    }

    [Fact]
    public async Task AnUpgradeOfAViewWithNoDocumentDoesNothing()
    {
        await HeadlessSession.Run(async () =>
        {
            var view = new HtmlView();
            var called = false;
            view.ImageUpgrader = _ =>
            {
                called = true;
                return ValueTask.FromResult(new global::PeachBrackets.Updates.UpdateResult(false, null, []));
            };
            await view.UpgradeImagesAsync();
            Assert.False(called);
        });
    }
}

public class CursorTests
{
    private const string Box = "<html><body style='margin:0'><div id='t' style='width:200px;height:100px;cursor:{0}'>x</div></body></html>";

    private static async Task HoverAsync(global::Avalonia.Controls.Window window, HtmlView view, Point at)
    {
        window.MouseMove(at);
        await HtmlViewInput.SettleAsync(view);
    }

    [Theory]
    [InlineData("pointer", StandardCursorType.Hand)]
    [InlineData("text", StandardCursorType.Ibeam)]
    [InlineData("wait", StandardCursorType.Wait)]
    [InlineData("not-allowed", StandardCursorType.No)]
    [InlineData("move", StandardCursorType.SizeAll)]
    public async Task AKeywordCursorIsShownOverTheElementAndRemovedOutsideIt(string css, StandardCursorType expected)
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(string.Format(Box, css), 400, 300);
            await HoverAsync(window, view, new Point(50, 40));
            Assert.Equal(expected, view.ShownKeywordCursor);

            await HoverAsync(window, view, new Point(300, 250));
            Assert.Equal(StandardCursorType.Arrow, view.ShownKeywordCursor);
            window.Close();
        });
    }

    [Fact]
    public async Task AnImageCursorReplacesTheKeywordOnceItHasLoaded()
    {
        await HeadlessSession.Run(async () =>
        {
            var uri = Images.DataUri(new SKColor(0, 0, 0), 16, 16);
            var (window, view) = await HeadlessSession.ShowAsync(string.Format(Box, $"url({uri}) 4 4, pointer"), 400, 300);
            await HoverAsync(window, view, new Point(50, 40));

            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.ImageCursorCount == 1 && view.ShownKeywordCursor is null), "the image cursor should be shown when it loaded");
            Assert.NotNull(view.Cursor);

            // Leaving the element goes back to the keyword; leaving the tree lets the images go.
            await HoverAsync(window, view, new Point(300, 250));
            Assert.Equal(StandardCursorType.Arrow, view.ShownKeywordCursor);
            window.Content = null;
            Assert.Equal(0, view.ImageCursorCount);
            window.Close();
        });
    }

    [Fact]
    public async Task AnImageCursorThatTakesTimeShowsTheKeywordFirstAndThenItself()
    {
        await HeadlessSession.Run(async () =>
        {
            using var server = new LocalServer().Map("/c.png", "image/png", Images.Png(new SKColor(0, 0, 0), 16, 16)).Start();

            // The server holds the image back, so the keyword is what shows first however quickly the machine would have loaded it.
            var hold = server.Hold("/c.png");
            var html = string.Format(Box, $"url({server.BaseAddress}c.png), pointer");
            var (window, view) = await HeadlessSession.ShowAsync(html, 400, 300, v => v.AllowNetworkAccess = true);
            window.MouseMove(new Point(50, 40));
            Assert.True(await HeadlessSession.PumpUntilAsync(() => server.Requests.Contains("/c.png")), "the cursor image should be asked for");
            Assert.Equal(StandardCursorType.Hand, view.ShownKeywordCursor);

            // The pointer has not moved since, and the image now arrives.
            hold.SetResult();
            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.ShownKeywordCursor is null), "the image should replace the keyword when it has loaded");
            Assert.Contains("/c.png", server.Requests);
            window.Close();
        });
    }

    [Fact]
    public async Task AnImageThatCannotBeUsedFallsBackToTheKeyword()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(string.Format(Box, "url(data:image/png;base64,AAAA), pointer"), 400, 300);
            await HoverAsync(window, view, new Point(50, 40));
            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.ImageCursorCount == 1 && view.ShownKeywordCursor == StandardCursorType.Hand));
            window.Close();
        });
    }

    [Fact]
    public async Task AnImageCursorIsMadeFromADataUriAScaledOneAFileAndANetworkAddress()
    {
        await HeadlessSession.Run(async () =>
        {
            var small = await HtmlView.TryCreateCursorAsync(new CursorImage(Images.DataUri(new SKColor(1, 2, 3), 16, 16), 3, 3), false);
            Assert.NotNull(small);

            // An image larger than a pointer is scaled down to fit.
            var large = await HtmlView.TryCreateCursorAsync(new CursorImage(Images.DataUri(new SKColor(1, 2, 3), 300, 300), null, null), false);
            Assert.NotNull(large);

            var dir = Directory.CreateTempSubdirectory("cursors");
            try
            {
                var file = Path.Combine(dir.FullName, "c.png");
                await File.WriteAllBytesAsync(file, Images.Png(new SKColor(9, 9, 9), 12, 12));
                Assert.NotNull(await HtmlView.TryCreateCursorAsync(new CursorImage(new Uri(file).AbsoluteUri, 1, 1), false));
                Assert.Null(await HtmlView.TryCreateCursorAsync(new CursorImage(new Uri(Path.Combine(dir.FullName, "none.png")).AbsoluteUri, null, null), false));

                var notAnImage = Path.Combine(dir.FullName, "x.png");
                await File.WriteAllTextAsync(notAnImage, "not an image");
                Assert.Null(await HtmlView.TryCreateCursorAsync(new CursorImage(new Uri(notAnImage).AbsoluteUri, null, null), false));
            }
            finally
            {
                dir.Delete(true);
            }

            using var server = new LocalServer().Map("/c.png", "image/png", Images.Png(new SKColor(5, 5, 5), 10, 10)).Start();
            var address = new CursorImage(new Uri(server.BaseAddress, "c.png").AbsoluteUri, null, null);
            Assert.Null(await HtmlView.TryCreateCursorAsync(address, false));
            Assert.Empty(server.Requests);
            Assert.NotNull(await HtmlView.TryCreateCursorAsync(address, true));
            Assert.Null(await HtmlView.TryCreateCursorAsync(new CursorImage(new Uri(server.BaseAddress, "missing.png").AbsoluteUri, null, null), true));

            // An address the document may not reach is refused without a fetch.
            Assert.Null(await HtmlView.TryCreateCursorAsync(new CursorImage("ms-appx:///c.png", null, null), true));
        });
    }
}

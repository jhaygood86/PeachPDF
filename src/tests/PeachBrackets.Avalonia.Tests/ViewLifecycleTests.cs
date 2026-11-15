using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using PeachBrackets.Avalonia.Tests.TestSupport;
using PeachDrawing.Text;
using SkiaSharp;

namespace PeachBrackets.Avalonia.Tests;

public class ViewLifecycleTests
{
    private const string Tall = "<html><body style='margin:0'><div style='height:3000px;background:#cc3333'></div></body></html>";
    private const string Other = "<html><body style='margin:0'><div style='height:2000px;background:#3333cc'></div></body></html>";

    private static async Task ReloadAsync(HtmlView view, string html)
    {
        var loaded = new TaskCompletionSource();
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        view.Html = html;
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task AChangedFontSetKeepsTheContextUntilTheNewDocumentIsTakenInWithItsOwn()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            var before = view.RenderContext;
            Assert.NotNull(before);

            var loaded = new TaskCompletionSource();
            view.DocumentLoaded += (_, _) => loaded.TrySetResult();
            var set = new FontSet();
            view.FontSet = set;

            // What is showing keeps painting with the context it was laid out with.
            Assert.Same(before, view.RenderContext);
            Assert.NotNull(view.Document);

            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotSame(before, view.RenderContext);
            Assert.Same(set, view.RenderContext!.Fonts);
            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.FramesPainted > 0));
            window.Close();
        });
    }

    [Fact]
    public async Task ClearingTheContentEmptiesTheView()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            Assert.True(view.ContentHeight > 2000);

            view.Html = null;
            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.Document is null && view.ContentHeight == 0));
            Assert.Null(view.AccessibilityTree);
            Assert.Equal(0, view.ScrollPixels.Y);
            window.Close();
        });
    }

    [Fact]
    public async Task AReloadEndsAFlingAndClosesAnOpenPicker()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync("<html><body style='margin:0'><input id='f' type='time' style='width:160px;height:24px'><div style='height:3000px'></div></body></html>");
            await HtmlViewInput.ClickAsync(window, view, "f");
            Assert.True(view.HasOpenFlyout);
            view.BeginFling(null, 0, 800);
            Assert.True(view.IsFlinging);

            await ReloadAsync(view, Other);
            Assert.False(view.IsFlinging);
            Assert.False(view.HasOpenFlyout);
            window.Close();
        });
    }

    [Fact]
    public async Task AReloadChoosesTheImagesOfTheNewDocumentAgainForTheZoom()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            view.SetZoom(2);
            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.ImagesChosenForZoom >= 2));

            var chosenAtLoad = -1.0;
            view.DocumentLoaded += (_, _) => chosenAtLoad = view.ImagesChosenForZoom;
            await ReloadAsync(view, Other);
            Assert.Equal(1, chosenAtLoad);
            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.ImagesChosenForZoom >= 2));
            window.Close();
        });
    }

    [Fact]
    public async Task AHandlerThatThrowsIsNotAFailureOfTheLoad()
    {
        await HeadlessSession.Run(async () =>
        {
            var failures = 0;
            var view = new HtmlView { Html = Tall };
            view.DocumentLoaded += (_, _) => throw new InvalidOperationException("the application's own bug");
            view.DocumentFailed += (_, _) => failures++;
            var window = new global::Avalonia.Controls.Window { Width = 300, Height = 300, Content = view };
            window.Show();
            Assert.True(await HeadlessSession.PumpUntilAsync(() => view.Document is not null));
            await HtmlViewInput.SettleAsync(view);
            Assert.Equal(0, failures);
            window.Close();
        });
    }

    [Fact]
    public async Task WorkThatWaitedOnAReplacedDocumentFailsQuietlyAndOtherWorkIsReported()
    {
        await HeadlessSession.Run(async () =>
        {
            var failures = new List<Exception>();
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            view.DocumentFailed += (_, e) => failures.Add(e.Exception);

            var gate = new TaskCompletionSource();
            view.QueueFocus(async () =>
            {
                await gate.Task;
                throw new ObjectDisposedException("document");
            });

            await ReloadAsync(view, Other);
            gate.SetResult();
            await view.FocusIdle;
            Assert.Empty(failures);

            view.QueueFocus(() => throw new InvalidOperationException("current"));
            await view.FocusIdle;
            Assert.Single(failures);
            window.Close();
        });
    }

    [Fact]
    public async Task APageBackgroundChangeAsksForTheTilesToBePaintedAgain()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            var before = view.TilesGeneration;
            view.PageBackground = global::Avalonia.Media.Colors.Yellow;
            Assert.True(view.TilesGeneration > before);
            window.Close();
        });
    }

    [Fact]
    public async Task TheWheelIsUsedUpOnlyWhenItMovedThePage()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            var handled = new List<bool>();
            view.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) => handled.Add(e.Handled), handledEventsToo: true);

            // At the top, a wheel that scrolls up moves nothing and goes on to a parent.
            window.MouseWheel(new Point(50, 50), new Vector(0, 1));
            Assert.False(handled[^1]);

            window.MouseWheel(new Point(50, 50), new Vector(0, -1));
            Assert.True(handled[^1]);
            Assert.True(view.ScrollPixels.Y > 0);

            window.MouseWheel(new Point(50, 50), new Vector(0, -200));
            window.MouseWheel(new Point(50, 50), new Vector(0, -1));
            Assert.False(handled[^1], "at the bottom the wheel is not used up");
            window.Close();
        });
    }

    [Fact]
    public async Task ADisplayScaleChangeKeepsTheSamePartOfThePageInView()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            window.MouseWheel(new Point(50, 50), new Vector(0, -2));
            var before = view.ScrollPixels.Y;
            Assert.True(before > 0);

            view.ApplyScaling(2);
            Assert.Equal(before * 2, view.ScrollPixels.Y, 3);
            window.Close();
        });
    }

    [Fact]
    public async Task AnAutoHeightViewWithNoWidthToFillIsNotLaidOutAtOnePixel()
    {
        await HeadlessSession.Run(async () =>
        {
            var view = new HtmlView { Html = Tall, ScrollMode = HtmlViewScrollMode.Disabled };
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Equal(HtmlView.UnconstrainedWidth, view.DesiredSize.Width);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AMeasuringCanvasIsKeptForTheNextUseAndOverlappingUsesGetTheirOwn()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Tall);
            var context = view.RenderContext!;
            global::PeachDrawing.Core.Canvas first;
            using (var lease = view.RentMeasuring(context))
                first = lease.Canvas;

            using var again = view.RentMeasuring(context);
            Assert.Same(first, again.Canvas);
            using var overlapping = view.RentMeasuring(context);
            Assert.NotSame(again.Canvas, overlapping.Canvas);
            window.Close();
        });
    }

    [Fact]
    public async Task AnImageCursorThatArrivesAfterTheCursorsWereReleasedIsThrownAway()
    {
        await HeadlessSession.Run(async () =>
        {
            using var server = new LocalServer().Map("/c.png", "image/png", Images.Png(new SKColor(0, 0, 0), 16, 16)).Start();
            var hold = server.Hold("/c.png");
            var html = $"<html><body style='margin:0'><div style='width:100px;height:100px;cursor:url({server.BaseAddress}c.png), pointer'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(html, 400, 300, v => v.AllowNetworkAccess = true);
            window.MouseMove(new Point(50, 40));
            Assert.True(await HeadlessSession.PumpUntilAsync(() => server.Requests.Contains("/c.png")));

            view.ReleaseCursors();
            hold.SetResult();
            await HeadlessSession.PumpUntilAsync(() => false, 600);
            Assert.Equal(0, view.ImageCursorCount);
            window.Close();
        });
    }

    [Fact]
    public async Task ReadingAnAddressHonorsTheSizeLimitMissingFilesAndCancellation()
    {
        var path = Path.Combine(Path.GetTempPath(), "hostbytes-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllBytesAsync(path, new byte[100], TestContext.Current.CancellationToken);
        try
        {
            var address = new Uri(path);
            Assert.Equal(100, (await HostBytes.ReadAsync(address, false, 100, TestContext.Current.CancellationToken))!.Length);
            await Assert.ThrowsAsync<InvalidDataException>(() => HostBytes.ReadAsync(address, false, 99, TestContext.Current.CancellationToken));
            Assert.Null(await HostBytes.ReadAsync(new Uri(path + ".missing"), false, 100, TestContext.Current.CancellationToken));
            Assert.Null(await HostBytes.ReadAsync(new Uri("http://127.0.0.1:1/x"), false, 100, TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostBytes.ReadAsync(address, false, 100, new CancellationToken(true)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Avalonia.Threading;
using PeachBrackets.Avalonia;

[assembly: AvaloniaTestApplication(typeof(PeachBrackets.Avalonia.Tests.TestSupport.TestApp))]

namespace PeachBrackets.Avalonia.Tests.TestSupport;

/// <summary>The application the headless platform runs the controls under: Skia drawing, no window system.</summary>
public sealed class TestApp : Application
{
    // A theme gives windows their template, and with it the overlay layer a menu or flyout opens in.
    public override void Initialize() => Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Runs a body on the UI thread of one headless application shared by every test, and shows an <see cref="HtmlView"/> in a window.</summary>
internal static class HeadlessSession
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

    // The Func<Task<T>> overload of Dispatch keeps the dispatcher running until the body finishes; the Func<T> overload would hand back the body's task
    // unawaited, and a failing body would pass unnoticed.
    public static Task Run(Func<Task> body) => Session.Value.Dispatch<int>(async () =>
    {
        await body();
        return 0;
    }, CancellationToken.None);

    public static Task<T> Run<T>(Func<Task<T>> body) => Session.Value.Dispatch<T>(body, CancellationToken.None);

    /// <summary>Opens a window of the given size holding a view that has loaded <paramref name="html"/>.</summary>
    public static async Task<(Window Window, HtmlView View)> ShowAsync(string html, double width = 400, double height = 300, Action<HtmlView>? configure = null)
    {
        var view = new HtmlView { Html = html };
        configure?.Invoke(view);
        var window = new Window { Width = width, Height = height, Content = view, SizeToContent = SizeToContent.Manual };
        var loaded = new TaskCompletionSource();
        view.DocumentLoaded += (_, _) => loaded.TrySetResult();
        view.DocumentFailed += (_, e) => loaded.TrySetException(e.Exception);
        window.Show();
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    /// <summary>Opens a window holding a configured view and waits for it to load, or to fail; returns what went wrong, or null when it loaded.</summary>
    public static async Task<(Window Window, HtmlView View, Exception? Failure)> ShowViewAsync(HtmlView view, double width = 400, double height = 300)
    {
        var window = new Window { Width = width, Height = height, Content = view, SizeToContent = SizeToContent.Manual };
        var done = new TaskCompletionSource<Exception?>();
        view.DocumentLoaded += (_, _) => done.TrySetResult(null);
        view.DocumentFailed += (_, e) => done.TrySetResult(e.Exception);
        window.Show();
        var failure = await done.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Dispatcher.UIThread.RunJobs();
        return (window, view, failure);
    }

    /// <summary>Runs the dispatcher and the render timer until the condition holds.</summary>
    public static async Task<bool> PumpUntilAsync(Func<bool> condition, int milliseconds = 15000)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (condition())
                return true;

            await Task.Delay(8);
        }

        return condition();
    }

    /// <summary>The window as it is drawn.</summary>
    public static WriteableBitmap Capture(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered.");
    }

    /// <summary>The color of one pixel, as (red, green, blue).</summary>
    public static (byte R, byte G, byte B) PixelAt(WriteableBitmap bitmap, int x, int y)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + (y * buffer.RowBytes) + (x * 4), bytes, 0, 4);
        // The channel order is the bitmap's own: Skia's headless bitmaps are RGBA on some platforms and BGRA on others.
        var bgra = bitmap.Format == global::Avalonia.Platform.PixelFormat.Bgra8888;
        return bgra ? (bytes[2], bytes[1], bytes[0]) : (bytes[0], bytes[1], bytes[2]);
    }
}

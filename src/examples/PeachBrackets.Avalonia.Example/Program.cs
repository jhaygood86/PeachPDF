using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace PeachBrackets.Avalonia.Example;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildApp() => AppBuilder.Configure<ExampleApp>().UsePlatformDetect();
}

/// <summary>
/// The demo application. With no options it opens the demo window. <c>--smoke &lt;file&gt;</c> loads a page, prints what the view and document report and exits;
/// <c>--showcase &lt;slug&gt;</c> shows one showcase, and with <c>--screenshot &lt;png&gt;</c> also writes what the window shows and exits.
/// </summary>
internal sealed class ExampleApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var smoke = Option(args, "--smoke");
            var showcase = Option(args, "--showcase");
            var screenshot = Option(args, "--screenshot");
            desktop.MainWindow = smoke is not null ? SmokeWindow(desktop, smoke) : DemoWindow(desktop, showcase, screenshot);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static Window DemoWindow(IClassicDesktopStyleApplicationLifetime desktop, string? slug, string? screenshot)
    {
        var window = new DemoWindow(AppContext.BaseDirectory);
        if (slug is null)
            return window;

        window.Opened += async (_, _) =>
        {
            var loaded = new TaskCompletionSource();
            window.View.DocumentLoaded += (_, _) => loaded.TrySetResult();
            await window.ShowAsync(slug);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(60));
            if (screenshot is null)
                return;

            // Let the first frames and any animation settle, then picture the window.
            await Task.Delay(1500);
            Dispatcher.UIThread.RunJobs();
            var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
            using var bitmap = new RenderTargetBitmap(size);
            bitmap.Render(window);
            #pragma warning disable CS0618 // The overload that takes encoder options is a static helper in Avalonia 12; the plain one writes a PNG.
            bitmap.Save(screenshot);
#pragma warning restore CS0618
            Console.WriteLine("OK screenshot=" + screenshot);
            desktop.Shutdown(0);
        };

        DispatcherTimer.RunOnce(() =>
        {
            if (screenshot is not null)
            {
                Console.WriteLine("FAILED timeout");
                desktop.Shutdown(2);
            }
        }, TimeSpan.FromSeconds(120));
        return window;
    }

    // Loads a page, writes what the view and document report to stdout, and exits: the app's own check that a real window paints.
    private static Window SmokeWindow(IClassicDesktopStyleApplicationLifetime desktop, string file)
    {
        var view = new HtmlView { Source = new Uri(Path.GetFullPath(file)) };
        var window = new Window { Width = 800, Height = 600, Content = view };
        view.DocumentLoaded += async (_, _) =>
        {
            await Task.Delay(300);
            Console.WriteLine($"OK loaded={file} height={view.ContentHeight:F0} document={view.Document is not null}");
            desktop.Shutdown(0);
        };
        view.DocumentFailed += (_, e) =>
        {
            Console.WriteLine("FAILED " + e.Exception.Message);
            desktop.Shutdown(1);
        };
        DispatcherTimer.RunOnce(() =>
        {
            Console.WriteLine("FAILED timeout");
            desktop.Shutdown(2);
        }, TimeSpan.FromSeconds(60));
        return window;
    }
}

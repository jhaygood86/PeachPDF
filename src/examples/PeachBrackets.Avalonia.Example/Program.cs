using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PeachBrackets.Avalonia;

namespace PeachBrackets.Avalonia.Example;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildApp() => AppBuilder.Configure<ExampleApp>().UsePlatformDetect();
}

internal sealed class ExampleApp : Application
{
    private const string Sample = """
        <!doctype html>
        <html><head><style>
          body { font-family: sans-serif; margin: 24px; max-width: 46em; }
          h1 { color: #b4503c; } a { color: #1a5fb4; }
          .card { border: 1px solid #ccc; border-radius: 8px; padding: 12px; margin: 12px 0; }
          button:hover { background: #fde8d8; }
        </style></head><body>
          <h1>PeachBrackets on Avalonia</h1>
          <p>This page is laid out by PeachBrackets and painted through Avalonia's Skia lease. Select text, follow <a href="#end">a link to the end</a>, or edit the field below.</p>
          <div class="card"><label>Name <input type="text" value="Peach"></label>
            <label><input type="checkbox" checked> Subscribe</label> <button>Submit</button></div>
          <div class="card" style="height:8em;overflow:auto">Scrolling inside a box.<br><br><br><br><br><br><br><br>The end of the box.</div>
          <p style="height:900px">Scroll down.</p><p id="end">The end.</p>
        </body></html>
        """;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var smoke = Array.IndexOf(args, "--smoke");
            desktop.MainWindow = smoke >= 0 && smoke + 1 < args.Length ? SmokeWindow(desktop, args[smoke + 1]) : MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window MainWindow()
    {
        var view = new HtmlView { Html = Sample, AnimationsEnabled = true };
        var open = new Button { Content = "Open HTML file..." };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        var window = new Window { Title = "PeachBrackets.Avalonia", Width = 900, Height = 700 };
        view.DocumentLoaded += (_, _) => status.Text = $"Loaded: {view.ContentHeight:F0} px tall";
        view.DocumentFailed += (_, e) => status.Text = "Failed: " + e.Exception.Message;
        view.LinkActivated += (_, e) => status.Text = "Link: " + (e.Uri ?? e.Destination);
        open.Click += async (_, _) =>
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open an HTML file",
                FileTypeFilter = [new FilePickerFileType("HTML") { Patterns = ["*.html", "*.htm"] }],
            });

            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                view.AllowNetworkAccess = false;
                view.Source = new Uri(path);
                view.Html = null;
            }
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), Children = { open, status } };
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(view);
        window.Content = root;
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
            Console.WriteLine($"OK loaded={file} height={view.ContentHeight:F0} frames={view.Document is not null}");
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

using System;
using Microsoft.UI.Xaml;
using PeachBrackets.WinUI;

namespace PeachBrackets.WinUI.Example;

// The demo: every showcase in an HtmlView with a panel of the knobs a host has. `--smoke <file>` loads one built-in page and checks what the view and
// the document report; `--smoke-all <file>` loads every showcase and checks the ones that have something to prove. Both write the result to <file> and
// exit, which is how the control is exercised without anyone looking at it.
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // What a generated Main does: without it an await in the UI thread's code resumes on a pool thread, and a page that needs the network cannot finish.
        Application.Start(callback =>
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _ = callback;
            new ExampleApp(args);
        });
    }
}

public sealed class ExampleApp : Application
{
    private const string Page = """
        <style>
        body { font: 16px 'Segoe UI', sans-serif; margin: 24px }
        h1 { margin: 0 0 12px }
        .card { display: block; width: 260px; padding: 12px; margin: 8px 0; background: #dfeaf7; border: 1px solid #5b7fa8; cursor: crosshair }
        .card:hover { background: #f7e3c4; height: 72px }
        a { color: #1a5fb4 }
        .locked { user-select: none; color: #777 }
        .badge { display: block; width: 200px; padding: 8px; margin: 8px 0; background: #fcc; cursor: url(data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAALElEQVR4nO3OIQEAAAwEoetf+hdjBoGn1T4lICAgICAgICAgICAgICCw58ABVA34aguOkNgAAAAASUVORK5CYII=) 8 8, pointer }
        .pinned { touch-action: none; padding: 8px; background: #eee }
        @media (hover: hover) { .hint::after { content: ' (hover capable)' } }
        </style>
        <h1>PeachBrackets.WinUI</h1>
        <p id="lead">Select this text, or follow <a id="link" href="https://peachpdf.net/">a link</a>. <span class="hint">Hint</span></p>
        <div class="card" id="card">A card that grows on hover and asks for a crosshair.</div>
        <p class="locked" id="locked">This line cannot be selected.</p>
        <div class="badge" id="badge">A red square pointer, hotspot 8,8</div>
        <div class="pinned" id="pinned">touch-action: none</div>
        """;

    private readonly string[] _args;
    private Window? _window;

    public ExampleApp(string[] args)
    {
        _args = args;

        // Said where it can be read: an exception in a XAML handler otherwise ends the process with a stowed exception and no message.
        UnhandledException += (_, e) => System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "demo-error.txt"), e.Exception + Environment.NewLine + e.Message);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var smokeAll = Array.IndexOf(_args, "--smoke-all");
        if (smokeAll >= 0 && smokeAll + 1 < _args.Length)
        {
            _window = new Window { Title = "PeachBrackets.WinUI smoke" };
            _window.Activate();
            Smoke.RunAll(_window, _args[smokeAll + 1], this);
            return;
        }

        var smoke = Array.IndexOf(_args, "--smoke");
        if (smoke >= 0 && smoke + 1 < _args.Length)
        {
            var view = new HtmlView { Html = Page };
            _window = new Window { Title = "PeachBrackets.WinUI", Content = view };
            _window.Activate();
            Smoke.Run(view, _args[smoke + 1], this);
            return;
        }

        // `--sample`: the built-in page alone in a plain window, which is what the accessibility check reads through UI Automation.
        if (Array.IndexOf(_args, "--sample") >= 0)
        {
            _window = new Window { Title = "PeachBrackets.WinUI", Content = new HtmlView { Html = Page } };
            _window.Activate();
            return;
        }

        // A fault while the window is made is written beside the program: a XAML failure otherwise ends the process with a stowed exception and no message.
        try
        {
            _window = new DemoWindow();
            _window.Activate();
        }
        catch (Exception e)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "demo-error.txt"), e.ToString());
            throw;
        }
    }
}

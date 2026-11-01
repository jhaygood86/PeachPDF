using System;
using Microsoft.UI.Xaml;
using PeachBrackets.WinUI;

namespace PeachBrackets.WinUI.Example;

// A window with an HtmlView. `--smoke <file>` loads the page, checks what the view and the document report, writes the result to <file> and exits,
// which is how the control is exercised without anyone looking at it.
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ => new ExampleApp(args));
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
        .pinned { touch-action: none; padding: 8px; background: #eee }
        @media (hover: hover) { .hint::after { content: ' (hover capable)' } }
        </style>
        <h1>PeachBrackets.WinUI</h1>
        <p id="lead">Select this text, or follow <a id="link" href="https://peachpdf.net/">a link</a>. <span class="hint">Hint</span></p>
        <div class="card" id="card">A card that grows on hover and asks for a crosshair.</div>
        <p class="locked" id="locked">This line cannot be selected.</p>
        <div class="pinned" id="pinned">touch-action: none</div>
        """;

    private readonly string[] _args;
    private Window? _window;

    public ExampleApp(string[] args) => _args = args;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var view = new HtmlView { Html = Page };
        _window = new Window { Title = "PeachBrackets.WinUI", Content = view };
        _window.Activate();

        var smoke = Array.IndexOf(_args, "--smoke");
        if (smoke >= 0 && smoke + 1 < _args.Length)
            Smoke.Run(view, _args[smoke + 1], this);
    }
}

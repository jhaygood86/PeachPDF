using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using PeachBrackets.Apple;
using PeachBrackets.Apple.Example;

NSApplication.Init();
var app = NSApplication.SharedApplication;
app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
var options = Options.Parse(args);
app.Delegate = new AppDelegate(options);
app.Run();

internal sealed record Options(string? SmokeReport, string? Screenshot, string? ScrollBench, bool Cpu, string? Showcase = null, double ScrollY = 0, bool WholeWindow = false)
{
    public static Options Parse(string[] args)
    {
        string? smoke = null, shot = null, bench = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--smoke") smoke = args[i + 1];
            if (args[i] == "--screenshot") shot = args[i + 1];
            if (args[i] == "--scroll-bench") bench = args[i + 1];
        }

        string? showcase = null;
        var scrollY = 0.0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--showcase") showcase = args[i + 1];
            if (args[i] == "--scroll") scrollY = double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
        }

        return new Options(smoke, shot, bench, args.Contains("--cpu"), showcase, scrollY, args.Contains("--whole-window"));
    }
}

internal sealed class AppDelegate(Options options) : NSApplicationDelegate
{
    private DemoWindow? _demo;
    private NSWindow? _window => _demo?.Window;
    private HtmlView? _view => _demo?.View;

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    public override void DidFinishLaunching(NSNotification notification)
    {
        _demo = new DemoWindow(options.Cpu);

        if (options.Showcase is not null && options.Screenshot is not null)
        {
            // One showcase, scrolled to a place, written out as a picture: for looking at what the view shows without driving it.
            _window!.OrderFrontRegardless();
            if (!_demo.Show(options.Showcase))
                Exit(false, "FAILED: no showcase " + options.Showcase);

            _view!.DocumentLoaded += (_, _) => Later(800, () =>
            {
                var clip = _view.ContentView;
                clip.ScrollToPoint(new CGPoint(0, options.ScrollY));
                _view.ReflectScrolledClipView(clip);
                Later(800, () =>
                {
                    WriteScreenshot(options.WholeWindow ? _window!.ContentView! : _view!, options.Screenshot);
                    Environment.Exit(0);
                });
            });
            return;
        }

        if (options.ScrollBench is not null)
        {
            // A long page scrolled the way a trackpad would, step by step, timing what each step costs the main thread.
            _window!.OrderFrontRegardless();
            _demo.Show("screen_long_page");
            _view!.DocumentLoaded += (_, _) => Later(1000, RunScrollBench);
            NSTimer.CreateScheduledTimer(120, _ => Exit(false, "FAILED: the benchmark did not finish in 120 s"));
            return;
        }

        if (options.SmokeReport is null && options.Screenshot is null)
        {
            _window!.MakeKeyAndOrderFront(null);
            // Activate() is macOS 14; the older call is what a Mac this example still supports (11) has.
#pragma warning disable CA1422
            NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
#pragma warning restore CA1422
            return;
        }

        _window!.OrderFrontRegardless();
        _view!.FormSubmitting += (_, _) => _submitted = true;
        // Let the first paint settle, drive the form with real mouse and key events, give the document's queued changes time to land, then check.
        _view.DocumentLoaded += (_, _) => Later(400, () =>
        {
            Interact();
            Later(600, () =>
            {
                // What the drag inside the field selected, read before the next control takes the focus.
                _fieldSelection = _view.Document?.GetEditState() is { } edit ? (edit.SelectionStart, edit.SelectionEnd) : (-1, -1);
                InteractAgain();
                Later(800, Finish);
            });
        });
        _view.DocumentFailed += (_, e) => Exit(false, "FAILED: load failed: " + e.Exception);
        NSTimer.CreateScheduledTimer(60, _ => Exit(false, "FAILED: the document did not load in 60 s"));
    }

    private void RunScrollBench()
    {
        var view = _view!;
        var clip = view.ContentView;
        var maxY = view.DocumentView!.Frame.Height - clip.Bounds.Height;
        const int steps = 400;
        var step = Math.Min(60.0, maxY / steps);
        var times = new List<double>(steps);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 1; i <= steps; i++)
        {
            var t = System.Diagnostics.Stopwatch.GetTimestamp();
            clip.ScrollToPoint(new CGPoint(0, step * i));
            view.ReflectScrolledClipView(clip);

            // Let Core Animation commit the frame, as it would between two trackpad events.
            CATransaction.Flush();
            times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            NSRunLoop.Main.RunUntil(NSDate.Now.AddSeconds(0.002));
        }

        times.Sort();
        double At(double q) => times[(int)Math.Min(times.Count - 1, q * times.Count)];
        var report = $"OK\nmode={(options.Cpu ? "cpu" : "gpu")} steps={steps} step={step:F0}pt scrolled={step * steps:F0}pt\n"
            + $"main-thread ms per step: mean={times.Average():F2} median={At(0.5):F2} p95={At(0.95):F2} p99={At(0.99):F2} max={times[^1]:F2}\n"
            + $"steps over 16.7 ms (a dropped frame at 60 Hz): {times.Count(t => t > 16.7)}\n"
            + $"total {watch.Elapsed.TotalMilliseconds:F0} ms";
        File.WriteAllText(options.ScrollBench!, report + "\n");
        Environment.Exit(0);
    }

    private bool _submitted;
    private (int, int) _fieldSelection;
    private readonly List<string> Debug = [];

    private static void Later(int milliseconds, Action action) =>
        _ = Task.Delay(milliseconds).ContinueWith(_ => NSRunLoop.Main.BeginInvokeOnMainThread(action));

    // A click at the middle of an element, as the window would receive it.
    private void Click(string id)
    {
        var view = _view!;
        if (view.Document?.GetElementBounds(id) is not { } b)
            return;

        var content = new CGPoint((b.X + (b.Width / 2)) * 96 / 72, (b.Y + (b.Height / 2)) * 96 / 72);
        var inWindow = view.DocumentView!.ConvertPointToView(content, null);
        var hit = _window!.ContentView!.HitTest(_window.ContentView.ConvertPointFromView(inWindow, null));
        Debug.Add($"click {id}: content=({content.X:F0},{content.Y:F0}) window=({inWindow.X:F0},{inWindow.Y:F0}) hit={hit?.GetType().Name} key={_window.IsKeyWindow} first={_window.FirstResponder?.GetType().Name}");
        foreach (var type in new[] { NSEventType.LeftMouseDown, NSEventType.LeftMouseUp })
            _window!.SendEvent(NSEvent.MouseEvent(type, inWindow, 0, 0, _window.WindowNumber, null, 0, 1, 1)!);
    }

    private void Type(string text)
    {
        foreach (var c in text)
        {
            var s = c.ToString();
            foreach (var type in new[] { NSEventType.KeyDown, NSEventType.KeyUp })
                _window!.SendEvent(NSEvent.KeyEvent(type, CGPoint.Empty, 0, 0, _window.WindowNumber, null, s, s, false, 0)!);
        }
    }

    // A press at a fraction of an element's width, a drag to another, and the release: the mouse as the window would receive it.
    private void Drag(string id, double from, double to)
    {
        var view = _view!;
        if (view.Document?.GetElementBounds(id) is not { } b)
            return;

        CGPoint At(double fraction) =>
            view.DocumentView!.ConvertPointToView(new CGPoint((b.X + (b.Width * fraction)) * 96 / 72, (b.Y + (b.Height / 2)) * 96 / 72), null);

        _window!.SendEvent(NSEvent.MouseEvent(NSEventType.LeftMouseDown, At(from), 0, 0, _window.WindowNumber, null, 0, 1, 1)!);
        for (var step = 1; step <= 4; step++)
            _window.SendEvent(NSEvent.MouseEvent(NSEventType.LeftMouseDragged, At(from + ((to - from) * step / 4)), 0, 0, _window.WindowNumber, null, 0, 1, 1)!);

        _window.SendEvent(NSEvent.MouseEvent(NSEventType.LeftMouseUp, At(to), 0, 0, _window.WindowNumber, null, 0, 1, 1)!);
    }

    private void Interact()
    {
        Click("agree");
        Click("name");
        Type("h i");
        Later(300, () => Drag("name", 0.9, 0.02));
    }

    private void InteractAgain()
    {
        Drag("level", 0.05, 0.97);
        Click("send");
    }

    private static string ScreenDir => Path.Combine(AppContext.BaseDirectory, "screen");

    // What the view and the document say at a few points, so the app can be checked without looking at it.
    private void Finish()
    {
        var view = _view!;
        var document = view.Document!;
        var lines = new List<string>();
        var failures = new List<string>();
        void Check(string name, string value, string expected)
        {
            lines.Add($"{name}={value}");
            if (value != expected)
                failures.Add($"{name}: expected {expected}, got {value}");
        }

        Check("pages", document.Pages.Count.ToString(), "1");
        string Cursor(string id)
        {
            var bounds = document.GetElementBounds(id);
            return bounds is { } b ? document.GetCursorAt(0, b.X + (b.Width / 2), b.Y + (b.Height / 2)).Shape.ToString() : "missing";
        }

        Check("link-cursor", Cursor("link"), "Pointer");
        Check("card-cursor", Cursor("card"), "Crosshair");
        if (document.GetElementBounds("locked") is { } locked)
            Check("locked-selectable", document.GetInteractionAt(0, locked.X + 8, locked.Y + 8).CanSelectText.ToString(), "False");

        if (document.GetElementBounds("pinned") is { } pinned)
            Check("pinned-touch", document.GetInteractionAt(0, pinned.X + 8, pinned.Y + 8).TouchActions.ToString(), "None");

        // What VoiceOver walks: the page view's accessibility children, among them the heading, the link and the card text.
        var parts = view.DocumentView?.AccessibilityChildren ?? [];
        var roles = parts.OfType<NSAccessibilityElement>().Select(p => p.AccessibilityRole).ToList();
        var labels = parts.OfType<NSAccessibilityElement>().Select(p => p.AccessibilityLabel).ToList();
        Check("a11y-parts", (parts.Length >= 4).ToString(), "True");
        Check("a11y-heading", (roles.Contains("AXHeading") && labels.Contains("PeachBrackets.Apple")).ToString(), "True");
        Check("a11y-link", (roles.Contains("AXLink") && labels.Contains("a link")).ToString(), "True");
        var checkedBox = document.GetElement("agree") is { } agree && document.GetFormState(agree) is { Checked: true };
        Check("form-checkbox-toggled", checkedBox.ToString(), "True");
        var typed = document.GetElement("name") is { } name ? document.GetFormState(name)?.Value ?? "" : "missing";
        Check("form-text-typed", typed, "h i");
        Check("form-submitted", _submitted.ToString(), "True");
        Check("focus-node", view.FocusedNode is not null ? "set" : "none", "set");
        Check("field-drag-selected", _fieldSelection.Item1 != _fieldSelection.Item2 ? "True" : $"False {_fieldSelection}", "True");
        var level = document.GetElement("level") is { } range ? document.GetFormState(range)?.Value ?? "" : "missing";
        Check("range-dragged", level, "10");
        lines.AddRange(Debug);
        lines.Add($"zoom={view.Zoom}");
        if (options.Screenshot is { } path)
            WriteScreenshot(view, path);

        Exit(failures.Count == 0, failures.Count == 0 ? "OK\n" + string.Join('\n', lines) : "FAILED: " + string.Join("; ", failures) + "\n" + string.Join('\n', lines));
    }

    private static void WriteScreenshot(NSView view, string path)
    {
        var bounds = view.Bounds;
        var rep = view.BitmapImageRepForCachingDisplayInRect(bounds);
        if (rep is null)
            return;

        view.CacheDisplay(bounds, rep);
        rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary())?.Save(NSUrl.FromFilename(path), true, out _);
    }

    private void Exit(bool ok, string report)
    {
        if (options.SmokeReport is { } file)
            File.WriteAllText(file, report + "\n");
        else
            Console.WriteLine(report);

        Environment.Exit(ok ? 0 : 1);
    }
}

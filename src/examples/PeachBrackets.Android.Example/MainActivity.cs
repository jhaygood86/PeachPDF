using System.Text.Json;
using Android.App;
using Android.Content.Res;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using PeachBrackets.Android;
using SkiaSharp;

namespace PeachBrackets.Android.Example;

// The demo: every showcase in an HtmlView, and a panel with each knob the view offers a host. After a page loads the activity logs (tag
// PEACH_SMOKE) what the document says, and the touch handlers log what they see, so the view can be exercised from `adb` without looking at it.
[Activity(Label = "PeachBrackets", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private const string Tag = "PEACH_SMOKE";

    private sealed record Showcase(string Slug, string Category, string Title, string Description, string AssetPath);

    private readonly List<Showcase> _showcases = [];
    private readonly List<Showcase> _shown = [];
    private HtmlView _view = null!;
    private FrameLayout _host = null!;
    private TextView _description = null!;
    private TextView _status = null!;
    private Spinner _categories = null!;
    private Spinner _list = null!;
    private int _widthPercent = 100;
    private int _heightPercent = 100;
    private long _loadStarted;
    private double _loadMilliseconds;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        LoadCatalog("demo/screen", "screen-showcases.json");
        LoadCatalog("demo/showcases", "showcases.json");

        // adb shell am start ... --ez cpu true  draws on the CPU instead of the graphics hardware, to compare the two.
        _view = new HtmlView(this, useGpu: !(Intent?.GetBooleanExtra("cpu", false) ?? false));
        _host = new FrameLayout(this);
        _host.AddView(_view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _host.SetBackgroundColor(global::Android.Graphics.Color.Argb(255, 200, 210, 230));

        _description = new TextView(this) { TextSize = 12 };
        _status = new TextView(this) { TextSize = 11, Typeface = global::Android.Graphics.Typeface.Monospace };
        _categories = new Spinner(this);
        _list = new Spinner(this);
        var knobs = new Button(this) { Text = "Knobs" };
        knobs.Click += (_, _) => ShowKnobs();

        var row = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
        row.AddView(_categories, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
        row.AddView(_list, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 2));
        row.AddView(knobs, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));

        // Android 15 draws edge to edge; the root keeps the page out from under the status and title bars.
        var root = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        root.SetFitsSystemWindows(true);
        root.AddView(row);
        root.AddView(_description);
        root.AddView(_host, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        root.AddView(_status);
        SetContentView(root);

        var categoryNames = new List<string> { "All showcases" };
        categoryNames.AddRange(_showcases.Select(s => s.Category).Distinct());
        _categories.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, categoryNames);
        _categories.ItemSelected += (_, _) => FillList();
        _list.ItemSelected += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < _shown.Count)
                Show(_shown[e.Position]);
        };
        FillList();

        WireEvents();
        _host.LayoutChange += (_, _) => UpdateStatus();
    }

    private void LoadCatalog(string directory, string manifest)
    {
        try
        {
            using var stream = Assets!.Open($"{directory}/{manifest}");
            using var document = JsonDocument.Parse(stream);
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                _showcases.Add(new Showcase(
                    entry.GetProperty("slug").GetString()!, entry.GetProperty("category").GetString()!, entry.GetProperty("title").GetString()!,
                    entry.GetProperty("description").GetString()!, $"{directory}/{entry.GetProperty("html").GetString()}"));
            }
        }
        catch (Exception e)
        {
            Log.Warn(Tag, $"no {directory}/{manifest}: {e.Message}");
        }
    }

    private void FillList()
    {
        var category = _categories.SelectedItemPosition > 0 ? (string?)_categories.SelectedItem : null;
        _shown.Clear();
        _shown.AddRange(_showcases.Where(s => category is null || s.Category == category));
        _list.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, _shown.Select(s => s.Title).ToList());
    }

    private void Show(Showcase showcase)
    {
        _description.Text = showcase.Description;
        using var reader = new StreamReader(Assets!.Open(showcase.AssetPath));
        _loadStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _view.Html = reader.ReadToEnd();
        UpdateStatus();
    }

    private void WireEvents()
    {
        _view.DocumentFailed += (_, e) => { Log.Error(Tag, "FAILED " + e.Exception); _status.Text = "Failed: " + e.Exception.Message; };
        _view.DocumentLoaded += (_, _) =>
        {
            _loadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(_loadStarted).TotalMilliseconds;
            var document = _view.Document!;
            document.Selection.SelectionChanged += (_, e) => Log.Info(Tag, $"selection anchor={e.Anchor} focus={e.Focus}");
            Log.Info(Tag, $"loaded pages={document.Pages.Count} content={document.GetContentHeight(0):0}pt");
            UpdateStatus();
        };
        _view.TouchStarted += (_, e) => Log.Info(Tag, $"touch page={e.PageIndex} x={e.X:0} y={e.Y:0} allowed={e.Allowed}");
        _view.Tapped += (_, e) => Log.Info(Tag, $"tapped x={e.X:0} y={e.Y:0}");
        _view.LongPressed += (_, e) =>
        {
            Log.Info(Tag, $"longpressed x={e.X:0} y={e.Y:0}");
            _view.PostDelayed(() => Log.Info(Tag, $"selected=\"{_view.SelectedText}\""), 800);
        };
        _view.LinkTapped += (_, e) => { Log.Info(Tag, $"link-tapped {e.Uri}"); _status.Text = "link: " + e.Uri; };
        _view.ScrollChanged += (_, _) => UpdateStatus();
        _view.FocusChanged += (_, _) => Log.Info(Tag, $"focus={_view.FocusedNode?.Name ?? "none"}");
        _view.ZoomChanged += (_, _) => { Log.Info(Tag, $"zoom {_view.Zoom:0.00}"); UpdateStatus(); };
        _view.RequestFocus();
    }

    // The panel of knobs: each has a name, a line saying what it does, and its control, applied live.
    private void ShowKnobs()
    {
        var panel = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        panel.SetPadding(32, 16, 32, 16);

        Knob(panel, "Host width", "How wide the host makes the widget, as a share of the screen. The document lays out to this width.",
            Seek(30, 100, _widthPercent, v => { _widthPercent = v; ApplyHostSize(); }));
        Knob(panel, "Host height", "How tall the host makes the widget. This is the viewport that vh and fixed boxes use.",
            Seek(30, 100, _heightPercent, v => { _heightPercent = v; ApplyHostSize(); }));
        Knob(panel, "PageBackground", "What shows behind the document where it paints nothing.",
            Choice(["White", "Transparent", "Light grey", "Dark"], 0, i =>
            {
                _view.PageBackground = i switch { 1 => SKColors.Transparent, 2 => new SKColor(235, 235, 235), 3 => new SKColor(28, 28, 32), _ => SKColors.White };
                _view.Invalidate();
            }));
        Knob(panel, "Input", "The pointing devices the document is told about (hover, pointer and any-pointer media features). Detect asks the system. Takes effect on the next load.",
            Choice(["Detect", "Mouse", "Touch", "None"], 0, i =>
            {
                _view.Input = i switch { 1 => InputCapabilities.Mouse, 2 => InputCapabilities.Touch, 3 => InputCapabilities.None, _ => null };
                ReloadCurrent();
            }));
        Knob(panel, "Theme", "The theme the view reports to prefers-color-scheme (Android 12 and later). Try the Light and dark showcase.",
            Choice(["System", "Light", "Dark"], 0, i =>
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(31))
                    return;

                var mode = i switch { 1 => UiNightMode.No, 2 => UiNightMode.Yes, _ => UiNightMode.Auto };
                ((UiModeManager?)GetSystemService(UiModeService))?.SetApplicationNightMode((int)mode);
            }));

        new AlertDialog.Builder(this)!.SetTitle("Host knobs")!.SetView(new ScrollView(this) { }.Also(s => s.AddView(panel)))!.SetPositiveButton("Close", (_, _) => { })!.Show();
    }

    private void ApplyHostSize()
    {
        var parent = (View)_host.Parent!;
        var width = parent.Width * _widthPercent / 100;
        var height = Math.Max(1, _host.Height > 0 && _heightPercent == 100 ? ViewGroup.LayoutParams.MatchParent : (int)(parent.Height * 0.8 * _heightPercent / 100));
        _view.LayoutParameters = new FrameLayout.LayoutParams(width, height);
        _view.RequestLayout();
    }

    private void ReloadCurrent()
    {
        if (_list.SelectedItemPosition >= 0 && _list.SelectedItemPosition < _shown.Count)
            Show(_shown[_list.SelectedItemPosition]);
    }

    private void UpdateStatus()
    {
        var document = _view.Document;
        _status.Text = document is null
            ? "loading"
            : $"pages {document.Pages.Count}   content {document.GetContentHeight(0):0}pt   view {_view.Width}x{_view.Height}px   viewport {document.Viewport.Y:0}pt   zoom {_view.Zoom:0.00}   load {_loadMilliseconds:0}ms";
    }

    private void Knob(LinearLayout panel, string name, string help, View control)
    {
        panel.AddView(new TextView(this) { Text = name, TextSize = 14, Typeface = global::Android.Graphics.Typeface.Monospace });
        panel.AddView(new TextView(this) { Text = help, TextSize = 11 });
        panel.AddView(control);
    }

    private SeekBar Seek(int min, int max, int value, Action<int> changed)
    {
        var bar = new SeekBar(this) { Max = max - min, Progress = value - min };
        bar.ProgressChanged += (_, e) => changed(e.Progress + min);
        return bar;
    }

    private Spinner Choice(string[] items, int selected, Action<int> changed)
    {
        var spinner = new Spinner(this) { Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, items) };
        spinner.SetSelection(selected);
        var first = true;
        spinner.ItemSelected += (_, e) =>
        {
            // The spinner reports its initial selection; that is not a change.
            if (first) { first = false; return; }
            changed(e.Position);
        };
        return spinner;
    }
}

internal static class ViewExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}

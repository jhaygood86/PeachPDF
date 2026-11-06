using System.Text.Json;
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using PeachBrackets.Android;
using SkiaSharp;

// A touch screen is not required: the app also runs on a laptop-class device (a Googlebook, ChromeOS) with a keyboard, mouse and trackpad only.
[assembly: UsesFeature("android.hardware.touchscreen", Required = false)]

namespace PeachBrackets.Android.Example;

// The demo: every showcase in an HtmlView, and a panel with each knob the view offers a host. After a page loads the activity logs (tag
// PEACH_SMOKE) what the document says, and the touch handlers log what they see, so the view can be exercised from `adb` without looking at it.
//
// It is built for every size of window: resizable (a free-form window on a tablet or a Googlebook is any size, and changes while the app runs), and it takes
// the configuration changes itself so a resize lays the page out again where it is instead of restarting the activity. From the Expanded window width class
// (840 dp) the list of showcases is a pane beside the page; narrower, it is a pair of drop-downs above it.
[Activity(Label = "PeachBrackets", MainLauncher = true, Exported = true, ResizeableActivity = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.Orientation
        | ConfigChanges.Density | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation | ConfigChanges.UiMode)]
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
    private ListView _listPane = null!;
    private Button _knobs = null!;
    private LinearLayout _root = null!;
    private bool? _expanded;
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
        _view = new HtmlView(this, useGpu: !(Intent?.GetBooleanExtra("cpu", false) ?? false)) { AnimationsEnabled = Intent?.GetBooleanExtra("animations", true) ?? true };
        _host = new FrameLayout(this);
        _host.AddView(_view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _host.SetBackgroundColor(global::Android.Graphics.Color.Argb(255, 200, 210, 230));

        _description = new TextView(this) { TextSize = 12 };
        _status = new TextView(this) { TextSize = 11, Typeface = global::Android.Graphics.Typeface.Monospace };
        _categories = new Spinner(this);
        _list = new Spinner(this);
        _knobs = new Button(this) { Text = "Knobs" };
        _knobs.Click += (_, _) => ShowKnobs();
        _listPane = new ListView(this);
        _listPane.ItemClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < _shown.Count)
                Show(_shown[e.Position]);
        };

        // Android 15 draws edge to edge; the root keeps the page out from under the status and title bars.
        _root = new LinearLayout(this);
        _root.SetFitsSystemWindows(true);
        SetContentView(_root);
        ApplyLayout();

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

        // adb shell am start ... --es showcase screen_animation_keyframes  opens that showcase instead of the first.
        if (Intent?.GetStringExtra("showcase") is { } slug && _showcases.Find(s => s.Slug == slug) is { } requested)
            _host.PostDelayed(() => Show(requested), 1000); // after the drop-down has reported its first selection

        _host.LayoutChange += (_, _) => UpdateStatus();
    }

    // Android's window size classes: a window 840 dp wide or more is Expanded (a tablet, a Googlebook window), and has room for the list beside the page.
    private const int ExpandedWidthDp = 840;

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ApplyLayout();
    }

    // Arranges the controls for the width of the window, keeping the page where it is: only the views around it are moved.
    private void ApplyLayout()
    {
        var expanded = Resources!.Configuration!.ScreenWidthDp >= ExpandedWidthDp;
        if (_expanded == expanded)
            return;

        _expanded = expanded;
        foreach (var view in new View[] { _categories, _list, _listPane, _knobs, _description, _host, _status })
            (view.Parent as ViewGroup)?.RemoveView(view);

        _root.RemoveAllViews();

        if (!expanded)
        {
            _root.Orientation = global::Android.Widget.Orientation.Vertical;
            var row = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Horizontal };
            row.AddView(_categories, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1));
            row.AddView(_list, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 2));
            row.AddView(_knobs, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
            _root.AddView(row);
            _root.AddView(_description);
            _root.AddView(_host, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
            _root.AddView(_status);
            return;
        }

        // Two panes: the showcases down the side, the page and what it says about itself beside them.
        _root.Orientation = global::Android.Widget.Orientation.Horizontal;
        var side = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        side.AddView(_categories, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        side.AddView(_listPane, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        side.AddView(_knobs, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        var pane = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        pane.AddView(_description);
        pane.AddView(_host, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        pane.AddView(_status);
        _root.AddView(side, new LinearLayout.LayoutParams((int)(320 * Resources.DisplayMetrics!.Density), ViewGroup.LayoutParams.MatchParent));
        _root.AddView(pane, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1));
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
        var titles = _shown.Select(s => s.Title).ToList();
        _list.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, titles);
        _listPane.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleListItem1, titles);
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
        _view.AnimationEvents += (_, e) =>
        {
            foreach (var animation in e.Events)
                Log.Info(Tag, $"animation {animation.Kind} {animation.Name}");
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
        Knob(panel, "AnimationsEnabled", "Whether CSS animations, transitions and animated images play (so whether the Animation showcases move). Loads the page again.",
            Choice(["On", "Off"], 0, i =>
            {
                _view.AnimationsEnabled = i == 0;
                ReloadCurrent();
            }));
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PeachDrawing.Text;
using PeachDrawing.Win2D;
using Windows.UI;

namespace PeachBrackets.WinUI.Example;

/// <summary>
/// The demo: every showcase in a list, the selected one in an <see cref="HtmlView"/> inside the box a host would give it, and a panel with each
/// knob the control offers a host, applied live. Built in code (the project has no XAML).
/// </summary>
internal sealed class DemoWindow : Window
{
    private readonly IReadOnlyList<Showcase> _showcases = ShowcaseCatalog.Load();
    private readonly DemoSettings _settings = new();
    private readonly HtmlView _view = new();
    private readonly Border _hostBox = new();
    private readonly ScrollViewer _stage = new();
    private readonly ListView _list = new();
    private readonly ComboBox _categories = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
    private readonly TextBlock _status = new() { FontFamily = new FontFamily("Cascadia Mono"), FontSize = 12 };
    private readonly Grid _root = new();
    private readonly List<Showcase> _shown = [];
    private readonly Stopwatch _loadTimer = new();
    private FontSet? _privateFonts;
    private Showcase? _current;
    private string _html = string.Empty;
    // The page of the web showcase as it was fetched, kept so a change of theme lays it out again with the other theme's classes without fetching it again.
    private string? _webSource;
    private Slider? _widthSlider;
    private ToggleSwitch? _fillToggle;
    private string _lastLink = string.Empty;
    private double _loadMilliseconds;
    private bool _followLinks;
    private TextBox? _addressBox;
    private readonly TextBlock _address = new() { Text = "No address opened", TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.7, FontSize = 12 };

    public DemoWindow()
    {
        Title = "PeachBrackets.WinUI demo";
        PhaseTimings.Start();
        _view.DocumentLoaded += (_, _) =>
        {
            _loadMilliseconds = _loadTimer.Elapsed.TotalMilliseconds;
            if (Environment.GetEnvironmentVariable("PEACH_TIMINGS_FILE") is { Length: > 0 } report)
                PhaseTimings.WriteReport(report);

            UpdateStatus();

            // `PEACH_SCROLL_Y=<dip>` scrolls the view there once the document has loaded, to look at a part of a long page.
            if (double.TryParse(Environment.GetEnvironmentVariable("PEACH_SCROLL_Y"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scrollY)
                )
            {
                var wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                wait.Tick += (_, _) =>
                {
                    wait.Stop();
                    if (FindScrollViewer(_view) is { } scroller)
                        scroller.ChangeView(null, scrollY, null, disableAnimation: true);
                };
                wait.Start();
            }
        };
        _view.DocumentFailed += (_, e) => { _status.Text = "Failed to load: " + e.Exception.Message; };
        _view.ContentHeightChanged += (_, _) => UpdateStatus();
        // A link to a web address is only reported, so the demo does not open a browser; a link to a place in the document is left to the view, which scrolls to it.
        _view.LinkClicked += (_, e) =>
        {
            _lastLink = e.Uri ?? "#" + e.Destination;
            e.Cancel = e.Uri is not null;
            if (_followLinks && e.Uri is { } target && Uri.TryCreate(target, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                NavigateTo(target);

            UpdateStatus();
        };

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var left = BuildList();
        Grid.SetColumn(left, 0);
        var center = BuildStage();
        Grid.SetColumn(center, 1);
        var right = BuildPanel();
        Grid.SetColumn(right, 2);
        _status.Margin = new Thickness(12, 6, 12, 6);
        var statusBar = new Border { Child = _status, BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)) };
        Grid.SetRow(statusBar, 1);
        Grid.SetColumnSpan(statusBar, 3);

        _root.Children.Add(left);
        _root.Children.Add(center);
        _root.Children.Add(right);
        _root.Children.Add(statusBar);
        Content = _root;
        _root.ActualThemeChanged += (_, _) =>
        {
            if (_current is { IsWeb: true } web && _webSource is not null)
                _ = ShowWebPageAsync(web);
        };

        FillList();
        ApplySettings();
        // `--showcase <slug>` opens that showcase first (the demo can then be driven without finding it in the list).
        var args = Environment.GetCommandLineArgs();
        // `--height <dip>` sets the height of the box the view sits in (the slider stops at 1000), to see more of a page at once.
        if (Array.IndexOf(args, "--height") is var heightAt and >= 0 && heightAt + 1 < args.Length
            && double.TryParse(args[heightAt + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hostHeight))
        {
            _settings.HostHeight = hostHeight;
            ApplySettings();
        }

        var wanted = Array.IndexOf(args, "--showcase") is var at and >= 0 && at + 1 < args.Length ? _shown.FindIndex(s => s.Slug == args[at + 1]) : -1;
        // `--uri <address>` opens that web address (or file) first.
        if (Array.IndexOf(args, "--uri") is var uriAt and >= 0 && uriAt + 1 < args.Length)
            NavigateTo(args[uriAt + 1]);
        else if (_list.Items.Count > 0)
            _list.SelectedIndex = Math.Max(0, wanted);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => UpdateStatus();
        timer.Start();
    }

    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Microsoft.UI.Xaml.Controls.ScrollViewer { ScrollableHeight: > 0 } viewer)
                return viewer;

            if (FindScrollViewer(child) is { } found)
                return found;
        }

        return null;
    }

    // The list of showcases, with a search box over it.
    private FrameworkElement BuildList()
    {
        // A text box would be the natural search field, but the text box of the Windows App SDK cannot be created in a code-only app on every
        // system (it asks for a feature that is not always there), so the list is narrowed by category instead.
        _categories.Margin = new Thickness(8);
        _categories.Items.Add("All showcases");
        foreach (var category in _showcases.Select(s => s.Category).Distinct())
            _categories.Items.Add(category);

        _categories.SelectedIndex = 0;
        _categories.SelectionChanged += (_, _) => FillList();

        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedIndex is >= 0 and var index && index < _shown.Count)
                Show(_shown[index]);
        };

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(_categories);
        Grid.SetRow(_list, 1);
        panel.Children.Add(_list);
        return panel;
    }

    /// <summary>
    /// Opens an address the way a browser would: an http(s) address is fetched, a file: address or an existing path is read from disk, and anything
    /// else that looks like a host name is tried as https.
    /// </summary>
    private void NavigateTo(string? text)
    {
        text = text?.Trim().Trim('"');
        if (string.IsNullOrEmpty(text))
        {
            _address.Text = "No address given";
            return;
        }

        Showcase showcase;
        if (File.Exists(text) || (Uri.TryCreate(text, UriKind.Absolute, out var fileUri) && fileUri.IsFile && File.Exists(fileUri.LocalPath)))
        {
            var path = File.Exists(text) ? System.IO.Path.GetFullPath(text) : new Uri(text).LocalPath;
            showcase = new Showcase("custom_file", "Address", System.IO.Path.GetFileName(path), path, path);
        }
        else
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                if (!text.Contains("://") && Uri.TryCreate("https://" + text, UriKind.Absolute, out uri) && uri.Host.Contains('.'))
                {
                }
                else
                {
                    _address.Text = "Not an address the demo can open: " + text;
                    return;
                }
            }

            showcase = new Showcase("custom_web", "Address", uri.Host, uri.AbsoluteUri, uri.AbsoluteUri);
        }

        _address.Text = showcase.Path;
        if (_addressBox is not null)
            _addressBox.Text = showcase.Path;
        _list.SelectedIndex = -1;
        Show(showcase);
    }

    private void FillList()
    {
        var category = _categories.SelectedIndex > 0 ? _categories.SelectedItem as string : null;
        _shown.Clear();
        _shown.AddRange(_showcases.Where(s => category is null || s.Category == category));

        _list.Items.Clear();
        foreach (var showcase in _shown)
        {
            var row = new StackPanel { Padding = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = showcase.Title, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(new TextBlock { Text = showcase.Category, FontSize = 11, Opacity = 0.6 });
            _list.Items.Add(row);
        }
    }

    // The address bar above the page: a field for the address, paste-and-go, and whether links in the page are followed.
    private FrameworkElement BuildAddressBar()
    {
        // Opening an address. A text box would be the natural field, but see above: the address is taken from the clipboard instead (copy it, press the
        // button), or from `--uri`, or by following a link in the page.
        var open = new Button { Content = "Open address from clipboard", HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += async (_, _) =>
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                _address.Text = "The clipboard holds no text";
                return;
            }

            NavigateTo(await content.GetTextAsync());
        };
        var box = new TextBox { PlaceholderText = "https://example.com or a file path", HorizontalAlignment = HorizontalAlignment.Stretch };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                NavigateTo(box.Text);
        };
        var go = new Button { Content = "Go" };
        go.Click += (_, _) => NavigateTo(box.Text);
        _addressBox = box;
        var follow = new ToggleSwitch { IsOn = _followLinks, OnContent = "Follow links", OffContent = "Follow links" };
        follow.Toggled += (_, _) => _followLinks = follow.IsOn;
        open.Content = "Paste & go";
        open.HorizontalAlignment = HorizontalAlignment.Left;
        var bar = new Grid { Margin = new Thickness(16, 12, 16, 0), ColumnSpacing = 8 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bar.Children.Add(box);
        Grid.SetColumn(go, 1);
        bar.Children.Add(go);
        Grid.SetColumn(open, 2);
        bar.Children.Add(open);
        follow.Header = null;
        follow.OnContent = "Follow links";
        follow.OffContent = "Follow links";
        Grid.SetColumn(follow, 3);
        bar.Children.Add(follow);
        Grid.SetRow(_address, 1);
        Grid.SetColumnSpan(_address, 4);
        bar.Children.Add(_address);
        return bar;
    }

    // The middle: the title of the showcase, and the box the widget sits in.
    private FrameworkElement BuildStage()
    {
        _hostBox.BorderThickness = new Thickness(1);
        _hostBox.BorderBrush = new SolidColorBrush(Color.FromArgb(160, 90, 120, 180));
        _hostBox.Margin = new Thickness(16);
        _hostBox.HorizontalAlignment = HorizontalAlignment.Left;
        _hostBox.VerticalAlignment = VerticalAlignment.Top;
        _hostBox.Child = _view;

        _stage.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _stage.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _stage.HorizontalScrollMode = ScrollMode.Auto;
        _stage.Content = _hostBox;

        // The box follows the width the window gives the stage, so the document fills it (a document with a width of its own keeps that).
        _stage.SizeChanged += (_, _) => FitHostWidth();

        var header = new StackPanel { Margin = new Thickness(16, 12, 16, 0), Spacing = 2 };
        header.Children.Add(_title);
        header.Children.Add(_description);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(BuildAddressBar());
        Grid.SetRow(header, 1);
        grid.Children.Add(header);
        Grid.SetRow(_stage, 2);
        grid.Children.Add(_stage);
        return grid;
    }

    // The knobs. Each has a name, one line saying what it does, and its control.
    private FrameworkElement BuildPanel()
    {
        var panel = new StackPanel { Padding = new Thickness(12), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Host knobs", FontSize = 18, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } });

        Section(panel, "The host's box");
        _fillToggle = Toggle(_settings.FillWidth, v => { _settings.FillWidth = v; ApplySettings(); });
        Knob(panel, "Fill width", "The host's box is as wide as the window and follows it as it is resized; the document lays out to that width, and fills it unless it sets a width of its own.", _fillToggle);
        _widthSlider = Slider(200, 1200, _settings.HostWidth, v => { _settings.HostWidth = v; ApplySettings(); });
        Knob(panel, "Width", "How wide the host makes the widget when Fill width is off. The document lays out to this width.", _widthSlider);
        Knob(panel, "Height", "Fixed: the widget has a height and scrolls. Content: the widget is as tall as its document, like a row in a list.",
            Combo(["Fixed", "Content"], (int)_settings.HeightMode, i => { _settings.HeightMode = (HeightMode)i; ApplySettings(); }));
        Knob(panel, "Fixed height", "The height used when Height is Fixed. It is also the viewport that vh and fixed boxes use.",
            Slider(200, 1000, _settings.HostHeight, v => { _settings.HostHeight = v; ApplySettings(); }));
        Knob(panel, "List mode", "Shows the document 100 times in a virtualized list: one view per row, recycled as it scrolls.",
            Toggle(_settings.ListMode, v => { _settings.ListMode = v; ApplySettings(); }));

        Section(panel, "HtmlView");
        Knob(panel, "ScrollMode", "Auto: the view scrolls and zooms itself. Disabled: the view is as tall as its document and leaves scrolling to its parent (forced by Content height and List mode).",
            Combo(["Auto", "Disabled"], (int)_settings.ScrollMode, i => { _settings.ScrollMode = (HtmlViewScrollMode)i; ApplySettings(); }));
        Knob(panel, "IsZoomEnabled", "Whether Ctrl+wheel and pinch zoom the document.",
            Toggle(_settings.IsZoomEnabled, v => { _settings.IsZoomEnabled = v; ApplySettings(); }));
        Knob(panel, "IsLayoutSynchronous", "Says the document needs nothing asynchronous, so it is laid out in the pass that measures it. Pages with images from the web will fail.",
            Toggle(_settings.IsLayoutSynchronous, v => { _settings.IsLayoutSynchronous = v; ApplySettings(); }));
        Knob(panel, "DocumentMargin", "Space left around the document, in device-independent pixels. None by default.",
            Slider(0, 48, _settings.DocumentMargin, v => { _settings.DocumentMargin = v; ApplySettings(); }));
        Knob(panel, "PageBackground", "What shows behind the document where it paints nothing.",
            Combo(["White", "Transparent", "Light grey", "Dark"], 0, i =>
            {
                _settings.PageBackground = i switch
                {
                    1 => Colors.Transparent,
                    2 => Color.FromArgb(255, 235, 235, 235),
                    3 => Color.FromArgb(255, 28, 28, 32),
                    _ => Colors.White,
                };
                ApplySettings();
            }));
        Knob(panel, "AnimationsEnabled", "Whether CSS animations, transitions and animated images play. transform and opacity are kept as textures on the GPU and drawn again each frame.",
            Toggle(_settings.AnimationsEnabled, v => { _settings.AnimationsEnabled = v; ApplySettings(); }));
        Knob(panel, "AllowNetworkAccess", "Whether the document may load images, style sheets and fonts from the web. Off by default; data: URIs and files always load.",
            Toggle(_settings.AllowNetworkAccess, v => { _settings.AllowNetworkAccess = v; ApplySettings(); }));
        Knob(panel, "SelectionColor", "The colour behind selected text. Select some text to see it.",
            Combo(["Light blue", "Yellow", "Pink", "Green"], 0, i =>
            {
                _settings.SelectionColor = i switch
                {
                    1 => Color.FromArgb(255, 253, 224, 71),
                    2 => Color.FromArgb(255, 249, 168, 212),
                    3 => Color.FromArgb(255, 134, 239, 172),
                    _ => Color.FromArgb(255, 173, 214, 255),
                };
                ApplySettings();
            }));
        Knob(panel, "Theme", "The theme the view reports to prefers-color-scheme. Try the Light and dark showcase.",
            Combo(["Default", "Light", "Dark"], 0, i => { _settings.Theme = (ElementTheme)i; ApplySettings(); }));
        Knob(panel, "Input", "The pointing devices the document is told about (hover, pointer and any-pointer media features). Detect asks the system.",
            Combo(["Detect", "Mouse", "Touch", "None"], 0, i => { _settings.Input = (InputChoice)i; ApplySettings(); }));
        Knob(panel, "TextRendering", "How text is anti-aliased: chosen by the system, grayscale, or LCD sub-pixel.",
            Combo(["Auto", "Grayscale", "LCD"], 0, i =>
            {
                _settings.TextRendering = i switch { 1 => Win2dTextRendering.Grayscale, 2 => Win2dTextRendering.Lcd, _ => Win2dTextRendering.Auto };
                ApplySettings();
            }));

        Knob(panel, "UserStyleSheet", "CSS applied before the document's own. The serif preset uses the registered demo font, once you have registered it below.",
            Combo(["None", "Larger text", "Serif and colour", "Tight lines", "Everything outlined"], 0, i =>
            {
                _settings.UserStyleSheet = i switch
                {
                    1 => "body { font-size: 20px }",
                    2 => "body { font-family: 'Demo Sans', serif } h1, h2 { color: #be123c } a { color: #047857 }",
                    3 => "body { line-height: 1.1 }",
                    4 => "* { outline: 1px solid rgba(220, 38, 38, .35) }",
                    _ => string.Empty,
                };
                ApplySettings();
            }));

        Section(panel, "Fonts");
        var fonts = new TextBlock { Text = "Not registered", Opacity = 0.7 };
        var register = new Button { Content = "Register 'Demo Sans'" };
        register.Click += (_, _) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "fonts", "SourceSans3-Regular.ttf");
            (_settings.PrivateFontSet ? EnsurePrivateFonts() : HtmlViewFonts.Shared).AddFile(path, new AddOptions { FamilyName = "Demo Sans" });
            fonts.Text = "Registered, parsed once and shared by every view";
            ReloadDocument();
        };
        Knob(panel, "HtmlViewFonts / FontSet", "Adds a font to the set every view shares, so it is parsed once. The Private FontSet switch gives this view a set of its own.",
            new StackPanel { Spacing = 6, Children = { register, fonts } });
        Knob(panel, "Private FontSet", "Gives the view its own FontSet instead of the shared one (the font is registered on whichever is in use).",
            Toggle(_settings.PrivateFontSet, v => { _settings.PrivateFontSet = v; _view.FontSet = (v ? EnsurePrivateFonts() : null)!; ReloadDocument(); }));

        return new ScrollViewer { Content = panel };
    }

    private FontSet EnsurePrivateFonts() => _privateFonts ??= new FontSet();

    private static void Section(Panel panel, string text) =>
        panel.Children.Add(new TextBlock { Text = text, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }, Margin = new Thickness(0, 8, 0, 0), Opacity = 0.8 });

    private static void Knob(Panel panel, string name, string help, UIElement control)
    {
        var box = new StackPanel { Spacing = 2 };
        box.Children.Add(new TextBlock { Text = name, FontFamily = new FontFamily("Cascadia Mono"), FontSize = 13 });
        box.Children.Add(new TextBlock { Text = help, FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
        box.Children.Add(control);
        panel.Children.Add(box);
    }

    private static ToggleSwitch Toggle(bool on, Action<bool> changed)
    {
        var toggle = new ToggleSwitch { IsOn = on, OnContent = "On", OffContent = "Off" };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        return toggle;
    }

    private static ComboBox Combo(string[] items, int selected, Action<int> changed)
    {
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in items)
            combo.Items.Add(item);

        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) changed(combo.SelectedIndex); };
        return combo;
    }

    private static Slider Slider(double min, double max, double value, Action<double> changed)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = value };
        slider.ValueChanged += (_, e) => changed(e.NewValue);
        return slider;
    }

    private void Show(Showcase showcase)
    {
        _current = showcase;
        _webSource = null;
        _title.Text = showcase.Title;
        _description.Text = showcase.Description;
        if (!showcase.IsWeb)
        {
            _view.DocumentBaseUri = showcase.Slug == "custom_file" ? new Uri(showcase.Path) : null;
            _html = File.ReadAllText(showcase.Path);
            ReloadDocument();
            return;
        }

        // A web page is laid out for a desktop-wide window.
        if (_fillToggle is not null)
            _fillToggle.IsOn = false;
        else
            _settings.FillWidth = false;

        if (_widthSlider is not null)
            _widthSlider.Value = 1200;
        else
            _settings.HostWidth = 1200;

        // A web page: fetch it off the UI thread, then come back to it. PageRewriter then gives it a base address (so what it refers to is found) and the
        // classes its own startup script would have set on <html>, which the control does not run.
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            string? page = null;
            string? failure = null;
            try
            {
                using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PeachBrackets.HostResourceLoader.UserAgent);
                page = await client.GetStringAsync(showcase.Path);
            }
            catch (Exception exception)
            {
                failure = $"<p style='font:16px sans-serif;padding:16pt'>Could not fetch {showcase.Path}: {System.Net.WebUtility.HtmlEncode(exception.Message)}</p>";
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_current, showcase))
                    return;

                if (failure is not null)
                {
                    _view.DocumentBaseUri = null;
                    _html = failure;
                    ReloadDocument();
                    return;
                }

                _webSource = page;
                _ = ShowWebPageAsync(showcase);
            });
        });
    }

    // Lays the fetched page of a web showcase out for the theme the demo shows now. The page is rewritten off the UI thread (it is parsed and written out
    // again), and dropped if the showcase changed meanwhile.
    private async System.Threading.Tasks.Task ShowWebPageAsync(Showcase showcase)
    {
        var source = _webSource;
        if (source is null)
            return;

        var dark = _root.ActualTheme == ElementTheme.Dark;
        string html;
        try
        {
            html = await System.Threading.Tasks.Task.Run(() => PageRewriter.Rewrite(source, showcase.Path, WebPageTraits.RootClasses(showcase.Slug, dark)));
        }
        catch (Exception exception)
        {
            html = $"<p style='font:16px sans-serif;padding:16pt'>Could not prepare {showcase.Path}: {System.Net.WebUtility.HtmlEncode(exception.Message)}</p>";
        }

        if (!ReferenceEquals(_current, showcase) || !ReferenceEquals(_webSource, source))
            return;

        _view.DocumentBaseUri = new Uri(showcase.Path);
        _settings.AllowNetworkAccess = true;
        ApplySettings();
        _html = html;
        ReloadDocument();
    }

    private void ReloadDocument()
    {
        PhaseTimings.Reset();
        _loadTimer.Restart();
        _lastLink = string.Empty;
        if (_settings.ListMode)
        {
            BuildListHost();
            return;
        }

        if (!ReferenceEquals(_hostBox.Child, _view))
            _hostBox.Child = _view;

        // A new string is what makes the view load again; the same one would do nothing.
        _view.Html = _html + "<!-- " + Environment.TickCount64 + " -->";
    }

    // The same document in each row of a virtualized list.
    private void BuildListHost()
    {
        var repeater = new ItemsRepeater
        {
            ItemsSource = Enumerable.Range(0, 100).ToList(),
            ItemTemplate = new RowFactory(this),
            Layout = new StackLayout { Spacing = 8 },
        };
        _hostBox.Child = new ScrollViewer { Content = repeater, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private sealed class RowFactory(DemoWindow owner) : IElementFactory
    {
        private readonly Stack<HtmlView> _pool = new();

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            var view = _pool.Count > 0 ? _pool.Pop() : new HtmlView();
            owner._settings.Apply(view, row: true);
            if (owner._privateFonts is { } fonts && owner._settings.PrivateFontSet)
                view.FontSet = fonts;

            view.Html = owner._html + "<!-- row " + args.Data + " -->";
            return view;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            if (args.Element is HtmlView view)
                _pool.Push(view);
        }
    }

    // The width of the box: the stage's, less the box's margin and the room a vertical scroll bar may take, when it fills the window; otherwise the knob's.
    private void FitHostWidth()
    {
        if (!_settings.FillWidth)
        {
            _hostBox.Width = _settings.HostWidth;
            return;
        }

        var available = _stage.ActualWidth - _hostBox.Margin.Left - _hostBox.Margin.Right - 20;
        _hostBox.Width = available >= 200 ? available : _settings.HostWidth;
    }

    // Hands every knob to the view and the box it sits in.
    private void ApplySettings()
    {
        _settings.Apply(_view);
        FitHostWidth();
        _hostBox.Height = _settings.HeightMode == HeightMode.Fixed || _settings.ListMode ? _settings.HostHeight : double.NaN;
        _hostBox.Background = new SolidColorBrush(_settings.PageBackground);
        _root.RequestedTheme = _settings.Theme;
        if (_settings.ListMode && _current is not null)
            ReloadDocument();

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var pages = _view.Document?.Pages.Count.ToString() ?? "-";
        var scroll = _view.Document is null ? "-" : $"{_view.Document.Viewport.Y:0}";
        _status.Text = $"{_current?.Slug}   pages: {pages}   ContentHeight: {_view.ContentHeight:0} DIP   view: {_view.ActualWidth:0}x{_view.ActualHeight:0}   "
            + $"viewport top: {scroll} pt   load: {_loadMilliseconds:0} ms" + (PhaseTimings.Summary() is { Length: > 0 } phases ? $"   [{phases}]" : string.Empty) + (_lastLink.Length > 0 ? $"   link: {_lastLink}" : string.Empty);
    }
}

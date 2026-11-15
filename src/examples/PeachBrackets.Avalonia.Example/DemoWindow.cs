using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using PeachBrackets.Apple.Example;
using PeachBrackets.Avalonia;
using PeachDrawing.Skia;

namespace PeachBrackets.Avalonia.Example;

/// <summary>
/// The demo window: the showcases on the left (with a category filter), the page in the middle, the view's options on the right and a status bar below. It is built in code,
/// as the other demos are, so the example has no XAML to compile.
/// </summary>
internal sealed class DemoWindow : Window
{
    private const string AllCategories = "All categories";

    private readonly IReadOnlyList<Showcase> _showcases;
    private readonly HtmlView _view = new() { AnimationsEnabled = true };
    private readonly ComboBox _category = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _list = new();
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 4), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _zoomText = new() { MinWidth = 44, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _dark = new() { Content = "Dark theme" };
    private readonly CheckBox _network = new() { Content = "Allow network access" };
    private int _showVersion;
    private Showcase? _current;

    public DemoWindow(string root)
    {
        Title = "PeachBrackets.Avalonia";
        Width = 1280;
        Height = 820;
        _showcases = ShowcaseCatalog.Load(root);

        var categories = new[] { AllCategories }.Concat(_showcases.Select(s => s.Category).Distinct()).ToList();
        _category.ItemsSource = categories;
        _category.SelectedIndex = 0;
        _category.SelectionChanged += (_, _) => FillList();

        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is Showcase showcase && !ReferenceEquals(showcase, _current))
                _ = ShowAsync(showcase);
        };
        _list.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<Showcase>((s, _) =>
            new TextBlock { Text = s?.Title, TextTrimming = TextTrimming.CharacterEllipsis }, supportsRecycling: true);

        WireView();

        var left = new DockPanel { Width = 270 };
        var filter = new StackPanel { Margin = new Thickness(8), Spacing = 6, Children = { new TextBlock { Text = "Showcases", FontWeight = FontWeight.SemiBold }, _category } };
        DockPanel.SetDock(filter, Dock.Top);
        left.Children.Add(filter);
        left.Children.Add(_list);

        var heading = new StackPanel { Margin = new Thickness(12, 8), Spacing = 2, Children = { _title, _description } };
        var center = new DockPanel();
        DockPanel.SetDock(heading, Dock.Top);
        center.Children.Add(heading);
        center.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 1), Child = _view });

        var status = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 1, 0, 0), Child = _status };
        var root2 = new DockPanel();
        DockPanel.SetDock(status, Dock.Bottom);
        root2.Children.Add(status);
        var right = BuildSettings();
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        root2.Children.Add(left);
        root2.Children.Add(right);
        root2.Children.Add(center);
        Content = root2;

        FillList();
        _status.Text = "Choose a showcase.";
    }

    /// <summary>The view, for the smoke and screenshot modes.</summary>
    public HtmlView View => _view;

    /// <summary>The showcases of the demo.</summary>
    public IReadOnlyList<Showcase> Showcases => _showcases;

    /// <summary>Shows a showcase by its slug (the sample page when there is none).</summary>
    public Task ShowAsync(string slug) => ShowAsync(_showcases.FirstOrDefault(s => s.Slug == slug) ?? _showcases[0]);

    private void FillList()
    {
        var category = _category.SelectedItem as string;
        var items = _showcases.Where(s => category is null or AllCategories || s.Category == category).ToList();
        _list.ItemsSource = items;
        if (_current is not null && items.Contains(_current))
            _list.SelectedItem = _current;
    }

    private void WireView()
    {
        _view.DocumentLoaded += (_, _) => Dispatcher.UIThread.Post(() => ReportStatus("Loaded"));
        _view.DocumentFailed += (_, e) => _status.Text = "Failed: " + e.Exception.Message;
        _view.LinkActivated += (_, e) => _status.Text = "Link: " + (e.Uri ?? "#" + e.Destination);
        _view.PageClicked += (_, e) => _status.Text = $"Clicked page {e.PageIndex} at {e.X:F0}, {e.Y:F0}";
        _view.FormSubmitting += (_, e) => _status.Text = "Form submitted: " + e.Submission.Serialize().Body.Length + " bytes";
        _view.FormInvalid += (_, e) => _status.Text = "Form invalid: " + e.Problems.Count + " problem(s)";
        _view.ZoomChanged += (_, _) => _zoomText.Text = $"{_view.Zoom:P0}";
        _view.ContentHeightChanged += (_, _) => ReportStatus("Laid out");
        _view.ScrollChanged += (_, _) => ReportStatus(null);
        _view.AnimationEvents += (_, e) => _status.Text = $"Animation events: {e.Events.Count} (first: {e.Events[0].Kind} {e.Events[0].Name})";
    }

    private void ReportStatus(string? what)
    {
        var selected = _view.SelectedText.Length;
        var prefix = what is null ? _status.Text?.Split(" | ")[0] ?? string.Empty : what;
        _status.Text = $"{prefix} | {_view.ContentHeight:F0} px tall | zoom {_view.Zoom:P0}"
            + (selected > 0 ? $" | {selected} characters selected" : string.Empty);
    }

    private async Task ShowAsync(Showcase showcase)
    {
        var version = ++_showVersion;
        _current = showcase;
        _title.Text = showcase.Title;
        _description.Text = showcase.Description;

        if (showcase.IsWeb)
        {
            // A web page needs the network, so the option goes on; the page is fetched here and prepared for a view that runs no scripts.
            _network.IsChecked = true;
            _status.Text = "Fetching " + showcase.Path + "...";
            var (page, fetched) = await WebPage.FetchAsync(showcase.Path);
            if (version != _showVersion)
                return;

            var html = fetched ? await Task.Run(() => WebPage.Prepare(page, showcase, _dark.IsChecked == true)) : page;
            if (version != _showVersion)
                return;

            _view.Source = null;
            _view.Html = html;
            return;
        }

        _view.Source = null;
        _view.Html = showcase.ReadHtml();
    }

    private Control BuildSettings()
    {
        var animations = Check("Play animations", _view.AnimationsEnabled, v => _view.AnimationsEnabled = v);
        var zoomEnabled = Check("Allow zoom", _view.IsZoomEnabled, v => _view.IsZoomEnabled = v);
        var sync = Check("Lay out synchronously", _view.IsLayoutSynchronous, v => _view.IsLayoutSynchronous = v);
        var autoHeight = Check("Take the height of the page (no scrolling)", false, v => _view.ScrollMode = v ? HtmlViewScrollMode.Disabled : HtmlViewScrollMode.Auto);
        _network.IsCheckedChanged += (_, _) => _view.AllowNetworkAccess = _network.IsChecked == true;
        _dark.IsCheckedChanged += (_, _) =>
        {
            var dark = _dark.IsChecked == true;
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            _view.PageBackground = dark ? Color.FromRgb(0x1e, 0x1e, 0x1e) : Colors.White;
        };
        _view.PageBackground = Colors.White;

        // How text is drawn: Auto follows the system (ClearType on Windows), the others force a mode.
        var textRendering = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Enum.GetValues<SkiaTextRendering>(), SelectedItem = _view.TextRendering };
        textRendering.SelectionChanged += (_, _) =>
        {
            if (textRendering.SelectedItem is SkiaTextRendering mode)
                _view.TextRendering = mode;
        };

        var margin = new Slider { Minimum = 0, Maximum = 80, Value = 0, Width = 200 };
        margin.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                _view.DocumentMargin = Math.Round(margin.Value);
        };

        var zoomOut = new Button { Content = "−" };
        var zoomIn = new Button { Content = "+" };
        var zoomReset = new Button { Content = "100%" };
        zoomOut.Click += (_, _) => _view.SetZoom(_view.Zoom / 1.2);
        zoomIn.Click += (_, _) => _view.SetZoom(_view.Zoom * 1.2);
        zoomReset.Click += (_, _) => _view.SetZoom(1);
        _zoomText.Text = "100%";

        var userStyle = new TextBox { AcceptsReturn = true, Height = 80, PlaceholderText = "A user style sheet, e.g. body { font-size: 20px }", FontFamily = new FontFamily("Consolas, monospace") };
        userStyle.LostFocus += (_, _) => _view.UserStyleSheet = string.IsNullOrWhiteSpace(userStyle.Text) ? null : userStyle.Text;

        var open = new Button { Content = "Open an HTML file..." };
        open.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open an HTML file",
                FileTypeFilter = [new FilePickerFileType("HTML") { Patterns = ["*.html", "*.htm"] }],
            });

            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                _current = null;
                _list.SelectedItem = null;
                _title.Text = Path.GetFileName(path);
                _description.Text = path;
                _view.Html = null;
                _view.Source = new Uri(path);
            }
        };

        var copy = new Button { Content = "Copy selection" };
        copy.Click += (_, _) => _view.CopySelection();
        var all = new Button { Content = "Select all" };
        all.Click += (_, _) => _view.SelectAll();

        var panel = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Options", FontWeight = FontWeight.SemiBold },
                animations, zoomEnabled, sync, autoHeight, _network, _dark,
                new TextBlock { Text = "Zoom" },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { zoomOut, zoomReset, zoomIn, _zoomText } },
                new TextBlock { Text = "Text rendering" },
                textRendering,
                new TextBlock { Text = "Margin around the document" },
                margin,
                new TextBlock { Text = "User style sheet (applies on the next load)" },
                userStyle,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { all, copy } },
                open,
            },
        };

        return new ScrollViewer { Width = 270, Content = panel };
    }

    private static CheckBox Check(string text, bool initial, Action<bool> set)
    {
        var box = new CheckBox { Content = text, IsChecked = initial };
        box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
        return box;
    }
}

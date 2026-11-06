using AppKit;
using CoreGraphics;
using Foundation;
using PeachBrackets.Apple;
using PeachDrawing.Core;

namespace PeachBrackets.Apple.Example;

/// <summary>
/// The demo, laid out like the Windows one: every showcase in a list (with a category filter) on the left, the selected one in an <see cref="HtmlView"/> inside the box a host
/// would give it in the middle, the knobs the view offers a host on the right, and a status bar under it all. Built in code, with no nib.
/// </summary>
internal sealed class DemoWindow
{
    private readonly IReadOnlyList<Showcase> _all;
    private readonly List<Showcase> _shown = [];
    private readonly Settings _settings = new();
    private readonly NSPopUpButton _categories = new(new CGRect(0, 0, 100, 26), false);
    private readonly NSTableView _table = new();
    private readonly NSTextField _title = Label(18, bold: true);
    private readonly NSTextField _description = Label(12, secondary: true);
    private readonly NSTextField _status = Label(11, mono: true);
    private readonly Stage _stage;
    private readonly KnobPanel _panel = new();
    private readonly System.Diagnostics.Stopwatch _loadTimer = new();
    private Showcase? _current;
    private string _html = string.Empty;
    private string? _webSource;
    private double _loadMilliseconds;
    private string _lastLink = string.Empty;
    private HtmlView _view;

    public DemoWindow(bool cpu)
    {
        _settings.UseGpu = !cpu;
        _all = ShowcaseCatalog.Load(AppContext.BaseDirectory);
        _stage = new Stage(_title, _description, _settings);
        _view = NewView();
        _stage.HostBox.AddSubview(_view);

        Window = new UnconstrainedWindow(new CGRect(60, 60, 1360, 860), NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered, false)
        {
            Title = "PeachBrackets.Apple demo",
            MinSize = new CGSize(900, 500),
        };

        const double statusHeight = 24;
        var content = new NSView(new CGRect(0, 0, 1360, 860)) { AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable };
        var split = new NSSplitView(new CGRect(0, statusHeight, 1360, 860 - statusHeight))
        {
            IsVertical = true,
            DividerStyle = NSSplitViewDividerStyle.Thin,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };
        var left = BuildList();
        var right = BuildPanel();
        split.AddArrangedSubview(left);
        split.AddArrangedSubview(_stage);
        split.AddArrangedSubview(right);
        left.Frame = new CGRect(0, 0, 270, split.Frame.Height);
        right.Frame = new CGRect(0, 0, 320, split.Frame.Height);
        split.SetHoldingPriority(260, 0);
        split.SetHoldingPriority(260, 2);
        split.SetHoldingPriority(240, 1);
        content.AddSubview(split);

        _status.Frame = new CGRect(12, 0, 1336, statusHeight);
        _status.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MaxYMargin;
        content.AddSubview(_status);
        Window.ContentView = content;
        split.SetPositionOfDivider(270, 0);
        split.SetPositionOfDivider(1360 - 320, 1);

        FillList();
        Apply();

        var timer = NSTimer.CreateRepeatingScheduledTimer(0.5, _ => UpdateStatus());
        _ = timer;
        if (_shown.Count > 0)
            _table.SelectRow(0, false);

        Show(_shown[0]);
    }

    /// <summary>The window.</summary>
    public NSWindow Window { get; }

    /// <summary>The view the showcase is in (it is replaced when the GPU setting changes).</summary>
    public HtmlView View => _view;

    /// <summary>Shows a showcase by its slug; false when there is no such one.</summary>
    public bool Show(string slug)
    {
        if (_all.FirstOrDefault(s => s.Slug == slug) is not { } showcase)
            return false;

        if (!_shown.Contains(showcase))
        {
            _categories.SelectItem(0);
            FillList();
        }

        var index = _shown.IndexOf(showcase);
        _table.SelectRow(index, false);
        _table.ScrollRowToVisible(index);
        Show(showcase);
        return true;
    }

    private HtmlView NewView()
    {
        var view = new HtmlView { AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable, UseGpu = _settings.UseGpu };
        view.DocumentLoaded += (_, _) =>
        {
            _loadMilliseconds = _loadTimer.Elapsed.TotalMilliseconds;
            UpdateStatus();
        };
        view.DocumentFailed += (_, e) => _status.StringValue = "Failed to load: " + e.Exception.Message;

        // A link to a web address is only reported, so the demo does not open a browser; a link to a place in the document is left to the view, which scrolls to it.
        view.LinkActivated += (_, e) =>
        {
            _lastLink = e.Uri ?? "#" + e.Destination;
            e.Cancel = e.Uri is not null;
            UpdateStatus();
        };
        return view;
    }

    // ---- the list ----

    private NSView BuildList()
    {
        var panel = new NSView(new CGRect(0, 0, 270, 800)) { AutoresizingMask = NSViewResizingMask.HeightSizable };
        _categories.AddItem("All showcases");
        foreach (var category in _all.Select(s => s.Category).Distinct())
            _categories.AddItem(category);

        _categories.Frame = new CGRect(8, 770, 254, 26);
        _categories.AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.MinYMargin;
        _categories.Activated += (_, _) => FillList();

        var column = new NSTableColumn("showcase") { Title = "Showcases", ResizingMask = NSTableColumnResizing.Autoresizing };
        _table.AddColumn(column);
        _table.HeaderView = null;
        _table.RowHeight = 40;
        _table.Style = NSTableViewStyle.SourceList;
        _table.DataSource = new TableSource(_shown);
        _table.Delegate = new TableDelegate(_shown, row =>
        {
            if (row >= 0 && row < _shown.Count)
                Show(_shown[row]);
        });

        var scroller = new NSScrollView(new CGRect(0, 0, 270, 764))
        {
            DocumentView = _table,
            HasVerticalScroller = true,
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
        };
        panel.AddSubview(_categories);
        panel.AddSubview(scroller);
        return panel;
    }

    private void FillList()
    {
        var category = _categories.IndexOfSelectedItem > 0 ? _categories.TitleOfSelectedItem : null;
        _shown.Clear();
        _shown.AddRange(_all.Where(s => category is null || s.Category == category));
        _table.ReloadData();
    }

    // ---- the knobs ----

    private NSView BuildPanel()
    {
        _panel.AutoresizingMask = NSViewResizingMask.WidthSizable;
        _panel.Heading("Host knobs");

        _panel.Section("The host's box");
        _panel.Knob("Width", "How wide the host makes the view. The document lays out to this width.",
            Slider(_settings.HostWidth, 320, 1400, v => { _settings.HostWidth = v; Apply(); }));
        _panel.Knob("Fill height", "On: the view fills the stage and scrolls. Off: it has the height below (which is also what vh and fixed boxes use).",
            Check("Fill the stage", _settings.FillHeight, v => { _settings.FillHeight = v; Apply(); }));
        _panel.Knob("Height", "The height used when Fill height is off.",
            Slider(_settings.HostHeight, 200, 1200, v => { _settings.HostHeight = v; Apply(); }));

        _panel.Section("HtmlView");
        _panel.Knob("PageBackground", "What shows behind the document where it paints nothing.",
            Popup(["White", "Transparent", "Light grey", "Dark"], 0, i => { _settings.Background = i; Apply(); }));
        _panel.Knob("AllowNetworkAccess", "Whether the document may load images, style sheets and fonts from the web. Off by default; data: URIs and files always load.",
            Check("Allow", _settings.AllowNetwork, v => { _settings.AllowNetwork = v; Apply(); Reload(); }));
        _panel.Knob("AnimationsEnabled", "Whether the document plays its CSS animations, transitions and animated images. Nothing plays when the system asks for reduced motion. Takes a new load.",
            Check("Play", _settings.Animations, v => { _settings.Animations = v; Apply(); Reload(); }));
        _panel.Knob("Appearance", "The appearance the view reports to prefers-color-scheme. Try the Light and dark showcase.",
            Popup(["System", "Light", "Dark"], 0, i => { _settings.Appearance = i; Apply(); }));
        _panel.Knob("Input", "The pointing devices the document is told about (hover, pointer and any-pointer media features). Detect reports a mouse.",
            Popup(["Detect", "Mouse", "Touch", "None"], 0, i => { _settings.Input = i; Apply(); Reload(); }));
        _panel.Knob("UseGpu", "Whether effects (blur, shadow, filter, mask) run on the graphics hardware. Takes a new view, so the document loads again.",
            Check("Use the GPU", _settings.UseGpu, v => { _settings.UseGpu = v; ReplaceView(); }));

        _panel.Section("Zoom");
        var reset = new NSButton(new CGRect(0, 0, 160, 24)) { Title = "Reset zoom", BezelStyle = NSBezelStyle.Rounded };
        reset.Activated += (_, _) => _view.Magnification = 1;
        _panel.Knob("Zoom", "Pinch on a trackpad, or Cmd and the scroll wheel, to zoom the document; this puts it back to 100%.", reset);

        var scroller = new NSScrollView(new CGRect(0, 0, 320, 800))
        {
            DocumentView = _panel,
            HasVerticalScroller = true,
            DrawsBackground = false,
            AutoresizingMask = NSViewResizingMask.HeightSizable,
        };
        return scroller;
    }

    private static NSSlider Slider(double value, double min, double max, Action<double> changed)
    {
        var slider = new NSSlider(new CGRect(0, 0, 270, 22)) { MinValue = min, MaxValue = max, DoubleValue = value, Continuous = true };
        slider.Activated += (_, _) => changed(slider.DoubleValue);
        return slider;
    }

    private static NSButton Check(string title, bool on, Action<bool> changed)
    {
        var button = new NSButton(new CGRect(0, 0, 270, 22)) { Title = title, State = on ? NSCellStateValue.On : NSCellStateValue.Off };
        button.SetButtonType(NSButtonType.Switch);
        button.Activated += (_, _) => changed(button.State == NSCellStateValue.On);
        return button;
    }

    private static NSPopUpButton Popup(string[] items, int selected, Action<int> changed)
    {
        var popup = new NSPopUpButton(new CGRect(0, 0, 270, 26), false);
        foreach (var item in items)
            popup.AddItem(item);

        popup.SelectItem(selected);
        popup.Activated += (_, _) => changed((int)popup.IndexOfSelectedItem);
        return popup;
    }

    // ---- showing a showcase ----

    private void Show(Showcase showcase)
    {
        _current = showcase;
        _webSource = null;
        _title.StringValue = showcase.Title;
        _description.StringValue = showcase.Description;
        _stage.NeedsLayout = true;
        if (!showcase.IsWeb)
        {
            _html = showcase.ReadHtml();
            Reload();
            return;
        }

        // A web page is laid out for a desktop-wide window, and may load what it refers to.
        _settings.HostWidth = 1200;
        _settings.AllowNetwork = true;
        Apply();

        // A web page: fetch it off the main thread, then come back to it. The rewriter then gives it a base address (so what it refers to is found) and the classes its own
        // startup script would have set on <html>, which the view does not run.
        _ = Task.Run(async () =>
        {
            var (page, fetched) = await WebPage.FetchAsync(showcase.Path);
            NSRunLoop.Main.BeginInvokeOnMainThread(() =>
            {
                if (!ReferenceEquals(_current, showcase))
                    return;

                if (!fetched)
                {
                    _html = page;
                    Reload();
                    return;
                }

                _webSource = page;
                ShowWebPage(showcase);
            });
        });
    }

    // Lays the fetched page of a web showcase out for the appearance the demo shows now. The page is parsed and written out again off the main thread, and dropped if the
    // showcase changed meanwhile.
    private void ShowWebPage(Showcase showcase)
    {
        var source = _webSource;
        if (source is null)
            return;

        var dark = IsDark;
        _ = Task.Run(() => WebPage.Prepare(source, showcase, dark)).ContinueWith(task => NSRunLoop.Main.BeginInvokeOnMainThread(() =>
        {
            if (!ReferenceEquals(_current, showcase) || !ReferenceEquals(_webSource, source))
                return;

            _html = task.Result;
            Reload();
        }));
    }

    private bool IsDark => Window.EffectiveAppearance.FindBestMatch([NSAppearance.NameAqua, NSAppearance.NameDarkAqua]) == NSAppearance.NameDarkAqua;

    private void Reload()
    {
        _loadTimer.Restart();
        _lastLink = string.Empty;
        _view.AllowNetworkAccess = _settings.AllowNetwork;
        _view.AnimationsEnabled = _settings.Animations;
        _view.DocumentBaseUri = _current is { IsWeb: true } web ? new Uri(web.Path) : null;
        _view.Input = _settings.Input switch
        {
            1 => InputCapabilities.Mouse,
            2 => InputCapabilities.Touch,
            3 => InputCapabilities.None,
            _ => null,
        };
        _view.Html = _html;
    }

    // The GPU setting is read when the view is first shown, so a change makes a new view.
    private void ReplaceView()
    {
        _view.RemoveFromSuperview();
        _view = NewView();
        _stage.HostBox.AddSubview(_view);
        _stage.NeedsLayout = true;
        Apply();
        Reload();
    }

    // Hands every knob to the view and the box it sits in.
    private void Apply()
    {
        _stage.NeedsLayout = true;
        _stage.LayoutSubtreeIfNeeded();
        var color = _settings.Background switch
        {
            1 => NSColor.Clear.CGColor,
            2 => NSColor.FromRgb(0.92f, 0.92f, 0.92f).CGColor,
            3 => NSColor.FromRgb(0.11f, 0.11f, 0.125f).CGColor,
            _ => NSColor.White.CGColor,
        };
        _view.PageBackground = color;
        _stage.HostBox.Layer!.BackgroundColor = color;
        Window.Appearance = _settings.Appearance switch
        {
            1 => NSAppearance.GetAppearance(NSAppearance.NameAqua),
            2 => NSAppearance.GetAppearance(NSAppearance.NameDarkAqua),
            _ => null,
        };
        if (_current is { IsWeb: true } web && _webSource is not null)
            ShowWebPage(web);

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var pages = _view.Document?.Pages.Count.ToString() ?? "-";
        var size = _view.Frame.Size;
        _status.StringValue = $"{_current?.Slug}   pages: {pages}   zoom: {_view.Zoom * 100:0}%   view: {size.Width:0}x{size.Height:0} pt   load: {_loadMilliseconds:0} ms"
            + (_lastLink.Length > 0 ? $"   link: {_lastLink}" : string.Empty) + $"   showcases: {_shown.Count}/{_all.Count}";
    }

    // ---- small pieces ----

    private static NSTextField Label(double size, bool bold = false, bool secondary = false, bool mono = false)
    {
        var label = NSTextField.CreateLabel(string.Empty);
        label.Font = (mono ? NSFont.MonospacedSystemFont((nfloat)size, NSFontWeight.Regular) : bold ? NSFont.BoldSystemFontOfSize((nfloat)size) : NSFont.SystemFontOfSize((nfloat)size))!;
        if (secondary)
            label.TextColor = NSColor.SecondaryLabel;

        label.LineBreakMode = NSLineBreakMode.ByWordWrapping;
        label.MaximumNumberOfLines = 0;
        return label;
    }

    /// <summary>The knobs that are not the view's own state, held in one place; the panel edits it and <see cref="Apply"/> hands it to the view.</summary>
    private sealed class Settings
    {
        public double HostWidth { get; set; } = 820;

        public bool FillHeight { get; set; } = true;

        public double HostHeight { get; set; } = 600;

        public int Background { get; set; }

        public bool AllowNetwork { get; set; }
        public bool Animations { get; set; } = true;

        public int Appearance { get; set; }

        public int Input { get; set; }

        public bool UseGpu { get; set; } = true;
    }

    /// <summary>The middle: the title and description of the showcase, and the box the view sits in.</summary>
    private sealed class Stage : NSView
    {
        private readonly NSTextField _title;
        private readonly NSTextField _description;
        private readonly Settings _settings;

        public Stage(NSTextField title, NSTextField description, Settings settings) : base(new CGRect(0, 0, 770, 800))
        {
            _title = title;
            _description = description;
            _settings = settings;
            HostBox = new NSView { WantsLayer = true };
            HostBox.Layer!.BorderWidth = 1;
            HostBox.Layer.BorderColor = NSColor.FromRgb(0.35f, 0.47f, 0.7f).CGColor;
            AddSubview(title);
            AddSubview(description);
            AddSubview(HostBox);
            AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable;
        }

        public NSView HostBox { get; }

        public override bool IsFlipped => true;

        public override void Layout()
        {
            base.Layout();
            var width = Bounds.Width - 32;
            _title.Frame = new CGRect(16, 12, width, 24);
            var descriptionHeight = Math.Ceiling(_description.Cell.CellSizeForBounds(new CGRect(0, 0, width, 1000)).Height);
            _description.Frame = new CGRect(16, 40, width, descriptionHeight);
            var top = 40 + descriptionHeight + 12;
            var boxWidth = Math.Min(_settings.HostWidth, width);
            var boxHeight = _settings.FillHeight ? Bounds.Height - top - 16 : Math.Min(_settings.HostHeight, Bounds.Height - top - 16);
            HostBox.Frame = new CGRect(16, top, boxWidth, Math.Max(100, boxHeight));
        }
    }

    /// <summary>The panel of knobs: a name in monospace, one line saying what it does, and the control.</summary>
    private sealed class KnobPanel : NSView
    {
        private readonly List<(NSView View, bool Wraps)> _items = [];

        public KnobPanel() : base(new CGRect(0, 0, 320, 1200))
        {
        }

        public override bool IsFlipped => true;

        public void Heading(string text) => Add(Text(text, 18, bold: true), wraps: false);

        public void Section(string text) => Add(Text(text, 13, bold: true, secondary: true), wraps: false);

        public void Knob(string name, string help, NSView control)
        {
            Add(Text(name, 12, mono: true), wraps: false);
            Add(Text(help, 11, secondary: true), wraps: true);
            Add(control, wraps: false);
        }

        private static NSTextField Text(string text, double size, bool bold = false, bool secondary = false, bool mono = false)
        {
            var label = Label(size, bold, secondary, mono);
            label.StringValue = text;
            return label;
        }

        private void Add(NSView view, bool wraps)
        {
            _items.Add((view, wraps));
            AddSubview(view);
        }

        public override void Layout()
        {
            base.Layout();
            var width = Superview?.Frame.Width ?? 320;
            var y = 12.0;
            foreach (var (view, wraps) in _items)
            {
                var height = view is NSTextField field
                    ? Math.Ceiling(field.Cell.CellSizeForBounds(new CGRect(0, 0, width - 32, 2000)).Height)
                    : view.Frame.Height;
                view.Frame = new CGRect(16, y, width - 32, height);
                y += height + (wraps || view is NSTextField ? 2 : 14);
            }

            SetFrameSize(new CGSize(width, y + 12));
        }
    }

    private sealed class TableSource(List<Showcase> shown) : NSTableViewDataSource
    {
        public override nint GetRowCount(NSTableView tableView) => shown.Count;
    }

    private sealed class TableDelegate(List<Showcase> shown, Action<int> selected) : NSTableViewDelegate
    {
        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var showcase = shown[(int)row];
            var cell = new NSView(new CGRect(0, 0, 250, 40));
            var title = Label(13);
            title.StringValue = showcase.Title;
            title.LineBreakMode = NSLineBreakMode.TruncatingTail;
            title.MaximumNumberOfLines = 1;
            title.Frame = new CGRect(6, 20, 238, 17);
            var category = Label(10, secondary: true);
            category.StringValue = showcase.Category;
            category.Frame = new CGRect(6, 3, 238, 14);
            cell.AddSubview(title);
            cell.AddSubview(category);
            return cell;
        }

        public override void SelectionDidChange(NSNotification notification) =>
            selected((int)((NSTableView)notification.Object!).SelectedRow);
    }
}

/// <summary>A window that keeps the size it was given: a titled window is otherwise cut to the screen it opens on, and a small one (a build agent's virtual display) leaves the page too little room.</summary>
internal sealed class UnconstrainedWindow(CGRect contentRect, NSWindowStyle style, NSBackingStore backing, bool defer) : NSWindow(contentRect, style, backing, defer)
{
    public override CGRect ConstrainFrameRect(CGRect frameRect, NSScreen? screen) => frameRect;
}

/// <summary>
/// A window with nothing in it but an <see cref="HtmlView"/> showing the sample page, at a size of its own: what the smoke run drives, so that what it clicks
/// does not depend on how the demo's panels share the width of whatever display the run has.
/// </summary>
internal sealed class BareWindow
{
    public BareWindow()
    {
        View = new HtmlView { Frame = new CGRect(0, 0, 1000, 760), AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable };
        Window = new UnconstrainedWindow(new CGRect(60, 60, 1000, 760), NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false) { Title = "PeachBrackets.Apple smoke" };
        Window.ContentView = View;
        View.Html = ShowcaseCatalog.Load(AppContext.BaseDirectory).First(s => s.Slug == "sample").ReadHtml();
    }

    public NSWindow Window { get; }

    public HtmlView View { get; }
}

using CoreGraphics;
using Foundation;
using PeachBrackets.Apple;
using PeachBrackets.Apple.Example;
using PeachDrawing.Core;
using UIKit;

namespace PeachBrackets.Apple.IOSExample;

public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions) => true;
}

/// <summary>iOS 26 and later require the scene lifecycle: the window is made when the scene connects.</summary>
[Register("SceneDelegate")]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            return;

        Window = new UIWindow(windowScene) { RootViewController = new DemoSplit() };
        Window.MakeKeyAndVisible();
    }
}

/// <summary>How the host sets the view up; the settings sheet edits it and every detail screen reads it.</summary>
internal static class DemoSettings
{
    public static int Background { get; set; }

    public static bool AllowNetwork { get; set; }

    public static int Appearance { get; set; }

    public static int Input { get; set; }

    public static bool UseGpu { get; set; } = true;

    public static event Action? Changed;

    public static void Raise() => Changed?.Invoke();
}

/// <summary>
/// The demo's navigation, like the Windows demo's: every showcase in a list (searchable, grouped by category), the chosen one beside it on a wide screen or pushed over it on a
/// phone. <c>--smoke</c> opens the sample page at once and runs the checks the CI script reads from the log.
/// </summary>
public sealed class DemoSplit : UISplitViewController
{
    private readonly ListController _list;

    public DemoSplit() : base(UISplitViewControllerStyle.DoubleColumn)
    {
        var showcases = ShowcaseCatalog.Load(NSBundle.MainBundle.BundlePath);
        _list = new ListController(showcases, Show);
        PreferredDisplayMode = UISplitViewControllerDisplayMode.OneBesideSecondary;
        PreferredSplitBehavior = UISplitViewControllerSplitBehavior.Tile;
        SetViewController(new UINavigationController(_list), UISplitViewControllerColumn.Primary);
        SetViewController(new UINavigationController(new DetailController(showcases[0])), UISplitViewControllerColumn.Secondary);

        // A phone starts on the list, not on the first showcase.
        Delegate = new Collapser();
        _smoke = NSProcessInfo.ProcessInfo.Arguments.Contains("--smoke") ? showcases[0] : null;
    }

    private Showcase? _smoke;

    // The sample page opens once the screen is up (a detail screen shown before that is not loaded).
    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (_smoke is { } showcase)
        {
            _smoke = null;
            Show(showcase);
        }
    }

    // The detail goes in the secondary column, and showing that column is what a wide screen does by leaving it beside the list and a phone does by pushing it.
    private void Show(Showcase showcase)
    {
        SetViewController(new UINavigationController(new DetailController(showcase)), UISplitViewControllerColumn.Secondary);
        ShowColumn(UISplitViewControllerColumn.Secondary);
    }

    private sealed class Collapser : UISplitViewControllerDelegate
    {
        [Export("splitViewController:topColumnForCollapsingToProposedTopColumn:")]
        public UISplitViewControllerColumn TopColumn(UISplitViewController svc, UISplitViewControllerColumn proposed) => UISplitViewControllerColumn.Primary;
    }
}

/// <summary>The list of showcases: sections for the categories, and a search field over the titles and descriptions.</summary>
internal sealed class ListController : UITableViewController, IUISearchResultsUpdating
{
    private readonly IReadOnlyList<Showcase> _all;
    private readonly Action<Showcase> _open;
    private IReadOnlyList<IGrouping<string, Showcase>> _groups;

    public ListController(IReadOnlyList<Showcase> all, Action<Showcase> open) : base(UITableViewStyle.InsetGrouped)
    {
        _all = all;
        _open = open;
        _groups = Group(all);
        Title = "Showcases";
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var search = new UISearchController((UIViewController?)null) { SearchResultsUpdater = this, ObscuresBackgroundDuringPresentation = false };
        search.SearchBar.Placeholder = "Search showcases";
        NavigationItem.SearchController = search;
        NavigationItem.HidesSearchBarWhenScrolling = false;
        DefinesPresentationContext = true;
    }

    private static IReadOnlyList<IGrouping<string, Showcase>> Group(IEnumerable<Showcase> showcases) => showcases.GroupBy(s => s.Category).ToList();

    public void UpdateSearchResults(UISearchController searchController)
    {
        var text = searchController.SearchBar.Text?.Trim() ?? string.Empty;
        _groups = Group(_all.Where(s => text.Length == 0 || s.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
            || s.Description.Contains(text, StringComparison.OrdinalIgnoreCase) || s.Category.Contains(text, StringComparison.OrdinalIgnoreCase)));
        TableView.ReloadData();
    }

    public override nint NumberOfSections(UITableView tableView) => _groups.Count;

    public override nint RowsInSection(UITableView tableView, nint section) => _groups[(int)section].Count();

    public override string? TitleForHeader(UITableView tableView, nint section) => _groups[(int)section].Key;

    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        var showcase = _groups[indexPath.Section].ElementAt(indexPath.Row);
        var cell = tableView.DequeueReusableCell("showcase") ?? new UITableViewCell(UITableViewCellStyle.Subtitle, "showcase");
        cell.TextLabel!.Text = showcase.Title;
        cell.DetailTextLabel!.Text = showcase.Description;
        cell.DetailTextLabel.LineBreakMode = UILineBreakMode.TailTruncation;
        cell.Accessory = UITableViewCellAccessory.DisclosureIndicator;
        return cell;
    }

    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        _open(_groups[indexPath.Section].ElementAt(indexPath.Row));
        tableView.DeselectRow(indexPath, true);
    }
}

/// <summary>One showcase in an <see cref="HtmlView"/>, with the host's settings applied and a line of status under it.</summary>
internal sealed class DetailController : UIViewController
{
    private const string Tag = "PEACH_SMOKE";
    private readonly Showcase _showcase;
    private readonly UILabel _status = new() { Font = UIFont.GetMonospacedSystemFont(11, UIFontWeight.Regular)!, TextColor = UIColor.SecondaryLabel, Lines = 2 };
    private readonly System.Diagnostics.Stopwatch _loadTimer = System.Diagnostics.Stopwatch.StartNew();
    private HtmlView _view;
    private string _html = string.Empty;
    private string? _webSource;
    private double _loadMilliseconds;
    private double _longestGapMs;
    private string _lastLink = string.Empty;
    private bool _ran;

    public DetailController(Showcase showcase)
    {
        _showcase = showcase;
        Title = showcase.Title;
        _view = NewView();
    }

    private static void Log(string message) => Console.WriteLine($"{Tag} {message}");

    private HtmlView NewView()
    {
        var view = new HtmlView { UseGpu = DemoSettings.UseGpu };
        view.DocumentLoaded += (_, _) =>
        {
            _loadMilliseconds = _loadTimer.Elapsed.TotalMilliseconds;
            UpdateStatus();
            if (_showcase.Slug == "sample")
                RunChecks();
        };
        view.DocumentFailed += (_, e) => Log("FAILED load: " + e.Exception);
        view.LinkActivated += (_, e) =>
        {
            Log($"link-activated uri={e.Uri}");
            _lastLink = e.Uri ?? "#" + e.Destination;
            e.Cancel = e.Uri is not null;
            UpdateStatus();
        };
        view.Tapped += (_, e) => Log($"tapped page={e.PageIndex}");
        view.TouchStarted += (_, e) => Log($"touch allowed={e.Allowed}");
        view.ScrollChanged += (_, _) => UpdateStatus();
        return view;
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        NavigationItem.LargeTitleDisplayMode = UINavigationItemLargeTitleDisplayMode.Never;
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(UIImage.GetSystemImage("slider.horizontal.3")!, UIBarButtonItemStyle.Plain, (_, _) => ShowSettings());
        View.AddSubview(_view);
        View.AddSubview(_status);
        DemoSettings.Changed += OnSettingsChanged;
        StartHeartbeat();
        Load();
    }

    public override void ViewDidDisappear(bool animated)
    {
        base.ViewDidDisappear(animated);
        if (IsMovingFromParentViewController)
            DemoSettings.Changed -= OnSettingsChanged;
    }

    public override void ViewDidLayoutSubviews()
    {
        base.ViewDidLayoutSubviews();
        var bounds = View!.Bounds;
        var safe = View.SafeAreaInsets;
        const double statusHeight = 32;
        _status.Frame = new CGRect(12, bounds.Height - safe.Bottom - statusHeight, bounds.Width - 24, statusHeight);
        _view.Frame = new CGRect(0, safe.Top, bounds.Width, Math.Max(0, bounds.Height - safe.Top - safe.Bottom - statusHeight));
    }

    // How long the main thread went without running anything from the start until the page had loaded: a load that holds it shows up here.
    private void StartHeartbeat()
    {
        var last = DateTime.UtcNow;
        NSTimer.CreateRepeatingScheduledTimer(0.05, _ =>
        {
            var now = DateTime.UtcNow;
            _longestGapMs = Math.Max(_longestGapMs, (now - last).TotalMilliseconds - 50);
            last = now;
        });
    }

    // ---- loading ----

    private void Load()
    {
        _loadTimer.Restart();
        if (!_showcase.IsWeb)
        {
            _html = _showcase.ReadHtml();
            Apply();
            _view.Html = _html;
            return;
        }

        // A web page is fetched off the main thread, then made ready for the view, which runs no scripts.
        DemoSettings.AllowNetwork = true;
        _ = Task.Run(async () =>
        {
            var (page, fetched) = await WebPage.FetchAsync(_showcase.Path);
            var html = fetched ? WebPage.Prepare(page, _showcase, dark: TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark) : page;
            NSRunLoop.Main.BeginInvokeOnMainThread(() =>
            {
                _html = html;
                _webSource = fetched ? page : null;
                Apply();
                _view.Html = _html;
            });
        });
    }

    private void OnSettingsChanged()
    {
        var gpu = DemoSettings.UseGpu;
        if (gpu != _view.UseGpu)
        {
            _view.RemoveFromSuperview();
            _view = NewView();
            View!.InsertSubview(_view, 0);
            View.SetNeedsLayout();
        }

        Apply();
        _loadTimer.Restart();
        _view.Html = _html;
    }

    // Hands every knob to the view.
    private void Apply()
    {
        _view.AllowNetworkAccess = DemoSettings.AllowNetwork;
        _view.DocumentBaseUri = _showcase.IsWeb ? new Uri(_showcase.Path) : null;
        _view.Input = DemoSettings.Input switch
        {
            1 => InputCapabilities.Mouse,
            2 => InputCapabilities.Touch,
            3 => InputCapabilities.None,
            _ => null,
        };
        _view.PageBackground = DemoSettings.Background switch
        {
            1 => UIColor.Clear.CGColor,
            2 => UIColor.FromWhiteAlpha(0.92f, 1).CGColor,
            3 => UIColor.FromRGB(0.11f, 0.11f, 0.125f).CGColor,
            _ => UIColor.White.CGColor,
        };
        OverrideUserInterfaceStyle = DemoSettings.Appearance switch
        {
            1 => UIUserInterfaceStyle.Light,
            2 => UIUserInterfaceStyle.Dark,
            _ => UIUserInterfaceStyle.Unspecified,
        };
    }

    private void ShowSettings()
    {
        var sheet = new UINavigationController(new SettingsController()) { ModalPresentationStyle = UIModalPresentationStyle.PageSheet };
        if (sheet.SheetPresentationController is { } presentation)
            presentation.Detents = [UISheetPresentationControllerDetent.CreateMediumDetent(), UISheetPresentationControllerDetent.CreateLargeDetent()];

        PresentViewController(sheet, true, null);
    }

    private void UpdateStatus() =>
        _status.Text = $"{_showcase.Slug}   pages: {_view.Document?.Pages.Count.ToString() ?? "-"}   zoom: {_view.Zoom * 100:0}%   load: {_loadMilliseconds:0} ms"
            + (_lastLink.Length > 0 ? $"\nlink: {_lastLink}" : string.Empty);

    // ---- what the CI script reads ----

    private void RunChecks()
    {
        if (_ran || _view.Document is not { } document)
            return;

        _ran = true;
        var failures = 0;
        void Check(string name, string value, string expected)
        {
            var ok = value == expected;
            failures += ok ? 0 : 1;
            Log($"{(ok ? "PASS" : "FAIL")} {name}={value}");
        }

        Log($"loaded pages={document.Pages.Count} stall-ms={_longestGapMs:F0}");
        string Cursor(string id) =>
            document.GetElementBounds(id) is { } b ? document.GetCursorAt(0, b.X + (b.Width / 2), b.Y + (b.Height / 2)).Shape.ToString() : "missing";

        Check("link-cursor", Cursor("link"), "Pointer");
        Check("card-cursor", Cursor("card"), "Crosshair");
        if (document.GetElementBounds("locked") is { } locked)
            Check("locked-selectable", document.GetInteractionAt(0, locked.X + 8, locked.Y + 8).CanSelectText.ToString(), "False");

        if (document.GetElementBounds("pinned") is { } pinned)
            Check("pinned-touch", document.GetInteractionAt(0, pinned.X + 8, pinned.Y + 8).TouchActions.ToString(), "None");

        // What VoiceOver walks: the page view's accessibility elements, among them the heading and the link.
        var page = _view.Subviews.First(v => v.GetType().Name == "PageView");
        var elements = page.PerformSelector(new ObjCRuntime.Selector("accessibilityElements")) as NSArray;
        var labels = new List<string>();
        var traits = new List<ulong>();
        for (nuint i = 0; elements is not null && i < elements.Count; i++)
        {
            var element = elements.GetItem<UIAccessibilityElement>(i)!;
            labels.Add(element.AccessibilityLabel ?? "");
            traits.Add(element.AccessibilityTraits);
        }

        Check("a11y-parts", (labels.Count >= 4).ToString(), "True");
        Check("a11y-heading", (labels.Contains("PeachBrackets.Apple") && (traits[labels.IndexOf("PeachBrackets.Apple")] & (ulong)UIAccessibilityTrait.Header) != 0).ToString(), "True");
        Check("a11y-link", (labels.Contains("a link") && (traits[labels.IndexOf("a link")] & (ulong)UIAccessibilityTrait.Link) != 0).ToString(), "True");
        Check("stall-under-3s", (_longestGapMs < 3000).ToString(), "True");

        // A scripted scroll: the content offset moves and the view reports it, and then it goes back where it began.
        _view.SetContentOffset(new CGPoint(0, 300), false);
        Log($"offset-y={_view.ContentOffset.Y:F0} zoom={_view.Zoom}");
        _view.SetContentOffset(new CGPoint(0, -_view.ContentInset.Top), false);
        Log(failures == 0 ? "RESULT ALL PASSED" : $"RESULT {failures} FAILED");
    }
}

/// <summary>The host's knobs, as a table: each row edits <see cref="DemoSettings"/> and tells the open showcase.</summary>
internal sealed class SettingsController : UITableViewController
{
    private static readonly string[] Names = ["Appearance", "Input", "PageBackground", "AllowNetworkAccess", "UseGpu"];
    private static readonly string[][] Choices =
    [
        ["System", "Light", "Dark"], ["Detect", "Mouse", "Touch", "None"], ["White", "Clear", "Grey", "Dark"], [], [],
    ];

    private static readonly string[] Help =
    [
        "The appearance the view reports to prefers-color-scheme. Try the Light and dark showcase.",
        "The pointing devices the document is told about (hover, pointer, any-pointer). Detect reports touch.",
        "What shows behind the document where it paints nothing.",
        "Whether the document may load images, style sheets and fonts from the web. Off by default; data: URIs and files always load.",
        "Whether effects (blur, shadow, filter, mask) run on the graphics hardware. Takes a new view, so the document loads again.",
    ];

    public SettingsController() : base(UITableViewStyle.InsetGrouped) => Title = "Host knobs";

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        NavigationItem.RightBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Done, (_, _) => DismissViewController(true, null));
    }

    public override nint NumberOfSections(UITableView tableView) => Names.Length;

    public override nint RowsInSection(UITableView tableView, nint section) => 1;

    public override string? TitleForHeader(UITableView tableView, nint section) => Names[(int)section];

    public override string? TitleForFooter(UITableView tableView, nint section) => Help[(int)section];

    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        var cell = new UITableViewCell(UITableViewCellStyle.Default, null) { SelectionStyle = UITableViewCellSelectionStyle.None };
        var section = indexPath.Section;
        if (Choices[section].Length > 0)
        {
            var control = new UISegmentedControl(Choices[section]) { SelectedSegment = Current(section) };
            control.ValueChanged += (_, _) =>
            {
                Set(section, (int)control.SelectedSegment);
                DemoSettings.Raise();
            };
            control.Frame = new CGRect(12, 6, tableView.Bounds.Width - 64, 32);
            control.AutoresizingMask = UIViewAutoresizing.FlexibleWidth;
            cell.ContentView.AddSubview(control);
        }
        else
        {
            var toggle = new UISwitch { On = section == 3 ? DemoSettings.AllowNetwork : DemoSettings.UseGpu };
            toggle.ValueChanged += (_, _) =>
            {
                if (section == 3)
                    DemoSettings.AllowNetwork = toggle.On;
                else
                    DemoSettings.UseGpu = toggle.On;

                DemoSettings.Raise();
            };
            cell.AccessoryView = toggle;
            cell.TextLabel!.Text = section == 3 ? "Allow" : "Use the GPU";
        }

        return cell;
    }

    private static int Current(int section) => section switch
    {
        0 => DemoSettings.Appearance,
        1 => DemoSettings.Input,
        _ => DemoSettings.Background,
    };

    private static void Set(int section, int value)
    {
        switch (section)
        {
            case 0: DemoSettings.Appearance = value; break;
            case 1: DemoSettings.Input = value; break;
            default: DemoSettings.Background = value; break;
        }
    }
}

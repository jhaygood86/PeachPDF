using System.Collections.ObjectModel;
using PeachDrawing.PDF;
using PeachReader.Core;

namespace PeachReader;

/// <summary>
/// The reader's window: a header, a sidebar with the table of contents, the page thumbnails and the reader's bookmarks, the pages themselves,
/// a find bar and a status bar. It owns the open document and wires what the user does to the <see cref="ViewportController"/> that drives the
/// page view; everything that decides something lives in PeachReader.Core.
/// </summary>
public partial class MainPage : ContentPage
{
    private const string RecentKey = "recent-files";
    private static readonly string[] ZoomChoices = ["Fit width", "Fit page", "50%", "75%", "100%", "125%", "150%", "200%", "300%"];

    private readonly BookmarkStorage _storage = new(Path.Combine(FileSystem.AppDataDirectory, "bookmarks"));
    private readonly ObservableCollection<OutlineRow> _outlineRows = [];
    private readonly Dictionary<OutlineNode, OutlineRow> _outlineRowFor = [];
    private readonly ObservableCollection<PageRow> _pageRows = [];
    private readonly ObservableCollection<BookmarkRow> _bookmarkRows = [];
    private readonly HashSet<int> _thumbnailsRequested = [];
    private ReaderDocument? _document;
    private ViewportController? _controller;
    private BookmarkCollection _bookmarks = new();
    private IReadOnlyList<OutlineNode> _outlineRoots = [];
    private CancellationTokenSource? _search;
    private SearchResults? _results;
    private string _tab = "contents";
    private bool _settingZoomPicker, _startedUp;
    private readonly Action _onIncoming;

    /// <summary>Creates the page.</summary>
    public MainPage()
    {
        InitializeComponent();
        ZoomPicker.ItemsSource = ZoomChoices;
        ZoomPicker.SelectedIndex = 0;
        ShowTab("contents");
        UpdateChrome();
        RenderRecent();
        Loaded += async (_, _) =>
        {
#if WINDOWS
            WinUI.Shortcuts.Install(Window?.Handler?.PlatformView, RunCommand);
#endif
            IncomingDocument.Offered -= _onIncoming;
            IncomingDocument.Offered += _onIncoming;
            await StartUpAsync();
        };
        _onIncoming = () => MainThread.BeginInvokeOnMainThread(async () => await OpenIncomingAsync());
        // The event is static, so a page that is gone must not stay subscribed to it.
        Unloaded += (_, _) => IncomingDocument.Offered -= _onIncoming;
        SizeChanged += (_, _) =>
        {
            if (Width <= 0) return;
            ApplyLayoutMode();
            if (_startedUp) return;
            _startedUp = true;
            Sidebar.IsVisible = !_narrow;             // a narrow window starts with the pages and the sidebar one tap away
        };
    }

    /// <summary>Runs a command by name: what a keyboard shortcut does on platforms where the menu bar cannot carry it.</summary>
    /// <param name="command">the command's name</param>
    internal void RunCommand(string command)
    {
        switch (command)
        {
            case "open": OnOpenClicked(null, EventArgs.Empty); break;
            case "copy": OnCopyClicked(null, EventArgs.Empty); break;
            case "select-all": OnSelectAllClicked(null, EventArgs.Empty); break;
            case "find": OnFindClicked(null, EventArgs.Empty); break;
            case "find-next": OnFindNextClicked(null, EventArgs.Empty); break;
            case "find-previous": OnFindPreviousClicked(null, EventArgs.Empty); break;
            case "bookmark": OnToggleBookmarkClicked(null, EventArgs.Empty); break;
            case "sidebar": OnToggleSidebarClicked(null, EventArgs.Empty); break;
            case "zoom-in": OnZoomInClicked(null, EventArgs.Empty); break;
            case "zoom-out": OnZoomOutClicked(null, EventArgs.Empty); break;
            case "fit-width": OnFitWidthClicked(null, EventArgs.Empty); break;
            case "fit-page": OnFitPageClicked(null, EventArgs.Empty); break;
            case "back": OnBackClicked(null, EventArgs.Empty); break;
            case "forward": OnForwardClicked(null, EventArgs.Empty); break;
        }
    }

    // ------------------------------------------------------------------ layout

    private bool _narrow;

    /// <summary>
    /// In a window as narrow as a phone the sidebar would leave no room for the pages, so it is laid over them instead of beside them, and goes
    /// away again when something in it is chosen.
    /// </summary>
    private void ApplyLayoutMode()
    {
        bool narrow = Width < 720;
        if (narrow == _narrow && Sidebar.ZIndex == (narrow ? 10 : 0)) return;
        _narrow = narrow;
        Grid.SetColumn(Sidebar, narrow ? 1 : 0);
        Sidebar.ZIndex = narrow ? 10 : 0;
        Sidebar.HorizontalOptions = narrow ? LayoutOptions.Start : LayoutOptions.Fill;
    }

    private void HideSidebarWhenNarrow()
    {
        if (_narrow) Sidebar.IsVisible = false;
    }

    // ------------------------------------------------------------------ opening

    private async Task StartUpAsync()
    {
        string? path = Environment.GetEnvironmentVariable("PEACHREADER_OPEN");
        path ??= Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) await OpenPathAsync(path);
        await OpenIncomingAsync();
    }

    private async Task OpenIncomingAsync()
    {
        try
        {
            if (IncomingDocument.Take() is { } incoming) await OpenBytesAsync(incoming.Bytes, incoming.Name, null);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Could not open the document", ex.Message, "OK");
        }
    }

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Open a PDF",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".pdf" },
                    [DevicePlatform.MacCatalyst] = new[] { "pdf" },
                    [DevicePlatform.iOS] = new[] { "com.adobe.pdf" },
                    [DevicePlatform.Android] = new[] { "application/pdf" },
                }),
            });
            if (picked is null) return;
            await using var stream = await picked.OpenReadAsync();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            await OpenBytesAsync(copy.ToArray(), picked.FileName, picked.FullPath);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Could not open the file", ex.Message, "OK");
        }
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            await OpenBytesAsync(await File.ReadAllBytesAsync(path), Path.GetFileName(path), path);
        }
        catch (Exception ex)
        {
            // A file that is not a PDF, or a PDF that is damaged in a way the reader did not expect, is a message and not the end of the app.
            await DisplayAlertAsync("Could not open the file", ex.Message, "OK");
        }
    }

    private TaskCompletionSource<string?>? _passwordAnswer;

    /// <summary>Asks for a password in an overlay whose entry masks what is typed; null when cancelled.</summary>
    private Task<string?> AskPasswordAsync(string message)
    {
        _passwordAnswer?.TrySetResult(null);
        var answer = _passwordAnswer = new TaskCompletionSource<string?>();
        PasswordMessage.Text = message;
        PasswordEntry.Text = "";
        PasswordOverlay.IsVisible = true;
        PasswordEntry.Focus();
        return answer.Task;
    }

    private void FinishPassword(string? value)
    {
        PasswordOverlay.IsVisible = false;
        string? typed = value;
        PasswordEntry.Text = "";
        _passwordAnswer?.TrySetResult(typed);
        _passwordAnswer = null;
    }

    private void OnPasswordOk(object? sender, EventArgs e) => FinishPassword(PasswordEntry.Text);

    private void OnPasswordCancel(object? sender, EventArgs e) => FinishPassword(null);

    private async Task OpenBytesAsync(byte[] bytes, string? fileName, string? path)
    {
        string? password = null;
        ReaderDocument? opened = null;
        while (opened is null)
        {
            try
            {
                string? pw = password;
                opened = await Task.Run(() => ReaderDocument.Open(bytes, pw, fileName));
            }
            catch (PdfPasswordException ex)
            {
                password = await AskPasswordAsync(ex.PasswordWasGiven
                    ? "That password did not open the document. Try again:" : "This document is protected. Enter its password:");
                if (string.IsNullOrEmpty(password)) return;
            }
            catch (Exception ex) when (ex is not PdfPasswordException)
            {
                await DisplayAlertAsync("Could not open the document", ex.Message, "OK");
                return;
            }
        }

        try
        {
            ShowDocument(opened);
        }
        catch (Exception ex)
        {
            // The document opened but something in it (its outline, its first page) could not be read.
            await DisplayAlertAsync("Could not show the document", ex.Message, "OK");
            return;
        }

        if (path is not null) Remember(path);
    }

    private void ShowDocument(ReaderDocument document)
    {
        if (_controller is { } old)
        {
            old.CurrentPageChanged -= OnCurrentPageChanged;
            old.LayoutChanged -= UpdateZoomLabel;
            old.UriRequested -= OnUriRequested;
            old.UriBlocked -= OnUriBlocked;
            old.SkippedFeaturesChanged -= UpdateStatus;
        }

        CancelSearch();
        _results = null;
        _thumbnailsRequested.Clear();
        _document = document;
        var controller = _controller = new ViewportController(document, Math.Max(200, Viewport.Width), Math.Max(200, Viewport.Height));
        controller.CurrentPageChanged += OnCurrentPageChanged;
        controller.LayoutChanged += UpdateZoomLabel;
        controller.UriRequested += OnUriRequested;
        controller.UriBlocked += OnUriBlocked;
        controller.SkippedFeaturesChanged += UpdateStatus;

        _outlineRoots = document.Outline;
        _outlineRowFor.Clear();
        RebuildOutline();
        _pageRows.Clear();
        for (int i = 0; i < document.PageCount; i++) _pageRows.Add(new PageRow(i, document.PageSizes[i].Width, document.PageSizes[i].Height, document.LabelOf(i)));
        _bookmarks = _storage.Load(document.Key);
        RefreshBookmarks();

        FindEntry.Text = "";
        FindCountLabel.Text = "";
        EmptyState.IsVisible = false;
        Viewport.Controller = controller;
        TitleLabel.Text = document.Title;
        if (Window is { } window) window.Title = document.Title + " - PeachReader";
        SelectZoomChoice(0);
        UpdateChrome();
        OnCurrentPageChanged(controller.CurrentPage);
        UpdateZoomLabel();
        UpdateStatus();
        EnsureThumbnails(0);
    }

    // ------------------------------------------------------------------ recent files

    private static List<string> RecentPaths() =>
        [.. Preferences.Default.Get(RecentKey, "").Split('|', StringSplitOptions.RemoveEmptyEntries).Where(File.Exists)];

    private void Remember(string path)
    {
        var recent = RecentPaths();
        recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, path);
        Preferences.Default.Set(RecentKey, string.Join('|', recent.Take(8)));
        RenderRecent();
    }

    private void RenderRecent()
    {
        RecentList.Children.Clear();
        var recent = RecentPaths();
        RecentHeading.IsVisible = recent.Count > 0;
        foreach (var path in recent)
        {
            var label = new Label { Text = Path.GetFileName(path), TextColor = Colors.CornflowerBlue, TextDecorations = TextDecorations.Underline };
            var tap = new TapGestureRecognizer();
            string captured = path;
            tap.Tapped += async (_, _) => await OpenPathAsync(captured);
            label.GestureRecognizers.Add(tap);
            ToolTipProperties.SetText(label, path);
            RecentList.Children.Add(label);
        }
    }

    // ------------------------------------------------------------------ chrome

    private void UpdateChrome()
    {
        bool open = _controller is not null;
        FindButton.IsEnabled = ZoomInButton.IsEnabled = ZoomOutButton.IsEnabled = open;
        PreviousPageButton.IsEnabled = NextPageButton.IsEnabled = PageEntry.IsEnabled = ZoomPicker.IsEnabled = open;
        PageCountLabel.Text = "/ " + (_document?.PageCount ?? 0);
        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = _controller?.History.CanGoBack == true;
        ForwardButton.IsEnabled = _controller?.History.CanGoForward == true;
    }

    private void UpdateZoomLabel()
    {
        if (_controller is not { } c) { ZoomLabel.Text = ""; return; }
        ZoomLabel.Text = $"{c.Layout.Zoom * 100:0}%";

        // The picker follows the zoom however it was changed (buttons, pinch, a fit mode); a zoom that is none of its choices leaves it blank.
        int choice = c.Layout.Mode switch
        {
            ZoomMode.FitWidth => 0,
            ZoomMode.FitPage => 1,
            _ => Array.FindIndex(ZoomChoices, 2, choice => double.TryParse(choice.TrimEnd('%'), out double percent) && Math.Abs(percent / 100 - c.Layout.Zoom) < 0.005),
        };
        SelectZoomChoice(choice);
    }

    private void UpdateStatus()
    {
        if (_controller is not { } c || c.SkippedFeatures.Count == 0) { StatusLabel.Text = ""; return; }
        int skipped = c.SkippedFeatures.Count(f => !f.StartsWith("font substituted:", StringComparison.Ordinal));
        int substituted = c.SkippedFeatures.Count - skipped;
        StatusLabel.Text = skipped > 0 ? "⚠ Some content could not be shown" : substituted > 0 ? "Some fonts were substituted" : "";
    }

    private async void OnStatusTapped(object? sender, TappedEventArgs e)
    {
        if (_controller is not { } c || c.SkippedFeatures.Count == 0) return;
        await DisplayAlertAsync("What could not be shown exactly", string.Join('\n', c.SkippedFeatures.Order()), "OK");
    }

    private void OnCurrentPageChanged(int page)
    {
        PageEntry.Text = _document?.LabelOf(page) ?? (page + 1).ToString();
        foreach (var row in _pageRows) row.IsCurrent = row.Index == page;
        var current = OutlineTree.CurrentFor(_outlineRoots, page);
        foreach (var row in _outlineRows) row.IsCurrent = ReferenceEquals(row.Node, current);
        UpdateNavigationButtons();
        if (_tab == "pages") EnsureThumbnails(page);
    }

    // ------------------------------------------------------------------ sidebar

    private void OnToggleSidebarClicked(object? sender, EventArgs e) => Sidebar.IsVisible = !Sidebar.IsVisible;

    private void OnTabClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string tab }) ShowTab(tab);
    }

    private void ShowTab(string tab)
    {
        _tab = tab;
        var selected = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#505050") : Color.FromArgb("#DADADA");
        ContentsTab.BackgroundColor = tab == "contents" ? selected : null;
        PagesTab.BackgroundColor = tab == "pages" ? selected : null;
        BookmarksTab.BackgroundColor = tab == "bookmarks" ? selected : null;
        AddBookmarkButton.IsVisible = tab == "bookmarks";
        SideList.ItemsSource = null;
        SideList.EmptyView = tab switch
        {
            "contents" => "This document has no table of contents.",
            "bookmarks" => "No bookmarks yet. Press Ctrl+D to save the place you are reading.",
            _ => "",
        };
        SideList.ItemsSource = tab switch
        {
            "contents" => _outlineRows,
            "pages" => _pageRows,
            _ => _bookmarkRows,
        };
        if (tab == "pages" && _controller is { } c) EnsureThumbnails(c.CurrentPage);
    }

    private void RebuildOutline()
    {
        _outlineRows.Clear();
        void Add(OutlineNode node, int level)
        {
            if (!_outlineRowFor.TryGetValue(node, out var row)) _outlineRowFor[node] = row = new OutlineRow(node, level);
            _outlineRows.Add(row);
            if (!row.IsExpanded) return;
            foreach (var child in node.Children) Add(child, level + 1);
        }

        foreach (var root in _outlineRoots) Add(root, 0);
    }

    private void OnChevronTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject { BindingContext: OutlineRow row } || !row.HasChildren) return;
        row.IsExpanded = !row.IsExpanded;
        RebuildOutline();
    }



    private void OnSideSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not { } item) return;
        SideList.SelectedItem = null;
        HideSidebarWhenNarrow();
        switch (item)
        {
            case OutlineRow row:
                if (row.Node.Location is { } place) { _controller?.GoTo(place); UpdateNavigationButtons(); }
                else if (row.Node.Uri is { } uri) OnUriRequested(uri);
                break;
            case PageRow row:
                _controller?.GoTo(new ReaderLocation(row.Index));
                UpdateNavigationButtons();
                break;
            case BookmarkRow row:
                _controller?.GoTo(row.Bookmark.Location);
                UpdateNavigationButtons();
                break;
        }
    }

    private void EnsureThumbnails(int center)
    {
        if (_document is not { } document || Thumbnails.Renderer is not { } renderer) return;
        for (int page = Math.Max(0, center - 6); page < Math.Min(document.PageCount, center + 24); page++)
        {
            if (!_thumbnailsRequested.Add(page)) continue;
            var row = _pageRows[page];
            _ = Task.Run(async () =>
            {
                if (!ReferenceEquals(_document, document)) return;           // another document was opened while this waited its turn
                var png = await renderer.RenderAsync(document, row.Index, 120);
                if (png is null) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (ReferenceEquals(_document, document)) row.Thumbnail = ImageSource.FromStream(() => new MemoryStream(png));
                });
            });
        }
    }

    // ------------------------------------------------------------------ bookmarks

    private void RefreshBookmarks()
    {
        _bookmarkRows.Clear();
        foreach (var bookmark in _bookmarks.Items) _bookmarkRows.Add(new BookmarkRow(bookmark, i => _document?.LabelOf(i) ?? (i + 1).ToString()));
    }

    private void SaveBookmarks(ReaderDocument document)
    {
        try
        {
            _storage.Save(document.Key, _bookmarks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A full disk or a folder that cannot be written: the bookmarks stay in the list for this session.
            _ = DisplayAlertAsync("Could not save the bookmarks", ex.Message, "OK");
        }
    }

    private void OnToggleBookmarkClicked(object? sender, EventArgs e)
    {
        if (_controller is not { } c || _document is not { } document) return;
        _bookmarks.Toggle(c.CurrentLocation);
        SaveBookmarks(document);
        RefreshBookmarks();
        ShowTab("bookmarks");
        Sidebar.IsVisible = true;
    }


    private void OnRemoveBookmarkClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject { BindingContext: BookmarkRow row } || _document is not { } document) return;
        _bookmarks.Remove(row.Bookmark.Id);
        SaveBookmarks(document);
        RefreshBookmarks();
    }

    // ------------------------------------------------------------------ navigation and zoom

    private void OnBackClicked(object? sender, EventArgs e) { _controller?.GoBack(); UpdateNavigationButtons(); }

    private void OnForwardClicked(object? sender, EventArgs e) { _controller?.GoForward(); UpdateNavigationButtons(); }

    private void OnPreviousPageClicked(object? sender, EventArgs e)
    {
        if (_controller is { } c) c.GoTo(new ReaderLocation(Math.Max(0, c.CurrentPage - 1)), recordHistory: false);
    }

    private void OnNextPageClicked(object? sender, EventArgs e)
    {
        if (_controller is { } c) c.GoTo(new ReaderLocation(Math.Min(c.Document.PageCount - 1, c.CurrentPage + 1)), recordHistory: false);
    }

    private void OnPageEntryCompleted(object? sender, EventArgs e)
    {
        if (_controller is not { } c) return;
        int target = c.Document.PageOf(PageEntry.Text ?? "");
        if (target >= 0) c.GoTo(new ReaderLocation(target));
        else PageEntry.Text = c.Document.LabelOf(c.CurrentPage);
        UpdateNavigationButtons();
    }

    private void OnZoomInClicked(object? sender, EventArgs e) => _controller?.ZoomBy(1.25);

    private void OnZoomOutClicked(object? sender, EventArgs e) => _controller?.ZoomBy(1 / 1.25);

    private void OnFitWidthClicked(object? sender, EventArgs e) { _controller?.SetZoomMode(ZoomMode.FitWidth); SelectZoomChoice(0); }

    private void OnFitPageClicked(object? sender, EventArgs e) { _controller?.SetZoomMode(ZoomMode.FitPage); SelectZoomChoice(1); }

    private void SelectZoomChoice(int index)
    {
        _settingZoomPicker = true;
        ZoomPicker.SelectedIndex = index;
        _settingZoomPicker = false;
    }

    private void OnZoomPicked(object? sender, EventArgs e)
    {
        if (_settingZoomPicker || _controller is not { } c || ZoomPicker.SelectedIndex < 0) return;
        switch (ZoomPicker.SelectedIndex)
        {
            case 0: c.SetZoomMode(ZoomMode.FitWidth); break;
            case 1: c.SetZoomMode(ZoomMode.FitPage); break;
            default:
                if (double.TryParse(ZoomChoices[ZoomPicker.SelectedIndex].TrimEnd('%'), out double percent)) c.SetZoom(percent / 100);
                break;
        }
    }

    // ------------------------------------------------------------------ links

    private async void OnUriRequested(string uri)
    {
        try
        {
            // An address can come from a link on a page or from the table of contents; both are the document's, and both get the same allowlist.
            if (LinkResolver.ForUri(uri).Kind != LinkKind.External)
            {
                OnUriBlocked(uri);
                return;
            }

            // A link in a document is the document's, not the reader's: say where it goes before leaving.
            if (!await DisplayAlertAsync("Open link?", uri, "Open", "Cancel")) return;
            await Launcher.Default.OpenAsync(new Uri(uri));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Could not open the link", ex.Message, "OK");
        }
    }

    private async void OnUriBlocked(string uri)
    {
        try
        {
            await DisplayAlertAsync("This link cannot be opened", "The document asks to open " + uri + ", which is not a web, mail or telephone address.", "OK");
        }
        catch (Exception)
        {
            // The page is gone; there is no one to tell.
        }
    }

    // ------------------------------------------------------------------ copy and select

    private async void OnCopyClicked(object? sender, EventArgs e)
    {
        if (_controller is not { } c) return;
        string text = c.CopySelection();
        if (text.Length == 0) return;
        try
        {
            await Clipboard.Default.SetTextAsync(text);
#if WINDOWS
            // The clipboard keeps what was copied only while the app runs unless it is flushed; a reader is often closed right after copying.
            global::Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
#endif
        }
        catch (Exception ex)
        {
#if WINDOWS
            WinUI.CrashLog.Write("copy failed: " + ex);
#else
            _ = ex;
#endif
        }
    }

    private void OnSelectAllClicked(object? sender, EventArgs e) => _controller?.Selection.SelectAll();

    // ------------------------------------------------------------------ find

    private void OnFindClicked(object? sender, EventArgs e)
    {
        if (_controller is null) return;
        FindBar.IsVisible = true;
        FindEntry.Focus();
    }

    private void OnFindDoneClicked(object? sender, EventArgs e)
    {
        FindBar.IsVisible = false;
        CancelSearch();
        if (_controller is { } c) c.Search = null;
        foreach (var row in _pageRows) row.Badge = "";
        _results = null;
    }

    private void OnFindEntryCompleted(object? sender, EventArgs e) => OnFindNextClicked(sender, e);

    private void OnFindOptionsChanged(object? sender, CheckedChangedEventArgs e) => _ = RunSearchAsync(0);

    private async void OnFindTextChanged(object? sender, TextChangedEventArgs e) => await RunSearchAsync(250);

    private async Task RunSearchAsync(int delayMilliseconds)
    {
        if (_controller is not { } c || _document is not { } document) return;
        CancelSearch();
        var cts = _search = new CancellationTokenSource();
        string query = FindEntry.Text ?? "";
        var options = (MatchCaseBox.IsChecked ? PdfTextSearchOptions.MatchCase : 0) | (WholeWordBox.IsChecked ? PdfTextSearchOptions.WholeWord : 0);
        try
        {
            if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds, cts.Token);
            if (string.IsNullOrWhiteSpace(query))
            {
                c.Search = null;
                _results = null;
                FindCountLabel.Text = "";
                foreach (var row in _pageRows) row.Badge = "";
                return;
            }

            var results = await DocumentSearch.RunAsync(document, query, options, cancellationToken: cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(_controller, c)) return;
            _results = results;
            results.MoveToFirstFrom(c.CurrentPage);
            c.Search = results;
            c.ShowCurrentSearchHit();
            UpdateFindLabel();
            foreach (var row in _pageRows)
            {
                int n = results.CountOnPage(row.Index);
                row.Badge = n == 0 ? "" : n == 1 ? "1 match" : n + " matches";
            }
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this one.
        }
        catch (Exception)
        {
            // Text that cannot be read from a page (a damaged one) must not end the app; the search just finds nothing.
            FindCountLabel.Text = "Search failed";
        }
    }

    private void CancelSearch()
    {
        var old = _search;
        _search = null;
        if (old is null) return;
        old.Cancel();
        old.Dispose();
    }

    private void UpdateFindLabel()
    {
        FindCountLabel.Text = _results is null ? "" : _results.Count == 0 ? "No matches" : $"{_results.CurrentIndex + 1} of {_results.Count}";
    }

    private void OnFindNextClicked(object? sender, EventArgs e)
    {
        if (_results is null || _controller is not { } c) return;
        _results.Next();
        c.ShowCurrentSearchHit();
        UpdateFindLabel();
    }

    private void OnFindPreviousClicked(object? sender, EventArgs e)
    {
        if (_results is null || _controller is not { } c) return;
        _results.Previous();
        c.ShowCurrentSearchHit();
        UpdateFindLabel();
    }
}

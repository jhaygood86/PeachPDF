using PeachDrawing.Core;
using PeachDrawing.PDF;

namespace PeachReader.Core;

/// <summary>The pointer a view should show over a place on a page.</summary>
public enum PointerCursor
{
    /// <summary>The ordinary pointer.</summary>
    Arrow,

    /// <summary>Over a link.</summary>
    Hand,

    /// <summary>Over text that can be selected.</summary>
    IBeam,
}

/// <summary>An occurrence of searched-for text to highlight on a page.</summary>
/// <param name="Rects">the rectangles to fill, in points from the top-left of the page, one per line the occurrence spans</param>
/// <param name="IsCurrent">whether it is the occurrence the reader is on</param>
public readonly record struct PageHighlight(IReadOnlyList<Rect> Rects, bool IsCurrent);

/// <summary>
/// Everything a scrolling view of a document does that is not drawing or windowing: the layout for the view's size and zoom, which page is
/// current, where a pointer is (arrow, hand or text cursor), clicks on links, drags and multiple clicks that select text, going to a place
/// and back, and what to highlight. A platform view feeds it the view's size, scroll position and pointer events in content pixels, paints
/// what it reports, and scrolls when it asks. Nothing here touches a window, so the same logic serves every platform.
/// </summary>
public sealed class ViewportController
{
    /// <summary>How far a pointer may move between press and release and still be a click, in pixels.</summary>
    public const double ClickSlop = 4;

    private PageLayoutModel _layout;
    private double _scrollY;
    private int _currentPage;
    private bool _pressed, _dragged, _selecting, _keepSelection;
    private double _pressX, _pressY;
    private SearchResults? _search;

    /// <summary>Makes a controller for a document in a view of an initial size.</summary>
    /// <param name="document">the document</param>
    /// <param name="viewportWidth">the view's width in pixels</param>
    /// <param name="viewportHeight">the view's height in pixels</param>
    public ViewportController(ReaderDocument document, double viewportWidth = 800, double viewportHeight = 600)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        Selection = new TextSelectionModel(document.GetText, document.PageCount);
        Selection.Changed += (_, _) => Invalidated?.Invoke();
        _layout = new PageLayoutModel(document.PageSizes, viewportWidth, viewportHeight);
    }

    /// <summary>The document being shown.</summary>
    public ReaderDocument Document { get; }

    /// <summary>The text the reader has selected.</summary>
    public TextSelectionModel Selection { get; }

    /// <summary>Where the reader has been, for back and forward.</summary>
    public NavigationHistory History { get; } = new();

    /// <summary>The current layout of the pages in the view.</summary>
    public PageLayoutModel Layout => _layout;

    /// <summary>The scroll position the view last reported, in pixels.</summary>
    public double ScrollY => _scrollY;

    /// <summary>The page the reader is on.</summary>
    public int CurrentPage => _currentPage;

    /// <summary>The place at the top of the view, for the history and the bookmark button.</summary>
    public ReaderLocation CurrentLocation => _layout.LocationAt(_scrollY);

    /// <summary>Raised when something painted by the view changed (a selection, a highlight) and it should paint again.</summary>
    public event Action? Invalidated;

    /// <summary>Raised when the layout changed (a resize, a zoom) so the view must change the size of its content.</summary>
    public event Action? LayoutChanged;

    /// <summary>Raised when the controller wants the view scrolled; the view scrolls and reports the position it reached with <see cref="SetScroll"/>.</summary>
    public event Action<double>? ScrollRequested;

    /// <summary>Raised when the current page changes.</summary>
    public event Action<int>? CurrentPageChanged;

    /// <summary>Raised when the reader clicks a link that opens an address; the view decides how, after <see cref="LinkResolver.IsLaunchable"/> has allowed it.</summary>
    public event Action<string>? UriRequested;

    /// <summary>Raised when the reader clicks a link the document is not allowed to open.</summary>
    public event Action<string>? UriBlocked;

    private readonly HashSet<string> _skipped = [];

    /// <summary>What the pages painted so far could not show or had to stand in for (an unsupported font, a missing shading), each named once.</summary>
    public IReadOnlyCollection<string> SkippedFeatures => _skipped;

    /// <summary>Raised when a painted page reports something not in <see cref="SkippedFeatures"/> yet.</summary>
    public event Action? SkippedFeaturesChanged;

    /// <summary>Records what a painted page said it could not draw; called by the platform view after each page.</summary>
    /// <param name="features">the names <see cref="PdfPageSource.Paint(int, PeachDrawing.Core.Canvas)"/> returned</param>
    public void ReportPaint(IReadOnlyCollection<string> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        bool added = false;
        foreach (var feature in features) added |= _skipped.Add(feature);
        if (added) SkippedFeaturesChanged?.Invoke();
    }

    /// <summary>The highlights of the search that is shown, or <see langword="null"/>.</summary>
    public SearchResults? Search
    {
        get => _search;
        set
        {
            _search = value;
            Invalidated?.Invoke();
        }
    }

    /// <summary>Tells the controller the size of the view.</summary>
    /// <param name="width">the width in pixels</param>
    /// <param name="height">the height in pixels</param>
    public void SetViewport(double width, double height)
    {
        if (Math.Abs(width - _layout.ViewportWidth) < 0.5 && Math.Abs(height - _layout.ViewportHeight) < 0.5) return;
        var location = CurrentLocation;
        _layout = _layout.WithViewport(width, height);
        LayoutChanged?.Invoke();
        // Keep the reader at the same place in the document when the width changes how tall the pages are.
        ScrollRequested?.Invoke(_layout.ScrollFor(location));
        Invalidated?.Invoke();
    }

    /// <summary>Tells the controller where the view is scrolled to.</summary>
    /// <param name="y">the scroll position in pixels from the top of the content</param>
    public void SetScroll(double y)
    {
        _scrollY = y;
        int page = _layout.PageCount == 0 ? 0 : _layout.CurrentPage(y);
        if (page == _currentPage) return;
        _currentPage = page;
        CurrentPageChanged?.Invoke(page);
    }

    /// <summary>Changes how the zoom is chosen: fit to width, fit to page, or the current zoom kept.</summary>
    /// <param name="mode">the way to choose</param>
    public void SetZoomMode(ZoomMode mode) => Relayout(_layout.WithMode(mode), 0);

    /// <summary>Sets the zoom, keeping what is at a point of the view where it is.</summary>
    /// <param name="zoom">the zoom; 1 is 100%</param>
    /// <param name="focusY">the vertical position in the view to keep still, in pixels from its top; the middle when not given</param>
    public void SetZoom(double zoom, double? focusY = null) => Relayout(_layout.WithZoom(zoom), focusY ?? _layout.ViewportHeight / 2);

    /// <summary>Changes the zoom by a factor, as a step of the zoom buttons or a pinch.</summary>
    /// <param name="factor">more than 1 zooms in, less than 1 zooms out</param>
    /// <param name="focusY">the vertical position in the view to keep still, in pixels from its top; the middle when not given</param>
    public void ZoomBy(double factor, double? focusY = null) => SetZoom(_layout.Zoom * factor, focusY);

    private void Relayout(PageLayoutModel next, double focusY)
    {
        double scroll = _layout.ScrollAfterZoom(_scrollY, next, focusY);
        _layout = next;
        LayoutChanged?.Invoke();
        ScrollRequested?.Invoke(scroll);
        Invalidated?.Invoke();
    }

    /// <summary>Scrolls to a place, remembering where the reader was so it can go back.</summary>
    /// <param name="location">the place to show</param>
    /// <param name="recordHistory"><see langword="false"/> when going back or forward, which must not add to the lists</param>
    public void GoTo(ReaderLocation location, bool recordHistory = true)
    {
        if (_layout.PageCount == 0) return;
        if (recordHistory) History.Navigated(CurrentLocation);
        ScrollRequested?.Invoke(_layout.ScrollFor(location));
    }

    /// <summary>Goes back to where the reader was before the last jump.</summary>
    /// <returns><see langword="true"/> when there was somewhere to go</returns>
    public bool GoBack()
    {
        if (History.GoBack(CurrentLocation) is not { } target) return false;
        GoTo(target, recordHistory: false);
        return true;
    }

    /// <summary>Goes forward again after going back.</summary>
    /// <returns><see langword="true"/> when there was somewhere to go</returns>
    public bool GoForward()
    {
        if (History.GoForward(CurrentLocation) is not { } target) return false;
        GoTo(target, recordHistory: false);
        return true;
    }

    /// <summary>Scrolls to the occurrence of a search that is current and makes it show.</summary>
    public void ShowCurrentSearchHit()
    {
        if (_search?.Current is not { } hit) return;
        var bounds = Document.GetText(hit.Page).GetBounds(hit.Range.Start, hit.Range.Length);
        double top = bounds.Count > 0 ? bounds[0].Y : 0;
        // Put it a third of the way down the view so there is context above it, unless it is already in view.
        var inContent = _layout.ToContent(hit.Page, bounds.Count > 0 ? bounds[0] : new Rect(0, 0, 1, 1));
        if (inContent.Y >= _scrollY + 20 && inContent.Y + inContent.Height <= _scrollY + _layout.ViewportHeight - 20)
        {
            Invalidated?.Invoke();
            return;
        }

        double y = _layout.PageRect(hit.Page).Y + top * _layout.Scale - _layout.ViewportHeight / 3;
        ScrollRequested?.Invoke(Math.Clamp(y, 0, Math.Max(0, _layout.ContentHeight - _layout.ViewportHeight)));
        Invalidated?.Invoke();
    }

    /// <summary>The highlights to paint on a page for the search that is shown.</summary>
    /// <param name="page">the page</param>
    /// <returns>one entry per occurrence on the page</returns>
    public IReadOnlyList<PageHighlight> HighlightsOnPage(int page)
    {
        if (_search is null || _search.CountOnPage(page) == 0) return [];
        var text = Document.GetText(page);
        var current = _search.Current;
        var result = new List<PageHighlight>();
        foreach (var hit in _search.HitsOnPage(page))
            result.Add(new PageHighlight(text.GetBounds(hit.Range.Start, hit.Range.Length), current is { } c && c == hit));
        return result;
    }

    /// <summary>The pointer to show at a place in the content.</summary>
    /// <param name="x">the position from the left of the content, in pixels</param>
    /// <param name="y">the position from the top of the content, in pixels</param>
    /// <returns>the cursor</returns>
    public PointerCursor CursorAt(double x, double y)
    {
        if (!_layout.TryToPage(x, y, out int page, out var point)) return PointerCursor.Arrow;
        if (LinkResolver.Hit(Document.GetLinks(page), point).Kind != LinkKind.None) return PointerCursor.Hand;
        return Document.GetText(page).HitTest(point) >= 0 ? PointerCursor.IBeam : PointerCursor.Arrow;
    }

    /// <summary>The pointer was pressed at a place in the content.</summary>
    /// <param name="x">the position from the left of the content, in pixels</param>
    /// <param name="y">the position from the top of the content, in pixels</param>
    /// <param name="clickCount">1 for a press, 2 for a double-click, 3 for a triple-click</param>
    /// <param name="extend"><see langword="true"/> while shift is held, to extend the selection to this place</param>
    public void PointerDown(double x, double y, int clickCount = 1, bool extend = false)
    {
        _pressed = true;
        _dragged = false;
        _selecting = false;
        _keepSelection = clickCount > 1 || extend;
        (_pressX, _pressY) = (x, y);
        if (!_layout.TryToPage(x, y, out int page, out var point))
        {
            Selection.Clear();
            return;
        }

        var text = Document.GetText(page);
        int caret = text.HitTest(point, nearest: false);
        if (clickCount >= 3 && caret >= 0) { Selection.SelectLine(page, caret); return; }
        if (clickCount == 2 && caret >= 0) { Selection.SelectWord(page, caret); return; }

        if (extend && Selection.Range is not null)
        {
            Selection.Extend(page, text.HitTest(point, nearest: true));
            _selecting = true;
            return;
        }

        if (caret >= 0)
        {
            Selection.Begin(page, caret);
            _selecting = true;
        }
        else
        {
            Selection.Clear();
        }
    }

    /// <summary>The pointer moved; while pressed this drags a selection.</summary>
    /// <param name="x">the position from the left of the content, in pixels</param>
    /// <param name="y">the position from the top of the content, in pixels</param>
    public void PointerMove(double x, double y)
    {
        if (!_pressed) return;
        if (!_dragged && Math.Abs(x - _pressX) < ClickSlop && Math.Abs(y - _pressY) < ClickSlop) return;
        _dragged = true;
        if (!_selecting) return;

        // A drag that leaves the pages (onto the desk, into a gap) keeps selecting: it picks the closest character of the closest page.
        int page = _layout.PageAt(y) is var at and >= 0 ? at : _layout.NearestPageTo(y);
        var r = _layout.PageRect(page);
        var point = new PaintPoint((x - r.X) / _layout.Scale, (y - r.Y) / _layout.Scale);
        int caret = Document.GetText(page).HitTest(point, nearest: true);
        if (caret >= 0) Selection.Extend(page, caret);
    }

    /// <summary>The pointer was released; a press and release that did not move over a link follows the link.</summary>
    /// <param name="x">the position from the left of the content, in pixels</param>
    /// <param name="y">the position from the top of the content, in pixels</param>
    /// <returns><see langword="true"/> when the release was a click on a link</returns>
    public bool PointerUp(double x, double y)
    {
        bool wasClick = _pressed && !_dragged;
        _pressed = false;
        _selecting = false;
        if (!wasClick || _keepSelection || !_layout.TryToPage(x, y, out int page, out var point)) return false;

        var target = LinkResolver.Hit(Document.GetLinks(page), point);
        switch (target.Kind)
        {
            case LinkKind.Internal:
                Selection.Clear();
                GoTo(target.Location);
                return true;
            case LinkKind.External:
                UriRequested?.Invoke(target.Uri!);
                return true;
            case LinkKind.Blocked:
                UriBlocked?.Invoke(target.Uri!);
                return true;
            default:
                // A plain click on text or the page clears the selection, as in any reader.
                if (Selection.HasSelection && Selection.Range is { } r && r.Start.Page == page) Selection.Clear();
                return false;
        }
    }

    /// <summary>The pointer left the view or was cancelled.</summary>
    public void PointerCancel()
    {
        _pressed = _dragged = _selecting = false;
    }

    /// <summary>The selected text, for copying.</summary>
    /// <returns>the text, empty when nothing is selected</returns>
    public string CopySelection() => Selection.GetSelectedText();
}

using PeachDrawing.Core;

namespace PeachReader.Core;

/// <summary>How the zoom of a document is chosen.</summary>
public enum ZoomMode
{
    /// <summary>The zoom is the number the reader set.</summary>
    Custom,

    /// <summary>The widest page fills the width of the view.</summary>
    FitWidth,

    /// <summary>The largest page fits the view whole.</summary>
    FitPage,
}

/// <summary>
/// Where each page of a document sits in the scrolled content of a view: stacked top to bottom, centered, each at its own size, with a gap
/// between them and a margin around. All lengths are device-independent pixels; a zoom of 1 is 100%, one point being 96/72 of a pixel as
/// on paper at 96 dpi. A layout never changes: zooming or resizing the view makes a new one.
/// </summary>
public sealed class PageLayoutModel
{
    /// <summary>The device-independent pixels in a point at a zoom of 1.</summary>
    public const double DipsPerPoint = 96.0 / 72.0;

    /// <summary>The smallest zoom a layout allows.</summary>
    public const double MinZoom = 0.1;

    /// <summary>The largest zoom a layout allows.</summary>
    public const double MaxZoom = 8;

    private readonly (double Width, double Height)[] _pages;
    private readonly double[] _tops;
    private readonly double _requestedZoom;

    /// <summary>Lays out pages in a view.</summary>
    /// <param name="pageSizes">the size of each page in points, as <see cref="PeachDrawing.PDF.PdfPageSource.GetPageSize"/> reports it</param>
    /// <param name="viewportWidth">the width of the view in pixels</param>
    /// <param name="viewportHeight">the height of the view in pixels</param>
    /// <param name="mode">how the zoom is chosen</param>
    /// <param name="zoom">the zoom for <see cref="ZoomMode.Custom"/></param>
    /// <param name="gap">the space between pages in pixels</param>
    /// <param name="margin">the space around the pages in pixels</param>
    public PageLayoutModel(IReadOnlyList<(double Width, double Height)> pageSizes, double viewportWidth, double viewportHeight,
        ZoomMode mode = ZoomMode.FitWidth, double zoom = 1, double gap = 12, double margin = 16)
    {
        ArgumentNullException.ThrowIfNull(pageSizes);
        _pages = [.. pageSizes];
        ViewportWidth = Math.Max(1, viewportWidth);
        ViewportHeight = Math.Max(1, viewportHeight);
        Mode = mode;
        Gap = gap;
        Margin = margin;
        _requestedZoom = zoom;

        double widest = _pages.Length == 0 ? 0 : _pages.Max(p => p.Width);
        double tallest = _pages.Length == 0 ? 0 : _pages.Max(p => p.Height);
        double effective = mode switch
        {
            ZoomMode.FitWidth when widest > 0 => (ViewportWidth - 2 * margin) / (widest * DipsPerPoint),
            ZoomMode.FitPage when widest > 0 && tallest > 0 => Math.Min((ViewportWidth - 2 * margin) / (widest * DipsPerPoint),
                (ViewportHeight - 2 * margin) / (tallest * DipsPerPoint)),
            _ => zoom,
        };
        Zoom = Math.Clamp(double.IsFinite(effective) ? effective : 1, MinZoom, MaxZoom);

        ContentWidth = Math.Max(ViewportWidth, widest * Scale + 2 * margin);
        _tops = new double[_pages.Length];
        double y = margin;
        for (int i = 0; i < _pages.Length; i++)
        {
            _tops[i] = y;
            y += _pages[i].Height * Scale + gap;
        }

        ContentHeight = _pages.Length == 0 ? 2 * margin : y - gap + margin;
    }

    /// <summary>The width of the view in pixels.</summary>
    public double ViewportWidth { get; }

    /// <summary>The height of the view in pixels.</summary>
    public double ViewportHeight { get; }

    /// <summary>How the zoom is chosen.</summary>
    public ZoomMode Mode { get; }

    /// <summary>The space between pages in pixels.</summary>
    public double Gap { get; }

    /// <summary>The space around the pages in pixels.</summary>
    public double Margin { get; }

    /// <summary>The zoom in effect, after fitting and the limits; 1 is 100%.</summary>
    public double Zoom { get; }

    /// <summary>The pixels in a point at the zoom in effect.</summary>
    public double Scale => Zoom * DipsPerPoint;

    /// <summary>The number of pages.</summary>
    public int PageCount => _pages.Length;

    /// <summary>The width of the scrolled content in pixels: the view's width, or more when a page is wider.</summary>
    public double ContentWidth { get; }

    /// <summary>The height of the scrolled content in pixels.</summary>
    public double ContentHeight { get; }

    /// <summary>The rectangle a page occupies in the content, in pixels.</summary>
    /// <param name="pageIndex">the zero-based page number</param>
    /// <returns>the page's rectangle</returns>
    public Rect PageRect(int pageIndex)
    {
        var (w, h) = _pages[pageIndex];
        double width = w * Scale;
        return new Rect((ContentWidth - width) / 2, _tops[pageIndex], width, h * Scale);
    }

    /// <summary>The page whose rectangle holds a vertical position of the content.</summary>
    /// <param name="y">a position from the top of the content, in pixels</param>
    /// <returns>the page, or -1 when the position is in a gap or margin</returns>
    public int PageAt(double y)
    {
        for (int i = 0; i < _pages.Length; i++)
            if (y >= _tops[i] && y < _tops[i] + _pages[i].Height * Scale) return i;
        return -1;
    }

    /// <summary>The first and last page that show any of a scrolled view, for painting.</summary>
    /// <param name="scrollY">the scroll position in pixels</param>
    /// <returns>the inclusive range, or (0, -1) when there are no pages</returns>
    public (int First, int Last) VisiblePages(double scrollY)
    {
        if (_pages.Length == 0) return (0, -1);
        double bottom = scrollY + ViewportHeight;
        int first = -1, last = -1;
        for (int i = 0; i < _pages.Length; i++)
        {
            if (_tops[i] + _pages[i].Height * Scale < scrollY) continue;
            if (_tops[i] > bottom) break;
            if (first < 0) first = i;
            last = i;
        }

        return first < 0 ? (Math.Clamp(NearestPage(scrollY), 0, _pages.Length - 1), -1) : (first, last);
    }

    /// <summary>The page the reader is on: the one under the middle of the view, or the nearest when the middle is in a gap.</summary>
    /// <param name="scrollY">the scroll position in pixels</param>
    /// <returns>the zero-based page number, or 0 with no pages</returns>
    public int CurrentPage(double scrollY) => _pages.Length == 0 ? 0 : NearestPage(scrollY + ViewportHeight / 2);

    /// <summary>The page under a vertical position, or the closest one when the position is in a gap, a margin or past either end.</summary>
    /// <param name="y">a position from the top of the content, in pixels</param>
    /// <returns>the zero-based page number, or 0 with no pages</returns>
    public int NearestPageTo(double y) => _pages.Length == 0 ? 0 : NearestPage(y);

    private int NearestPage(double y)
    {
        int at = PageAt(y);
        if (at >= 0) return at;
        int best = 0;
        double distance = double.MaxValue;
        for (int i = 0; i < _pages.Length; i++)
        {
            double d = y < _tops[i] ? _tops[i] - y : y - (_tops[i] + _pages[i].Height * Scale);
            if (d < distance) { distance = d; best = i; }
        }

        return best;
    }

    /// <summary>The scroll position that shows a place at the top of the view, one margin below the edge so it is not touching it; <see cref="LocationAt"/> is its inverse.</summary>
    /// <param name="location">the place to show</param>
    /// <returns>the scroll position in pixels, within the content</returns>
    public double ScrollFor(ReaderLocation location)
    {
        if (_pages.Length == 0) return 0;
        int page = Math.Clamp(location.PageIndex, 0, _pages.Length - 1);
        double y = _tops[page] + Math.Max(0, location.TopPoints) * Scale - Margin;
        return Math.Clamp(y, 0, Math.Max(0, ContentHeight - ViewportHeight));
    }

    /// <summary>The place at the top of a view scrolled to a position (one margin below its edge): the inverse of <see cref="ScrollFor"/>.</summary>
    /// <param name="scrollY">the scroll position in pixels</param>
    /// <returns>the page there and how far below its top, never above it</returns>
    public ReaderLocation LocationAt(double scrollY)
    {
        if (_pages.Length == 0) return default;
        double y = scrollY + Margin;
        int page = NearestPage(y);
        return new ReaderLocation(page, Math.Max(0, (y - _tops[page]) / Scale));
    }

    /// <summary>Where a pixel of the content is on the page under it.</summary>
    /// <param name="x">a position from the left of the content, in pixels</param>
    /// <param name="y">a position from the top of the content, in pixels</param>
    /// <param name="pageIndex">the page under the position, or -1</param>
    /// <param name="point">the position in points from the top-left of that page</param>
    /// <returns><see langword="true"/> when a page is under the position</returns>
    public bool TryToPage(double x, double y, out int pageIndex, out PaintPoint point)
    {
        pageIndex = PageAt(y);
        point = default;
        if (pageIndex < 0) return false;
        var r = PageRect(pageIndex);
        if (x < r.X || x >= r.X + r.Width) { pageIndex = -1; return false; }
        point = new PaintPoint((x - r.X) / Scale, (y - r.Y) / Scale);
        return true;
    }

    /// <summary>Moves a rectangle in points on a page to pixels in the content.</summary>
    /// <param name="pageIndex">the page the rectangle is on</param>
    /// <param name="bounds">a rectangle in points from the top-left of the page</param>
    /// <returns>the rectangle in pixels from the top-left of the content</returns>
    public Rect ToContent(int pageIndex, Rect bounds)
    {
        var r = PageRect(pageIndex);
        return new Rect(r.X + bounds.X * Scale, r.Y + bounds.Y * Scale, bounds.Width * Scale, bounds.Height * Scale);
    }

    /// <summary>The same pages in a view of another size.</summary>
    /// <param name="width">the new width in pixels</param>
    /// <param name="height">the new height in pixels</param>
    /// <returns>the new layout</returns>
    public PageLayoutModel WithViewport(double width, double height) => new(_pages, width, height, Mode, _requestedZoom, Gap, Margin);

    /// <summary>The same pages at a zoom the reader chose.</summary>
    /// <param name="zoom">the zoom; 1 is 100%</param>
    /// <returns>the new layout, in <see cref="ZoomMode.Custom"/></returns>
    public PageLayoutModel WithZoom(double zoom) => new(_pages, ViewportWidth, ViewportHeight, ZoomMode.Custom, zoom, Gap, Margin);

    /// <summary>The same pages with the zoom chosen another way.</summary>
    /// <param name="mode">the way to choose</param>
    /// <returns>the new layout</returns>
    public PageLayoutModel WithMode(ZoomMode mode) => new(_pages, ViewportWidth, ViewportHeight, mode, Zoom, Gap, Margin);

    /// <summary>
    /// The scroll position that keeps what is at a point of the view where it is after the zoom changes to <paramref name="newLayout"/>, as
    /// when the reader zooms around the pointer. Both layouts must be of the same pages.
    /// </summary>
    /// <param name="scrollY">the scroll position before, in pixels</param>
    /// <param name="newLayout">the layout after the zoom</param>
    /// <param name="focusY">the vertical position in the view, in pixels from its top, to keep still</param>
    /// <returns>the new scroll position in pixels, within the new content</returns>
    public double ScrollAfterZoom(double scrollY, PageLayoutModel newLayout, double focusY)
    {
        ArgumentNullException.ThrowIfNull(newLayout);
        if (_pages.Length == 0 || newLayout._pages.Length != _pages.Length) return 0;
        double contentY = scrollY + focusY;
        int page = NearestPage(contentY);
        double pointsInPage = (contentY - _tops[page]) / Scale;
        double y = newLayout._tops[page] + pointsInPage * newLayout.Scale - focusY;
        return Math.Clamp(y, 0, Math.Max(0, newLayout.ContentHeight - newLayout.ViewportHeight));
    }
}

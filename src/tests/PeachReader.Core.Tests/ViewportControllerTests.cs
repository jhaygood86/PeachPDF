using PeachDrawing.Core;

namespace PeachReader.Core.Tests;

public class ViewportControllerTests
{
    // Three 300 x 200 pt pages with a web link and an internal link on the first, and page 3 is 200 x 300.
    private static ViewportController Controller(double width = 1000, double height = 500) =>
        new(ReaderDocument.Open(TestDocument.Sample()), width, height);

    /// <summary>The content pixel of a point on a page.</summary>
    private static (double X, double Y) At(ViewportController c, int page, double xPoints, double yPoints)
    {
        var r = c.Layout.PageRect(page);
        return (r.X + xPoints * c.Layout.Scale, r.Y + yPoints * c.Layout.Scale);
    }

    [Fact]
    public void TheCursorIsAHandOverALink_ATextCursorOverText_AndAnArrowElsewhere()
    {
        var c = Controller();

        var (lx, ly) = At(c, 0, 50, 35);                 // over the web link
        var (tx, ty) = At(c, 0, 14, 23);                 // over "Introduction", clear of the links
        var (ex, ey) = At(c, 0, 280, 190);               // empty paper
        Assert.Equal(PointerCursor.Hand, c.CursorAt(lx, ly));
        Assert.Equal(PointerCursor.IBeam, c.CursorAt(tx, ty));
        Assert.Equal(PointerCursor.Arrow, c.CursorAt(ex, ey));
        Assert.Equal(PointerCursor.Arrow, c.CursorAt(2, 2));       // the desk
    }

    [Fact]
    public void AClickOnAnExternalLink_AsksTheViewToOpenIt()
    {
        var c = Controller();
        var opened = new List<string>();
        c.UriRequested += opened.Add;
        var (x, y) = At(c, 0, 50, 30);

        c.PointerDown(x, y);
        bool clicked = c.PointerUp(x, y);

        Assert.True(clicked);
        Assert.Equal(["https://example.com/peaches"], opened);
    }

    [Fact]
    public void AClickOnAnInternalLink_ScrollsThereAndCanGoBack()
    {
        var c = Controller();
        var scrolls = new List<double>();
        c.ScrollRequested += scrolls.Add;
        var (x, y) = At(c, 0, 50, 90);

        c.PointerDown(x, y);
        Assert.True(c.PointerUp(x, y));

        Assert.Equal(c.Layout.ScrollFor(new ReaderLocation(2, 50)), Assert.Single(scrolls), 6);
        Assert.True(c.History.CanGoBack);
        c.SetScroll(scrolls[0]);
        Assert.True(c.GoBack());
        Assert.Equal(0, scrolls[^1], 6);
        Assert.True(c.GoForward());
        Assert.Equal(scrolls[0], scrolls[^1], 6);
        Assert.False(c.GoForward());
    }

    [Fact]
    public void AClickOnAnUnsafeLink_IsReportedAndNotOpened()
    {
        var bytes = TestDocument.Build([new TestPage(300, 200, "", TestDocument.UriLink(10, 100, 100, 120, "file:///c:/secret"))]);
        var c = new ViewportController(ReaderDocument.Open(bytes), 1000, 500);
        var blocked = new List<string>();
        c.UriBlocked += blocked.Add;
        c.UriRequested += _ => Assert.Fail("an unsafe address must not be opened");
        var (x, y) = At(c, 0, 50, 90);

        c.PointerDown(x, y);
        c.PointerUp(x, y);

        Assert.Equal(["file:///c:/secret"], blocked);
    }

    [Fact]
    public void ADragSelectsText_AndDoesNotFollowALinkItEndsOn()
    {
        var c = Controller();
        var opened = new List<string>();
        c.UriRequested += opened.Add;
        var (x0, y0) = At(c, 0, 12, 23);                 // the "I" of Introduction (text at 10 pt, 5 pt per character)
        var (x1, y1) = At(c, 0, 50, 23);

        c.PointerDown(x0, y0);
        c.PointerMove(x1, y1);
        bool clicked = c.PointerUp(x1, y1);

        Assert.False(clicked);
        Assert.Empty(opened);
        Assert.Equal("Introd", c.CopySelection().Substring(0, 6));
        Assert.True(c.Selection.HasSelection);
    }

    [Fact]
    public void ADragThatLeavesThePages_KeepsSelectingTheClosestText()
    {
        var c = Controller();
        var (x0, y0) = At(c, 0, 12, 23);

        c.PointerDown(x0, y0);
        double farRight = c.Layout.ContentWidth - 2;         // right of page 2, on the desk
        c.PointerMove(farRight, c.Layout.PageRect(1).Y + 40);
        c.PointerUp(farRight, c.Layout.PageRect(1).Y + 40);

        Assert.StartsWith("Introduction", c.CopySelection());
        Assert.Contains("Methods", c.CopySelection());
    }

    [Fact]
    public void DoubleAndTripleClick_SelectAWordAndALine_AndShiftClickExtends()
    {
        var c = Controller();
        var (x, y) = At(c, 0, 14, 23);

        c.PointerDown(x, y, clickCount: 2);
        c.PointerUp(x, y);
        Assert.Equal("Introduction", c.CopySelection());

        c.PointerDown(x, y, clickCount: 3);
        c.PointerUp(x, y);
        Assert.Equal("Introduction to peaches", c.CopySelection());

        c.PointerDown(x, y);
        c.PointerMove(x + 20, y);
        c.PointerUp(x + 20, y);
        var (ex, ey) = At(c, 0, 100, 23);
        c.PointerDown(ex, ey, extend: true);
        c.PointerUp(ex, ey);
        Assert.True(c.CopySelection().Length > 6);
    }

    [Fact]
    public void ExtendSelectionTo_MovesTheFreeEndOfAWordSelectionAnywhereOnThePages()
    {
        var c = Controller();
        var (x, y) = At(c, 0, 14, 23);
        c.PointerDown(x, y, clickCount: 2);
        c.PointerUp(x, y);
        Assert.Equal("Introduction", c.CopySelection());

        var (ex, ey) = At(c, 0, 87, 23);
        c.ExtendSelectionTo(ex, ey);
        Assert.StartsWith("Introduction to", c.CopySelection());

        c.ExtendSelectionTo(c.Layout.ContentWidth - 2, c.Layout.PageRect(1).Y + 40);      // on the desk beside page 2
        Assert.Contains("Methods", c.CopySelection());

        c.Selection.Clear();
        c.ExtendSelectionTo(ex, ey);                                                      // nothing selected: nothing to extend
        Assert.False(c.Selection.HasSelection);
    }

    [Fact]
    public void AClickOnPlainPaper_ClearsTheSelection()
    {
        var c = Controller();
        c.Selection.SelectAll();
        var (x, y) = At(c, 0, 280, 190);

        c.PointerDown(x, y);
        c.PointerUp(x, y);

        Assert.False(c.Selection.HasSelection);
    }

    [Fact]
    public void ResizingKeepsThePlaceInTheDocument()
    {
        var c = Controller(width: 1000, height: 500);
        int layouts = 0;
        double? scrolled = null;
        c.LayoutChanged += () => layouts++;
        c.ScrollRequested += y => scrolled = y;
        var place = new ReaderLocation(1, 0);
        c.SetScroll(c.Layout.ScrollFor(place));

        c.SetViewport(1000, 500);                          // the same size: nothing to do
        Assert.Equal(0, layouts);

        var before = c.CurrentLocation;
        c.SetViewport(700, 500);
        Assert.Equal(1, layouts);
        Assert.Equal(c.Layout.ScrollFor(before), scrolled);
        Assert.Equal(700, c.Layout.ViewportWidth);
    }

    [Fact]
    public void ZoomingChangesTheLayoutAndKeepsTheFocusedPlaceStill()
    {
        var c = Controller();
        double? scrolled = null;
        c.ScrollRequested += y => scrolled = y;
        int layouts = 0;
        c.LayoutChanged += () => layouts++;
        double before = c.Layout.Zoom;

        c.ZoomBy(2, focusY: 100);

        Assert.Equal(before * 2, c.Layout.Zoom, 6);
        Assert.Equal(ZoomMode.Custom, c.Layout.Mode);
        Assert.Equal(1, layouts);
        Assert.NotNull(scrolled);

        c.SetZoomMode(ZoomMode.FitWidth);
        Assert.Equal(ZoomMode.FitWidth, c.Layout.Mode);
        c.SetZoom(1);
        Assert.Equal(1, c.Layout.Zoom, 6);
    }

    [Fact]
    public void TheCurrentPageFollowsTheScroll()
    {
        var c = Controller(width: 1000, height: 400);
        var seen = new List<int>();
        c.CurrentPageChanged += seen.Add;

        c.SetScroll(0);
        c.SetScroll(c.Layout.PageRect(1).Y);
        c.SetScroll(c.Layout.PageRect(1).Y + 5);
        c.SetScroll(c.Layout.ContentHeight);

        Assert.Equal([1, 2], seen);
        Assert.Equal(2, c.CurrentPage);
        Assert.Equal(0, c.CurrentLocation.PageIndex == 2 ? 0 : 1);
    }

    [Fact]
    public async Task SearchHighlightsAreReportedPerPage_AndShowingAHitScrollsToIt()
    {
        var c = Controller(width: 1000, height: 300);
        var results = await DocumentSearch.RunAsync(c.Document, "peaches", cancellationToken: TestContext.Current.CancellationToken);
        var scrolls = new List<double>();
        c.ScrollRequested += scrolls.Add;
        int repaints = 0;
        c.Invalidated += () => repaints++;
        Assert.Empty(c.HighlightsOnPage(0));

        c.Search = results;

        Assert.Equal(1, repaints);
        var onFirst = c.HighlightsOnPage(0);
        Assert.Equal(2, onFirst.Count);                                      // "peaches" twice on page 1
        Assert.True(onFirst[0].IsCurrent);
        Assert.False(onFirst[1].IsCurrent);
        Assert.Single(c.HighlightsOnPage(2));
        Assert.Empty(c.HighlightsOnPage(1));

        results.Next();
        results.Next();                                                      // the hit on page 3, below the view
        c.ShowCurrentSearchHit();
        Assert.Single(scrolls);
        Assert.True(scrolls[0] > c.Layout.PageRect(2).Y - c.Layout.ViewportHeight);

        c.Search = null;
        Assert.Empty(c.HighlightsOnPage(0));
    }

    [Fact]
    public async Task ShowingAHitAlreadyInViewDoesNotScroll()
    {
        var c = Controller(width: 1000, height: 900);
        c.Search = await DocumentSearch.RunAsync(c.Document, "Introduction", cancellationToken: TestContext.Current.CancellationToken);
        int scrolls = 0;
        c.ScrollRequested += _ => scrolls++;

        c.ShowCurrentSearchHit();

        Assert.Equal(0, scrolls);
        c.Search = null;
        c.ShowCurrentSearchHit();                                            // no search: nothing happens
        Assert.Equal(0, scrolls);
    }

    [Fact]
    public void WithNoPointerPressed_MovesDoNothing_AndCancelEndsAPress()
    {
        var c = Controller();
        var (x, y) = At(c, 0, 14, 23);

        c.PointerMove(x, y);
        Assert.False(c.Selection.HasSelection);
        c.PointerDown(x, y);
        c.PointerCancel();
        c.PointerMove(x + 30, y);
        Assert.False(c.Selection.HasSelection);
        Assert.False(c.PointerUp(x, y));
        Assert.Throws<ArgumentNullException>(() => new ViewportController(null!));
    }
}

using PeachDrawing.Core;

namespace PeachReader.Core.Tests;

public class PageLayoutModelTests
{
    // Letter, A4-ish and a landscape page, in points.
    private static readonly (double, double)[] Pages = [(612, 792), (595, 842), (792, 612)];

    private static PageLayoutModel Layout(double width = 1000, double height = 800, ZoomMode mode = ZoomMode.Custom, double zoom = 1) =>
        new(Pages, width, height, mode, zoom);

    [Fact]
    public void Pages_AreStackedCenteredAtTheirOwnSizeWithAGapBetween()
    {
        var layout = Layout();
        double s = PageLayoutModel.DipsPerPoint;

        var first = layout.PageRect(0);
        var second = layout.PageRect(1);
        var third = layout.PageRect(2);

        Assert.Equal(612 * s, first.Width, 6);
        Assert.Equal(792 * s, first.Height, 6);
        Assert.Equal(16, first.Y, 6);                                       // the margin
        Assert.Equal(first.Y + first.Height + 12, second.Y, 6);             // the gap
        Assert.Equal(595 * s, second.Width, 6);
        Assert.Equal(792 * s, third.Width, 6);                              // a landscape page keeps its own shape
        Assert.Equal((layout.ContentWidth - first.Width) / 2, first.X, 6);  // centered
        Assert.Equal(third.Y + third.Height + 16, layout.ContentHeight, 6);
    }

    [Fact]
    public void ContentIsAsWideAsTheViewUnlessAPageIsWider()
    {
        Assert.Equal(1200, Layout(width: 1200).ContentWidth);
        var zoomed = Layout(width: 400);
        Assert.Equal(792 * PageLayoutModel.DipsPerPoint + 32, zoomed.ContentWidth, 6);
    }

    [Fact]
    public void FitWidth_MakesTheWidestPageFillTheViewLessItsMargins()
    {
        var layout = Layout(width: 832, mode: ZoomMode.FitWidth);

        Assert.Equal(800, layout.PageRect(2).Width, 6);                     // 832 - 2 * 16
        Assert.Equal(ZoomMode.FitWidth, layout.Mode);
    }

    [Fact]
    public void FitPage_FitsTheLargestPageWholeInTheView()
    {
        var layout = Layout(width: 2000, height: 600, mode: ZoomMode.FitPage);

        Assert.True(layout.PageRect(1).Height <= 600 - 32 + 1e-6);
        Assert.Equal(600 - 32, layout.PageRect(1).Height, 6);               // the tallest page, A4-ish, is the limit
    }

    [Theory]
    [InlineData(0.001, PageLayoutModel.MinZoom)]
    [InlineData(100, PageLayoutModel.MaxZoom)]
    [InlineData(2, 2)]
    public void Zoom_IsHeldInsideTheLimits(double requested, double expected)
    {
        Assert.Equal(expected, Layout(zoom: requested).Zoom, 6);
    }

    [Fact]
    public void ANonsenseZoomOrNoPages_DoNotBreakTheLayout()
    {
        Assert.Equal(1, Layout(zoom: double.NaN).Zoom);
        var empty = new PageLayoutModel([], 500, 400);
        Assert.Equal(0, empty.PageCount);
        Assert.Equal(32, empty.ContentHeight);
        Assert.Equal(0, empty.CurrentPage(0));
        Assert.Equal(0, empty.ScrollFor(new ReaderLocation(3)));
        Assert.Equal((0, -1), empty.VisiblePages(0));
    }

    [Fact]
    public void PageAt_FindsThePageUnderAVerticalPositionAndNothingInTheGaps()
    {
        var layout = Layout();
        var first = layout.PageRect(0);

        Assert.Equal(0, layout.PageAt(first.Y + 1));
        Assert.Equal(-1, layout.PageAt(first.Y + first.Height + 5));
        Assert.Equal(1, layout.PageAt(layout.PageRect(1).Y + 1));
        Assert.Equal(-1, layout.PageAt(0));
    }

    [Fact]
    public void VisiblePages_ListsThePagesATallViewTouches()
    {
        var layout = Layout(height: 800);

        Assert.Equal((0, 0), layout.VisiblePages(0));
        var second = layout.PageRect(1);
        Assert.Equal((0, 1), layout.VisiblePages(second.Y - 100));
        Assert.Equal((2, 2), layout.VisiblePages(layout.PageRect(2).Y));
    }

    [Fact]
    public void CurrentPage_IsThePageUnderTheMiddleOfTheView()
    {
        var layout = Layout(height: 800);
        var second = layout.PageRect(1);

        Assert.Equal(0, layout.CurrentPage(0));
        Assert.Equal(1, layout.CurrentPage(second.Y));
        Assert.Equal(2, layout.CurrentPage(layout.ContentHeight));
        // In a gap it is the nearest page.
        double gapMiddle = layout.PageRect(0).Y + layout.PageRect(0).Height + 2;
        Assert.Equal(0, layout.CurrentPage(gapMiddle - 400));
    }

    [Fact]
    public void ScrollFor_PutsAPlaceNearTheTopOfTheViewAndStaysInsideTheContent()
    {
        var layout = Layout(height: 800);
        var second = layout.PageRect(1);

        Assert.Equal(0, layout.ScrollFor(new ReaderLocation(0)));
        Assert.Equal(second.Y - 12, layout.ScrollFor(new ReaderLocation(1)), 6);
        Assert.Equal(second.Y + 100 * layout.Scale - 16, layout.ScrollFor(new ReaderLocation(1, 100)), 6);
        Assert.Equal(layout.ContentHeight - 800, layout.ScrollFor(new ReaderLocation(2, 600)), 6);     // clamped to the end
        Assert.Equal(layout.ScrollFor(new ReaderLocation(2)), layout.ScrollFor(new ReaderLocation(99)));   // a page past the end is the last page
    }

    [Fact]
    public void TryToPage_MapsAPixelToPointsOnThePageUnderIt()
    {
        var layout = Layout();
        var r = layout.PageRect(1);

        Assert.True(layout.TryToPage(r.X + 10 * layout.Scale, r.Y + 20 * layout.Scale, out int page, out var point));
        Assert.Equal(1, page);
        Assert.Equal(10, point.X, 6);
        Assert.Equal(20, point.Y, 6);

        Assert.False(layout.TryToPage(r.X - 5, r.Y + 5, out page, out _));       // left of the page, on the desk
        Assert.Equal(-1, page);
        Assert.False(layout.TryToPage(r.X + 5, 0, out _, out _));                // above the first page
    }

    [Fact]
    public void ToContent_MapsPageRectanglesToPixelsInTheContent()
    {
        var layout = Layout();
        var r = layout.PageRect(2);

        var mapped = layout.ToContent(2, new Rect(10, 20, 30, 40));

        Assert.Equal(r.X + 10 * layout.Scale, mapped.X, 6);
        Assert.Equal(r.Y + 20 * layout.Scale, mapped.Y, 6);
        Assert.Equal(30 * layout.Scale, mapped.Width, 6);
        Assert.Equal(40 * layout.Scale, mapped.Height, 6);
    }

    [Fact]
    public void WithViewportZoomAndMode_MakeNewLayoutsOfTheSamePages()
    {
        var layout = Layout(width: 832, mode: ZoomMode.FitWidth);

        var wider = layout.WithViewport(1632, 800);
        Assert.Equal(1600, wider.PageRect(2).Width, 6);                    // still fitting the width
        var custom = layout.WithZoom(2);
        Assert.Equal(ZoomMode.Custom, custom.Mode);
        Assert.Equal(2, custom.Zoom);
        Assert.Equal(ZoomMode.FitWidth, custom.WithMode(ZoomMode.FitWidth).Mode);
        Assert.Equal(layout.Zoom, layout.WithMode(ZoomMode.Custom).Zoom, 6);   // leaving a fit mode keeps the zoom it had
        Assert.Equal(3, wider.PageCount);
    }

    [Fact]
    public void ScrollAfterZoom_KeepsTheContentUnderTheFocusWhereItWas()
    {
        var before = Layout(height: 800);
        var after = before.WithZoom(2);
        double scroll = before.PageRect(1).Y + 100 * before.Scale - 300;      // 100 pt into page 2 sits 300 px down the view

        double next = before.ScrollAfterZoom(scroll, after, focusY: 300);

        Assert.Equal(after.PageRect(1).Y + 100 * after.Scale - 300, next, 6);
        Assert.Throws<ArgumentNullException>(() => before.ScrollAfterZoom(0, null!, 0));
    }
}

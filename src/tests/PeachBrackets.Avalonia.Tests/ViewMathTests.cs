using PeachBrackets.HtmlViewCore;
using PeachDrawing.Core;

namespace PeachBrackets.Avalonia.Tests;

/// <summary>The arithmetic the views share (compiled into the view from PeachBrackets.HtmlView.Core), written the way the view calls it.</summary>
public class ViewMathTests
{
    [Fact]
    public void AVerticalPositionIsOnThePageThatHoldsItWithTheOffsetInsideItOrOnNone()
    {
        double[] tops = [0, 120, 300];
        double[] heights = [100, 100, 50];

        Assert.Equal(0, PageLayout.PageAt(tops, heights, 0, out var local));
        Assert.Equal(0, local);
        Assert.Equal(1, PageLayout.PageAt(tops, heights, 170, out local));
        Assert.Equal(50, local);
        Assert.Equal(2, PageLayout.PageAt(tops, heights, 349, out local));
        Assert.Equal(49, local);

        // The gap between pages, the end of the last page and above the first are on no page.
        Assert.Equal(-1, PageLayout.PageAt(tops, heights, 110, out local));
        Assert.Equal(0, local);
        Assert.Equal(-1, PageLayout.PageAt(tops, heights, 350, out _));
        Assert.Equal(-1, PageLayout.PageAt(tops, heights, -1, out _));
    }

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:a@example.com", true)]
    [InlineData("tel:+15555550100", true)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    public void OnlyTheSchemesADocumentCannotAbuseAreHandedToTheSystem(string uri, bool launchable) =>
        Assert.Equal(launchable, PageLayout.IsLaunchable(uri));

    [Fact]
    public void AScrollOffsetStaysInsideTheContentAndAZoomInsideItsLimits()
    {
        Assert.Equal(0, ViewportMath.ClampScroll(-20, 1000, 300));
        Assert.Equal(700, ViewportMath.ClampScroll(900, 1000, 300));
        Assert.Equal(250, ViewportMath.ClampScroll(250, 1000, 300));
        Assert.Equal(0, ViewportMath.ClampScroll(50, 200, 300));

        Assert.Equal(0.5, ViewportMath.ClampZoom(0.1, 0.5, 4));
        Assert.Equal(4, ViewportMath.ClampZoom(9, 0.5, 4));
        Assert.Equal(2, ViewportMath.ClampZoom(2, 0.5, 4));
    }

    [Fact]
    public void ZoomingKeepsTheContentUnderTheFocusWhereItIs()
    {
        // The content at 100 px into the view (scroll 200 + focus 100 = 300 at zoom 1) is at 600 at zoom 2, so the scroll is 500.
        Assert.Equal(500, ViewportMath.ScrollAfterZoom(200, 1, 2, 100));
        Assert.Equal(200, ViewportMath.ScrollAfterZoom(200, 1, 1, 100));
    }

    [Fact]
    public void TheHostCanOnlyTakeGesturesAwayFromWhatTheDocumentAllows()
    {
        var document = TouchActions.PanUp | TouchActions.PanDown | TouchActions.PanLeft;
        var host = TouchActions.PanUp | TouchActions.PanRight;
        Assert.Equal(TouchActions.PanUp, ViewportMath.Allowed(document, host));
    }

    [Fact]
    public void APanIsFilteredByTheDirectionsTouchActionAllows()
    {
        // A finger moving left (positive) is a pan-left, moving right a pan-right; up (positive) is pan-up, down pan-down.
        Assert.Equal((5.0, 0.0), ViewportMath.FilterPan(5, 7, TouchActions.PanLeft));
        Assert.Equal((0.0, 7.0), ViewportMath.FilterPan(5, 7, TouchActions.PanUp));
        Assert.Equal((-5.0, 0.0), ViewportMath.FilterPan(-5, -7, TouchActions.PanRight));
        Assert.Equal((0.0, -7.0), ViewportMath.FilterPan(-5, -7, TouchActions.PanDown));
        Assert.Equal((0.0, 0.0), ViewportMath.FilterPan(5, 7, TouchActions.None));
        Assert.Equal((5.0, 7.0), ViewportMath.FilterPan(5, 7, TouchActions.PanLeft | TouchActions.PanUp));
    }
}

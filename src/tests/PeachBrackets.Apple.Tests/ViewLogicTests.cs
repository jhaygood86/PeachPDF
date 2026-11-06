using PeachBrackets.Apple.Logic;
using PeachDrawing.Core;

namespace PeachBrackets.Apple.Tests;

public class ViewLogicTests
{
    [Theory]
    [InlineData(CursorShape.Default, "Arrow")]
    [InlineData(CursorShape.None, "Hidden")]
    [InlineData(CursorShape.Pointer, "PointingHand")]
    [InlineData(CursorShape.Text, "IBeam")]
    [InlineData(CursorShape.VerticalText, "IBeamVertical")]
    [InlineData(CursorShape.Crosshair, "Crosshair")]
    [InlineData(CursorShape.Wait, "Arrow")]
    [InlineData(CursorShape.NotAllowed, "NotAllowed")]
    [InlineData(CursorShape.Move, "OpenHand")]
    [InlineData(CursorShape.Grab, "OpenHand")]
    [InlineData(CursorShape.Grabbing, "ClosedHand")]
    [InlineData(CursorShape.Copy, "DragCopy")]
    [InlineData(CursorShape.Alias, "DragLink")]
    [InlineData(CursorShape.ContextMenu, "ContextualMenu")]
    [InlineData(CursorShape.ResizeColumn, "ResizeLeftRight")]
    [InlineData(CursorShape.ResizeRow, "ResizeUpDown")]
    [InlineData(CursorShape.ResizeDiagonalUp, "ResizeLeftRight")]
    public void EveryShapeHasAPointer(CursorShape shape, string expected) => Assert.Equal(expected, AppleCursors.ToKind(shape).ToString());

    [Fact]
    public void NoShapeIsLeftWithoutAPointer()
    {
        foreach (var shape in Enum.GetValues<CursorShape>())
            Assert.True(Enum.IsDefined(AppleCursors.ToKind(shape)));
    }

    [Fact]
    public void DevicesAddUpToWhatTheMediaFeaturesReport()
    {
        Assert.Equal(InputCapabilities.None, AppleInput.Capabilities(false, false, false));
        Assert.Equal(InputCapabilities.Mouse, AppleInput.Capabilities(true, false, false));
        Assert.Equal(InputCapabilities.Mouse, AppleInput.Capabilities(false, true, false));
        Assert.Equal(InputCapabilities.Touch, AppleInput.Capabilities(false, false, true));

        var both = AppleInput.Capabilities(true, false, true);
        Assert.Equal(PointerTypes.Fine, both.PrimaryPointer);
        Assert.True(both.AnyCanHover);
        Assert.Equal(PointerTypes.Fine | PointerTypes.Coarse, both.AnyPointers);
    }

    [Fact]
    public void ATouchDisplayMacFollowsTheLastInputUsed()
    {
        var touchLast = AppleInput.Capabilities(true, false, true, touchUsedLast: true);
        Assert.Equal(PointerTypes.Coarse, touchLast.PrimaryPointer);
        Assert.False(touchLast.PrimaryCanHover);
        Assert.True(touchLast.AnyCanHover);
        Assert.Equal(PointerTypes.Fine | PointerTypes.Coarse, touchLast.AnyPointers);

        var touchOnly = AppleInput.Capabilities(false, false, true, touchUsedLast: true);
        Assert.Equal(InputCapabilities.Touch, touchOnly);
    }

    [Fact]
    public void APositionIsMappedToThePageItIsOn()
    {
        double[] tops = [4, 120];
        double[] heights = [100, 50];

        Assert.Equal(0, PageLayout.PageAt(tops, heights, 4, out var local));
        Assert.Equal(0, local);
        Assert.Equal(-1, PageLayout.PageAt(tops, heights, 110, out _));
        Assert.Equal(1, PageLayout.PageAt(tops, heights, 130, out local));
        Assert.Equal(10, local);
        Assert.Equal(-1, PageLayout.PageAt(tops, heights, 2, out _));
    }

    [Theory]
    [InlineData("https://example.com/", true)]
    [InlineData("mailto:a@example.com", true)]
    [InlineData("tel:+15555550100", true)]
    [InlineData("intent://scan/#Intent;scheme=zxing;end", false)]
    [InlineData("file:///data/data/app/secret", false)]
    [InlineData("content://contacts/people", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("relative.html", false)]
    public void OnlySafeSchemesAreLaunched(string uri, bool expected) => Assert.Equal(expected, PageLayout.IsLaunchable(uri));

    [Theory]
    [InlineData(-10, 1000, 400, 0)]
    [InlineData(250, 1000, 400, 250)]
    [InlineData(900, 1000, 400, 600)]
    [InlineData(50, 300, 400, 0)] // content smaller than the view cannot scroll
    public void ScrollIsHeldInsideTheContent(double scroll, double content, double viewport, double expected) =>
        Assert.Equal(expected, ViewportMath.ClampScroll(scroll, content, viewport));

    [Fact]
    public void ZoomIsHeldBetweenItsLimits()
    {
        Assert.Equal(0.5, ViewportMath.ClampZoom(0.1, 0.5, 4));
        Assert.Equal(4, ViewportMath.ClampZoom(10, 0.5, 4));
        Assert.Equal(2, ViewportMath.ClampZoom(2, 0.5, 4));
    }

    [Fact]
    public void ZoomingKeepsTheContentUnderTheFocusWhereItIs()
    {
        // The content point under a focus at 300 with a scroll of 200 at zoom 1 is 500; after zooming to 2 it must still be under 300.
        var scroll = ViewportMath.ScrollAfterZoom(200, 1, 2, 300);

        Assert.Equal(700, scroll);
        Assert.Equal(500, (scroll + 300) / 2);
    }

    [Fact]
    public void TheApplicationCanOnlyTakeGesturesAway()
    {
        Assert.Equal(TouchActions.PanUp, ViewportMath.Allowed(TouchActions.PanUp | TouchActions.PanDown, TouchActions.PanUp | TouchActions.PinchZoom));
        Assert.Equal(TouchActions.None, ViewportMath.Allowed(TouchActions.None, TouchActions.All));
    }

    [Fact]
    public void AScrollIsFilteredByTheDirectionsTouchActionAllows()
    {
        // A positive distance means the finger moved left or up: a pan-left or a pan-up.
        Assert.Equal((5.0, 0.0), ViewportMath.FilterPan(5, 8, TouchActions.PanLeft));
        Assert.Equal((0.0, 8.0), ViewportMath.FilterPan(5, 8, TouchActions.PanUp));
        Assert.Equal((-5.0, -8.0), ViewportMath.FilterPan(-5, -8, TouchActions.PanRight | TouchActions.PanDown));
        Assert.Equal((0.0, 0.0), ViewportMath.FilterPan(5, 8, TouchActions.None));
        Assert.Equal((5.0, 8.0), ViewportMath.FilterPan(5, 8, TouchActions.All));
        Assert.Equal((0.0, -8.0), ViewportMath.FilterPan(5, -8, TouchActions.PanDown));
    }
}

using Microsoft.UI.Input;
using PeachBrackets;
using PeachBrackets.WinUI;
using PeachDrawing.Core;

namespace PeachBrackets.WinUI.Tests;

public class HtmlViewLogicTests
{
    [Theory]
    [InlineData(CursorShape.Default, InputSystemCursorShape.Arrow)]
    [InlineData(CursorShape.None, InputSystemCursorShape.Arrow)]
    [InlineData(CursorShape.Pointer, InputSystemCursorShape.Hand)]
    [InlineData(CursorShape.Text, InputSystemCursorShape.IBeam)]
    [InlineData(CursorShape.VerticalText, InputSystemCursorShape.IBeam)]
    [InlineData(CursorShape.Wait, InputSystemCursorShape.Wait)]
    [InlineData(CursorShape.Progress, InputSystemCursorShape.AppStarting)]
    [InlineData(CursorShape.Help, InputSystemCursorShape.Help)]
    [InlineData(CursorShape.Crosshair, InputSystemCursorShape.Cross)]
    [InlineData(CursorShape.NotAllowed, InputSystemCursorShape.UniversalNo)]
    [InlineData(CursorShape.Move, InputSystemCursorShape.SizeAll)]
    [InlineData(CursorShape.ResizeColumn, InputSystemCursorShape.SizeWestEast)]
    [InlineData(CursorShape.ResizeRow, InputSystemCursorShape.SizeNorthSouth)]
    [InlineData(CursorShape.ResizeDiagonalUp, InputSystemCursorShape.SizeNortheastSouthwest)]
    [InlineData(CursorShape.ResizeDiagonalDown, InputSystemCursorShape.SizeNorthwestSoutheast)]
    [InlineData(CursorShape.Grab, InputSystemCursorShape.Arrow)]
    public void EveryShapeHasASystemPointer(CursorShape shape, InputSystemCursorShape expected) =>
        Assert.Equal(expected, HtmlViewLogic.ToSystemCursor(shape));

    [Fact]
    public void NoShapeIsLeftWithoutAPointer()
    {
        foreach (var shape in Enum.GetValues<CursorShape>())
            _ = HtmlViewLogic.ToSystemCursor(shape);
    }

    [Fact]
    public void DevicesAddUpToWhatTheMediaFeaturesReport()
    {
        Assert.Equal(InputCapabilities.None, HtmlViewLogic.Capabilities(false, false, false));
        Assert.Equal(InputCapabilities.Mouse, HtmlViewLogic.Capabilities(true, false, false));
        Assert.Equal(InputCapabilities.Mouse, HtmlViewLogic.Capabilities(false, true, false));
        Assert.Equal(InputCapabilities.Touch, HtmlViewLogic.Capabilities(false, false, true));

        // A mouse and a touch screen: the mouse is primary, and every kind is available.
        var both = HtmlViewLogic.Capabilities(true, false, true);
        Assert.Equal(PointerTypes.Fine, both.PrimaryPointer);
        Assert.True(both.PrimaryCanHover);
        Assert.Equal(PointerTypes.Fine | PointerTypes.Coarse, both.AnyPointers);
    }

    [Fact]
    public void APositionIsMappedToThePageItIsOn()
    {
        double[] tops = [4, 120];
        double[] heights = [100, 50];

        Assert.Equal(0, HtmlViewLogic.PageAt(tops, heights, 4, out var local));
        Assert.Equal(0, local);
        Assert.Equal(0, HtmlViewLogic.PageAt(tops, heights, 103.9, out _));
        Assert.Equal(-1, HtmlViewLogic.PageAt(tops, heights, 110, out _)); // the gap between the pages
        Assert.Equal(1, HtmlViewLogic.PageAt(tops, heights, 130, out local));
        Assert.Equal(10, local);
        Assert.Equal(-1, HtmlViewLogic.PageAt(tops, heights, 2, out _));
        Assert.Equal(-1, HtmlViewLogic.PageAt(tops, heights, 170, out _));
    }

    [Theory]
    [InlineData("https://example.com/", true)]
    [InlineData("http://example.com/", true)]
    [InlineData("mailto:someone@example.com", true)]
    [InlineData("tel:+15555550100", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("relative/path.html", false)]
    public void OnlySafeSchemesAreLaunched(string uri, bool expected) =>
        Assert.Equal(expected, HtmlViewLogic.IsLaunchable(uri));
}

using Avalonia.Input;
using PeachBrackets.Avalonia.Logic;
using PeachDrawing.Core;

namespace PeachBrackets.Avalonia.Tests;

public class ViewLogicTests
{
    [Theory]
    [InlineData(CursorShape.Default, StandardCursorType.Arrow)]
    [InlineData(CursorShape.None, StandardCursorType.None)]
    [InlineData(CursorShape.ContextMenu, StandardCursorType.Arrow)]
    [InlineData(CursorShape.Help, StandardCursorType.Help)]
    [InlineData(CursorShape.Pointer, StandardCursorType.Hand)]
    [InlineData(CursorShape.Progress, StandardCursorType.AppStarting)]
    [InlineData(CursorShape.Wait, StandardCursorType.Wait)]
    [InlineData(CursorShape.Cell, StandardCursorType.Cross)]
    [InlineData(CursorShape.Crosshair, StandardCursorType.Cross)]
    [InlineData(CursorShape.Text, StandardCursorType.Ibeam)]
    [InlineData(CursorShape.VerticalText, StandardCursorType.Ibeam)]
    [InlineData(CursorShape.Alias, StandardCursorType.DragLink)]
    [InlineData(CursorShape.Copy, StandardCursorType.DragCopy)]
    [InlineData(CursorShape.NoDrop, StandardCursorType.No)]
    [InlineData(CursorShape.NotAllowed, StandardCursorType.No)]
    [InlineData(CursorShape.Move, StandardCursorType.SizeAll)]
    [InlineData(CursorShape.ScrollAll, StandardCursorType.SizeAll)]
    [InlineData(CursorShape.Grab, StandardCursorType.SizeAll)]
    [InlineData(CursorShape.Grabbing, StandardCursorType.SizeAll)]
    [InlineData(CursorShape.ResizeEast, StandardCursorType.RightSide)]
    [InlineData(CursorShape.ResizeWest, StandardCursorType.LeftSide)]
    [InlineData(CursorShape.ResizeNorth, StandardCursorType.TopSide)]
    [InlineData(CursorShape.ResizeSouth, StandardCursorType.BottomSide)]
    [InlineData(CursorShape.ResizeNorthEast, StandardCursorType.TopRightCorner)]
    [InlineData(CursorShape.ResizeDiagonalUp, StandardCursorType.TopRightCorner)]
    [InlineData(CursorShape.ResizeSouthWest, StandardCursorType.BottomLeftCorner)]
    [InlineData(CursorShape.ResizeNorthWest, StandardCursorType.TopLeftCorner)]
    [InlineData(CursorShape.ResizeDiagonalDown, StandardCursorType.TopLeftCorner)]
    [InlineData(CursorShape.ResizeSouthEast, StandardCursorType.BottomRightCorner)]
    [InlineData(CursorShape.ResizeHorizontal, StandardCursorType.SizeWestEast)]
    [InlineData(CursorShape.ResizeColumn, StandardCursorType.SizeWestEast)]
    [InlineData(CursorShape.ResizeVertical, StandardCursorType.SizeNorthSouth)]
    [InlineData(CursorShape.ResizeRow, StandardCursorType.SizeNorthSouth)]
    [InlineData(CursorShape.ZoomIn, StandardCursorType.Arrow)]
    [InlineData(CursorShape.ZoomOut, StandardCursorType.Arrow)]
    public void EveryShapeHasTheNearestStandardCursor(CursorShape shape, StandardCursorType expected) =>
        Assert.Equal(expected, AvaloniaCursors.ToType(shape));

    [Fact]
    public void NoShapeIsLeftOutOfTheCursorMap()
    {
        foreach (var shape in Enum.GetValues<CursorShape>())
            Assert.True(Enum.IsDefined(AvaloniaCursors.ToType(shape)), shape.ToString());
    }

    [Fact]
    public void AMouseOrPenIsFineAndHoversAndATouchScreenAloneIsCoarseAndCannot()
    {
        var mouse = AvaloniaInput.Capabilities(true, false, false);
        Assert.Equal(PointerTypes.Fine, mouse.PrimaryPointer);
        Assert.True(mouse.PrimaryCanHover);
        Assert.Equal(PointerTypes.Fine, mouse.AnyPointers);
        Assert.True(mouse.AnyCanHover);

        var pen = AvaloniaInput.Capabilities(false, true, false);
        Assert.Equal(PointerTypes.Fine, pen.PrimaryPointer);

        var both = AvaloniaInput.Capabilities(true, false, true);
        Assert.Equal(PointerTypes.Fine, both.PrimaryPointer);
        Assert.Equal(PointerTypes.Fine | PointerTypes.Coarse, both.AnyPointers);

        var touch = AvaloniaInput.Capabilities(false, false, true);
        Assert.Equal(PointerTypes.Coarse, touch.PrimaryPointer);
        Assert.False(touch.PrimaryCanHover);
        Assert.False(touch.AnyCanHover);

        Assert.Same(InputCapabilities.None, AvaloniaInput.Capabilities(false, false, false));
    }
}

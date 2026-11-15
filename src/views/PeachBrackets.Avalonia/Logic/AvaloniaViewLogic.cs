using Avalonia.Input;
using PeachDrawing.Core;

namespace PeachBrackets.Avalonia.Logic;

/// <summary>Which of Avalonia's standard cursors a CSS <c>cursor</c> shape is shown as; a shape with no cursor of its own gets the nearest one.</summary>
internal static class AvaloniaCursors
{
    /// <summary>The cursor for a shape.</summary>
    /// <param name="shape">the shape the document asks for</param>
    public static StandardCursorType ToType(CursorShape shape) => shape switch
    {
        CursorShape.None => StandardCursorType.None,
        CursorShape.ContextMenu => StandardCursorType.Arrow,
        CursorShape.Help => StandardCursorType.Help,
        CursorShape.Pointer => StandardCursorType.Hand,
        CursorShape.Progress => StandardCursorType.AppStarting,
        CursorShape.Wait => StandardCursorType.Wait,
        CursorShape.Cell or CursorShape.Crosshair => StandardCursorType.Cross,
        CursorShape.Text or CursorShape.VerticalText => StandardCursorType.Ibeam,
        CursorShape.Alias => StandardCursorType.DragLink,
        CursorShape.Copy => StandardCursorType.DragCopy,
        CursorShape.NoDrop or CursorShape.NotAllowed => StandardCursorType.No,
        CursorShape.Move or CursorShape.ScrollAll or CursorShape.Grab or CursorShape.Grabbing => StandardCursorType.SizeAll,
        CursorShape.ResizeEast => StandardCursorType.RightSide,
        CursorShape.ResizeWest => StandardCursorType.LeftSide,
        CursorShape.ResizeNorth => StandardCursorType.TopSide,
        CursorShape.ResizeSouth => StandardCursorType.BottomSide,
        CursorShape.ResizeNorthEast or CursorShape.ResizeDiagonalUp => StandardCursorType.TopRightCorner,
        CursorShape.ResizeSouthWest => StandardCursorType.BottomLeftCorner,
        CursorShape.ResizeNorthWest or CursorShape.ResizeDiagonalDown => StandardCursorType.TopLeftCorner,
        CursorShape.ResizeSouthEast => StandardCursorType.BottomRightCorner,
        CursorShape.ResizeHorizontal or CursorShape.ResizeColumn => StandardCursorType.SizeWestEast,
        CursorShape.ResizeVertical or CursorShape.ResizeRow => StandardCursorType.SizeNorthSouth,
        _ => StandardCursorType.Arrow,
    };
}

/// <summary>What the pointing devices attached to the machine amount to for the <c>hover</c> and <c>pointer</c> media features.</summary>
internal static class AvaloniaInput
{
    /// <summary>A mouse or pen is accurate and can hover; a touch screen is coarse and cannot; the first of those present is primary.</summary>
    /// <param name="mouse">whether a mouse or trackpad is attached</param>
    /// <param name="pen">whether a pen is attached</param>
    /// <param name="touch">whether a touch screen is attached</param>
    public static InputCapabilities Capabilities(bool mouse, bool pen, bool touch)
    {
        var fine = mouse || pen;
        var any = (fine ? PointerTypes.Fine : PointerTypes.None) | (touch ? PointerTypes.Coarse : PointerTypes.None);
        if (fine)
            return new InputCapabilities(PointerTypes.Fine, true, any, true);

        return touch ? new InputCapabilities(PointerTypes.Coarse, false, any, false) : InputCapabilities.None;
    }
}

using PeachDrawing.Core;
using System;

namespace PeachBrackets.Apple.Logic;

// The parts of the Apple view that need no AppKit or UIKit: they are compiled into the test project as well, so they run on any machine.

/// <summary>The pointer a <see cref="CursorShape"/> is shown as. AppKit's cursors and UIKit's pointer shapes are both chosen from this set.</summary>
internal enum AppleCursorKind
{
    /// <summary><c>cursor: none</c>: no pointer at all.</summary>
    Hidden,
    Arrow,
    IBeam,
    IBeamVertical,
    Crosshair,
    PointingHand,
    OpenHand,
    ClosedHand,
    ResizeLeftRight,
    ResizeUpDown,
    ContextualMenu,
    NotAllowed,
    DragCopy,
    DragLink,
}

/// <summary>Which pointer a CSS <c>cursor</c> becomes on Apple platforms (which have no public wait, help, zoom or diagonal-resize pointers).</summary>
internal static class AppleCursors
{
    /// <summary>The pointer for a shape; a shape with no pointer of its own gets the nearest public one.</summary>
    public static AppleCursorKind ToKind(CursorShape shape) => shape switch
    {
        CursorShape.None => AppleCursorKind.Hidden,
        CursorShape.ContextMenu => AppleCursorKind.ContextualMenu,
        CursorShape.Pointer => AppleCursorKind.PointingHand,
        CursorShape.Text => AppleCursorKind.IBeam,
        CursorShape.VerticalText => AppleCursorKind.IBeamVertical,
        CursorShape.Cell or CursorShape.Crosshair => AppleCursorKind.Crosshair,
        CursorShape.Alias => AppleCursorKind.DragLink,
        CursorShape.Copy => AppleCursorKind.DragCopy,
        CursorShape.NoDrop or CursorShape.NotAllowed => AppleCursorKind.NotAllowed,
        CursorShape.Move or CursorShape.ScrollAll or CursorShape.Grab => AppleCursorKind.OpenHand,
        CursorShape.Grabbing => AppleCursorKind.ClosedHand,
        CursorShape.ResizeHorizontal or CursorShape.ResizeEast or CursorShape.ResizeWest or CursorShape.ResizeColumn
            or CursorShape.ResizeDiagonalUp or CursorShape.ResizeNorthEast or CursorShape.ResizeSouthWest
            or CursorShape.ResizeDiagonalDown or CursorShape.ResizeNorthWest or CursorShape.ResizeSouthEast => AppleCursorKind.ResizeLeftRight,
        CursorShape.ResizeVertical or CursorShape.ResizeNorth or CursorShape.ResizeSouth or CursorShape.ResizeRow => AppleCursorKind.ResizeUpDown,
        _ => AppleCursorKind.Arrow,
    };
}

/// <summary>What the pointing devices attached to the device amount to for the <c>hover</c> and <c>pointer</c> media features.</summary>
internal static class AppleInput
{
    /// <summary>A mouse, trackpad or Pencil is accurate and can hover; a touch screen is coarse and cannot; the first of those present is primary.</summary>
    public static InputCapabilities Capabilities(bool mouse, bool stylus, bool touch, bool touchUsedLast = false)
    {
        var fine = mouse || stylus;
        var any = (fine ? PointerTypes.Fine : PointerTypes.None) | (touch ? PointerTypes.Coarse : PointerTypes.None);
        // A Mac with a touch display has both: the pointer the person used last is the primary one, so (pointer: ...) and (hover: ...) stay truthful.
        if (fine && !(touch && touchUsedLast))
            return new InputCapabilities(PointerTypes.Fine, true, any, true);

        return touch ? new InputCapabilities(PointerTypes.Coarse, false, any, fine) : InputCapabilities.None;
    }
}

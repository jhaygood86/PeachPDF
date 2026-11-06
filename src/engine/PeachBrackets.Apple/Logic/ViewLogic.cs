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

/// <summary>Where the pages sit in the view's scrolled, zoomed content, and what a link may do.</summary>
internal static class PageLayout
{
    /// <summary>The page a vertical position (in points, from the top of the content) is on, with the position inside the page; -1 between pages.</summary>
    public static int PageAt(double[] tops, double[] heights, double y, out double localY)
    {
        for (var i = 0; i < tops.Length; i++)
        {
            if (y >= tops[i] && y < tops[i] + heights[i])
            {
                localY = y - tops[i];
                return i;
            }
        }

        localY = 0;
        return -1;
    }

    /// <summary>Whether a link may be handed to the system: only the schemes a document cannot abuse.</summary>
    public static bool IsLaunchable(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https" or "mailto" or "tel";
}

/// <summary>Scroll and zoom arithmetic, in pixels of the scaled content.</summary>
internal static class ViewportMath
{
    /// <summary>Keeps a scroll offset inside the content: 0 up to what is left over, or 0 when the content is smaller than the view.</summary>
    public static double ClampScroll(double scroll, double contentPx, double viewportPx) =>
        Math.Clamp(scroll, 0, Math.Max(0, contentPx - viewportPx));

    /// <summary>A zoom factor held inside its limits.</summary>
    public static double ClampZoom(double zoom, double min, double max) => Math.Clamp(zoom, min, max);

    /// <summary>
    /// The scroll offset after zooming from <paramref name="zoom"/> to <paramref name="newZoom"/> that keeps the content under
    /// <paramref name="focus"/> (a position in the view, in pixels) where it is.
    /// </summary>
    public static double ScrollAfterZoom(double scroll, double zoom, double newZoom, double focus) =>
        (((scroll + focus) / zoom) * newZoom) - focus;

    /// <summary>The gestures a touch is allowed after the document's own policy and the host's, which can only take away.</summary>
    public static TouchActions Allowed(TouchActions document, TouchActions host) => document & host;

    /// <summary>
    /// What is left of a scroll gesture's distance (positive when the finger moved left or up) once the pans
    /// <c>touch-action</c> allows are applied. A finger moving left is a <c>pan-left</c>, moving up a <c>pan-up</c>.
    /// </summary>
    public static (double Dx, double Dy) FilterPan(double dx, double dy, TouchActions allowed)
    {
        var x = dx > 0 ? ((allowed & TouchActions.PanLeft) != 0 ? dx : 0) : ((allowed & TouchActions.PanRight) != 0 ? dx : 0);
        var y = dy > 0 ? ((allowed & TouchActions.PanUp) != 0 ? dy : 0) : ((allowed & TouchActions.PanDown) != 0 ? dy : 0);
        return (x, y);
    }
}

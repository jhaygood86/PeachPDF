using PeachDrawing.Core;
using System;

namespace PeachBrackets.Android.Logic;

// The parts of the Android view that need no Android: they are compiled into the test project as well, so they run without an emulator.

/// <summary>The system pointer icons (<c>android.view.PointerIcon.TYPE_*</c>) a <see cref="CursorShape"/> is shown as.</summary>
internal static class AndroidCursors
{
    public const int Null = 0;
    public const int Arrow = 1000;
    public const int Context = 1001;
    public const int Hand = 1002;
    public const int Help = 1003;
    public const int Wait = 1004;
    public const int Cell = 1006;
    public const int Crosshair = 1007;
    public const int Text = 1008;
    public const int VerticalText = 1009;
    public const int Alias = 1010;
    public const int Copy = 1011;
    public const int NoDrop = 1012;
    public const int AllScroll = 1013;
    public const int HorizontalDoubleArrow = 1014;
    public const int VerticalDoubleArrow = 1015;
    public const int TopRightDiagonalDoubleArrow = 1016;
    public const int TopLeftDiagonalDoubleArrow = 1017;
    public const int ZoomIn = 1018;
    public const int ZoomOut = 1019;
    public const int Grab = 1020;
    public const int Grabbing = 1021;

    /// <summary>The icon type for a shape; a shape with no icon of its own gets the nearest one. <c>cursor: none</c> is no icon.</summary>
    public static int ToType(CursorShape shape) => shape switch
    {
        CursorShape.None => Null,
        CursorShape.ContextMenu => Context,
        CursorShape.Pointer => Hand,
        CursorShape.Help => Help,
        CursorShape.Wait or CursorShape.Progress => Wait,
        CursorShape.Cell => Cell,
        CursorShape.Crosshair => Crosshair,
        CursorShape.Text => Text,
        CursorShape.VerticalText => VerticalText,
        CursorShape.Alias => Alias,
        CursorShape.Copy => Copy,
        CursorShape.NoDrop or CursorShape.NotAllowed => NoDrop,
        CursorShape.Move or CursorShape.ScrollAll => AllScroll,
        CursorShape.ResizeHorizontal or CursorShape.ResizeEast or CursorShape.ResizeWest or CursorShape.ResizeColumn => HorizontalDoubleArrow,
        CursorShape.ResizeVertical or CursorShape.ResizeNorth or CursorShape.ResizeSouth or CursorShape.ResizeRow => VerticalDoubleArrow,
        CursorShape.ResizeDiagonalUp or CursorShape.ResizeNorthEast or CursorShape.ResizeSouthWest => TopRightDiagonalDoubleArrow,
        CursorShape.ResizeDiagonalDown or CursorShape.ResizeNorthWest or CursorShape.ResizeSouthEast => TopLeftDiagonalDoubleArrow,
        CursorShape.ZoomIn => ZoomIn,
        CursorShape.ZoomOut => ZoomOut,
        CursorShape.Grab => Grab,
        CursorShape.Grabbing => Grabbing,
        _ => Arrow,
    };
}

/// <summary>What the pointing devices attached to the device amount to for the <c>hover</c> and <c>pointer</c> media features.</summary>
internal static class AndroidInput
{
    /// <summary>A mouse, trackpad or stylus is accurate and can hover; a touch screen is coarse and cannot; the first of those present is primary.</summary>
    public static InputCapabilities Capabilities(bool mouse, bool stylus, bool touch)
    {
        var fine = mouse || stylus;
        var any = (fine ? PointerTypes.Fine : PointerTypes.None) | (touch ? PointerTypes.Coarse : PointerTypes.None);
        if (fine)
            return new InputCapabilities(PointerTypes.Fine, true, any, true);

        return touch ? new InputCapabilities(PointerTypes.Coarse, false, any, false) : InputCapabilities.None;
    }
}

/// <summary>Where the pages sit in the view's scrolled, zoomed content, and what a link may do.</summary>
internal static class PageLayout
{
    /// <summary>The page a vertical position (in dp, from the top of the content) is on, with the position inside the page; -1 between pages.</summary>
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
    /// What is left of a scroll gesture's distance (as Android reports it: positive when the finger moved left or up) once the pans
    /// <c>touch-action</c> allows are applied. A finger moving left is a <c>pan-left</c>, moving up a <c>pan-up</c>.
    /// </summary>
    public static (double Dx, double Dy) FilterPan(double dx, double dy, TouchActions allowed)
    {
        var x = dx > 0 ? ((allowed & TouchActions.PanLeft) != 0 ? dx : 0) : ((allowed & TouchActions.PanRight) != 0 ? dx : 0);
        var y = dy > 0 ? ((allowed & TouchActions.PanUp) != 0 ? dy : 0) : ((allowed & TouchActions.PanDown) != 0 ? dy : 0);
        return (x, y);
    }
}

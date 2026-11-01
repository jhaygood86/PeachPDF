using Microsoft.UI.Input;
using PeachDrawing.Core;
using System;

namespace PeachBrackets.WinUI;

/// <summary>Raised when a link in an <see cref="HtmlView"/> is clicked or tapped.</summary>
public sealed class HtmlLinkClickedEventArgs : EventArgs
{
    internal HtmlLinkClickedEventArgs(string? uri, string? destination)
    {
        Uri = uri;
        Destination = destination;
    }

    /// <summary>The address the link leads to, or <see langword="null"/> for a link to a place in the document.</summary>
    public string? Uri { get; }

    /// <summary>The name of the place in the document the link leads to, or <see langword="null"/> for a link to an address.</summary>
    public string? Destination { get; }

    /// <summary>Set to <see langword="true"/> to stop the view following the link, for example because the application handles it.</summary>
    public bool Cancel { get; set; }
}

/// <summary>Raised when an <see cref="HtmlView"/> could not load or lay out its document.</summary>
public sealed class HtmlLoadFailedEventArgs : EventArgs
{
    internal HtmlLoadFailedEventArgs(Exception exception) => Exception = exception;

    /// <summary>What went wrong.</summary>
    public Exception Exception { get; }
}

/// <summary>The pieces of <see cref="HtmlView"/> that do not need a window, kept apart so they can be tested without one.</summary>
internal static class HtmlViewLogic
{
    /// <summary>The system pointer a <see cref="CursorShape"/> is shown as; the shapes WinUI has no pointer for fall back to the nearest one.</summary>
    public static InputSystemCursorShape ToSystemCursor(CursorShape shape) => shape switch
    {
        CursorShape.Pointer => InputSystemCursorShape.Hand,
        CursorShape.Text or CursorShape.VerticalText => InputSystemCursorShape.IBeam,
        CursorShape.Wait => InputSystemCursorShape.Wait,
        CursorShape.Help => InputSystemCursorShape.Help,
        CursorShape.Crosshair or CursorShape.Cell => InputSystemCursorShape.Cross,
        CursorShape.NotAllowed => InputSystemCursorShape.UniversalNo,
        CursorShape.Move or CursorShape.ScrollAll => InputSystemCursorShape.SizeAll,
        CursorShape.ResizeHorizontal or CursorShape.ResizeEast or CursorShape.ResizeWest or CursorShape.ResizeColumn => InputSystemCursorShape.SizeWestEast,
        CursorShape.ResizeVertical or CursorShape.ResizeNorth or CursorShape.ResizeSouth or CursorShape.ResizeRow => InputSystemCursorShape.SizeNorthSouth,
        CursorShape.ResizeDiagonalUp or CursorShape.ResizeNorthEast or CursorShape.ResizeSouthWest => InputSystemCursorShape.SizeNortheastSouthwest,
        CursorShape.ResizeDiagonalDown or CursorShape.ResizeNorthWest or CursorShape.ResizeSouthEast => InputSystemCursorShape.SizeNorthwestSoutheast,
        CursorShape.Progress => InputSystemCursorShape.AppStarting,
        _ => InputSystemCursorShape.Arrow,
    };

    /// <summary>The pointing devices a set of device kinds amounts to, as the <c>hover</c> and <c>pointer</c> media features report them.</summary>
    public static InputCapabilities Capabilities(bool mouse, bool pen, bool touch)
    {
        var any = (mouse || pen ? PointerTypes.Fine : PointerTypes.None) | (touch ? PointerTypes.Coarse : PointerTypes.None);
        var anyHover = mouse || pen;
        if (mouse || pen)
            return new InputCapabilities(PointerTypes.Fine, true, any, anyHover);

        return touch ? new InputCapabilities(PointerTypes.Coarse, false, any, anyHover) : InputCapabilities.None;
    }

    /// <summary>The page a vertical position falls on, with the position inside it.</summary>
    /// <param name="tops">the top of each page, in the view's units</param>
    /// <param name="heights">the height of each page, in the same units</param>
    /// <param name="y">the vertical position</param>
    /// <param name="localY">where the position is from the top of that page</param>
    /// <returns>the page index, or -1 when the position is between pages or outside them</returns>
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

    /// <summary>Whether a link may be opened by the system: only the schemes a document cannot abuse.</summary>
    public static bool IsLaunchable(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && parsed.Scheme is "http" or "https" or "mailto" or "tel";
}

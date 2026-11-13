using PeachDrawing.Core;
using System;

namespace PeachBrackets.HtmlViewCore;

/// <summary>Where the pages sit in the view's scrolled, zoomed content, and what a link may do.</summary>
internal static class PageLayout
{
    /// <summary>The page a vertical position (in pixels, from the top of the content) is on, with the position inside the page; -1 between pages.</summary>
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
    /// What is left of a scroll gesture's distance (as a touch screen reports it: positive when the finger moved left or up) once the pans
    /// <c>touch-action</c> allows are applied. A finger moving left is a <c>pan-left</c>, moving up a <c>pan-up</c>.
    /// </summary>
    public static (double Dx, double Dy) FilterPan(double dx, double dy, TouchActions allowed)
    {
        var x = dx > 0 ? ((allowed & TouchActions.PanLeft) != 0 ? dx : 0) : ((allowed & TouchActions.PanRight) != 0 ? dx : 0);
        var y = dy > 0 ? ((allowed & TouchActions.PanUp) != 0 ? dy : 0) : ((allowed & TouchActions.PanDown) != 0 ? dy : 0);
        return (x, y);
    }
}

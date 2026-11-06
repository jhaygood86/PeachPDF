using System;
using System.Collections.Generic;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Updates;
using PeachDrawing.Core;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    /// <summary>
    /// Raised when a scroll container of a document shown on a screen is scrolled by <see cref="SetScrollPosition"/>, <see cref="ScrollBy"/> or
    /// <see cref="ScrollIntoView"/>: the element, and where its content is now (CSSOM View's <c>scrollLeft</c> and <c>scrollTop</c>).
    /// </summary>
    public event EventHandler<ScrollPositionChangedEventArgs>? ScrollPositionChanged;

    /// <summary>
    /// Whether <paramref name="element"/> is a scroll container: on a document shown on a screen, a box whose <c>overflow</c> is <c>auto</c>, <c>scroll</c> or <c>hidden</c>
    /// (CSS Overflow 3 §2). Its content is clipped to its padding box and moves as it is scrolled. A paged document has no scroll containers, and the root
    /// and the body, whose overflow belongs to the viewport, are not one either (the host scrolls the surface itself).
    /// </summary>
    /// <param name="element">the element to ask about</param>
    public bool IsScrollContainer(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();
        return _container.View.IsScrollContainer(_container, element.Box);
    }

    /// <summary>Where a scroll container is scrolled to and how far it can go, or null when <paramref name="element"/> is not a scroll container (see <see cref="IsScrollContainer"/>).</summary>
    /// <param name="element">the scroll container</param>
    public ScrollMetrics? GetScrollMetrics(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        if (_container.View.ScrollStateOf(_container, element.Box) is not { } state)
            return null;

        var ppp = _unitsPerPoint;
        return new ScrollMetrics(state.X / ppp, state.Y / ppp, state.MaxX / ppp, state.MaxY / ppp,
            new Rect(state.Port.X / ppp, state.Port.Y / ppp, state.Port.Width / ppp, state.Port.Height / ppp), state.ContentWidth / ppp, state.ContentHeight / ppp);
    }

    /// <summary>
    /// Scrolls a scroll container to (<paramref name="x"/>, <paramref name="y"/>) points from the start of its content, kept within what it can scroll (CSSOM View
    /// <c>scrollTo</c>). Nothing is relaid out: the regions returned are the scrollport to paint again.
    /// </summary>
    /// <param name="element">the scroll container</param>
    /// <param name="x">how far to scroll to the left; <see cref="double.NaN"/> leaves that axis where it is</param>
    /// <param name="y">how far to scroll up; <see cref="double.NaN"/> leaves that axis where it is</param>
    /// <returns>the regions that look different, which is none when the element is not a scroll container or was already there</returns>
    public IReadOnlyList<DirtyRegion> SetScrollPosition(DocumentElement element, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        var ppp = _unitsPerPoint;
        if (_container.View.SetScroll(_container, element.Box, double.IsNaN(x) ? double.NaN : x * ppp, double.IsNaN(y) ? double.NaN : y * ppp) is not { } port)
            return [];

        return AfterScroll(element, port);
    }

    /// <summary>Scrolls a scroll container by (<paramref name="dx"/>, <paramref name="dy"/>) points (CSSOM View <c>scrollBy</c>); see <see cref="SetScrollPosition"/>.</summary>
    /// <param name="element">the scroll container</param>
    /// <param name="dx">how far to scroll to the left, in points</param>
    /// <param name="dy">how far to scroll up, in points</param>
    public IReadOnlyList<DirtyRegion> ScrollBy(DocumentElement element, double dx, double dy)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        var offset = _container.View.ScrollOffsetOf(element.Box);
        var ppp = _unitsPerPoint;
        return SetScrollPosition(element, (offset.X / ppp) + dx, (offset.Y / ppp) + dy);
    }

    /// <summary>
    /// The innermost scroll container at a point of a page, for a host that sends a wheel turn or a touch drag to what is under the pointer. When a direction is
    /// given (<paramref name="dx"/>, <paramref name="dy"/>: the way the content would move, positive scrolling toward the end), a container that cannot scroll that way is
    /// passed over for the next one out, as scroll chaining does; null when none can, and the host scrolls the surface itself.
    /// </summary>
    /// <param name="pageIndex">the page</param>
    /// <param name="x">the point's distance from the left of the content area, in points</param>
    /// <param name="y">the point's distance from the top of the content area, in points</param>
    /// <param name="dx">the horizontal direction the content is to scroll in, or 0 for either</param>
    /// <param name="dy">the vertical direction, or 0 for either</param>
    public DocumentElement? GetScrollContainerAt(int pageIndex, double x, double y, double dx = 0, double dy = 0)
    {
        ThrowIfDisposed();
        if (pageIndex != 0 || !_container.View.HasScrollContainers(_container))
            return null;

        var ppp = _unitsPerPoint;
        var found = _container.View.ScrollContainerAt(_container, x * ppp, y * ppp, dx, dy);
        return found is null ? null : new DocumentElement(found);
    }

    /// <summary>
    /// Scrolls every scroll container that holds <paramref name="element"/> as little as it takes to bring it into view (CSSOM View <c>scrollIntoView</c> with
    /// <c>nearest</c> alignment), outermost first. The element's <c>scroll-margin</c> is kept clear at the edges. Nothing is relaid out.
    /// </summary>
    /// <param name="element">the element to show</param>
    /// <returns>the regions that look different</returns>
    public IReadOnlyList<DirtyRegion> ScrollIntoView(DocumentElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        var regions = new List<DirtyRegion>();
        foreach (var (scroller, port) in _container.View.ScrollIntoView(_container, element.Box))
            regions.AddRange(AfterScroll(new DocumentElement(scroller), port));

        return regions;
    }

    /// <summary>
    /// The scrollbar under a point of a page, if one is: the container it belongs to, the axis, which part of it the point is on, and the track and thumb along the axis
    /// in points, so a host can start a drag on the thumb (<see cref="ScrollThumbTo"/>) or page the container by a press on the track (<see cref="ScrollByPage"/>).
    /// </summary>
    /// <param name="pageIndex">the page</param>
    /// <param name="x">the point's distance from the left of the content area, in points</param>
    /// <param name="y">the point's distance from the top of the content area, in points</param>
    public ScrollbarHit? GetScrollbarAt(int pageIndex, double x, double y)
    {
        ThrowIfDisposed();
        if (pageIndex != 0 || !_container.View.HasScrollContainers(_container))
            return null;

        var ppp = _unitsPerPoint;
        if (_container.View.ScrollbarAt(_container, x * ppp, y * ppp) is not { } target)
            return null;

        var vertical = target.Vertical;
        var part = target.Part switch { < 0 => ScrollbarPart.TrackBefore, > 0 => ScrollbarPart.TrackAfter, _ => ScrollbarPart.Thumb };
        return new ScrollbarHit(new DocumentElement(target.Box), vertical ? ScrollbarAxis.Vertical : ScrollbarAxis.Horizontal, part,
            (vertical ? target.Track.Y : target.Track.X) / ppp, (vertical ? target.Track.Height : target.Track.Width) / ppp,
            (vertical ? target.Thumb.Y : target.Thumb.X) / ppp, (vertical ? target.Thumb.Height : target.Thumb.Width) / ppp);
    }

    /// <summary>
    /// Scrolls a container so that its scrollbar thumb starts <paramref name="thumbStart"/> points from the top (or left) of the page: what dragging the thumb does. The
    /// thumb is kept on its track.
    /// </summary>
    /// <param name="element">the scroll container</param>
    /// <param name="axis">the scrollbar to drag</param>
    /// <param name="thumbStart">where the thumb's leading edge is to be, in points from the top or left of the content area</param>
    /// <returns>the regions to paint again</returns>
    public IReadOnlyList<DirtyRegion> ScrollThumbTo(DocumentElement element, ScrollbarAxis axis, double thumbStart)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();

        if (_container.View.ScrollStateOf(_container, element.Box) is not { } state)
            return [];

        var ppp = _unitsPerPoint;
        var (vertical, horizontal) = Html.ScrollbarGeometry.Of(element.Box, state, ppp);
        if ((axis == ScrollbarAxis.Vertical ? vertical : horizontal) is not { } bar)
            return [];

        // Where the container is drawn: it may itself be carried by a container around it.
        var (shiftX, shiftY) = _container.View.DisplacementOf(element.Box);
        var isVertical = axis == ScrollbarAxis.Vertical;
        var trackStart = (isVertical ? bar.Track.Y + shiftY : bar.Track.X + shiftX) / ppp;
        var travel = ((isVertical ? bar.Track.Height - bar.Thumb.Height : bar.Track.Width - bar.Thumb.Width) / ppp);
        if (!(travel > 0))
            return [];

        var fraction = Math.Clamp((thumbStart - trackStart) / travel, 0, 1);
        var max = (isVertical ? state.MaxY : state.MaxX) / ppp;
        return isVertical ? SetScrollPosition(element, double.NaN, fraction * max) : SetScrollPosition(element, fraction * max, double.NaN);
    }

    /// <summary>Scrolls a container by a page along one axis (seven eighths of its scrollport, as a press on the track of a scrollbar does).</summary>
    /// <param name="element">the scroll container</param>
    /// <param name="axis">the axis</param>
    /// <param name="direction">positive toward the end of the content, negative toward the start</param>
    /// <returns>the regions to paint again</returns>
    public IReadOnlyList<DirtyRegion> ScrollByPage(DocumentElement element, ScrollbarAxis axis, int direction)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (GetScrollMetrics(element) is not { } metrics)
            return [];

        var page = (axis == ScrollbarAxis.Vertical ? metrics.Viewport.Height : metrics.Viewport.Width) * 0.875 * Math.Sign(direction);
        return axis == ScrollbarAxis.Vertical ? ScrollBy(element, 0, page) : ScrollBy(element, page, 0);
    }

    /// <summary>
    /// The scroll container that holds <paramref name="element"/> (the element itself when it is one) and can scroll the way asked, nearest first: where a key goes when
    /// the focus is in <paramref name="element"/>. Null when none can, and the host scrolls the surface.
    /// </summary>
    /// <param name="element">the element that has the focus</param>
    /// <param name="dx">the horizontal direction the content is to scroll in, or 0</param>
    /// <param name="dy">the vertical direction, or 0</param>
    public DocumentElement? GetScrollContainerFor(DocumentElement element, double dx = 0, double dy = 0)
    {
        ArgumentNullException.ThrowIfNull(element);
        ThrowIfDisposed();
        return _container.View.ScrollContainerFor(_container, element.Box, dx, dy) is { } found ? new DocumentElement(found) : null;
    }

    /// <summary>The element of a node of the accessibility tree, or null when the node is not an element.</summary>
    /// <param name="node">the node</param>
    public DocumentElement? GetElementOf(PeachBrackets.Accessibility.AccessibilityNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ThrowIfDisposed();
        return node.Id.Length >= 2 && node.Id[0] == 'e'
            && int.TryParse(node.Id.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ordinal)
            && FindByOrdinal(_container.Root, ordinal) is { } box
                ? new DocumentElement(box)
                : null;
    }

    // Scrolling moved what a scroll container holds, so what was recorded for the old places (text runs, link rectangles) is stale.
    private IReadOnlyList<DirtyRegion> AfterScroll(DocumentElement element, Rect port)
    {
        _textRuns = null;
        _annotations = null;
        _container.AnimationClock?.NotifyViewportChanged();

        var ppp = _unitsPerPoint;
        const double bleed = 2;
        var region = new DirtyRegion(0, new Rect((port.X / ppp) - bleed, (port.Y / ppp) - bleed, (port.Width / ppp) + (2 * bleed), (port.Height / ppp) + (2 * bleed)));

        if (ScrollPositionChanged is { } handler && GetScrollMetrics(element) is { } metrics)
            handler(this, new ScrollPositionChangedEventArgs(element, metrics.X, metrics.Y));

        return [region];
    }
}

/// <summary>Where a scroll container is scrolled to and how far it can go, in points (CSS Overflow 3 §2).</summary>
/// <param name="X">how far the content is scrolled to the left (<c>scrollLeft</c>)</param>
/// <param name="Y">how far the content is scrolled up (<c>scrollTop</c>)</param>
/// <param name="MaxX">the most it can be scrolled to the left; zero when its content fits across</param>
/// <param name="MaxY">the most it can be scrolled up; zero when its content fits down</param>
/// <param name="Viewport">the scrollport (the container's padding box), from the top-left of the content area</param>
/// <param name="ContentWidth">the width of the scrollable overflow area (<c>scrollWidth</c>)</param>
/// <param name="ContentHeight">the height of the scrollable overflow area (<c>scrollHeight</c>)</param>
public readonly record struct ScrollMetrics(double X, double Y, double MaxX, double MaxY, Rect Viewport, double ContentWidth, double ContentHeight);

/// <summary>Which scrollbar of a scroll container.</summary>
public enum ScrollbarAxis
{
    /// <summary>The bar along the right edge, which scrolls the content up and down.</summary>
    Vertical,

    /// <summary>The bar along the bottom edge, which scrolls the content from side to side.</summary>
    Horizontal,
}

/// <summary>The part of a scrollbar a point is on.</summary>
public enum ScrollbarPart
{
    /// <summary>The thumb, which a drag moves.</summary>
    Thumb,

    /// <summary>The track before the thumb: a press scrolls a page toward the start.</summary>
    TrackBefore,

    /// <summary>The track after the thumb: a press scrolls a page toward the end.</summary>
    TrackAfter,
}

/// <summary>A scrollbar under a point (<see cref="HtmlDocument.GetScrollbarAt"/>); positions are along the bar's axis, in points from the top or left of the content area.</summary>
/// <param name="Element">the scroll container the bar belongs to</param>
/// <param name="Axis">which of its bars</param>
/// <param name="Part">the part the point is on</param>
/// <param name="TrackStart">where the track starts</param>
/// <param name="TrackLength">how long the track is</param>
/// <param name="ThumbStart">where the thumb starts</param>
/// <param name="ThumbLength">how long the thumb is</param>
public sealed record ScrollbarHit(DocumentElement Element, ScrollbarAxis Axis, ScrollbarPart Part, double TrackStart, double TrackLength, double ThumbStart, double ThumbLength);

/// <summary>What <see cref="HtmlDocument.ScrollPositionChanged"/> reports.</summary>
public sealed class ScrollPositionChangedEventArgs : EventArgs
{
    internal ScrollPositionChangedEventArgs(DocumentElement element, double x, double y)
    {
        Element = element;
        X = x;
        Y = y;
    }

    /// <summary>The scroll container that was scrolled.</summary>
    public DocumentElement Element { get; }

    /// <summary>How far its content is scrolled to the left now, in points.</summary>
    public double X { get; }

    /// <summary>How far its content is scrolled up now, in points.</summary>
    public double Y { get; }
}

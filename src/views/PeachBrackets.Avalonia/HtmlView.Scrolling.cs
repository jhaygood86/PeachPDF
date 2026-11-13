using Avalonia.Threading;
using PeachBrackets.Avalonia.Logic;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

// Scroll containers (overflow: auto/scroll boxes, text areas), the scroll bars the document paints, and the wheel and keys that move them.
public sealed partial class HtmlView
{
    private const double KeyArrowPoints = 40;
    private const double PageFraction = 0.875;

    // A scroll bar thumb being dragged: the container, the bar, and how far from the thumb's start the pointer took hold of it (in points).
    private (DocumentElement Element, ScrollbarAxis Axis, double Grab)? _scrollbarDrag;

    // A press held on a scroll bar track keeps paging toward the pointer.
    private DispatcherTimer? _repeatTimer;
    private (DocumentElement Element, ScrollbarAxis Axis, int Direction, int Page, double X, double Y)? _trackRepeat;

    /// <summary>Repaints what a scroll of a container changed, and wakes the animations that follow its scroll. True when anything changed.</summary>
    private bool ApplyScroll(IReadOnlyList<DirtyRegion> regions)
    {
        if (regions.Count == 0)
            return false;

        InvalidateRegions(regions);
        EnsureAnimationLoop();
        return true;
    }

    /// <summary>How far the page is scrolled, in pixels. For tests.</summary>
    internal (double X, double Y) ScrollPixels => (_scrollX, _scrollY);

    /// <summary>Whether a scroll bar thumb is being dragged. For tests.</summary>
    internal bool IsDraggingScrollbar => _scrollbarDrag is not null;

    // A press on a scroll bar of a container: the thumb is taken hold of, the track scrolls a page toward the press. True when the press was on a bar.
    private bool BeginScrollbarDrag(int page, double x, double y)
    {
        if (_document is not { } document || document.GetScrollbarAt(page, x, y) is not { } bar)
            return false;

        if (bar.Part == ScrollbarPart.Thumb)
        {
            _scrollbarDrag = (bar.Element, bar.Axis, (bar.Axis == ScrollbarAxis.Vertical ? y : x) - bar.ThumbStart);
            return true;
        }

        var direction = bar.Part == ScrollbarPart.TrackAfter ? 1 : -1;
        ApplyScroll(document.ScrollByPage(bar.Element, bar.Axis, direction));
        BeginTrackRepeat(bar.Element, bar.Axis, direction, page, x, y);
        return true;
    }

    // The pointer moved with a thumb held (or a track held, which then pages toward the new place): true when a scroll bar owns the press.
    private bool DragScrollbar(int page, double x, double y)
    {
        if (_trackRepeat is { } repeat)
        {
            _trackRepeat = repeat with { Page = page, X = x, Y = y };
            return true;
        }

        if (_scrollbarDrag is not { } drag || _document is not { } document)
            return false;

        ApplyScroll(document.ScrollThumbTo(drag.Element, drag.Axis, (drag.Axis == ScrollbarAxis.Vertical ? y : x) - drag.Grab));
        return true;
    }

    private void EndScrollbarDrag()
    {
        EndTrackRepeat();
        if (_scrollbarDrag is { } drag && _document is { } document)
            ApplyScroll(document.SettleScroll(drag.Element));

        _scrollbarDrag = null;
    }

    // After a pause, a press held on the track pages again at a steady rate, until the thumb reaches the pointer or the button is let go.
    private void BeginTrackRepeat(DocumentElement element, ScrollbarAxis axis, int direction, int page, double x, double y)
    {
        EndTrackRepeat();
        _trackRepeat = (element, axis, direction, page, x, y);
        _repeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(TouchMath.TrackRepeatDelay) };
        _repeatTimer.Tick += OnTrackRepeatTick;
        _repeatTimer.Start();
    }

    private void OnTrackRepeatTick(object? sender, EventArgs e)
    {
        if (_trackRepeat is not { } repeat || _document is not { } document || _repeatTimer is null)
        {
            EndTrackRepeat();
            return;
        }

        // Still on the same side of the thumb: the track ahead of it in the direction asked.
        var bar = document.GetScrollbarAt(repeat.Page, repeat.X, repeat.Y);
        var expected = repeat.Direction > 0 ? ScrollbarPart.TrackAfter : ScrollbarPart.TrackBefore;
        if (bar is null || bar.Part != expected || !ApplyScroll(document.ScrollByPage(repeat.Element, repeat.Axis, repeat.Direction)))
        {
            EndTrackRepeat();
            return;
        }

        _repeatTimer.Interval = TimeSpan.FromSeconds(TouchMath.TrackRepeatInterval);
    }

    private void EndTrackRepeat()
    {
        _trackRepeat = null;
        if (_repeatTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnTrackRepeatTick;
        }

        _repeatTimer = null;
    }

    // The wheel over a scroll container: true when a container (or a text area) took it (otherwise the page scrolls). Distances are in points.
    private bool WheelScrollContainer(int page, double x, double y, double dxPoints, double dyPoints)
    {
        if (_document is not { } document)
            return false;

        if (document.GetScrollContainerAt(page, x, y, dxPoints, dyPoints) is { } container)
        {
            // A container that takes the wheel keeps it even when it is already at its end and nothing moves (overscroll-behavior: contain).
            ApplyScroll(document.ScrollBy(container, dxPoints, dyPoints));
            return true;
        }

        return dyPoints != 0 && ScrollTextAreaAt(page, x, y, dyPoints);
    }

    // Scrolls the text area under a point by an amount in points; false when the point is not over one or it cannot move that way.
    private bool ScrollTextAreaAt(int page, double x, double y, double points)
    {
        if (_document is not { } document || _context is not { } context || document.GetElementAt(page, x, y) is not { } element)
            return false;

        using var measuring = new MeasuringCanvas(context);
        var result = document.ScrollTextArea(element, points, measuring.Canvas);
        if (result.Applied && result.DirtyRegions.Count > 0)
            InvalidateRegions(result.DirtyRegions);

        return result.Applied;
    }

    // An arrow or page key while a scroll container holds the focus: true when it scrolled. The core passes pixels: an arrow is 40 DIPs, a page
    // is most of the view; here an arrow is 40 points and a page is 0.875 of the container's own height.
    internal bool ScrollFocusedContainer(double dx, double dy)
    {
        if (_document is not { } document || document.FocusedNode is not { } node || document.GetElementOf(node) is not { } focused)
            return false;

        if (document.GetScrollContainerFor(focused, Math.Sign(dx), Math.Sign(dy)) is not { } container || document.GetScrollMetrics(container) is not { } metrics)
            return false;

        var arrow = (KeyArrowPoints * PixelsPerPoint * _dpi) + 0.5;
        double Step(double delta, double port) => delta == 0 ? 0 : Math.Sign(delta) * (Math.Abs(delta) <= arrow ? KeyArrowPoints : port * PageFraction);
        return ApplyScroll(document.ScrollBy(container, Step(dx, metrics.Viewport.Width), Step(dy, metrics.Viewport.Height)));
    }

    /// <summary>Scrolls a scroll container of the document (an <c>overflow</c> box) to a position, in points. False when the element is not one.</summary>
    internal bool SetContainerScroll(string elementId, double x, double y) =>
        _document is { } document && document.GetElement(elementId) is { } element && document.GetScrollMetrics(element) is not null
        && ApplyScroll(document.SetScrollPosition(element, x, y));

    /// <summary>Where a scroll container of the document is scrolled to. For tests and hosts that mirror the position.</summary>
    internal ScrollMetrics? GetContainerScroll(string elementId) =>
        _document is { } document && document.GetElement(elementId) is { } element ? document.GetScrollMetrics(element) : null;

    // The focus moved to something that may be inside a scroll container: the containers around it scroll to show it. True when one did.
    private bool ScrollContainersTo(DocumentElement element) => _document is { } document && ApplyScroll(document.ScrollIntoView(element));
}

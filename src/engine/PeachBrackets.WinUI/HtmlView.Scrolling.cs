using PeachBrackets.Accessibility;
using PeachBrackets.Updates;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.System;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    // The scroll containers of the document, driven by the input a scroll container takes: the wheel (HtmlView.Input.cs), a scrollbar pressed or dragged, the keyboard when the
    // focus is inside one, and a finger dragged over one.

    // A scrollbar thumb being dragged: the container, the bar, and how far from the thumb's start the pointer took hold of it (in points).
    private (DocumentElement Element, ScrollbarAxis Axis, double Grab)? _scrollbarDrag;

    // A finger that went down over a scroll container: where it is now (canvas pixels), and where it went down, which says which containers it can reach.
    private Point? _touchLast;
    private (int Page, double X, double Y)? _touchStart;
    private bool _touchScrolling;
    private DocumentElement? _touchContainer;

    // The speed of the finger (points per second) and when it last moved: a lifted finger carries the container on at this speed (a fling).
    private double _touchVx;
    private double _touchVy;
    private long _touchTicks;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _flingTimer;
    private DocumentElement? _flingContainer;
    private long _flingTicks;

    // A press held on a scrollbar track keeps paging toward the pointer.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _repeatTimer;
    private (DocumentElement Element, ScrollbarAxis Axis, int Direction, Point Position)? _trackRepeat;

    private const double KeyArrowPoints = 40;
    private const double PageFraction = 0.875;

    /// <summary>Repaints what a scroll of a container changed, and wakes the animations that follow its scroll.</summary>
    private bool ApplyScroll(IReadOnlyList<DirtyRegion> regions)
    {
        foreach (var region in regions)
            InvalidateRegion(region.PageIndex, region.Bounds);

        if (regions.Count > 0)
        {
            // An animation on the container's scroll timeline moves with it, and what is under the pointer is not what was.
            EnsureAnimationLoop();
        }

        return regions.Count > 0;
    }

    // A press on a scrollbar of a container: the thumb is taken hold of, the track scrolls a page toward the press. True when the press was on a bar.
    internal bool PressScrollbarAt(int page, double x, double y)
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

    // After a pause, a press held on the track pages again at a steady rate, until the thumb reaches the pointer or the button is let go.
    private void BeginTrackRepeat(DocumentElement element, ScrollbarAxis axis, int direction, int page, double x, double y)
    {
        EndTrackRepeat();
        _trackRepeat = (element, axis, direction, new Point(x, y));
        _repeatTimer = DispatcherQueue.CreateTimer();
        _repeatTimer.Interval = TimeSpan.FromSeconds(HtmlViewLogic.TrackRepeatDelay);
        _repeatTimer.IsRepeating = false;
        _repeatTimer.Tick += (_, _) =>
        {
            if (_trackRepeat is not { } repeat || _document is not { } document)
            {
                EndTrackRepeat();
                return;
            }

            // Still on the same side of the thumb: the track ahead of it in the direction asked.
            var bar = document.GetScrollbarAt(page, repeat.Position.X, repeat.Position.Y);
            var expected = repeat.Direction > 0 ? ScrollbarPart.TrackAfter : ScrollbarPart.TrackBefore;
            if (bar is null || bar.Part != expected || !ApplyScroll(document.ScrollByPage(repeat.Element, repeat.Axis, repeat.Direction)))
            {
                EndTrackRepeat();
                return;
            }

            _repeatTimer!.Interval = TimeSpan.FromSeconds(HtmlViewLogic.TrackRepeatInterval);
            _repeatTimer.Start();
        };
        _repeatTimer.Start();
    }

    private void EndTrackRepeat()
    {
        _trackRepeat = null;
        _repeatTimer?.Stop();
        _repeatTimer = null;
    }

    // The pointer moved while a track is held: the repeat follows it.
    private void MoveTrackRepeat(double x, double y)
    {
        if (_trackRepeat is { } repeat)
            _trackRepeat = repeat with { Position = new Point(x, y) };
    }

    // The pointer moved with a thumb held: the container scrolls so the thumb stays under it.
    internal bool DragScrollbar(double x, double y)
    {
        if (_scrollbarDrag is not { } drag || _document is not { } document)
            return false;

        ApplyScroll(document.ScrollThumbTo(drag.Element, drag.Axis, (drag.Axis == ScrollbarAxis.Vertical ? y : x) - drag.Grab));
        return true;
    }

    // The keyboard when the focus is inside a scroll container: the arrows, Page Up and Down, Home, End and Space scroll the nearest container around the focused element that can
    // go that way; anywhere else (no focus, or every container around it already at that end) the key is left to the page.
    internal bool HandleScrollKey(VirtualKey key)
    {
        if (_document is not { } document || document.FocusedNode is not { } node || document.GetElementOf(node) is not { } focused)
            return false;

        var shift = IsDown(VirtualKey.Shift);
        double dx = 0, dy = 0;
        var toStart = false;
        var toEnd = false;
        switch (key)
        {
            case VirtualKey.Down: dy = 1; break;
            case VirtualKey.Up: dy = -1; break;
            case VirtualKey.Right: dx = 1; break;
            case VirtualKey.Left: dx = -1; break;
            case VirtualKey.PageDown: dy = 1; break;
            case VirtualKey.PageUp: dy = -1; break;
            case VirtualKey.Space: dy = shift ? -1 : 1; break;
            case VirtualKey.Home: dy = -1; toStart = true; break;
            case VirtualKey.End: dy = 1; toEnd = true; break;
            default: return false;
        }

        if (document.GetScrollContainerFor(focused, dx, dy) is not { } container || document.GetScrollMetrics(container) is not { } metrics)
            return false;

        var page = key is VirtualKey.PageDown or VirtualKey.PageUp or VirtualKey.Space;
        if (toStart || toEnd)
            return ApplyScroll(document.SetScrollPosition(container, double.NaN, toStart ? metrics.MinY : metrics.MaxY));

        var step = page ? metrics.Viewport.Height * PageFraction : KeyArrowPoints;
        return ApplyScroll(document.ScrollBy(container, dx * step, dy * step));
    }

    // The focus moved to something that may be inside a scroll container: the containers around it scroll to show it. True when one did.
    private bool ScrollContainersTo(HtmlDocument document, string nodeId)
    {
        if (document.GetAccessibilityTree().FindById(nodeId) is not { } node || document.GetElementOf(node) is not { } element)
            return false;

        return ApplyScroll(document.ScrollIntoView(element));
    }

    /// <summary>Where the element of an accessibility node is scrolled to, when it is a scroll container of the document.</summary>
    internal PeachBrackets.ScrollMetrics? ScrollMetricsOf(AccessibilityNode node) =>
        _document is { } document && document.GetElementOf(node) is { } element ? document.GetScrollMetrics(element) : null;

    // UI Automation scrolls a container by a small step (the arrow keys') or a large one (a page).
    internal void ScrollFromAutomation(AccessibilityNode node, Microsoft.UI.Xaml.Automation.ScrollAmount horizontal, Microsoft.UI.Xaml.Automation.ScrollAmount vertical)
    {
        if (_document is not { } document || document.GetElementOf(node) is not { } element || document.GetScrollMetrics(element) is not { } metrics)
            return;

        double Step(Microsoft.UI.Xaml.Automation.ScrollAmount amount, double port) => amount switch
        {
            Microsoft.UI.Xaml.Automation.ScrollAmount.LargeIncrement => port * PageFraction,
            Microsoft.UI.Xaml.Automation.ScrollAmount.LargeDecrement => -port * PageFraction,
            Microsoft.UI.Xaml.Automation.ScrollAmount.SmallIncrement => KeyArrowPoints,
            Microsoft.UI.Xaml.Automation.ScrollAmount.SmallDecrement => -KeyArrowPoints,
            _ => 0,
        };

        ApplyScroll(document.ScrollBy(element, Step(horizontal, metrics.Viewport.Width), Step(vertical, metrics.Viewport.Height)));
    }

    internal void ScrollToPercentFromAutomation(AccessibilityNode node, double horizontalPercent, double verticalPercent)
    {
        if (_document is not { } document || document.GetElementOf(node) is not { } element || document.GetScrollMetrics(element) is not { } metrics)
            return;

        // NoScroll (-1) leaves an axis where it is.
        var x = horizontalPercent < 0 ? double.NaN : metrics.MinX + ((metrics.MaxX - metrics.MinX) * Math.Clamp(horizontalPercent, 0, 100) / 100);
        var y = verticalPercent < 0 ? double.NaN : metrics.MinY + ((metrics.MaxY - metrics.MinY) * Math.Clamp(verticalPercent, 0, 100) / 100);
        ApplyScroll(document.SetScrollPosition(element, x, y));
    }

    // A finger went down: when it is over a scroll container that can scroll, the view's own scroller leaves the drag to the container (until it lets go).
    internal void BeginTouchScroll(int page, double x, double y, Point position)
    {
        _touchStart = null;
        _touchScrolling = false;
        EndFling(settle: false);
        if (_document is not { } document || _scrollMode == HtmlViewScrollMode.Disabled)
            return;

        // A container the finger can scroll in some direction.
        if (document.GetScrollContainerAt(page, x, y, 0, 1) is null && document.GetScrollContainerAt(page, x, y, 0, -1) is null
            && document.GetScrollContainerAt(page, x, y, 1, 0) is null && document.GetScrollContainerAt(page, x, y, -1, 0) is null)
        {
            return;
        }

        _touchStart = (page, x, y);
        _touchLast = position;
    }

    // The finger moved: the container under where it went down that can go the way the content is pulled scrolls by the distance moved, and the page does not.
    internal bool MoveTouchScroll(Point position)
    {
        if (_touchStart is not { } start || _touchLast is not { } last || _document is not { } document)
            return false;

        // The content follows the finger: pulling up scrolls toward the end.
        var dx = (last.X - position.X) / PixelsPerPoint;
        var dy = (last.Y - position.Y) / PixelsPerPoint;
        if (!_touchScrolling && Math.Abs(dx) < 3 && Math.Abs(dy) < 3)
            return false;

        var container = document.GetScrollContainerAt(start.Page, start.X, start.Y, dx, dy);
        _touchLast = position;
        if (container is null)
        {
            // At the end of what it can do, the gesture belongs to nothing: it does not hand over to the page half way (scroll chaining starts with a new gesture).
            return _touchScrolling;
        }

        if (!_touchScrolling)
        {
            _touchScrolling = true;
            _scroller.HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
            _scroller.VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
            _touchPolicyActive = true;
        }

        _touchContainer = container;
        // The finger's speed over this movement.
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = _touchTicks == 0 ? 0 : (now - _touchTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        _touchVx = HtmlViewLogic.SmoothVelocity(_touchVx, dx, seconds);
        _touchVy = HtmlViewLogic.SmoothVelocity(_touchVy, dy, seconds);
        _touchTicks = now;

        ApplyScroll(document.DragScrollBy(container, dx, dy));
        return true;
    }

    /// <summary>Whether a finger is dragging a scroll container, which the view's own scroller leaves to it. For tests.</summary>
    internal bool IsTouchScrollingContainer => _touchScrolling;

    // A lifted finger carries the container on at the speed it left with, slowing to a stop; the container then settles on a snap position.
    private void BeginFling(DocumentElement container, double vx, double vy)
    {
        EndFling(settle: false);
        _flingContainer = container;
        _flingTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _flingTimer = DispatcherQueue.CreateTimer();
        _flingTimer.Interval = TimeSpan.FromMilliseconds(8);
        _flingTimer.IsRepeating = true;
        _flingTimer.Tick += (_, _) => StepFling(ref vx, ref vy);
        _flingTimer.Start();
    }

    private void StepFling(ref double vx, ref double vy)
    {
        if (_flingContainer is not { } container || _document is not { } document)
        {
            EndFling(settle: false);
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = Math.Min((now - _flingTicks) / (double)System.Diagnostics.Stopwatch.Frequency, 0.05);
        _flingTicks = now;

        var decay = HtmlViewLogic.FlingDecay(seconds);
        vx *= decay;
        vy *= decay;

        // Stops at the end of the content, or when it has slowed to a crawl.
        var moved = ApplyScroll(document.DragScrollBy(container, vx * seconds, vy * seconds));
        if (!moved || Math.Max(Math.Abs(vx), Math.Abs(vy)) < HtmlViewLogic.FlingStopSpeed)
            EndFling(settle: true);
    }

    private void EndFling(bool settle)
    {
        _flingTimer?.Stop();
        _flingTimer = null;
        var container = _flingContainer;
        _flingContainer = null;
        if (settle && container is not null && _document is { } document)
            ApplyScroll(document.SettleScroll(container));
    }

    internal void EndScrollbarDrag()
    {
        EndTrackRepeat();
        if (_scrollbarDrag is { } drag && _document is { } document)
            ApplyScroll(document.SettleScroll(drag.Element));

        _scrollbarDrag = null;
    }

    internal void EndTouchScroll()
    {
        var container = _touchContainer;
        _touchContainer = null;

        // A finger lifted while it was still moving carries the container on; one that stopped first settles it.
        var idle = (System.Diagnostics.Stopwatch.GetTimestamp() - _touchTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (container is not null && _touchScrolling && idle < 0.1 && Math.Max(Math.Abs(_touchVx), Math.Abs(_touchVy)) > HtmlViewLogic.FlingStopSpeed * 4)
            BeginFling(container, _touchVx, _touchVy);
        else if (container is not null && _document is { } document)
            ApplyScroll(document.SettleScroll(container));

        _touchVx = _touchVy = 0;
        _touchTicks = 0;
        _touchStart = null;
        _touchLast = null;
        _touchScrolling = false;
    }
}

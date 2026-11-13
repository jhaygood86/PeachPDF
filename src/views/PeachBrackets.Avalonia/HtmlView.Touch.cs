using Avalonia.Input;
using Avalonia.Threading;
using PeachBrackets.Avalonia.Logic;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

// Touch: the fling that follows a drag, pinch to zoom, double tap to zoom and the touch-action policy.
public sealed partial class HtmlView
{
    // A finger that went down over a scroll container: where it went down, which says which containers it can reach.
    private (int Page, double X, double Y)? _touchStart;
    private bool _touchScrolling;
    private DocumentElement? _touchContainer;
    private bool _touchPressValid;

    // The speed of the finger (container: points per second; page: pixels per second) and when it last moved.
    private double _touchVx;
    private double _touchVy;
    private long _touchTicks;

    private DispatcherTimer? _flingTimer;
    private DocumentElement? _flingContainer;
    private long _flingTicks;
    private double _flingVx;
    private double _flingVy;

    private double _pinchStartZoom;
    private bool _pinching;

    private double _lastTapX;
    private double _lastTapY;
    private long _lastTapMs;

    private void InitializeTouch()
    {
        GestureRecognizers.Add(new PinchGestureRecognizer());
        Pinch += OnPinch;
        PinchEnded += OnPinchEnded;
    }

    /// <summary>Whether a finger is dragging a scroll container, which the page's own scrolling leaves to it. For tests.</summary>
    internal bool IsTouchScrollingContainer => _touchScrolling;

    /// <summary>Whether a fling (the page or a container carried on by a lifted finger) is running. For tests.</summary>
    internal bool IsFlinging => _flingTimer is not null;

    /// <summary>The touch-action gestures allowed for the touch in progress. For tests.</summary>
    internal TouchActions TouchAllowed
    {
        get => _touchAllowed;
        set => _touchAllowed = value;
    }

    // A finger went down (not on a scroll bar): a running fling stops, and a scroll container under the finger is remembered.
    internal void BeginTouchPress(int page, double x, double y)
    {
        EndFling(settle: true);
        _touchPressValid = true;
        _touchStart = null;
        _touchContainer = null;
        _touchScrolling = false;
        _touchVx = _touchVy = 0;
        _touchTicks = 0;
        if (_document is { } document
            && (document.GetScrollContainerAt(page, x, y, 0, 1) is not null || document.GetScrollContainerAt(page, x, y, 0, -1) is not null
                || document.GetScrollContainerAt(page, x, y, 1, 0) is not null || document.GetScrollContainerAt(page, x, y, -1, 0) is not null))
        {
            _touchStart = (page, x, y);
        }
    }

    // A finger that moves over the page scrolls the scroll container it went down on, or else the page, as far as the document's touch-action lets it.
    // The distances are in device independent pixels, positive when the finger moved left or up. True when something took the drag.
    internal bool TouchDragBy(double dx, double dy)
    {
        var (x, y) = ViewportMath.FilterPan(dx, dy, _touchAllowed);
        if (x == 0 && y == 0)
            return false;

        if (DragTouchContainer(x, y))
            return true;

        if (IsAutoHeight)
            return false;

        TouchScroll(x * _dpi, y * _dpi);
        return true;
    }

    private bool DragTouchContainer(double dx, double dy)
    {
        if (_touchStart is not { } start || _document is not { } document)
            return false;

        var pointsX = dx / PixelsPerPoint / _zoom;
        var pointsY = dy / PixelsPerPoint / _zoom;
        var container = document.GetScrollContainerAt(start.Page, start.X, start.Y, pointsX, pointsY);
        if (container is null)
        {
            // At the end of what it can do, the gesture belongs to nothing: it does not hand over to the page half way.
            return _touchScrolling;
        }

        _touchScrolling = true;
        _touchContainer = container;
        var seconds = TouchSeconds();
        _touchVx = TouchMath.SmoothVelocity(_touchVx, pointsX, seconds);
        _touchVy = TouchMath.SmoothVelocity(_touchVy, pointsY, seconds);
        ApplyScroll(document.DragScrollBy(container, pointsX, pointsY));
        return true;
    }

    // The seconds since the finger last moved; restarts the clock.
    private double TouchSeconds()
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = _touchTicks == 0 ? 0 : (now - _touchTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        _touchTicks = now;
        return seconds;
    }

    // Scrolls the page by pixels under a finger, noting its speed (pixels per second) for the fling.
    private void ScrollPageUnderFinger(double dx, double dy)
    {
        var seconds = TouchSeconds();
        _touchVx = TouchMath.SmoothVelocity(_touchVx, dx, seconds);
        _touchVy = TouchMath.SmoothVelocity(_touchVy, dy, seconds);
        ScrollTo(_scrollX + dx, _scrollY + dy);
    }

    // The finger lifted: a still-moving finger carries the container (or the page) on; a tap that follows another is a double tap.
    private void EndTouchDrag()
    {
        var container = _touchContainer;
        var wasValid = _touchPressValid;
        var scrolledContainer = _touchScrolling;
        _touchPressValid = false;
        _touchContainer = null;
        _touchStart = null;
        _touchScrolling = false;

        if (!wasValid)
            return;

        var idle = _touchTicks == 0 ? double.MaxValue : (System.Diagnostics.Stopwatch.GetTimestamp() - _touchTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
        var speed = Math.Max(Math.Abs(_touchVx), Math.Abs(_touchVy));
        var moving = idle < 0.1 && speed > TouchMath.FlingStopSpeed * 4;
        if (_dragged)
        {
            if (container is not null && scrolledContainer)
            {
                if (moving)
                    BeginFling(container, _touchVx, _touchVy);
                else if (_document is { } document)
                    ApplyScroll(document.SettleScroll(container));
            }
            else if (moving && !IsAutoHeight)
            {
                BeginFling(null, _touchVx, _touchVy);
            }
        }
        else
        {
            HandleTouchTap(_lastPoint);
        }

        _touchVx = _touchVy = 0;
        _touchTicks = 0;
    }

    internal void HandleTouchTap(global::Avalonia.Point position)
    {
        var now = Environment.TickCount64;
        if ((_touchAllowed & TouchActions.DoubleTapZoom) != 0 && TouchMath.IsDoubleTap(_lastTapX, _lastTapY, _lastTapMs, position.X, position.Y, now))
        {
            _lastTapMs = 0;
            SetZoom(TouchMath.DoubleTapZoom(_zoom), position);
            return;
        }

        _lastTapX = position.X;
        _lastTapY = position.Y;
        _lastTapMs = now == 0 ? 1 : now;
    }

    // ---- the fling ----

    // A lifted finger carries a container (or, with none, the page) on at the speed it left with, slowing to a stop.
    internal void BeginFling(DocumentElement? container, double vx, double vy)
    {
        EndFling(settle: false);
        _flingContainer = container;
        _flingVx = vx;
        _flingVy = vy;
        _flingTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _flingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _flingTimer.Tick += OnFlingTick;
        _flingTimer.Start();
    }

    private void OnFlingTick(object? sender, EventArgs e)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = Math.Min((now - _flingTicks) / (double)System.Diagnostics.Stopwatch.Frequency, 0.05);
        _flingTicks = now;
        StepFling(seconds);
    }

    /// <summary>One step of the running fling, over <paramref name="seconds"/>. For the timer and for tests.</summary>
    internal void StepFling(double seconds)
    {
        if (_flingTimer is null || _document is not { } document)
        {
            EndFling(settle: false);
            return;
        }

        var decay = TouchMath.FlingDecay(seconds);
        _flingVx *= decay;
        _flingVy *= decay;
        var slow = Math.Max(Math.Abs(_flingVx), Math.Abs(_flingVy)) < TouchMath.FlingStopSpeed;
        bool moved;
        if (_flingContainer is { } container)
        {
            moved = ApplyScroll(document.DragScrollBy(container, _flingVx * seconds, _flingVy * seconds));
        }
        else
        {
            var before = (_scrollX, _scrollY);
            ScrollTo(_scrollX + (_flingVx * seconds), _scrollY + (_flingVy * seconds));
            moved = before != (_scrollX, _scrollY);
        }

        if (!moved || slow)
            EndFling(settle: true);
    }

    private void EndFling(bool settle)
    {
        if (_flingTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnFlingTick;
        }

        _flingTimer = null;
        var container = _flingContainer;
        _flingContainer = null;
        if (settle && container is not null && _document is { } document)
            ApplyScroll(document.SettleScroll(container));
    }

    // ---- pinch ----

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (_document is null || (_touchAllowed & TouchActions.PinchZoom) == 0 && !_pinching)
            return;

        ApplyPinch(e.Scale, e.ScaleOrigin);
        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e) => EndPinch();

    /// <summary>One report of a pinch: <paramref name="scale"/> is how far the fingers have spread since it began, about <paramref name="origin"/> (control coordinates).</summary>
    internal void ApplyPinch(double scale, global::Avalonia.Point origin)
    {
        if (!_pinching)
        {
            _pinching = true;
            _pinchStartZoom = _zoom;
            // A second finger ends what the first one was doing.
            EndFling(settle: false);
            _selecting = false;
        }

        SetZoom(TouchMath.PinchZoom(_pinchStartZoom, scale), origin);
    }

    /// <summary>The fingers of a pinch lifted: the images are chosen for the zoom it settled on.</summary>
    internal void EndPinch()
    {
        if (!_pinching)
            return;

        _pinching = false;
        QueueImageUpgrade();
    }
}

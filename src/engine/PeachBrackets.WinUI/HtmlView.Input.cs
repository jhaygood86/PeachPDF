using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using System;
using System.Linq;
using System.Threading.Tasks;
using PointerDeviceType = Microsoft.UI.Input.PointerDeviceType;
using Windows.Foundation;
using Windows.System;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    // What the pointer last asked of the document, kept so that a run of moves is applied one at a time and only the latest is applied.
    private (int Page, double X, double Y, bool Pressed)? _pendingPointer;
    private bool _clearPending;
    private bool _pumping;
    private bool _pressed;
    private bool _dragSelecting;
    private Point _pressPoint;
    private bool _touchPolicyActive;

    private void WireInput()
    {
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.PointerCanceled += (_, _) => EndPress();
        _canvas.PointerCaptureLost += (_, _) => EndPress();
        _canvas.PointerExited += (_, _) => RequestClear();
        _canvas.DoubleTapped += OnDoubleTapped;
        _canvas.Holding += OnHolding;
        _canvas.RightTapped += OnRightTapped;
        _canvas.PointerWheelChanged += OnPointerWheelChanged;
        Unloaded += (_, _) => CloseFlyouts();
        KeyDown += OnKeyDown;
        WireFocus();
        WireTextInput();
    }

    // Where a position of the canvas (device-independent pixels) is on the pages, in points from the page's top-left.
    private bool TryLocate(Point position, out int page, out double x, out double y)
    {
        page = HtmlViewLogic.PageAt(_pageTops, _pageHeights, position.Y, out var localY);
        x = position.X / PixelsPerPoint;
        y = localY / PixelsPerPoint;
        return page >= 0 && _document is not null;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_canvas).Position;
        if (!TryLocate(position, out var page, out var x, out var y))
        {
            SetSystemCursor(CursorShape.Default);
            return;
        }

        var document = _document!;

        MoveTrackRepeat(x, y);

        // A scrollbar thumb being dragged, or a finger dragging a scroll container, takes the movement.
        if (DragScrollbar(x, y) || (e.Pointer.PointerDeviceType == PointerDeviceType.Touch && MoveTouchScroll(position)))
            return;

        if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
        {
            // The pointer the document asks for here, and the state that :hover and :active rules read.
            ShowCursor(document.GetCursorAt(page, x, y));
            RequestState(page, x, y, _pressed);
        }

        if (_dragSelecting)
            document.Selection.ExtendTo(page, x, y);
        else if (_fieldAnchor is { } anchor)
            _ = ExtendFieldSelectionAsync(document, page, x, y, anchor);
        else if (_rangeElement is not null)
            SetRange(x);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(_canvas);
        if (!TryLocate(point.Position, out var page, out var x, out var y))
            return;

        var document = _document!;
        _pressed = true;
        _pressPoint = point.Position;
        // A finger that starts on an auto-height view may be starting to scroll the list the view is in: the list must keep seeing it.
        if (_scrollMode != HtmlViewScrollMode.Disabled || e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
            _canvas.CapturePointer(e.Pointer);

        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
        {
            ApplyTouchPolicy(document.GetInteractionAt(page, x, y));
            BeginTouchScroll(page, x, y, point.Position);
            RequestState(page, x, y, pressed: true);
            return;
        }

        RequestState(page, x, y, pressed: true);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        // A press on a scrollbar of a scroll container takes hold of the thumb or pages the container.
        if (PressScrollbarAt(page, x, y))
            return;

        // A press on a text field puts the caret there at once, so a drag can select inside it.
        _fieldPressed = FocusTextFieldAt(page, x, y);
        if (!_fieldPressed && BeginRangeDrag(page, x, y))
            return;

        if (!_fieldPressed && document.GetInteractionAt(page, x, y).CanSelectText)
            _dragSelecting = document.Selection.SelectAt(page, x, y);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_canvas).Position;
        var wasClick = Math.Abs(position.X - _pressPoint.X) <= ClickSlop && Math.Abs(position.Y - _pressPoint.Y) <= ClickSlop;
        var wasSelecting = _dragSelecting;
        var wasField = _fieldPressed || _rangeElement is not null || _scrollbarDrag is not null || _touchScrolling;
        EndScrollbarDrag();
        EndTouchScroll();
        EndPress();
        _canvas.ReleasePointerCapture(e.Pointer);

        if (wasClick && TryLocate(position, out var page, out var x, out var y))
        {
            if (wasSelecting)
                _document!.Selection.RemoveAllRanges();

            if (!wasField && !FocusTextFieldAt(page, x, y) && !ActivateControlAt(page, x, y) && !OpenPickerAt(page, x, y, position) && !ResetFormAt(page, x, y) && !SubmitFormAt(page, x, y))
                FollowLinkAt(page, x, y);
        }
    }

    private void EndPress()
    {
        if (!_pressed)
            return;

        _pressed = false;
        EndTrackRepeat();
        _dragSelecting = false;
        _fieldAnchor = null;
        _fieldPressed = false;
        EndRangeDrag();
        RestoreTouchPolicy();
        if (_document is not null && TryLocate(_pressPoint, out var page, out var x, out var y))
            RequestState(page, x, y, pressed: false);
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.PointerDeviceType == PointerDeviceType.Touch)
            return;

        if (TryLocate(e.GetPosition(_canvas), out var page, out var x, out var y) && _document!.GetInteractionAt(page, x, y).CanSelectText)
            _document.Selection.SelectWordAt(page, x, y);
    }

    private void OnHolding(object sender, HoldingRoutedEventArgs e)
    {
        // A long press on a text field selects its word and shows the field's menu.
        if (e.HoldingState == Microsoft.UI.Input.HoldingState.Started && TryLocate(e.GetPosition(_canvas), out var fieldPage, out var fieldX, out var fieldY)
            && OpenFieldMenu(fieldPage, fieldX, fieldY, e.GetPosition(_canvas), selectWord: true))
        {
            return;
        }

        // A long press with a finger selects the word, as a long press does elsewhere.
        if (e.HoldingState == Microsoft.UI.Input.HoldingState.Started
            && TryLocate(e.GetPosition(_canvas), out var page, out var x, out var y)
            && _document!.GetInteractionAt(page, x, y).CanSelectText)
        {
            _document.Selection.SelectWordAt(page, x, y);
        }
    }

    // The wheel over a scroll container (an element with overflow auto, scroll or hidden) scrolls it, and passes on to the container around it, then to the page, at the
    // end of its content (scroll chaining); over a text area that has more lines than show it scrolls the text area; anywhere else the page scrolls.
    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas);
        if (!TryLocate(point.Position, out var page, out var x, out var y))
            return;

        var notches = point.Properties.MouseWheelDelta / 120.0 * WheelNotchPoints;
        var horizontal = point.Properties.IsHorizontalMouseWheel;
        if (ScrollContainerAt(page, x, y, horizontal ? notches : 0, horizontal ? 0 : -notches))
        {
            e.Handled = true;
            return;
        }

        if (!horizontal && ScrollTextAreaAt(page, x, y, -notches))
            e.Handled = true;
    }

    // Scrolls the innermost scroll container under a point that can go the way asked, by an amount in points; false when there is none, and the page scrolls instead.
    internal bool ScrollContainerAt(int page, double x, double y, double dx, double dy)
    {
        if (_document is not { } document || document.GetScrollContainerAt(page, x, y, dx, dy) is not { } container)
            return false;

        // A container that takes the wheel keeps it even when it is already at its end and nothing moves (overscroll-behavior: contain).
        ApplyScroll(document.ScrollBy(container, dx, dy));
        return true;
    }

    private const double WheelNotchPoints = 42;

    // Scrolls the text area under a point by an amount in points; false when the point is not over one or it cannot move that way.
    internal bool ScrollTextAreaAt(int page, double x, double y, double points)
    {
        if (_document is not { } document || _context is null || document.GetElementAt(page, x, y) is not { } element)
            return false;

        using var measuring = new PeachDrawing.Win2D.Win2dComposer(_context, 1, 1);
        var result = document.ScrollTextArea(element, points, measuring.Canvas);
        Repaint(result);
        return result.Applied;
    }

    // A right click (or a touch hold the system reports as one) on a text field shows the field's menu.
    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var position = e.GetPosition(_canvas);
        if (TryLocate(position, out var page, out var x, out var y) && OpenFieldMenu(page, x, y, position, selectWord: false))
            e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (HandleFocusKey(e))
        {
            e.Handled = true;
            return;
        }

        if (HandleEditKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (HandleScrollKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (control && e.Key == VirtualKey.C)
        {
            CopySelection();
            e.Handled = true;
        }
        else if (control && e.Key == VirtualKey.A)
        {
            SelectAll();
            e.Handled = true;
        }
    }

    // A touch that starts on an element with `touch-action` lets the view pan and zoom only as far as the document allows.
    private void ApplyTouchPolicy(DocumentInteraction interaction)
    {
        if (_scrollMode == HtmlViewScrollMode.Disabled)
            return;

        _touchPolicyActive = true;
        var actions = interaction.TouchActions;
        _scroller.HorizontalScrollMode = (actions & (TouchActions.PanLeft | TouchActions.PanRight)) != 0 ? Microsoft.UI.Xaml.Controls.ScrollMode.Enabled : Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
        _scroller.VerticalScrollMode = (actions & (TouchActions.PanUp | TouchActions.PanDown)) != 0 ? Microsoft.UI.Xaml.Controls.ScrollMode.Enabled : Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
        _scroller.ZoomMode = actions.HasFlag(TouchActions.PinchZoom) && _zoomEnabled ? ZoomMode.Enabled : ZoomMode.Disabled;
    }

    private void RestoreTouchPolicy()
    {
        if (!_touchPolicyActive)
            return;

        _touchPolicyActive = false;
        _scroller.HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Auto;
        _scroller.VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Auto;
        _scroller.ZoomMode = _zoomEnabled ? ZoomMode.Enabled : ZoomMode.Disabled;
    }

    internal void FollowLinkAt(int page, double x, double y)
    {
        var document = _document;
        if (document is null)
            return;

        // The topmost link under the point, unless the point is over something that does not receive pointer events.
        var link = document.Annotations.Links.LastOrDefault(l => l.PageIndex == page && l.Bounds.Contains(x, y));
        if (link is null)
            return;

        var args = new HtmlLinkClickedEventArgs(link.Uri, link.Anchor);
        LinkClicked?.Invoke(this, args);
        if (args.Cancel)
            return;

        if (link.Uri is { } uri)
        {
            if (HtmlViewLogic.IsLaunchable(uri))
                _ = Launcher.LaunchUriAsync(new Uri(uri));
        }
        else if (link.Anchor is { } anchor && document.GetScrollOffset(anchor) is { } offset && _pageTops.Length > 0)
        {
            // Where the target starts less the room it asks for above it (scroll-margin-top, scroll-padding-top), so a bar stuck to the top does not cover it.
            ScrollToY(_pageTops[0] + (offset * PixelsPerPoint), animate: false);
        }
        else if (link.Destination is { } destination && (uint)destination.PageIndex < (uint)_pageTops.Length)
        {
            ScrollToY(_pageTops[destination.PageIndex] + (destination.Top * PixelsPerPoint), animate: false);
        }
    }

    private void RequestState(int page, double x, double y, bool pressed)
    {
        _pendingPointer = (page, x, y, pressed);
        _clearPending = false;
        _ = PumpStateAsync();
    }

    private void RequestClear()
    {
        _pendingPointer = null;
        _clearPending = true;
        SetSystemCursor(CursorShape.Default);
        _ = PumpStateAsync();
    }

    // One state change at a time: a restyle lays the document out again, and only the pointer's latest position matters.
    private async Task PumpStateAsync()
    {
        if (_pumping)
            return;

        _pumping = true;
        try
        {
            while (_document is { } document && (_pendingPointer is not null || _clearPending))
            {
                UpdateResult result;
                using var measuring = new PeachDrawing.Win2D.Win2dComposer(_context!, 1, 1);

                // A restyle drops the old layout before the new one is in; a region painted in between would be cleared and left empty, so the
                // painter leaves what is on the surface alone until the new layout is there (see OnRegionsInvalidated).
                _restyling = true;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    if (_pendingPointer is { } pointer)
                    {
                        _pendingPointer = null;
                        result = await document.SetPointerStateAsync(pointer.Page, pointer.X, pointer.Y, pointer.Pressed, measuring.Canvas);
                    }
                    else
                    {
                        _clearPending = false;
                        result = await document.ClearPointerStateAsync(measuring.Canvas);
                    }
                }
                finally
                {
                    _restyling = false;
                }

                HtmlViewDiagnostics.Trace?.Invoke(
                    $"pointer state: {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms, applied={result.Applied}, regions={result.DirtyRegions.Count}");

                if (_paintDeferred)
                {
                    // Something was asked for while the layout was away and went unpainted.
                    _paintDeferred = false;
                    if (ReferenceEquals(document, _document))
                    {
                        UpdateMetrics();
                        _canvas.Invalidate();
                        _overlay.Invalidate();
                    }
                }

                if (!result.Applied || !ReferenceEquals(document, _document))
                    continue;

                // A hover that starts a transition has the clock to run.
                EnsureAnimationLoop();

                if (result.DirtyRegions.Count > 0)
                {
                    UpdateMetrics();
                    foreach (var region in result.DirtyRegions)
                        InvalidateRegion(region.PageIndex, region.Bounds);
                }
            }
        }
        finally
        {
            _pumping = false;
        }
    }
}

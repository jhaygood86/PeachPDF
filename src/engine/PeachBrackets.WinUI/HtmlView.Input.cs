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
        KeyDown += OnKeyDown;
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
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            return;
        }

        var document = _document!;
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch)
        {
            // The pointer the document asks for here, and the state that :hover and :active rules read.
            ProtectedCursor = InputSystemCursor.Create(HtmlViewLogic.ToSystemCursor(document.GetCursorAt(page, x, y).Shape));
            RequestState(page, x, y, _pressed);
        }

        if (_dragSelecting)
            document.Selection.ExtendTo(page, x, y);
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
        _canvas.CapturePointer(e.Pointer);

        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
        {
            ApplyTouchPolicy(document.GetInteractionAt(page, x, y));
            RequestState(page, x, y, pressed: true);
            return;
        }

        RequestState(page, x, y, pressed: true);
        if (point.Properties.IsLeftButtonPressed && document.GetInteractionAt(page, x, y).CanSelectText)
            _dragSelecting = document.Selection.SelectAt(page, x, y);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(_canvas).Position;
        var wasClick = Math.Abs(position.X - _pressPoint.X) <= ClickSlop && Math.Abs(position.Y - _pressPoint.Y) <= ClickSlop;
        var wasSelecting = _dragSelecting;
        EndPress();
        _canvas.ReleasePointerCapture(e.Pointer);

        if (wasClick && TryLocate(position, out var page, out var x, out var y))
        {
            if (wasSelecting)
                _document!.Selection.RemoveAllRanges();

            FollowLinkAt(page, x, y);
        }
    }

    private void EndPress()
    {
        if (!_pressed)
            return;

        _pressed = false;
        _dragSelecting = false;
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
        // A long press with a finger selects the word, as a long press does elsewhere.
        if (e.HoldingState == Microsoft.UI.Input.HoldingState.Started
            && TryLocate(e.GetPosition(_canvas), out var page, out var x, out var y)
            && _document!.GetInteractionAt(page, x, y).CanSelectText)
        {
            _document.Selection.SelectWordAt(page, x, y);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
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
        _touchPolicyActive = true;
        var actions = interaction.TouchActions;
        _scroller.HorizontalScrollMode = (actions & (TouchActions.PanLeft | TouchActions.PanRight)) != 0 ? ScrollMode.Enabled : ScrollMode.Disabled;
        _scroller.VerticalScrollMode = (actions & (TouchActions.PanUp | TouchActions.PanDown)) != 0 ? ScrollMode.Enabled : ScrollMode.Disabled;
        _scroller.ZoomMode = actions.HasFlag(TouchActions.PinchZoom) ? ZoomMode.Enabled : ZoomMode.Disabled;
    }

    private void RestoreTouchPolicy()
    {
        if (!_touchPolicyActive)
            return;

        _touchPolicyActive = false;
        _scroller.HorizontalScrollMode = ScrollMode.Auto;
        _scroller.VerticalScrollMode = ScrollMode.Auto;
        _scroller.ZoomMode = ZoomMode.Enabled;
    }

    private void FollowLinkAt(int page, double x, double y)
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
        else if (link.Destination is { } destination && (uint)destination.PageIndex < (uint)_pageTops.Length)
        {
            _scroller.ChangeView(null, (_pageTops[destination.PageIndex] + (destination.Top * PixelsPerPoint)) * _scroller.ZoomFactor, null);
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
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
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

                if (!result.Applied || !ReferenceEquals(document, _document))
                    continue;

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

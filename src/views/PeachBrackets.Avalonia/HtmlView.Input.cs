using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PeachBrackets.Avalonia.Logic;
using PeachDrawing.Core;

namespace PeachBrackets.Avalonia;

public sealed partial class HtmlView
{
    // How far a press may move and still be a click, in device independent pixels.
    private const double ClickSlop = 4;

    private bool _pressed;
    private bool _dragged;
    private bool _selecting;
    private bool _pressedInField;
    private global::Avalonia.Point _pressPoint;
    private global::Avalonia.Point _lastPoint;
    private PointerType _pressedType;
    private TouchActions _touchAllowed = TouchActions.All;
    private StandardCursorType? _shownCursor;

    /// <summary>The keyword pointer shown now, or null when an image pointer (or none) is. For tests.</summary>
    internal StandardCursorType? ShownKeywordCursor => _shownCursor;

    private bool _pumping;
    private PointerSample? _pendingPointer;
    private bool _clearPending;

    private readonly record struct PointerSample(int Page, double X, double Y, bool Pressed);

    // Where a point of the control is in the document: the page, and the position on it in points. False when it is not on a page.
    private bool TryLocate(global::Avalonia.Point point, out int page, out double x, out double y)
    {
        page = -1;
        x = y = 0;
        if (_document is null)
            return false;

        var scale = Scale;
        var documentX = (_scrollX + (point.X * _dpi)) / scale;
        var documentY = (_scrollY + (point.Y * _dpi)) / scale;
        page = PageLayout.PageAt(_pageTops, _pageHeights, documentY, out var localY);
        if (page < 0)
            return false;

        x = documentX / PixelsPerPoint;
        y = localY / PixelsPerPoint;
        return true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        if (_document is not { } document)
            return;

        _pressedType = e.Pointer.Type;
        if (!point.Properties.IsLeftButtonPressed)
        {
            OnSecondaryPress(e, point.Position);
            return;
        }

        _pressed = true;
        _dragged = false;
        _selecting = false;
        _pressedInField = false;
        _pressPoint = _lastPoint = point.Position;
        _touchAllowed = TouchActions.All;
        e.Pointer.Capture(this);
        e.Handled = true;

        if (!TryLocate(point.Position, out var page, out var x, out var y))
            return;

        if (BeginScrollbarDrag(page, x, y))
        {
            _pressedInField = true;
            return;
        }

        if (_pressedType == PointerType.Touch)
        {
            _touchAllowed = document.GetInteractionAt(page, x, y).TouchActions;
            BeginTouchPress(page, x, y);
        }

        RequestPointerState(page, x, y, pressed: true);
        if (BeginRangeDrag(page, x, y))
        {
            _pressedInField = true;
            return;
        }

        if (PressInTextField(page, x, y, e.ClickCount, e.KeyModifiers))
        {
            _pressedInField = true;
            return;
        }

        if (_pressedType != PointerType.Touch && document.GetInteractionAt(page, x, y).CanSelectText)
        {
            // Shift+click moves the end of the selection there, and dragging on goes on moving it.
            var extend = e.ClickCount == 1 && (e.KeyModifiers & KeyModifiers.Shift) != 0 && document.Selection.HasSelection;
            _selecting = e.ClickCount switch
            {
                _ when extend => document.Selection.ExtendTo(page, x, y),
                2 => document.Selection.SelectWordAt(page, x, y),
                >= 3 => document.Selection.SelectBlockAt(page, x, y),
                _ => document.Selection.SelectAt(page, x, y),
            };

            // A double or triple click is a selection of its own, not the start of a click on a link.
            // (A second press on a check box or button is a second click on it, as in a browser.)
            if (extend || (e.ClickCount > 1 && !(document.GetElementAt(page, x, y) is { } pressed && IsControl(pressed))))
                _dragged = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_document is not { } document)
            return;

        var position = e.GetPosition(this);
        if (!_pressed)
        {
            if (e.Pointer.Type != PointerType.Touch)
                Hover(position);

            return;
        }

        if (!_dragged && (Math.Abs(position.X - _pressPoint.X) > ClickSlop || Math.Abs(position.Y - _pressPoint.Y) > ClickSlop))
            _dragged = true;

        if (_pressedInField)
        {
            if (DragRange(position))
            {
                _lastPoint = position;
                return;
            }

            if (TryLocate(position, out var fieldPage, out var fieldX, out var fieldY) && !DragScrollbar(fieldPage, fieldX, fieldY))
                DragInTextField(fieldPage, fieldX, fieldY);

            _lastPoint = position;
            return;
        }

        if (_pressedType == PointerType.Touch)
        {
            // While two fingers pinch, the pinch carries the page.
            if (_dragged && !_pinching)
                e.Handled |= TouchDragBy(_lastPoint.X - position.X, _lastPoint.Y - position.Y);
        }
        else if (_selecting && TryLocate(position, out var page, out var x, out var y))
        {
            document.Selection.ExtendTo(page, x, y);
        }

        if (_pressedType != PointerType.Touch && TryLocate(position, out var hoverPage, out var hoverX, out var hoverY))
        {
            ShowCursor(document.GetCursorAt(hoverPage, hoverX, hoverY));
            RequestPointerState(hoverPage, hoverX, hoverY, pressed: true);
        }

        _lastPoint = position;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // The press may have been ended by a reload while the pointer was still held: the capture is let go of all the same.
        if (!_pressed)
        {
            if (ReferenceEquals(e.Pointer.Captured, this))
                e.Pointer.Capture(null);

            return;
        }

        _pressed = false;
        e.Pointer.Capture(null);
        var position = e.GetPosition(this);
        var wasField = _pressedInField;
        _pressedInField = false;
        EndScrollbarDrag();
        if (_document is { } document && TryLocate(position, out var page, out var x, out var y))
        {
            RequestPointerState(page, x, y, pressed: false);
            if (!wasField && !_dragged)
                ClickAt(page, x, y);
        }

        if (_pressedType == PointerType.Touch)
            EndTouchDrag();

        _selecting = false;
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_pressed)
            RequestClear();

        SetCursorKind(StandardCursorType.Arrow);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_pressed)
        {
            _pressed = false;
            _pressedInField = false;
            _selecting = false;
            EndScrollbarDrag();
            RequestClear();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_document is null)
            return;

        var position = e.GetPosition(this);
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (IsZoomEnabled && !IsAutoHeight)
            {
                SetZoom(_zoom * Math.Pow(1.1, e.Delta.Y), position);
                e.Handled = true;
            }

            return;
        }

        // A notch is three lines, as in a browser.
        const double notch = 48;
        var dx = -e.Delta.X * notch;
        var dy = -e.Delta.Y * notch;
        if (TryLocate(position, out var page, out var x, out var y) && WheelScrollContainer(page, x, y, dx / PixelsPerPoint / _zoom, dy / PixelsPerPoint / _zoom))
        {
            e.Handled = true;
            return;
        }

        if (IsAutoHeight)
            return;

        // Only a wheel that moved the page is used up: at the top or the bottom it goes on to a scroll viewer around the view.
        var before = (_scrollX, _scrollY);
        ScrollTo(_scrollX + (dx * _dpi), _scrollY + (dy * _dpi));
        e.Handled = before != (_scrollX, _scrollY);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_document is not { } document || e.Handled)
            return;

        // The focused control takes the keys first (a text field edits, a button activates), then the page scrolls and copies.
        if (HandleFocusKey(e))
        {
            e.Handled = true;
            return;
        }

        var control = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var step = 40 * _dpi;
        var page = ViewportHeightPx * 0.875;
        switch (e.Key)
        {
            case Key.C when control:
                CopySelection();
                break;
            case Key.A when control:
                SelectAll();
                break;
            case Key.OemPlus or Key.Add when control:
                SetZoom(_zoom * 1.1);
                break;
            case Key.OemMinus or Key.Subtract when control:
                SetZoom(_zoom / 1.1);
                break;
            case Key.D0 or Key.NumPad0 when control:
                SetZoom(1);
                break;
            case Key.Down:
                ScrollKey(0, step);
                break;
            case Key.Up:
                ScrollKey(0, -step);
                break;
            case Key.Right:
                ScrollKey(step, 0);
                break;
            case Key.Left:
                ScrollKey(-step, 0);
                break;
            case Key.PageDown or Key.Space when e.Key != Key.Space || !IsEditingField:
                ScrollKey(0, page);
                break;
            case Key.PageUp:
                ScrollKey(0, -page);
                break;
            case Key.Home when control:
                ScrollTo(_scrollX, 0);
                break;
            case Key.End when control:
                ScrollTo(_scrollX, double.MaxValue);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void ScrollKey(double dx, double dy)
    {
        if (!IsAutoHeight && !ScrollFocusedContainer(dx, dy))
            ScrollTo(_scrollX + dx, _scrollY + dy);
    }

    // A move of the mouse over the page, when no button is down.
    private void Hover(global::Avalonia.Point position)
    {
        if (_document is not { } document)
            return;

        if (!TryLocate(position, out var page, out var x, out var y))
        {
            SetCursorKind(StandardCursorType.Arrow);
            RequestClear();
            return;
        }

        ShowCursor(document.GetCursorAt(page, x, y));
        RequestPointerState(page, x, y, pressed: false);
    }

    private void ShowCursor(DocumentCursor cursor)
    {
        if (cursor.Images.Count > 0 && TryShowImageCursor(cursor))
            return;

        // The keyword's pointer stands in while the image loads; the load shows the image when it finishes, so it is still waited for.
        SetCursorKind(AvaloniaCursors.ToType(cursor.Shape), keepPendingImage: _pendingImageCursor is not null && ReferenceEquals(_pendingImageCursor, cursor));
    }

    private void SetCursorKind(StandardCursorType kind, bool keepPendingImage = false)
    {
        if (!keepPendingImage)
            _pendingImageCursor = null;

        if (_shownCursor == kind)
            return;

        _shownImageKey = null;

        _shownCursor = kind;
        Cursor = new Cursor(kind);
    }

    // A click: a control of a form, a link, or the page itself.
    private void ClickAt(int page, double x, double y)
    {
        if (_document is not { } document)
            return;

        if (ActivateControlAt(page, x, y))
            return;

        var link = document.Annotations.Links.LastOrDefault(l => l.PageIndex == page && l.Bounds.Contains(x, y));
        if (link is null)
        {
            document.Selection.RemoveAllRanges();
            RaisePageClicked(page, x, y);
            return;
        }

        FollowLink(link);
    }

    private void FollowLink(PeachBrackets.Annotations.PageLink link)
    {
        var args = new HtmlLinkActivatedEventArgs(link.Uri, link.Anchor);
        RaiseLinkActivated(args);
        if (args.Cancel)
            return;

        if (link.Uri is { } uri)
        {
            if (PageLayout.IsLaunchable(uri) && Uri.TryCreate(uri, UriKind.Absolute, out var target) && TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
                Forget(launcher.LaunchUriAsync(target));
        }
        else if (link.Anchor is { } anchor && _document?.GetScrollOffset(anchor) is { } offset && _pageTops.Length > 0)
        {
            // Where the target starts less the room it asks for above it (scroll-margin-top, scroll-padding-top), so a bar stuck to the top does not cover it.
            ScrollToDocumentY(_pageTops[0] + (offset * PixelsPerPoint));
        }
        else if (link.Destination is { } destination && (uint)destination.PageIndex < (uint)_pageTops.Length)
        {
            ScrollToDocumentY(_pageTops[destination.PageIndex] + (destination.Top * PixelsPerPoint));
        }
    }

    // TouchDragBy (a finger that moves over the page) is in HtmlView.Touch.cs.
    private void TouchScroll(double dx, double dy) => ScrollPageUnderFinger(dx, dy);

    // ---- the pointer state: :hover and :active ----

    private void RequestPointerState(int page, double x, double y, bool pressed)
    {
        _pendingPointer = new PointerSample(page, x, y, pressed);
        _clearPending = false;
        Forget(PumpStateAsync());
    }

    private void RequestClear()
    {
        _pendingPointer = null;
        _clearPending = true;
        Forget(PumpStateAsync());
    }

    // One state change at a time: a restyle lays the document out again, and only the pointer's latest position matters.
    private async Task PumpStateAsync()
    {
        if (_pumping)
            return;

        _pumping = true;
        HtmlDocument? working = null;
        try
        {
            while (_document is { } document && _context is { } context && (_pendingPointer is not null || _clearPending))
            {
                working = document;
                PeachBrackets.Updates.UpdateResult result;
                using var measuring = RentMeasuring(context);
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

                if (result.Applied && ReferenceEquals(document, _document) && result.DirtyRegions.Count > 0)
                {
                    UpdateMetrics();
                    InvalidateRegions(result.DirtyRegions);
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // What a document that was replaced meanwhile throws is nothing the one that replaced it has to answer for.
            if (working is null || IsCurrent(working))
                RaiseDocumentFailed(exception);
        }
        finally
        {
            _pumping = false;
        }
    }

    // The pointing devices the document's media queries report: a mouse on a desktop, a touch screen where there is nothing else.
    private static InputCapabilities DetectInput() =>
        OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() ? AvaloniaInput.Capabilities(false, false, true) : AvaloniaInput.Capabilities(true, false, false);

    // A thin indicator of where the view is in a long page; it takes no space and no input.
    private void PaintScrollbarIndicator(DrawingContext context)
    {
        if (IsAutoHeight || _contentHeight * Scale <= ViewportHeightPx + 1)
            return;

        var track = Bounds.Height;
        var thumbLength = Math.Max(24, track * (ViewportHeightPx / (_contentHeight * Scale)));
        var travel = track - thumbLength;
        var maxScroll = (_contentHeight * Scale) - ViewportHeightPx;
        var thumbTop = maxScroll <= 0 ? 0 : travel * (_scrollY / maxScroll);
        var thumb = new global::Avalonia.Rect(Bounds.Width - 7, thumbTop, 5, thumbLength);
        context.DrawRectangle(IndicatorBrush, null, new RoundedRect(thumb, 2.5));
    }

    private static readonly IBrush IndicatorBrush = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(110, 90, 90, 90));
}

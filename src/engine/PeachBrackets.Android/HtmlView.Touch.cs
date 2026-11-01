using Android.Content;
using Android.Views;
using Android.Widget;
using PeachBrackets.Android.Logic;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using AndroidUri = Android.Net.Uri;

namespace PeachBrackets.Android;

public sealed partial class HtmlView
{
    private GestureDetector? _gestures;
    private ScaleGestureDetector? _scaler;
    private OverScroller? _scroller;
    private TouchActions _allowed = TouchActions.All;
    private bool _touchStarted;
    private bool _selectingAfterLongPress;
    private float _longPressX;
    private float _longPressY;
    private bool _dragExtended;
    private bool _scaling;
    private ActionMode? _actionMode;
    private (int Page, double X, double Y, bool Pressed)? _pendingPointer;
    private bool _clearPending;
    private bool _pumping;

    private void InitTouch(Context context)
    {
        _scroller = new OverScroller(context);
        _gestures = new GestureDetector(context, new GestureListener(this));
        _scaler = new ScaleGestureDetector(context, new ScaleListener(this));
    }

    // Where a position in the view (pixels) is on the pages, in points from the page's top-left.
    private bool TryLocate(float px, float py, out int page, out double x, out double y)
    {
        var contentX = (_scrollX + px) / Scale;
        var contentY = (_scrollY + py) / Scale;
        page = PageLayout.PageAt(_pageTops, _pageHeights, contentY, out var localY);
        x = contentX / PixelsPerPoint;
        y = localY / PixelsPerPoint;
        return page >= 0 && _document is not null;
    }

    /// <inheritdoc />
    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null || _document is null)
            return base.OnTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                BeginTouch(e);
                break;
            case MotionEventActions.Move when _selectingAfterLongPress:
                // A finger rests with a little jitter; only a real drag moves the end of the selection.
                var slop = ViewConfiguration.Get(Context!)?.ScaledTouchSlop ?? 8;
                if ((_dragExtended || Math.Abs(e.GetX() - _longPressX) > slop || Math.Abs(e.GetY() - _longPressY) > slop)
                    && TryLocate(e.GetX(), e.GetY(), out var page, out var x, out var y))
                {
                    _dragExtended = true;
                    _document.Selection.ExtendTo(page, x, y);
                }

                return true;
        }

        // Pinch zoom only when the document and the application allow it.
        if ((_allowed & TouchActions.PinchZoom) != 0 || _scaling)
            _scaler?.OnTouchEvent(e);

        if (!_scaling)
            _gestures?.OnTouchEvent(e);

        // The finger-up is what makes the detector report a fling or a tap, so the touch ends only after it has been seen: the gestures it
        // reports are still judged against what the document allowed when the touch began.
        if (e.ActionMasked is MotionEventActions.Up or MotionEventActions.Cancel)
            EndTouch(e);

        return true;
    }

    private void BeginTouch(MotionEvent e)
    {
        _scroller?.ForceFinished(true);
        _touchStarted = true;
        _selectingAfterLongPress = false;

        var document = _document!;
        if (TryLocate(e.GetX(), e.GetY(), out var page, out var x, out var y))
        {
            var args = new HtmlTouchStartedEventArgs(page, x, y, document.GetInteractionAt(page, x, y));
            TouchStarted?.Invoke(this, args);
            _allowed = args.Allowed;
            RequestState(page, x, y, pressed: true);
        }
        else
        {
            var args = new HtmlTouchStartedEventArgs(-1, 0, 0, new DocumentInteraction(false, TouchActions.All, true));
            TouchStarted?.Invoke(this, args);
            _allowed = args.Allowed;
        }

        // A page that keeps the gesture for itself must not have the parent scroll it away.
        Parent?.RequestDisallowInterceptTouchEvent((_allowed & TouchActions.Pan) != TouchActions.None);
    }

    private void EndTouch(MotionEvent e)
    {
        if (!_touchStarted)
            return;

        _touchStarted = false;
        _selectingAfterLongPress = false;
        _allowed = TouchActions.All;
        if (_document is not null && TryLocate(e.GetX(), e.GetY(), out var page, out var x, out var y))
            RequestState(page, x, y, pressed: false);
        else
            RequestClear();
    }

    /// <inheritdoc />
    public override void ComputeScroll()
    {
        base.ComputeScroll();
        if (_scroller is { } scroller && scroller.ComputeScrollOffset())
        {
            ScrollTo(scroller.CurrX, scroller.CurrY);
            PostInvalidateOnAnimation();
        }
    }

    /// <inheritdoc />
    public override bool OnGenericMotionEvent(MotionEvent? e)
    {
        // A mouse, trackpad or stylus hovering: the pointer is over the document without pressing, which is what :hover is.
        if (e is not null && _document is not null)
        {
            switch (e.ActionMasked)
            {
                case MotionEventActions.HoverMove or MotionEventActions.HoverEnter:
                    if (TryLocate(e.GetX(), e.GetY(), out var page, out var x, out var y))
                        RequestState(page, x, y, pressed: false);
                    else
                        RequestClear();

                    return true;
                case MotionEventActions.HoverExit:
                    RequestClear();
                    return true;
                case MotionEventActions.Scroll:
                    ScrollTo(_scrollX - (e.GetAxisValue(Axis.Hscroll) * 64 * _density), _scrollY - (e.GetAxisValue(Axis.Vscroll) * 64 * _density));
                    return true;
            }
        }

        return base.OnGenericMotionEvent(e);
    }

    /// <inheritdoc />
    public override PointerIcon? OnResolvePointerIcon(MotionEvent? e, int pointerIndex)
    {
        if (e is null || _document is not { } document || Context is null)
            return base.OnResolvePointerIcon(e, pointerIndex);

        // The pointer the document asks for under the mouse; `cursor: none` and a point outside the pages fall back to the system's own.
        if (!TryLocate(e.GetX(pointerIndex), e.GetY(pointerIndex), out var page, out var x, out var y))
            return PointerIcon.GetSystemIcon(Context, (PointerIconType)AndroidCursors.Arrow);

        var shape = document.GetCursorAt(page, x, y).Shape;
        return PointerIcon.GetSystemIcon(Context, (PointerIconType)AndroidCursors.ToType(shape));
    }

    /// <inheritdoc />
    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (e is { IsCtrlPressed: true })
        {
            if (keyCode == Keycode.C)
            {
                CopySelection();
                return true;
            }

            if (keyCode == Keycode.A)
            {
                SelectAll();
                return true;
            }
        }

        return base.OnKeyDown(keyCode, e);
    }

    private bool TapAt(float px, float py)
    {
        if (!TryLocate(px, py, out var page, out var x, out var y))
            return false;

        var document = _document!;
        var link = document.Annotations.Links.LastOrDefault(l => l.PageIndex == page && l.Bounds.Contains(x, y));
        if (link is null)
        {
            Tapped?.Invoke(this, new HtmlTappedEventArgs(page, x, y));
            document.Selection.RemoveAllRanges();
            return true;
        }

        var args = new HtmlLinkTappedEventArgs(link.Uri, link.Anchor);
        LinkTapped?.Invoke(this, args);
        if (args.Cancel)
            return true;

        if (link.Uri is { } uri)
        {
            if (PageLayout.IsLaunchable(uri) && Context is { } context)
            {
                var intent = new Intent(Intent.ActionView, AndroidUri.Parse(uri));
                intent.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(intent);
            }
        }
        else if (link.Destination is { } destination && (uint)destination.PageIndex < (uint)_pageTops.Length)
        {
            ScrollTo(_scrollX, (_pageTops[destination.PageIndex] + (destination.Top * PixelsPerPoint)) * Scale);
        }

        return true;
    }

    private void LongPressAt(float px, float py)
    {
        if (!TryLocate(px, py, out var page, out var x, out var y))
            return;

        LongPressed?.Invoke(this, new HtmlTappedEventArgs(page, x, y));
        var document = _document!;
        if (document.GetInteractionAt(page, x, y).CanSelectText && document.Selection.SelectWordAt(page, x, y))
        {
            // The finger stays down: moving it now extends the selection, and the copy menu appears beside it.
            _selectingAfterLongPress = true;
            _dragExtended = false;
            _longPressX = px;
            _longPressY = py;
            StartActionMode();
        }
    }

    private void ZoomBy(double factor, float focusX, float focusY)
    {
        var newZoom = ViewportMath.ClampZoom(_zoom * factor, MinZoom, MaxZoom);
        if (newZoom == _zoom)
            return;

        var oldScale = Scale;
        _zoom = newZoom;
        var newScale = Scale;
        _scrollX = ViewportMath.ScrollAfterZoom(_scrollX, oldScale, newScale, focusX);
        _scrollY = ViewportMath.ScrollAfterZoom(_scrollY, oldScale, newScale, focusY);
        _scrollX = ViewportMath.ClampScroll(_scrollX, _contentWidth * newScale, Width);
        _scrollY = ViewportMath.ClampScroll(_scrollY, _contentHeight * newScale, Height);
        _surface.Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class GestureListener(HtmlView view) : GestureDetector.SimpleOnGestureListener
    {
        public override bool OnDown(MotionEvent e) => true;

        public override bool OnSingleTapUp(MotionEvent e) => view.TapAt(e.GetX(), e.GetY());

        public override void OnLongPress(MotionEvent e) => view.LongPressAt(e.GetX(), e.GetY());

        public override bool OnScroll(MotionEvent? e1, MotionEvent e2, float distanceX, float distanceY)
        {
            var (dx, dy) = ViewportMath.FilterPan(distanceX, distanceY, view._allowed);
            if (dx == 0 && dy == 0)
                return false;

            view.ScrollTo(view._scrollX + dx, view._scrollY + dy);
            return true;
        }

        public override bool OnFling(MotionEvent? e1, MotionEvent e2, float velocityX, float velocityY)
        {
            var (vx, vy) = ViewportMath.FilterPan(-velocityX, -velocityY, view._allowed);
            if (vx == 0 && vy == 0 || view._scroller is not { } scroller)
                return false;

            scroller.Fling((int)view._scrollX, (int)view._scrollY, (int)vx, (int)vy,
                0, (int)Math.Max(0, (view._contentWidth * view.Scale) - view.Width),
                0, (int)Math.Max(0, (view._contentHeight * view.Scale) - view.Height));
            view.PostInvalidateOnAnimation();
            return true;
        }

        public override bool OnDoubleTap(MotionEvent e)
        {
            // Double tap zooms in, or back out; the document can forbid it with `touch-action`.
            if ((view._allowed & TouchActions.DoubleTapZoom) == 0)
                return false;

            view.ZoomBy(view._zoom > 1.01 ? 1 / view._zoom : 2, e.GetX(), e.GetY());
            return true;
        }
    }

    private sealed class ScaleListener(HtmlView view) : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        public override bool OnScaleBegin(ScaleGestureDetector detector)
        {
            view._scaling = true;
            return true;
        }

        public override bool OnScale(ScaleGestureDetector detector)
        {
            view.ZoomBy(detector.ScaleFactor, detector.FocusX, detector.FocusY);
            return true;
        }

        public override void OnScaleEnd(ScaleGestureDetector detector) => view._scaling = false;
    }

    // ---- the pointer state the document's :hover and :active rules read ----

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
            while (_document is { } document && _context is { } context && (_pendingPointer is not null || _clearPending))
            {
                PeachBrackets.Updates.UpdateResult result;
                using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1, 1));
                using var measuring = new PeachDrawing.Skia.SkiaComposer(context, surface, 1, 1);
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
                    _surface.Invalidate();
                }
            }
        }
        finally
        {
            _pumping = false;
        }
    }

    // ---- the copy menu after a long press ----

    private void StartActionMode()
    {
        if (_actionMode is not null)
            return;

        _actionMode = StartActionMode(new CopyMenu(this), ActionModeType.Floating);
    }

    private void EndActionMode()
    {
        _actionMode?.Finish();
        _actionMode = null;
    }

    private sealed class CopyMenu(HtmlView view) : ActionMode.Callback2
    {
        private const int Copy = 1;
        private const int SelectAllId = 2;

        public override bool OnCreateActionMode(ActionMode? mode, IMenu? menu)
        {
            menu?.Add(0, Copy, 0, "Copy");
            menu?.Add(0, SelectAllId, 1, "Select all");
            return true;
        }

        public override bool OnPrepareActionMode(ActionMode? mode, IMenu? menu) => false;

        public override bool OnActionItemClicked(ActionMode? mode, IMenuItem? item)
        {
            if (item?.ItemId == Copy)
                view.CopySelection();
            else if (item?.ItemId == SelectAllId)
                view.SelectAll();

            mode?.Finish();
            return true;
        }

        public override void OnDestroyActionMode(ActionMode? mode) => view._actionMode = null;

        public override void OnGetContentRect(ActionMode? mode, global::Android.Views.View? view2, global::Android.Graphics.Rect? outRect)
        {
            if (outRect is null)
                return;

            // Beside the selection when it has a caret, else at the top of the view.
            var region = view._document?.Selection.GetRegions().FirstOrDefault();
            if (region is { } r && r.PageIndex < view._pageTops.Length)
            {
                var scale = view.Scale;
                var left = (int)((r.Bounds.X * PixelsPerPoint * scale) - view._scrollX);
                var top = (int)(((view._pageTops[r.PageIndex] + (r.Bounds.Y * PixelsPerPoint)) * scale) - view._scrollY);
                outRect.Set(left, top, left + (int)(r.Bounds.Width * PixelsPerPoint * scale), top + (int)(r.Bounds.Height * PixelsPerPoint * scale));
            }
            else
            {
                outRect.Set(0, 0, view.Width, 1);
            }
        }
    }
}

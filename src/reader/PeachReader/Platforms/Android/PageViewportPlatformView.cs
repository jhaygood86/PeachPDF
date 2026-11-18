using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using PeachDrawing.PDF;
using PeachDrawing.Skia;
using PeachReader.Core;
using SkiaSharp;
using SkiaSharp.Views.Android;
using ATheme = Android.Content.Res.UiMode;

namespace PeachReader.Droid;

/// <summary>
/// The Android view behind <see cref="Views.PageViewport"/>: a Skia GL surface that draws each page as a sheet of paper (shadow, white, border) and its
/// content as 1024 px tiles kept as textures on the graphics hardware, painted by <see cref="PdfPageSource"/> through <see cref="SkiaCanvas"/> when a tile
/// first comes into view. Scrolling, fling, pinch zoom, double tap, tap on a link and long press to select are this view's own, over the same
/// <see cref="ViewportController"/> every platform uses.
/// </summary>
internal sealed class PageViewportPlatformView : FrameLayout
{
    private const int MaxCachedTiles = 24;
    private const int TilesPerFrame = 2;

    private readonly SKGLSurfaceView _surface;
    private readonly GestureDetector _gestures;
    private readonly ScaleGestureDetector _scale;
    private readonly OverScroller _scroller;
    private readonly float _density;
    private readonly SkiaRenderContext _context = new();
    private readonly Dictionary<(int Page, int Column, int Row), SKImage> _tiles = [];
    private readonly LinkedList<(int Page, int Column, int Row)> _tileOrder = new();

    private ViewportController? _controller;
    private Views.PageViewport? _owner;
    private double _tilesScale;
    private ReaderDocument? _tilesDocument;
    private GRContext? _tilesGpu;

    private volatile float _scrollX, _scrollY;
    private volatile float _pinch = 1;
    private float _pinchFocusX, _pinchFocusY;
    private bool _scaling, _selectingByTouch;

    internal PageViewportPlatformView(Context context) : base(context)
    {
        _density = context.Resources?.DisplayMetrics?.Density ?? 1;
        _surface = new SKGLSurfaceView(context) { RenderMode = Android.Opengl.Rendermode.WhenDirty };
        _surface.PaintSurface += OnPaintSurface;
        AddView(_surface, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        SetWillNotDraw(false);
        Clickable = true;
        Focusable = true;

        _scroller = new OverScroller(context);
        _gestures = new GestureDetector(context, new GestureListener(this));
        _gestures.IsLongpressEnabled = true;
        _scale = new ScaleGestureDetector(context, new ScaleListener(this));
    }

    private bool Dark => (Resources?.Configuration?.UiMode & ATheme.NightMask) == ATheme.NightYes;
    private SKColor Desk => Dark ? new SKColor(0x2B, 0x2B, 0x2B) : new SKColor(0xE8, 0xE8, 0xE8);
    private SKColor PageBorder => Dark ? new SKColor(0x55, 0x55, 0x55) : new SKColor(0xC8, 0xC8, 0xC8);

    // ------------------------------------------------------------------ connecting

    internal void Connect(Views.PageViewport owner, ViewportController? controller)
    {
        if (ReferenceEquals(_controller, controller) && ReferenceEquals(_owner, owner)) return;
        Disconnect();
        _owner = owner;
        _controller = controller;
        _scrollX = _scrollY = 0;
        if (controller is null)
        {
            _surface.RequestRender();
            return;
        }

        controller.LayoutChanged += OnLayoutChanged;
        controller.ScrollRequested += OnScrollRequested;
        controller.Invalidated += OnControllerInvalidated;
        if (Width > 0) controller.SetViewport(Width / _density, Height / _density);
        OnLayoutChanged();
    }

    internal void Disconnect()
    {
        if (_controller is not { } c) return;
        c.LayoutChanged -= OnLayoutChanged;
        c.ScrollRequested -= OnScrollRequested;
        c.Invalidated -= OnControllerInvalidated;
        _controller = null;
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        _controller?.SetViewport(w / _density, h / _density);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        Disconnect();
        lock (_tiles) ClearTiles();
        _context.Dispose();
    }

    // ------------------------------------------------------------------ layout and scrolling

    private float MaxScrollX => _controller is { } c ? Math.Max(0, (float)(c.Layout.ContentWidth * _density) - Width) : 0;
    private float MaxScrollY => _controller is { } c ? Math.Max(0, (float)(c.Layout.ContentHeight * _density) - Height) : 0;

    private void OnLayoutChanged()
    {
        Post(() =>
        {
            ClampScroll();
            _surface.RequestRender();
        });
    }

    private void OnScrollRequested(double yDip)
    {
        Post(() =>
        {
            _scroller.ForceFinished(true);
            _scrollY = Math.Clamp((float)(yDip * _density), 0, MaxScrollY);
            _controller?.SetScroll(_scrollY / _density);
            _surface.RequestRender();
        });
    }

    private void OnControllerInvalidated() => _surface.RequestRender();

    private void ClampScroll()
    {
        _scrollX = Math.Clamp(_scrollX, 0, MaxScrollX);
        _scrollY = Math.Clamp(_scrollY, 0, MaxScrollY);
        _controller?.SetScroll(_scrollY / _density);
    }

    private void ScrollTo(float x, float y)
    {
        _scrollX = Math.Clamp(x, 0, MaxScrollX);
        _scrollY = Math.Clamp(y, 0, MaxScrollY);
        _controller?.SetScroll(_scrollY / _density);
        _surface.RequestRender();
    }

    /// <inheritdoc />
    public override void ComputeScroll()
    {
        if (!_scroller.ComputeScrollOffset()) return;
        ScrollTo(_scroller.CurrX, _scroller.CurrY);
        PostInvalidateOnAnimation();
    }

    // ------------------------------------------------------------------ touch

    /// <inheritdoc />
    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e is null || _controller is null) return base.OnTouchEvent(e);
        _scale.OnTouchEvent(e);
        if (!_scaling) _gestures.OnTouchEvent(e);

        switch (e.ActionMasked)
        {
            case MotionEventActions.Move when _selectingByTouch:
                {
                    var (x, y) = Content(e.GetX(), e.GetY());
                    _controller.ExtendSelectionTo(x, y);
                    break;
                }
            case MotionEventActions.Up or MotionEventActions.Cancel when _selectingByTouch:
                _selectingByTouch = false;
                ShowSelectionMenu(e.GetX(), e.GetY());
                break;
        }

        return true;
    }

    /// <summary>The place in the content, in layout pixels, under a point of the view in device pixels.</summary>
    private (double X, double Y) Content(float x, float y) => ((x + _scrollX) / _density, (y + _scrollY) / _density);

    private void ShowSelectionMenu(float x, float y)
    {
        if (_controller is not { } c || !c.Selection.HasSelection) return;
        var menu = new PopupMenu(Context!, this);
        var items = menu.Menu!;
        items.Add(0, 1, 0, "Copy");
        items.Add(0, 2, 1, "Select all");
        items.Add(0, 3, 2, "Clear");
        menu.MenuItemClick += (_, args) =>
        {
            switch (args.Item?.ItemId)
            {
                case 1:
                    Copy(c);
                    c.Selection.Clear();
                    break;
                case 2:
                    c.Selection.SelectAll();
                    break;
                case 3:
                    c.Selection.Clear();
                    break;
            }
        };
        menu.Show();
    }

    private void Copy(ViewportController c)
    {
        string text = c.CopySelection();
        if (text.Length == 0 || Context?.GetSystemService(Context.ClipboardService) is not ClipboardManager clipboard) return;
        clipboard.PrimaryClip = ClipData.NewPlainText("PDF text", text);
        _owner?.RaiseTextCopied();
    }

    private sealed class GestureListener(PageViewportPlatformView view) : GestureDetector.SimpleOnGestureListener
    {
        public override bool OnDown(MotionEvent e)
        {
            view._scroller.ForceFinished(true);
            return true;
        }

        public override bool OnScroll(MotionEvent? e1, MotionEvent e2, float distanceX, float distanceY)
        {
            view.ScrollTo(view._scrollX + distanceX, view._scrollY + distanceY);
            return true;
        }

        public override bool OnFling(MotionEvent? e1, MotionEvent e2, float velocityX, float velocityY)
        {
            view._scroller.Fling((int)view._scrollX, (int)view._scrollY, (int)-velocityX, (int)-velocityY, 0, (int)view.MaxScrollX, 0, (int)view.MaxScrollY);
            view.PostInvalidateOnAnimation();
            return true;
        }

        public override bool OnSingleTapUp(MotionEvent e)
        {
            if (view._controller is not { } c) return false;
            var (x, y) = view.Content(e.GetX(), e.GetY());
            c.PointerDown(x, y);
            c.PointerUp(x, y);
            return true;
        }

        public override bool OnDoubleTap(MotionEvent e)
        {
            if (view._controller is not { } c) return false;
            // A double tap zooms in around the finger, or back to the width of the screen when already zoomed.
            if (c.Layout.Mode == ZoomMode.FitWidth) c.SetZoom(c.Layout.Zoom * 2, e.GetY() / view._density);
            else c.SetZoomMode(ZoomMode.FitWidth);
            return true;
        }

        public override void OnLongPress(MotionEvent e)
        {
            if (view._controller is not { } c) return;
            var (x, y) = view.Content(e.GetX(), e.GetY());
            c.PointerDown(x, y, clickCount: 2);
            c.PointerUp(x, y);
            if (c.Selection.HasSelection)
            {
                view._selectingByTouch = true;
                view.PerformHapticFeedback(FeedbackConstants.LongPress);
            }
        }
    }

    private sealed class ScaleListener(PageViewportPlatformView view) : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        public override bool OnScaleBegin(ScaleGestureDetector detector)
        {
            view._scaling = true;
            view._selectingByTouch = false;
            view._pinch = 1;
            (view._pinchFocusX, view._pinchFocusY) = (detector.FocusX, detector.FocusY);
            return true;
        }

        public override bool OnScale(ScaleGestureDetector detector)
        {
            if (view._controller is not { } c) return false;
            // The pages are drawn as they are, scaled about the fingers; the layout is made again once they let go.
            double wanted = c.Layout.Zoom * view._pinch * detector.ScaleFactor;
            double limited = Math.Clamp(wanted, PageLayoutModel.MinZoom, PageLayoutModel.MaxZoom);
            view._pinch = (float)(limited / c.Layout.Zoom);
            (view._pinchFocusX, view._pinchFocusY) = (detector.FocusX, detector.FocusY);
            view._surface.RequestRender();
            return true;
        }

        public override void OnScaleEnd(ScaleGestureDetector detector)
        {
            if (view._controller is { } c && Math.Abs(view._pinch - 1) > 0.001)
            {
                float factor = view._pinch;
                float focusX = view._pinchFocusX;
                view._pinch = 1;
                view._scrollX = Math.Max(0, (view._scrollX + focusX) * factor - focusX);
                c.SetZoom(c.Layout.Zoom * factor, view._pinchFocusY / view._density);
            }

            view._pinch = 1;
            view._scaling = false;
            view._surface.RequestRender();
        }
    }

    // ------------------------------------------------------------------ painting

    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(Desk);
        if (_controller is not { } controller) return;
        try
        {
            PaintFrame(canvas, e.Info, _surface.GRContext, controller);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("PeachReader", "paint failed: " + ex);
        }
    }

    private void PaintFrame(SKCanvas canvas, SKImageInfo info, GRContext? gpu, ViewportController controller)
    {
        var layout = controller.Layout;
        var document = controller.Document;
        double pxPerPoint = layout.Scale * _density;
        float scrollX = _scrollX, scrollY = _scrollY;
        _context.GpuContext = gpu;

        lock (_tiles)
        {
            if (!ReferenceEquals(_tilesDocument, document) || !ReferenceEquals(_tilesGpu, gpu) || _tilesScale != pxPerPoint)
            {
                ClearTiles();
                (_tilesDocument, _tilesGpu, _tilesScale) = (document, gpu, pxPerPoint);
            }
        }

        canvas.Save();
        float pinch = _pinch;
        if (pinch != 1)
        {
            canvas.Translate(_pinchFocusX, _pinchFocusY);
            canvas.Scale(pinch);
            canvas.Translate(-_pinchFocusX, -_pinchFocusY);
        }

        var (first, last) = layout.VisiblePages(scrollY / _density);
        int budget = TilesPerFrame;
        bool more = false;
        using var border = new SKPaint { Color = PageBorder, IsStroke = true, StrokeWidth = 1, IsAntialias = false };
        using var paper = new SKPaint { Color = SKColors.White };
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(Dark ? 110 : 64)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3 * _density) };

        for (int page = first; page <= last; page++)
        {
            var r = layout.PageRect(page);
            float x = (float)Math.Round(r.X * _density - scrollX), y = (float)Math.Round(r.Y * _density - scrollY);
            float w = (float)Math.Round(r.Width * _density), h = (float)Math.Round(r.Height * _density);

            canvas.DrawRect(x, y + 2 * _density, w, h, shadow);
            canvas.DrawRect(x, y, w, h, paper);

            var (pw, ph) = document.PageSizes[page];
            // The window of the screen in page pixels; while pinching the screen shows a scaled part of the layout, so the window is the inverse of that.
            double viewLeft = (0 - _pinchFocusX) / pinch + _pinchFocusX, viewRight = (info.Width - _pinchFocusX) / pinch + _pinchFocusX;
            double viewTop = (0 - _pinchFocusY) / pinch + _pinchFocusY, viewBottom = (info.Height - _pinchFocusY) / pinch + _pinchFocusY;
            var tiles = PageTiles.Visible(w, h, viewLeft - x, viewTop - y, viewRight - x, viewBottom - y);
            foreach (var tile in tiles)
            {
                var key = (page, tile.Column, tile.Row);
                SKImage? image;
                lock (_tiles)
                {
                    _tiles.TryGetValue(key, out image);
                    if (image is not null)
                    {
                        _tileOrder.Remove(key);
                        _tileOrder.AddFirst(key);
                    }
                }

                if (image is null)
                {
                    if (budget-- <= 0 || gpu is null) { more = true; continue; }
                    image = RenderTile(gpu, controller, page, tile, w / pw, h / ph, pw, ph);
                    if (image is null) continue;
                    lock (_tiles)
                    {
                        _tiles[key] = image;
                        _tileOrder.AddFirst(key);
                        while (_tileOrder.Count > MaxCachedTiles)
                        {
                            var oldest = _tileOrder.Last!.Value;
                            _tileOrder.RemoveLast();
                            if (_tiles.Remove(oldest, out var old)) old.Dispose();
                        }
                    }
                }

                canvas.DrawImage(image, x + tile.X, y + tile.Y);
            }

            canvas.DrawRect(x - 0.5f, y - 0.5f, w + 1, h + 1, border);
            PaintHighlights(canvas, controller, page, scrollX, scrollY);
        }

        canvas.Restore();
        if (more) _surface.RequestRender();
    }

    private SKImage? RenderTile(GRContext gpu, ViewportController controller, int page, PageTile tile, double scaleX, double scaleY, double widthPoints, double heightPoints)
    {
        using var surface = SKSurface.Create(gpu, true, new SKImageInfo(tile.Width, tile.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(-tile.X, -tile.Y);
        canvas.Scale((float)scaleX, (float)scaleY);
        IReadOnlyCollection<string> report;
        using (var target = SkiaCanvas.FromCanvas(_context, canvas, new PeachDrawing.Core.Rect(0, 0, widthPoints, heightPoints)))
        {
            var document = controller.Document;
            lock (document.PaintGate) report = document.Source.Paint(page, target, PdfAnnotationPaint.ExceptFormFields);
        }

        // The controller's listeners belong to the interface thread, and this is the graphics thread.
        if (report.Count > 0) Post(() => controller.ReportPaint(report));
        return surface.Snapshot();
    }

    private void PaintHighlights(SKCanvas canvas, ViewportController controller, int page, float scrollX, float scrollY)
    {
        var layout = controller.Layout;
        using var selection = new SKPaint { Color = new SKColor(0x30, 0x8C, 0xE8, 90) };
        using var found = new SKPaint { Color = new SKColor(255, 214, 0, 120) };
        using var current = new SKPaint { Color = new SKColor(255, 140, 0, 170) };

        foreach (var highlight in controller.HighlightsOnPage(page))
            foreach (var rect in highlight.Rects)
                DrawRect(canvas, layout.ToContent(page, rect), scrollX, scrollY, highlight.IsCurrent ? current : found);
        foreach (var rect in controller.Selection.RectsOnPage(page))
            DrawRect(canvas, layout.ToContent(page, rect), scrollX, scrollY, selection);
    }

    private void DrawRect(SKCanvas canvas, PeachDrawing.Core.Rect r, float scrollX, float scrollY, SKPaint paint) =>
        canvas.DrawRect((float)(r.X * _density - scrollX), (float)(r.Y * _density - scrollY), (float)(r.Width * _density), (float)(r.Height * _density), paint);

    private void ClearTiles()
    {
        foreach (var image in _tiles.Values) image.Dispose();
        _tiles.Clear();
        _tileOrder.Clear();
    }

    /// <summary>Copies the selection of the connected controller to the clipboard.</summary>
    internal void CopySelection()
    {
        if (_controller is { } c) Copy(c);
    }
}

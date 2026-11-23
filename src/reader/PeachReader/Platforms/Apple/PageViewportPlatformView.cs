using CoreAnimation;
using CoreGraphics;
using Foundation;
using PeachDrawing.CoreGraphics;
using PeachDrawing.PDF;
using PeachReader.Core;
using UIKit;

namespace PeachReader.Apple;

/// <summary>
/// The UIKit view behind <see cref="Views.PageViewport"/> on iOS and Mac Catalyst: a <see cref="UIScrollView"/> over a content view that holds one
/// <see cref="CALayer"/> sheet per visible page (shadow, white, border) and, on it, the page's content as 1024 px tile layers painted by
/// <see cref="PdfPageSource"/> through <see cref="CoreGraphicsCanvas"/> the first time they come into view, so Core Animation composites scrolling on the
/// graphics hardware and nothing repaints while the finger moves. Tap (links), double tap (zoom), long press (select), pinch (zoom) and, on a Mac, the
/// pointer (hover, cursor, drag to select) go to the same <see cref="ViewportController"/> every platform uses.
/// </summary>
internal sealed class PageViewportPlatformView : UIScrollView, IUIGestureRecognizerDelegate
{
    private const int MaxCachedTiles = 24;
    private const int TilesPerPass = 3;

    private readonly UIView _content = new();
    private readonly CoreGraphicsRenderContext _context = new();
    private readonly Dictionary<(int Page, int Column, int Row), CALayer> _tiles = [];
    private readonly LinkedList<(int Page, int Column, int Row)> _tileOrder = new();
    private readonly Dictionary<int, CALayer> _sheets = [];
    private readonly CALayer _overlay = CALayer.Create();
    private readonly bool _smoke = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PEACHREADER_SMOKE"));

    private ViewportController? _controller;
    private Views.PageViewport? _owner;
    private ReaderDocument? _tilesDocument;
    private double _tilesScale;
    private bool _refreshQueued;
    private nfloat _pinchStartZoom;
    private bool _selecting;
    private int _smokeTiles;
#if MACCATALYST
    private CGPoint _hover;
#endif

    internal PageViewportPlatformView()
    {
        AlwaysBounceVertical = true;
        ShowsHorizontalScrollIndicator = true;
        ContentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentBehavior.Never;
        _content.Layer.AddSublayer(_overlay);
        AddSubview(_content);

        var tap = new UITapGestureRecognizer(OnTap) { NumberOfTapsRequired = 1 };
        var doubleTap = new UITapGestureRecognizer(OnDoubleTap) { NumberOfTapsRequired = 2 };
        tap.RequireGestureRecognizerToFail(doubleTap);
        var longPress = new UILongPressGestureRecognizer(OnLongPress) { MinimumPressDuration = 0.4 };
        var pinch = new UIPinchGestureRecognizer(OnPinch);
        _content.UserInteractionEnabled = true;
        _content.AddGestureRecognizer(tap);
        _content.AddGestureRecognizer(doubleTap);
        _content.AddGestureRecognizer(longPress);
        AddGestureRecognizer(pinch);

#if MACCATALYST
        // The mouse drags to select, and the scroll view keeps touches and the wheel.
        PanGestureRecognizer.AllowedTouchTypes = [(NSNumber)(int)UITouchType.Direct];
        var drag = new UIPanGestureRecognizer(OnMouseDrag) { AllowedTouchTypes = [(NSNumber)(int)UITouchType.Indirect, (NSNumber)(int)UITouchType.IndirectPointer] };
        _content.AddGestureRecognizer(drag);
        _content.AddGestureRecognizer(new UIHoverGestureRecognizer(OnHover));
        _content.AddInteraction(new UIPointerInteraction(new PointerDelegate(this)));
#endif
        _content.AddInteraction(new UIEditMenuInteraction(new MenuDelegate(this)));
    }

    private bool Dark => TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark;
    private UIColor Desk => Dark ? UIColor.FromRGB(0x2B, 0x2B, 0x2B) : UIColor.FromRGB(0xE8, 0xE8, 0xE8);
    private nfloat Density => Window?.Screen?.Scale ?? UIScreen.MainScreen.Scale;

    // ------------------------------------------------------------------ connecting

    internal void Connect(Views.PageViewport owner, ViewportController? controller)
    {
        if (ReferenceEquals(_controller, controller) && ReferenceEquals(_owner, owner)) return;
        Disconnect();
        _owner = owner;
        _controller = controller;
        ContentOffset = CGPoint.Empty;
        if (controller is null)
        {
            ClearAll();
            return;
        }

        controller.LayoutChanged += OnLayoutChanged;
        controller.ScrollRequested += OnScrollRequested;
        controller.Invalidated += OnInvalidated;
        if (Bounds.Width > 0) controller.SetViewport(Bounds.Width, Bounds.Height);
        OnLayoutChanged();
    }

    internal void Disconnect()
    {
        if (_controller is not { } c) return;
        c.LayoutChanged -= OnLayoutChanged;
        c.ScrollRequested -= OnScrollRequested;
        c.Invalidated -= OnInvalidated;
        _controller = null;
    }

    /// <inheritdoc />
    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        BackgroundColor = Desk;
        _controller?.SetViewport(Bounds.Width, Bounds.Height);
        QueueRefresh();
    }

    /// <inheritdoc />
#pragma warning disable CA1422 // the replacement protocol is iOS 17 and later; the app supports 15
    public override void TraitCollectionDidChange(UITraitCollection? previousTraitCollection)
    {
        base.TraitCollectionDidChange(previousTraitCollection);
        BackgroundColor = Desk;
        ClearSheets();
        QueueRefresh();
    }
#pragma warning restore CA1422

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Disconnect();
            ClearAll();
            _context.Dispose();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ layout and scrolling

    private void OnLayoutChanged() => InvokeOnMainThread(() =>
    {
        if (_controller is not { } c) return;
        var size = new CGSize(c.Layout.ContentWidth, c.Layout.ContentHeight);
        ContentSize = size;
        _content.Frame = new CGRect(CGPoint.Empty, size);
        ClampOffset();
        ClearSheets();
        QueueRefresh();
    });

    private void OnScrollRequested(double y) => InvokeOnMainThread(() =>
    {
        if (_controller is null) return;
        double max = Math.Max(0, ContentSize.Height - Bounds.Height);
        SetContentOffset(new CGPoint(ContentOffset.X, Math.Clamp(y, 0, max)), false);
    });

    private void OnInvalidated() => InvokeOnMainThread(QueueRefresh);

    private void ClampOffset()
    {
        double maxX = Math.Max(0, ContentSize.Width - Bounds.Width), maxY = Math.Max(0, ContentSize.Height - Bounds.Height);
        ContentOffset = new CGPoint(Math.Clamp(ContentOffset.X, 0, maxX), Math.Clamp(ContentOffset.Y, 0, maxY));
    }

    /// <inheritdoc />
    public override CGPoint ContentOffset
    {
        get => base.ContentOffset;
        set
        {
            base.ContentOffset = value;
            _controller?.SetScroll(value.Y);
            QueueRefresh();
        }
    }

    // ------------------------------------------------------------------ painting

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            _refreshQueued = false;
            Refresh();
        });
    }

    private void Refresh()
    {
        if (_controller is not { } controller || Bounds.Width <= 0) return;
        var layout = controller.Layout;
        var document = controller.Document;
        double scale = Density;
        double pxPerPoint = layout.Scale * scale;
        if (!ReferenceEquals(_tilesDocument, document) || _tilesScale != pxPerPoint)
        {
            ClearTiles();
            (_tilesDocument, _tilesScale) = (document, pxPerPoint);
        }

        CATransaction.Begin();
        CATransaction.DisableActions = true;
        var (first, last) = layout.VisiblePages(ContentOffset.Y);
        var visible = new HashSet<int>();
        int budget = TilesPerPass;
        bool more = false;
        double viewLeft = ContentOffset.X, viewTop = ContentOffset.Y, viewRight = viewLeft + Bounds.Width, viewBottom = viewTop + Bounds.Height;

        for (int page = first; page <= last && page < document.PageCount; page++)
        {
            visible.Add(page);
            var r = layout.PageRect(page);
            var sheet = SheetFor(page, r);
            var (pw, ph) = document.PageSizes[page];
            double widthPx = Math.Round(r.Width * scale), heightPx = Math.Round(r.Height * scale);
            var tiles = PageTiles.Visible(widthPx, heightPx, (viewLeft - r.X) * scale, (viewTop - r.Y) * scale, (viewRight - r.X) * scale, (viewBottom - r.Y) * scale);
            foreach (var tile in tiles)
            {
                var key = (page, tile.Column, tile.Row);
                if (_tiles.TryGetValue(key, out var layer))
                {
                    _tileOrder.Remove(key);
                    _tileOrder.AddFirst(key);
                    continue;
                }

                if (budget-- <= 0) { more = true; continue; }
                using var image = RenderTile(controller, page, tile, widthPx / pw, heightPx / ph, pw, ph, scale);
                if (image is null) continue;
                layer = CALayer.Create();
                layer.Contents = image;
                layer.ContentsScale = (nfloat)scale;
                layer.Frame = new CGRect(tile.X / scale, tile.Y / scale, tile.Width / scale, tile.Height / scale);
                layer.MagnificationFilter = CALayer.FilterLinear;
                sheet.AddSublayer(layer);
                _tiles[key] = layer;
                _tileOrder.AddFirst(key);
                if (_smoke && ++_smokeTiles == 1) Console.WriteLine($"PEACH_SMOKE first tile painted page={page} pages={document.PageCount} tile={tile.Width}x{tile.Height}");
                while (_tileOrder.Count > MaxCachedTiles)
                {
                    var oldest = _tileOrder.Last!.Value;
                    _tileOrder.RemoveLast();
                    if (_tiles.Remove(oldest, out var old)) old.RemoveFromSuperLayer();
                }
            }
        }

        foreach (int page in _sheets.Keys.Where(p => !visible.Contains(p)).ToList())
        {
            _sheets[page].RemoveFromSuperLayer();
            _sheets.Remove(page);
            foreach (var key in _tiles.Keys.Where(k => k.Page == page).ToList())
            {
                _tiles.Remove(key);
                _tileOrder.Remove(key);
            }
        }

        PaintOverlay(controller, visible);
        CATransaction.Commit();
        if (more) QueueRefresh();
    }

    private CALayer SheetFor(int page, PeachDrawing.Core.Rect r)
    {
        var frame = new CGRect(Math.Round(r.X), Math.Round(r.Y), Math.Round(r.Width), Math.Round(r.Height));
        if (!_sheets.TryGetValue(page, out var sheet))
        {
            sheet = CALayer.Create();
            sheet.BackgroundColor = UIColor.White.CGColor;
            sheet.BorderWidth = 0.5f;
            sheet.BorderColor = (Dark ? UIColor.FromRGB(0x55, 0x55, 0x55) : UIColor.FromRGB(0xC8, 0xC8, 0xC8)).CGColor;
            sheet.ShadowColor = UIColor.Black.CGColor;
            sheet.ShadowOpacity = Dark ? 0.45f : 0.25f;
            sheet.ShadowRadius = 3;
            sheet.ShadowOffset = new CGSize(0, 2);
            sheet.MasksToBounds = false;
            _content.Layer.InsertSublayer(sheet, 0);
            _sheets[page] = sheet;
        }

        sheet.Frame = frame;
        return sheet;
    }

    private CGImage? RenderTile(ViewportController controller, int page, PageTile tile, double scaleX, double scaleY, double widthPoints, double heightPoints, double density)
    {
        using var colorSpace = CGColorSpace.CreateSrgb();
        using var bitmap = new CGBitmapContext(IntPtr.Zero, tile.Width, tile.Height, 8, 0, colorSpace, CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little);
        bitmap.SetFillColor(new CGColor(1, 1, 1, 1));
        bitmap.FillRect(new CGRect(0, 0, tile.Width, tile.Height));
        // Y down from the tile's top-left, as the canvas draws.
        bitmap.TranslateCTM(0, tile.Height);
        bitmap.ScaleCTM(1, -1);
        bitmap.TranslateCTM(-tile.X, -tile.Y);
        bitmap.ScaleCTM((nfloat)scaleX, (nfloat)scaleY);
        IReadOnlyCollection<string> report;
        using (var target = CoreGraphicsCanvas.FromContext(_context, bitmap, new PeachDrawing.Core.Rect(0, 0, widthPoints, heightPoints), originAtBottomLeft: false))
        {
            var document = controller.Document;
            lock (document.PaintGate) report = document.Source.Paint(page, target, PdfAnnotationPaint.ExceptFormFields);
        }

        if (report.Count > 0) controller.ReportPaint(report);
        return bitmap.ToImage();
    }

    private void PaintOverlay(ViewportController controller, HashSet<int> visible)
    {
        _overlay.Sublayers?.ToList().ForEach(l => l.RemoveFromSuperLayer());
        var layout = controller.Layout;
        var selection = UIColor.FromRGBA(0x30, 0x8C, 0xE8, 90).CGColor;
        var found = UIColor.FromRGBA(255, 214, 0, 120).CGColor;
        var current = UIColor.FromRGBA(255, 140, 0, 170).CGColor;

        void Add(PeachDrawing.Core.Rect rect, CGColor color)
        {
            var layer = CALayer.Create();
            layer.BackgroundColor = color;
            layer.Frame = new CGRect(rect.X, rect.Y, rect.Width, rect.Height);
            _overlay.AddSublayer(layer);
        }

        foreach (int page in visible)
        {
            foreach (var highlight in controller.HighlightsOnPage(page))
                foreach (var rect in highlight.Rects)
                    Add(layout.ToContent(page, rect), highlight.IsCurrent ? current : found);
            foreach (var rect in controller.Selection.RectsOnPage(page))
                Add(layout.ToContent(page, rect), selection);
        }

        _overlay.ZPosition = 10;
    }

    private void ClearSheets()
    {
        foreach (var sheet in _sheets.Values) sheet.RemoveFromSuperLayer();
        _sheets.Clear();
        _tiles.Clear();
        _tileOrder.Clear();
    }

    private void ClearTiles()
    {
        foreach (var layer in _tiles.Values) layer.RemoveFromSuperLayer();
        _tiles.Clear();
        _tileOrder.Clear();
    }

    private void ClearAll()
    {
        ClearSheets();
        _tilesDocument = null;
        _overlay.Sublayers?.ToList().ForEach(l => l.RemoveFromSuperLayer());
    }

    // ------------------------------------------------------------------ gestures

    private void OnTap(UITapGestureRecognizer r)
    {
        if (_controller is not { } c) return;
        var p = r.LocationInView(_content);
        c.PointerDown(p.X, p.Y);
        c.PointerUp(p.X, p.Y);
    }

    private void OnDoubleTap(UITapGestureRecognizer r)
    {
        if (_controller is not { } c) return;
        var p = r.LocationInView(this);
        // A double tap zooms in around the finger, or back to the width of the screen when already zoomed.
        if (c.Layout.Mode == ZoomMode.FitWidth) c.SetZoom(c.Layout.Zoom * 2, p.Y);
        else c.SetZoomMode(ZoomMode.FitWidth);
    }

    private void OnLongPress(UILongPressGestureRecognizer r)
    {
        if (_controller is not { } c) return;
        var p = r.LocationInView(_content);
        switch (r.State)
        {
            case UIGestureRecognizerState.Began:
                c.PointerDown(p.X, p.Y, clickCount: 2);
                c.PointerUp(p.X, p.Y);
                _selecting = c.Selection.HasSelection;
                break;
            case UIGestureRecognizerState.Changed when _selecting:
                c.ExtendSelectionTo(p.X, p.Y);
                break;
            case UIGestureRecognizerState.Ended or UIGestureRecognizerState.Cancelled:
                if (_selecting) ShowSelectionMenu(p);
                _selecting = false;
                break;
        }
    }

    private void OnPinch(UIPinchGestureRecognizer r)
    {
        if (_controller is not { } c) return;
        switch (r.State)
        {
            case UIGestureRecognizerState.Began:
                _pinchStartZoom = (nfloat)c.Layout.Zoom;
                break;
            case UIGestureRecognizerState.Ended:
                c.SetZoom(Math.Clamp(_pinchStartZoom * r.Scale, PageLayoutModel.MinZoom, PageLayoutModel.MaxZoom), r.LocationInView(this).Y);
                break;
        }
    }

#if MACCATALYST
    private void OnMouseDrag(UIPanGestureRecognizer r)
    {
        if (_controller is not { } c) return;
        var p = r.LocationInView(_content);
        switch (r.State)
        {
            case UIGestureRecognizerState.Began:
                // The press began where the pointer was before it moved.
                var start = new CGPoint(p.X - r.TranslationInView(_content).X, p.Y - r.TranslationInView(_content).Y);
                c.PointerDown(start.X, start.Y);
                break;
            case UIGestureRecognizerState.Changed:
                c.PointerMove(p.X, p.Y);
                break;
            case UIGestureRecognizerState.Ended or UIGestureRecognizerState.Cancelled:
                c.PointerUp(p.X, p.Y);
                break;
        }
    }

    private void OnHover(UIHoverGestureRecognizer r)
    {
        if (_controller is not { } c || r.State is not (UIGestureRecognizerState.Began or UIGestureRecognizerState.Changed)) return;
        var p = r.LocationInView(_content);
        _hover = p;
        c.PointerMove(p.X, p.Y);
    }

    private sealed class PointerDelegate(PageViewportPlatformView view) : UIPointerInteractionDelegate
    {
        public override UIPointerStyle? GetStyleForRegion(UIPointerInteraction interaction, UIPointerRegion region)
        {
            if (view._controller is not { } c) return null;
            var p = view._hover;
            return c.CursorAt(p.X, p.Y) switch
            {
                PointerCursor.IBeam => UIPointerStyle.Create(UIPointerShape.CreateBeam(16, UIAxis.Vertical), UIAxis.Vertical),
                _ => null,
            };
        }
    }
#endif

    // ------------------------------------------------------------------ selection menu and copying

    private void ShowSelectionMenu(CGPoint at)
    {
        if (_controller is not { } c || !c.Selection.HasSelection) return;
        var interaction = _content.Interactions.OfType<UIEditMenuInteraction>().FirstOrDefault();
        interaction?.PresentEditMenu(UIEditMenuConfiguration.Create(null, at));
    }

    private void Copy(ViewportController c)
    {
        string text = c.CopySelection();
        if (text.Length == 0) return;
        UIPasteboard.General.String = text;
        _owner?.RaiseTextCopied();
    }

    private sealed class MenuDelegate(PageViewportPlatformView view) : UIEditMenuInteractionDelegate
    {
        public override UIMenu GetMenu(UIEditMenuInteraction interaction, UIEditMenuConfiguration configuration, UIMenuElement[] suggestedActions)
        {
            var c = view._controller;
            if (c is null) return UIMenu.Create("", []);
            return UIMenu.Create("", [
                UIAction.Create("Copy", null, null, _ => { view.Copy(c); c.Selection.Clear(); }),
                UIAction.Create("Select All", null, null, _ => c.Selection.SelectAll()),
                UIAction.Create("Clear", null, null, _ => c.Selection.Clear())]);
        }
    }

    /// <summary>Copies the selection of the connected controller to the clipboard.</summary>
    internal void CopySelection()
    {
        if (_controller is { } c) Copy(c);
    }
}

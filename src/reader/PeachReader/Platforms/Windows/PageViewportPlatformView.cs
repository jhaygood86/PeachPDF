using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using PeachDrawing.PDF;
using PeachDrawing.Win2D;
using PeachReader.Core;
using DataPackage = global::Windows.ApplicationModel.DataTransfer.DataPackage;
using WClipboard = global::Windows.ApplicationModel.DataTransfer.Clipboard;
using Windows.System;
using Color = global::Windows.UI.Color;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using WScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using WScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;
using WSolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;

namespace PeachReader.WinUI;

/// <summary>
/// The WinUI view behind <see cref="Views.PageViewport"/>: a <see cref="CanvasVirtualControl"/> as large as all the pages stacked, inside a scroll
/// viewer, that paints each invalidated region with Win2D (the paper, its shadow and border, then the page through
/// <see cref="PdfPageSource.Paint(int, PeachDrawing.Core.Canvas, PdfAnnotationPaint, PdfTagSession?)"/>), and a second control above it for the
/// selection and search highlights, which move with a pointer and so must not repaint the pages.
/// </summary>
internal sealed class PageViewportPlatformView : Microsoft.UI.Xaml.Controls.ContentControl
{
    private static readonly Color DeskLight = Color.FromArgb(255, 0xE8, 0xE8, 0xE8), DeskDark = Color.FromArgb(255, 0x2B, 0x2B, 0x2B);
    private static readonly Color BorderLight = Color.FromArgb(255, 0xC8, 0xC8, 0xC8), BorderDark = Color.FromArgb(255, 0x55, 0x55, 0x55);

    private readonly WGrid _layers = new();
    private readonly WScrollViewer _scroller = new();
    private readonly CanvasVirtualControl _canvas = new();
    private readonly CanvasControl _overlay = new() { IsHitTestVisible = false };
    private Win2dRenderContext? _context;
    private CanvasRenderTarget? _target;
    private ViewportController? _controller;
    private Views.PageViewport? _owner;
    private DateTime _lastPress;
    private Windows_Point _lastPressAt;
    private int _clicks;
    private bool _dragging;

    private readonly record struct Windows_Point(double X, double Y);

    internal PageViewportPlatformView()
    {
        // A control, not a bare grid, so that it can take the keyboard focus the shortcuts and copy depend on.
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
        Content = _layers;
        Background = new WSolidColorBrush(Desk);
        _canvas.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
        _canvas.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top;
        _canvas.ClearColor = Desk;
        _canvas.RegionsInvalidated += OnRegionsInvalidated;
        _scroller.Content = _canvas;
        _scroller.HorizontalScrollBarVisibility = WScrollBarVisibility.Auto;
        _scroller.VerticalScrollBarVisibility = WScrollBarVisibility.Auto;
        _scroller.ViewChanged += (_, _) => OnViewChanged();
        _overlay.Draw += OnOverlayDraw;
        _layers.Children.Add(_scroller);
        _layers.Children.Add(_overlay);

        SizeChanged += (_, e) => _controller?.SetViewport(e.NewSize.Width, e.NewSize.Height);
        ActualThemeChanged += (_, _) => { Background = new WSolidColorBrush(Desk); _canvas.ClearColor = Desk; _canvas.Invalidate(); _overlay.Invalidate(); };
        Unloaded += (_, _) => Release();

        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), handledEventsToo: true);
        AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) => _controller?.PointerCancel()), handledEventsToo: true);
        _canvas.PointerWheelChanged += OnWheel;
        KeyDown += OnKeyDown;
    }

    private Color Desk => ActualTheme == ElementTheme.Dark ? DeskDark : DeskLight;
    private Color PageBorder => ActualTheme == ElementTheme.Dark ? BorderDark : BorderLight;

    /// <summary>Sets the controller whose pages this view shows, or none.</summary>
    internal void Connect(Views.PageViewport owner, ViewportController? controller)
    {
        if (ReferenceEquals(_controller, controller) && ReferenceEquals(_owner, owner)) return;
        Disconnect();
        _owner = owner;
        _controller = controller;
        if (controller is null)
        {
            _canvas.Invalidate();
            _overlay.Invalidate();
            return;
        }

        controller.LayoutChanged += OnLayoutChanged;
        controller.ScrollRequested += OnScrollRequested;
        controller.Invalidated += OnControllerInvalidated;
        if (ActualWidth > 0) controller.SetViewport(ActualWidth, ActualHeight);
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

    private void Release()
    {
        Disconnect();
        _canvas.RemoveFromVisualTree();
        _target?.Dispose();
        _target = null;
        _context?.Dispose();
        _context = null;
    }

    // ------------------------------------------------------------------ layout and scrolling

    private void OnLayoutChanged()
    {
        if (_controller is not { } c) return;
        _canvas.Width = c.Layout.ContentWidth;
        _canvas.Height = c.Layout.ContentHeight;
        _canvas.Invalidate();
        _overlay.Invalidate();
    }


    private void OnScrollRequested(double y)
    {
        // Asked for during a layout pass (a resize), when the scroll viewer must not be measured again; and the content has just been resized, so it
        // must have been measured before it can scroll to a place near its end. Both are met by doing it after the pass.
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _scroller.UpdateLayout();
                _scroller.ChangeView(null, y, null, disableAnimation: true);
            }
            catch (Exception ex)
            {
                CrashLog.Write("Scroll: " + ex);
            }
        });
    }

    private void OnViewChanged()
    {
        _controller?.SetScroll(_scroller.VerticalOffset);
        _overlay.Invalidate();
    }

    private void OnControllerInvalidated() => _overlay.Invalidate();

    // ------------------------------------------------------------------ painting the pages

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        try { PaintRegions(sender, args); }
        catch (Exception ex) { CrashLog.Write("RegionsInvalidated: " + ex); }
    }

    private void PaintRegions(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        var regions = args.InvalidatedRegions.ToList();
        if (regions.Count == 0) return;
        if (_controller is not { } controller)
        {
            foreach (var region in regions)
            {
                using var empty = sender.CreateDrawingSession(region);
                empty.Clear(Desk);
            }

            return;
        }

        if (_context is null || !ReferenceEquals(_context.Device, sender.Device))
        {
            _target?.Dispose();
            _target = null;
            _context?.Dispose();
            _context = new Win2dRenderContext(sender.Device);
        }

        // Every region is cut from one picture of what the regions cover together, so a page is read once however many pieces the surface asks for.
        double left = regions.Min(r => r.Left), top = regions.Min(r => r.Top);
        double right = regions.Max(r => r.Right), bottom = regions.Max(r => r.Bottom);
        var bound = new Windows.Foundation.Rect(left, top, right - left, bottom - top);
        var target = TargetFor(sender, bound);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Desk);
            PaintPages(session, target, bound, controller);
        }

        foreach (var region in regions)
        {
            using var session = sender.CreateDrawingSession(region);
            session.Blend = CanvasBlend.Copy;
            session.DrawImage(target, (float)region.Left, (float)region.Top,
                new Windows.Foundation.Rect(region.Left - bound.Left, region.Top - bound.Top, region.Width, region.Height));
        }
    }

    private CanvasRenderTarget TargetFor(CanvasVirtualControl sender, Windows.Foundation.Rect bound)
    {
        float width = (float)Math.Ceiling(bound.Width), height = (float)Math.Ceiling(bound.Height);
        if (_target is { } t && ReferenceEquals(t.Device, sender.Device) && t.Dpi == sender.Dpi && t.Size.Width >= width && t.Size.Height >= height) return t;
        _target?.Dispose();
        _target = new CanvasRenderTarget(sender, Math.Max(width, 256), Math.Max(height, 256), sender.Dpi,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);
        return _target;
    }

    private void PaintPages(CanvasDrawingSession session, CanvasRenderTarget target, Windows.Foundation.Rect bound, ViewportController controller)
    {
        var layout = controller.Layout;
        var document = controller.Document;
        for (int page = 0; page < layout.PageCount; page++)
        {
            var r = layout.PageRect(page);
            if (r.Y + r.Height + 16 < bound.Top || r.Y - 16 > bound.Bottom) continue;

            // Pages sit on whole pixels so their edges are crisp.
            float x = (float)(Math.Round(r.X) - bound.Left), y = (float)(Math.Round(r.Y) - bound.Top);
            float w = (float)Math.Round(r.Width), h = (float)Math.Round(r.Height);
            DrawShadow(session, x, y, w, h);
            session.FillRectangle(x, y, w, h, Colors_White);

            session.Transform = Matrix3x2.CreateScale((float)layout.Scale) * Matrix3x2.CreateTranslation(x, y);
            try
            {
                var (pw, ph) = document.PageSizes[page];
                using var canvas = Win2dCanvas.FromRenderTarget(_context!, target, session, new PeachDrawing.Core.Rect(0, 0, pw, ph));
                IReadOnlyCollection<string> report;
                lock (document.PaintGate) report = document.Source.Paint(page, canvas, PdfAnnotationPaint.ExceptFormFields);
                controller.ReportPaint(report);
            }
            finally
            {
                session.Transform = Matrix3x2.Identity;
            }

            session.DrawRectangle(x - 0.5f, y - 0.5f, w + 1, h + 1, PageBorder, 1);
        }
    }

    private static readonly Color Colors_White = Color.FromArgb(255, 255, 255, 255);

    // A soft shadow a little below the sheet, light enough to read as a sheet lying on the desk and not a card floating over it.
    private void DrawShadow(CanvasDrawingSession session, float x, float y, float w, float h)
    {
        using var list = new CanvasCommandList(session);
        using (var s = list.CreateDrawingSession()) s.FillRectangle(0, 0, w, h, Colors_White);
        bool dark = ActualTheme == ElementTheme.Dark;
        using var shadow = new ShadowEffect { Source = list, BlurAmount = 6, ShadowColor = Color.FromArgb((byte)(dark ? 110 : 64), 0, 0, 0) };
        session.DrawImage(shadow, x, y + 2);
    }

    // ------------------------------------------------------------------ highlights

    private void OnOverlayDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        try { DrawOverlay(args); }
        catch (Exception ex) { CrashLog.Write("OverlayDraw: " + ex); }
    }

    private void DrawOverlay(CanvasDrawEventArgs args)
    {
        if (_controller is not { } c) return;
        var layout = c.Layout;
        double scrollX = _scroller.HorizontalOffset, scrollY = _scroller.VerticalOffset;
        var (first, last) = layout.VisiblePages(scrollY);
        var selection = AccentColor with { A = 90 };
        var found = Color.FromArgb(120, 255, 214, 0);
        var current = Color.FromArgb(170, 255, 140, 0);
        var session = args.DrawingSession;

        for (int page = first; page <= last; page++)
        {
            foreach (var highlight in c.HighlightsOnPage(page))
                foreach (var rect in highlight.Rects)
                {
                    var r = layout.ToContent(page, rect);
                    session.FillRectangle((float)(r.X - scrollX), (float)(r.Y - scrollY), (float)r.Width, (float)r.Height, highlight.IsCurrent ? current : found);
                }

            foreach (var rect in c.Selection.RectsOnPage(page))
            {
                var r = layout.ToContent(page, rect);
                session.FillRectangle((float)(r.X - scrollX), (float)(r.Y - scrollY), (float)r.Width, (float)r.Height, selection);
            }
        }
    }

    private static Color AccentColor
    {
        get
        {
            try
            {
                var ui = new global::Windows.UI.ViewManagement.UISettings();
                return ui.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent);
            }
            catch (Exception)
            {
                return Color.FromArgb(255, 0, 120, 215);
            }
        }
    }

    // ------------------------------------------------------------------ pointer and keyboard

    private (double X, double Y) ContentPoint(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_canvas).Position;
        return (p.X, p.Y);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_controller is not { } c) return;
        Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        var (x, y) = ContentPoint(e);
        var now = DateTime.UtcNow;
        bool again = (now - _lastPress).TotalMilliseconds < 500 && Math.Abs(x - _lastPressAt.X) < 5 && Math.Abs(y - _lastPressAt.Y) < 5;
        _clicks = again ? Math.Min(3, _clicks + 1) : 1;
        (_lastPress, _lastPressAt) = (now, new Windows_Point(x, y));

        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
        // A finger pans the page (the scroll viewer's job); a mouse or pen selects.
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return;
        _dragging = true;
        CapturePointer(e.Pointer);
        c.PointerDown(x, y, _clicks, shift);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_controller is not { } c) return;
        var (x, y) = ContentPoint(e);
        if (_dragging)
        {
            c.PointerMove(x, y);
            return;
        }

        var shape = c.CursorAt(x, y) switch
        {
            PointerCursor.Hand => InputSystemCursorShape.Hand,
            PointerCursor.IBeam => InputSystemCursorShape.IBeam,
            _ => InputSystemCursorShape.Arrow,
        };
        ProtectedCursor = InputSystemCursor.Create(shape);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_controller is not { } c) return;
        var (x, y) = ContentPoint(e);
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            // A tap that did not become a pan follows a link.
            c.PointerDown(x, y);
            c.PointerUp(x, y);
            return;
        }

        if (!_dragging) return;
        _dragging = false;
        ReleasePointerCapture(e.Pointer);
        c.PointerUp(x, y);
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        if (_controller is not { } c) return;
        if (!InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
        int delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        double focus = e.GetCurrentPoint(this).Position.Y;
        c.ZoomBy(delta > 0 ? 1.1 : 1 / 1.1, focus);
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_controller is not { } c) return;
        bool ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
        double page = Math.Max(40, ActualHeight * 0.9);
        double? scroll = null;
        switch (e.Key)
        {
            case VirtualKey.C when ctrl:
                Copy(c);
                break;
            case VirtualKey.A when ctrl:
                c.Selection.SelectAll();
                break;
            case VirtualKey.Escape:
                c.Selection.Clear();
                break;
            case VirtualKey.Add or (VirtualKey)187 when ctrl:
                c.ZoomBy(1.1);
                break;
            case VirtualKey.Subtract or (VirtualKey)189 when ctrl:
                c.ZoomBy(1 / 1.1);
                break;
            case VirtualKey.Number0 or VirtualKey.NumberPad0 when ctrl:
                c.SetZoomMode(ZoomMode.FitWidth);
                break;
            case VirtualKey.PageDown or VirtualKey.Space:
                scroll = _scroller.VerticalOffset + page;
                break;
            case VirtualKey.PageUp:
                scroll = _scroller.VerticalOffset - page;
                break;
            case VirtualKey.Down:
                scroll = _scroller.VerticalOffset + 48;
                break;
            case VirtualKey.Up:
                scroll = _scroller.VerticalOffset - 48;
                break;
            case VirtualKey.Home:
                scroll = 0;
                break;
            case VirtualKey.End:
                scroll = c.Layout.ContentHeight;
                break;
            default:
                return;
        }

        if (scroll is { } y) _scroller.ChangeView(null, Math.Clamp(y, 0, Math.Max(0, c.Layout.ContentHeight - ActualHeight)), null, disableAnimation: true);
        e.Handled = true;
    }

    /// <summary>Puts the selected text on the clipboard.</summary>
    internal void Copy(ViewportController c)
    {
        string text = c.CopySelection();
        if (text.Length == 0) return;
        var package = new DataPackage();
        package.SetText(text);
        WClipboard.SetContent(package);
        try { WClipboard.Flush(); }
        catch (Exception) { /* another program holds the clipboard */ }
        _owner?.RaiseTextCopied();
    }

    /// <summary>Copies the selection of the connected controller.</summary>
    internal void CopySelection()
    {
        if (_controller is { } c) Copy(c);
    }
}

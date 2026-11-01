using Android.Content;
using Android.Content.Res;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using PeachBrackets.Android.Logic;
using PeachDrawing.Core;
using PeachDrawing.Skia;
using SkiaSharp;
using SkiaSharp.Views.Android;

namespace PeachBrackets.Android;

/// <summary>
/// A view that shows an HTML document laid out by PeachBrackets as one scrolling, zoomable surface. It follows what the document asks for - the CSS
/// <c>cursor</c>, the <c>:hover</c> and <c>:active</c> rules, <c>pointer-events</c>, <c>touch-action</c> and <c>user-select</c> - and turns taps,
/// long presses, flings and pinches into the behaviour a reader expects, exposing each as an event an application can hook.
/// </summary>
/// <remarks>
/// <para>
/// Set <see cref="Html"/> and the view loads it, lays it out for its own width and paints only the part in view. One CSS pixel is one density
/// independent pixel, so a page looks as it does in a mobile browser. The document is laid out again when the view is resized or the theme changes.
/// </para>
/// <para>
/// Text is drawn with the fonts of the device (Roboto and the Noto families, read from <c>/system/fonts</c>) and with any fonts the document brings.
/// Limits of this first version: form controls are painted but not interactive, text is not exposed to accessibility services, a custom
/// <c>url()</c> cursor is shown as its keyword, selection has no drag handles (a drag after a long press extends it), and zooming re-renders
/// at the new scale, which is slow on very large pages.
/// </para>
/// </remarks>
public sealed partial class HtmlView : FrameLayout
{
    private const double PixelsPerPoint = 96.0 / 72.0;
    private const double PageGap = 8;
    private const double ContinuousPageHeightPoints = 14000;
    private const double MinZoom = 0.5;
    private const double MaxZoom = 4;

    private readonly SKCanvasView _surface;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private SkiaRenderContext? _context;
    private HtmlDocument? _document;
    private double[] _pageTops = [];
    private double[] _pageHeights = [];
    private double _contentWidth;
    private double _contentHeight;
    private double _scrollX;
    private double _scrollY;
    private double _zoom = 1;
    private double _density = 1;
    private double _loadedWidth = -1;
    private int _loadVersion;
    private string? _html;
    private bool _attached;
    private Action? _reflow;

    /// <summary>Creates a view with nothing in it.</summary>
    /// <param name="context">the context the view lives in</param>
    public HtmlView(Context context) : base(context)
    {
        _surface = new SKCanvasView(context) { IgnorePixelScaling = false }; // full resolution: the surface is in device pixels, not dp
        _surface.PaintSurface += OnPaintSurface;
        AddView(_surface, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _density = context.Resources?.DisplayMetrics?.Density ?? 1;
        Focusable = true;
        FocusableInTouchMode = true;
        InitTouch(context);
    }

    /// <summary>Creates a view from a layout file.</summary>
    /// <param name="context">the context the view lives in</param>
    /// <param name="attributes">the attributes set in the layout</param>
    public HtmlView(Context context, IAttributeSet attributes) : this(context)
    {
        _ = attributes;
    }

    /// <summary>The HTML to show. Setting it loads and lays out the document.</summary>
    public string? Html
    {
        get => _html;
        set
        {
            _html = value;
            if (_attached)
                _ = ReloadAsync();
        }
    }

    /// <summary>The colour behind the page.</summary>
    public SKColor PageBackground { get; set; } = SKColors.White;

    /// <summary>
    /// The pointing devices the document's <c>hover</c> and <c>pointer</c> media queries report. <see langword="null"/> (the default) asks the
    /// system which are attached.
    /// </summary>
    public InputCapabilities? Input { get; set; }

    /// <summary>The loaded document, or <see langword="null"/> before one has loaded. It is replaced when the view reloads.</summary>
    public HtmlDocument? Document => _document;

    /// <summary>How far the page is zoomed: 1 is one CSS pixel to one density independent pixel.</summary>
    public double Zoom => _zoom;

    /// <summary>The text that is selected, or an empty string.</summary>
    public string SelectedText => _document is { } document && document.Selection.HasSelection ? document.Selection.GetText() : string.Empty;

    /// <summary>Raised when a document has loaded and laid out.</summary>
    public event EventHandler? DocumentLoaded;

    /// <summary>Raised when a document could not be loaded.</summary>
    public event EventHandler<HtmlLoadFailedEventArgs>? DocumentFailed;

    /// <summary>Raised before a link is followed. Set <see cref="HtmlLinkTappedEventArgs.Cancel"/> to handle it yourself.</summary>
    public event EventHandler<HtmlLinkTappedEventArgs>? LinkTapped;

    /// <summary>Raised when a finger goes down, after the document has said which gestures it allows there; the application may take gestures away.</summary>
    public event EventHandler<HtmlTouchStartedEventArgs>? TouchStarted;

    /// <summary>Raised when the page is tapped (a short touch that is not a link and not a scroll).</summary>
    public event EventHandler<HtmlTappedEventArgs>? Tapped;

    /// <summary>Raised when the page is long-pressed, before the word under the finger is selected.</summary>
    public event EventHandler<HtmlTappedEventArgs>? LongPressed;

    /// <summary>Raised when the scroll position changes.</summary>
    public event EventHandler? ScrollChanged;

    /// <summary>Raised when the zoom changes.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Puts the selected text on the clipboard.</summary>
    public void CopySelection()
    {
        var text = SelectedText;
        if (text.Length == 0)
            return;

        if (Context?.GetSystemService(Context.ClipboardService) is ClipboardManager clipboard)
            clipboard.PrimaryClip = ClipData.NewPlainText("PeachBrackets", text);
    }

    /// <summary>Selects all the text.</summary>
    public void SelectAll()
    {
        if (_document is { } document)
            document.Selection.SelectAll();
    }

    /// <summary>Scrolls so that the element with the given <c>id</c> is at the top.</summary>
    /// <param name="elementId">the element's <c>id</c></param>
    /// <returns><see langword="true"/> when there is such an element</returns>
    public bool ScrollToElement(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);
        if (_document is not { } document || document.GetElementBounds(elementId) is not { } bounds)
            return false;

        for (var page = 0; page < _pageTops.Length; page++)
        {
            if (bounds.Y * PixelsPerPoint < _pageHeights[page])
            {
                ScrollTo(_scrollX, (_pageTops[page] + (bounds.Y * PixelsPerPoint)) * Scale);
                return true;
            }
        }

        return false;
    }

    private double Scale => _density * _zoom;

    /// <inheritdoc />
    protected override void OnAttachedToWindow()
    {
        base.OnAttachedToWindow();
        _attached = true;
        _context ??= new SkiaRenderContext { PixelsPerPoint = PixelsPerPoint };
        _ = ReloadAsync();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromWindow()
    {
        _attached = false;
        _loadVersion++;
        EndActionMode();
        if (_reflow is not null)
            _handler.RemoveCallbacks(_reflow);

        _document?.Dispose();
        _document = null;
        _context?.Dispose();
        _context = null;
        base.OnDetachedFromWindow();
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        if (_attached && Math.Abs(w - (_loadedWidth * _density)) > 1)
            QueueReflow();
    }

    /// <inheritdoc />
    protected override void OnConfigurationChanged(Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        _density = Context?.Resources?.DisplayMetrics?.Density ?? _density;
        if (_attached)
            QueueReflow();
    }

    // A resize or a theme change reflows the document, but only once it has settled.
    private void QueueReflow()
    {
        if (_reflow is not null)
            _handler.RemoveCallbacks(_reflow);

        _reflow = () => _ = ReloadAsync();
        _handler.PostDelayed(_reflow, 150);
    }

    private async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        var widthDp = Width / _density;
        if (_context is null || _html is null || widthDp < 1)
            return;

        try
        {
            await LoadDeviceFontsAsync(_context);
            var options = BuildOptions(widthDp);
            var document = await HtmlDocument.LoadAsync(_html, _context, options);
            using (var surface = SKSurface.Create(new SKImageInfo(1, 1)))
            using (var measuring = new SkiaComposer(_context, surface, 1, 1))
                await document.LayoutAsync(measuring.Canvas);

            if (version != _loadVersion)
            {
                document.Dispose();
                return;
            }

            _document?.Dispose();
            _document = document;
            _loadedWidth = widthDp;
            document.Selection.SelectionChanged += (_, _) => _surface.Invalidate();
            UpdateMetrics();
            ScrollTo(_scrollX, _scrollY);
            _surface.Invalidate();
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (version == _loadVersion)
        {
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
    }

    private HtmlDocumentOptions BuildOptions(double widthDp) => new()
    {
        Media = "screen",
        PageWidthPoints = widthDp / PixelsPerPoint,
        PageHeightPoints = ContinuousPageHeightPoints,
        MarginTopPoints = 0,
        MarginRightPoints = 0,
        MarginBottomPoints = 0,
        MarginLeftPoints = 0,
        RecordTextRuns = true,
        EnableSelectionStyling = true,
        CollectFormFields = true,
        ColorScheme = IsNightMode() ? ColorSchemePreference.Dark : ColorSchemePreference.Light,
        Input = Input ?? DetectInput(),
    };

    private bool IsNightMode() =>
        ((Context?.Resources?.Configuration?.UiMode ?? 0) & UiMode.NightMask) == UiMode.NightYes;

    private static InputCapabilities DetectInput()
    {
        bool mouse = false, stylus = false, touch = false;
        foreach (var id in InputDevice.GetDeviceIds() ?? [])
        {
            var sources = InputDevice.GetDevice(id)?.Sources ?? 0;
            mouse |= (sources & InputSourceType.Mouse) == InputSourceType.Mouse || (sources & InputSourceType.Touchpad) == InputSourceType.Touchpad;
            stylus |= (sources & InputSourceType.Stylus) == InputSourceType.Stylus;
            touch |= (sources & InputSourceType.Touchscreen) == InputSourceType.Touchscreen;
        }

        return AndroidInput.Capabilities(mouse, stylus, touch);
    }

    // The device's own fonts: the engine reads font files, so the system families are registered by name, and the generic CSS families
    // are pointed at the nearest of them.
    private static async Task LoadDeviceFontsAsync(SkiaRenderContext context)
    {
        if (s_fontsLoadedInto.Contains(context))
            return;

        s_fontsLoadedInto.Add(context);
        foreach (var (file, family) in new[]
        {
            ("Roboto-Regular.ttf", "sans-serif"), ("Roboto-Regular.ttf", "Roboto"), ("NotoSerif-Regular.ttf", "serif"),
            ("DroidSansMono.ttf", "monospace"), ("CutiveMono.ttf", "monospace"),
        })
        {
            var path = "/system/fonts/" + file;
            if (!File.Exists(path))
                continue;

            await using var stream = File.OpenRead(path);
            await context.AddFont(stream, family);
        }
    }

    private static readonly HashSet<SkiaRenderContext> s_fontsLoadedInto = [];

    // The pages stack one under another with a small gap; the content is as big as all of them.
    private void UpdateMetrics()
    {
        var pages = _document?.Pages ?? [];
        _pageTops = new double[pages.Count];
        _pageHeights = new double[pages.Count];
        var top = PageGap / 2;
        var width = 0d;
        for (var i = 0; i < pages.Count; i++)
        {
            _pageTops[i] = top;
            _pageHeights[i] = pages[i].HeightPoints * PixelsPerPoint;
            top += _pageHeights[i] + PageGap;
            width = Math.Max(width, pages[i].WidthPoints * PixelsPerPoint);
        }

        _contentWidth = width;
        _contentHeight = top;
    }

    private void ScrollTo(double x, double y)
    {
        var newX = ViewportMath.ClampScroll(x, _contentWidth * Scale, Width);
        var newY = ViewportMath.ClampScroll(y, _contentHeight * Scale, Height);
        if (newX == _scrollX && newY == _scrollY)
            return;

        _scrollX = newX;
        _scrollY = newY;
        _surface.Invalidate();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(PageBackground);
        var document = _document;
        var context = _context;
        if (document is null || context is null)
            return;

        var scale = (float)Scale;
        var visibleTop = _scrollY / Scale;
        var visibleBottom = (_scrollY + e.Info.Height) / Scale;
        var visibleLeft = _scrollX / Scale;
        var visibleRight = (_scrollX + e.Info.Width) / Scale;

        for (var page = 0; page < _pageTops.Length; page++)
        {
            var top = _pageTops[page];
            if (top + _pageHeights[page] < visibleTop || top > visibleBottom)
                continue;

            var size = document.Pages[page];
            canvas.Save();
            canvas.Translate((float)-_scrollX, (float)-_scrollY);
            canvas.Scale(scale);
            canvas.Translate(0, (float)top);

            // Only the part of the page in view, in points from the top-left of the sheet.
            var region = new PeachDrawing.Core.Rect(
                Math.Max(0, visibleLeft) / PixelsPerPoint, Math.Max(0, visibleTop - top) / PixelsPerPoint,
                (visibleRight - visibleLeft) / PixelsPerPoint, (visibleBottom - Math.Max(visibleTop, top)) / PixelsPerPoint);

            using (var target = SkiaCanvas.FromCanvas(context, canvas, new PeachDrawing.Core.Rect(0, 0, size.WidthPoints * PixelsPerPoint, size.HeightPoints * PixelsPerPoint)))
            {
                // Painting finishes without waiting for anything (what it draws was resolved during layout), so the task is already complete.
                document.PaintPageRegionAsync(page, target, region).GetAwaiter().GetResult();
            }

            canvas.Restore();
        }
    }
}

/// <summary>Raised when the page is tapped or long-pressed.</summary>
public sealed class HtmlTappedEventArgs : EventArgs
{
    internal HtmlTappedEventArgs(int pageIndex, double x, double y)
    {
        PageIndex = pageIndex;
        X = x;
        Y = y;
    }

    /// <summary>The page, or -1 when the point is not on a page.</summary>
    public int PageIndex { get; }

    /// <summary>The distance from the left edge of the page, in points.</summary>
    public double X { get; }

    /// <summary>The distance from the top edge of the page, in points.</summary>
    public double Y { get; }
}

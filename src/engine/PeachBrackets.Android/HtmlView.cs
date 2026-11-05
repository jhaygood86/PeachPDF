using Android.Content;
using Android.Content.Res;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Views.Accessibility;
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
    private double _loadedViewportDp = -1;
    private const double MinZoom = 0.5;
    private const double MaxZoom = 4;

    private readonly View _surface;
    private readonly SKGLSurfaceView? _glSurface;
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
    private System.Action? _reflow;

    /// <summary>Creates a view with nothing in it, drawn on the graphics hardware.</summary>
    /// <param name="context">the context the view lives in</param>
    public HtmlView(Context context) : this(context, useGpu: true)
    {
    }

    /// <summary>Creates a view with nothing in it.</summary>
    /// <param name="context">the context the view lives in</param>
    /// <param name="useGpu">
    /// whether the page is drawn on the graphics hardware (an OpenGL surface, with the painted page kept as textures while it scrolls and
    /// zooms) or on the CPU, which suits a device or emulator without a working GL driver
    /// </param>
    public HtmlView(Context context, bool useGpu) : base(context)
    {
        if (useGpu)
        {
            // Full resolution: the surface is in device pixels, not dp.
            _glSurface = new SKGLSurfaceView(context);
            _glSurface.RenderMode = global::Android.Opengl.Rendermode.WhenDirty; // drawn when asked to, not continuously
            _glSurface.PaintSurface += OnPaintGlSurface;
            _surface = _glSurface;
        }
        else
        {
            var cpu = new SKCanvasView(context) { IgnorePixelScaling = false };
            cpu.PaintSurface += OnPaintSurface;
            _surface = cpu;
        }

        AddView(_surface, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        _density = context.Resources?.DisplayMetrics?.Density ?? 1;
        Focusable = true;
        FocusableInTouchMode = true;
        InitTouch(context);
    }

    /// <summary>Creates a view from a layout file.</summary>
    /// <param name="context">the context the view lives in</param>
    /// <param name="attributes">the attributes set in the layout</param>
    public HtmlView(Context context, IAttributeSet attributes) : this(context, useGpu: true)
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

    /// <summary>
    /// Whether the document may load images, style sheets and fonts from <c>http</c> and <c>https</c> addresses. Off by default, so a document cannot make
    /// the application call out; <c>data:</c> URIs and files are always read. Takes effect on the next load.
    /// </summary>
    public bool AllowNetworkAccess { get; set; }

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

    /// <summary>Raised when a submit button of a form is tapped, with the form as it would be submitted. The view sends nothing itself.</summary>
    public event EventHandler<HtmlFormSubmittingEventArgs>? FormSubmitting;

    /// <summary>
    /// Raised instead of <see cref="FormSubmitting"/> when a submit was tried and a control's value does not satisfy its constraints (<c>required</c>,
    /// <c>pattern</c>, <c>min</c>... unless the form has <c>novalidate</c>). The first such control has been given the focus.
    /// </summary>
    public event EventHandler<HtmlFormInvalidEventArgs>? FormInvalid;

    // Submits the form a button belongs to (at a point, for an image button); false when the element is not a submit button in a form.
    private bool SubmitForm(PeachBrackets.Updates.DocumentElement submitter, int page = -1, double x = 0, double y = 0) =>
        _document is { } document
        && Submit(document, submitter, page >= 0 ? document.CreateFormSubmission(submitter, page, x, y) : document.CreateFormSubmission(submitter));

    // Submits the form a field belongs to, as Enter or the keyboard's action key does; false when that does not submit it.
    private bool SubmitFromField(PeachBrackets.Updates.DocumentElement field) =>
        _document is { } document && Submit(document, field, document.CreateImplicitSubmission(field));

    // Raises FormSubmitting - or, when a value is not valid, gives the first such control the focus and raises FormInvalid.
    private bool Submit(PeachBrackets.HtmlDocument document, PeachBrackets.Updates.DocumentElement trigger, PeachBrackets.Updates.FormSubmission? submission)
    {
        if (submission is null)
            return false;

        if (document.GetValidationProblems(trigger) is { Count: > 0 } problems)
        {
            QueueFocus(() => FocusElementAsync(document, problems[0].Element));
            FormInvalid?.Invoke(this, new HtmlFormInvalidEventArgs(problems));
            return true;
        }

        FormSubmitting?.Invoke(this, new HtmlFormSubmittingEventArgs(submission));
        return true;
    }

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
        _context ??= new HtmlViewRenderContext(() => AllowNetworkAccess) { PixelsPerPoint = PixelsPerPoint };
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

        ResetTiles();
        _document?.Dispose();
        _document = null;
        RetireContext(_context);
        _context = null;
        base.OnDetachedFromWindow();
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        // The view's height is the viewport `vh` resolves against, so it reflows the document as well as the width.
        if (_attached && (Math.Abs(w - (_loadedWidth * _density)) > 1 || (_loadedViewportDp > 0 && Math.Abs(h - (_loadedViewportDp * _density)) > 1)))
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
        if (_context is not { } context || _html is not { } html || widthDp < 1)
            return;

        var options = BuildOptions(widthDp);
        BeginLoad(context);
        try
        {
            // Reading the fonts, parsing, styling and laying out the document is seconds of work on a cold runtime and a slow device, and Android reports
            // an application that does not take an input event within five seconds as not responding. So it runs on a pool thread and the view goes on
            // taking input; what follows (taking the document in, the first paint) is quick and needs the main thread.
            var document = await Task.Run(() => LoadDocumentAsync(html, context, options));

            if (version != _loadVersion)
            {
                document.Dispose();
                return;
            }

            _document?.Dispose();
            _document = document;
            _loadedWidth = widthDp;
            document.Selection.SelectionChanged += (_, _) => InvalidateContent();
            UpdateMetrics();
            UpdateViewport();
            RefreshAccessibility(document);
            ScrollTo(_scrollX, _scrollY);
            InvalidateContent();
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (version == _loadVersion)
        {
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
        finally
        {
            EndLoad(context);
        }
    }

    // The part of a load that does not touch the view: it runs on a pool thread, so it uses only what it is handed.
    private static async Task<HtmlDocument> LoadDocumentAsync(string html, SkiaRenderContext context, HtmlDocumentOptions options)
    {
        await LoadDeviceFontsAsync(context);
        var document = await HtmlDocument.LoadAsync(html, context, options);
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(1, 1));
            using var measuring = new SkiaComposer(context, surface, 1, 1);
            await document.LayoutAsync(measuring.Canvas);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    // A load that is running uses its render context from a pool thread, so a context the view lets go of (it left the window) is only disposed once the
    // last load that holds it has finished. All of this runs on the main thread.
    private readonly Dictionary<SkiaRenderContext, int> _loadsUsing = [];
    private readonly HashSet<SkiaRenderContext> _retiredContexts = [];

    private void BeginLoad(SkiaRenderContext context) => _loadsUsing[context] = _loadsUsing.GetValueOrDefault(context) + 1;

    private void EndLoad(SkiaRenderContext context)
    {
        if (--_loadsUsing[context] > 0)
            return;

        _loadsUsing.Remove(context);
        if (_retiredContexts.Remove(context))
            context.Dispose();
    }

    private void RetireContext(SkiaRenderContext? context)
    {
        if (context is null)
            return;

        if (_loadsUsing.ContainsKey(context))
            _retiredContexts.Add(context);
        else
            context.Dispose();
    }

    private HtmlDocumentOptions BuildOptions(double widthDp) => new()
    {
        MediaType = MediaType.Screen,
        PageWidthPoints = widthDp / PixelsPerPoint,
        ViewportHeightPoints = PinViewport() is { } viewportDp ? viewportDp / PixelsPerPoint : null,
        MarginTopPoints = 0,
        MarginRightPoints = 0,
        MarginBottomPoints = 0,
        MarginLeftPoints = 0,
        RecordTextRuns = true,
        EnableSelectionStyling = true,
        CollectFormFields = true,
        PaintFormControls = true,
        ColorScheme = IsNightMode() ? ColorSchemePreference.Dark : ColorSchemePreference.Light,
        Input = Input ?? DetectInput(),
        DevicePixelRatio = Logic.ImageScale.DevicePixelRatio(_density),
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
    // are pointed at the nearest of them. Once per render context: a load that starts while another is still reading them waits for the same work.
    private static Task LoadDeviceFontsAsync(SkiaRenderContext context)
    {
        lock (s_fontsGate)
        {
            if (!s_fontsLoadedInto.TryGetValue(context, out var load) || load.IsFaulted)
            {
                load = AddDeviceFontsAsync(context);
                s_fontsLoadedInto.AddOrUpdate(context, load);
            }

            return load;
        }
    }

    private static async Task AddDeviceFontsAsync(SkiaRenderContext context)
    {
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

    private static readonly object s_fontsGate = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SkiaRenderContext, Task> s_fontsLoadedInto = new();

    // The height `vh` and a percentage root height resolve against, pinned when a document is loaded so it never follows the content.
    private double? PinViewport()
    {
        _loadedViewportDp = Height > 1 && _density > 0 ? Height / _density : -1;
        return _loadedViewportDp > 0 ? _loadedViewportDp : null;
    }

    // A screen document is one surface, so there is a single "page" at the top and no gap around it.
    private void UpdateMetrics()
    {
        var pages = _document?.Pages ?? [];
        _pageTops = new double[pages.Count];
        _pageHeights = new double[pages.Count];
        var top = 0d;
        var width = 0d;
        for (var i = 0; i < pages.Count; i++)
        {
            _pageTops[i] = top;
            _pageHeights[i] = pages[i].HeightPoints * PixelsPerPoint;
            top += _pageHeights[i];
            width = Math.Max(width, pages[i].WidthPoints * PixelsPerPoint);
        }

        _contentWidth = width;
        _contentHeight = top;
    }

    // Tells the document which part of it is showing, so a fixed box stays where it was put in the view and a sticky one follows the top of it.
    private void UpdateViewport()
    {
        if (_document is not { } document || Width <= 0 || Height <= 0)
            return;

        var perPoint = Scale * PixelsPerPoint;
        document.SetViewport(new PeachDrawing.Core.Rect(_scrollX / perPoint, _scrollY / perPoint, Width / perPoint, Height / perPoint));
    }

    private void ScrollTo(double x, double y)
    {
        var newX = ViewportMath.ClampScroll(x, _contentWidth * Scale, Width);
        var newY = ViewportMath.ClampScroll(y, _contentHeight * Scale, Height);
        if (newX == _scrollX && newY == _scrollY)
            return;

        _scrollX = newX;
        _scrollY = newY;
        UpdateViewport();
        RequestPaint();
        SendAccessibilityEvent(EventTypes.ViewScrolled);
        ScrollChanged?.Invoke(this, EventArgs.Empty);
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

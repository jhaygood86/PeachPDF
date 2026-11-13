using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using PeachDrawing.Skia;
using PeachDrawing.Text;
using SkiaSharp;

namespace PeachBrackets.Avalonia;

/// <summary>
/// An Avalonia control that shows an HTML document laid out by PeachBrackets as one scrolling, zoomable surface. It follows what the document asks for - the CSS
/// <c>cursor</c>, the <c>:hover</c> and <c>:active</c> rules, <c>pointer-events</c>, <c>touch-action</c> and <c>user-select</c> - and turns clicks, taps, drags, the
/// wheel and pinches into the behavior a reader expects, exposing each as an event an application can hook.
/// </summary>
/// <remarks>
/// <para>
/// Set <see cref="Html"/> or <see cref="Source"/> and the control loads the document, lays it out for its own width and paints only the part in view. One CSS pixel is
/// one device independent pixel, so a page looks as it does in a desktop browser. The document is laid out again when the control is resized, the theme changes or the
/// display scale changes.
/// </para>
/// <para>
/// The page is drawn through the Skia surface Avalonia gives a custom draw operation, so it needs Avalonia's Skia renderer (the default on every desktop platform).
/// On the graphics hardware it is kept as textures while it scrolls and zooms; without a GPU it is painted straight onto the CPU surface.
/// </para>
/// </remarks>
public sealed partial class HtmlView : Control
{
    private const double PixelsPerPoint = 96.0 / 72.0;
    private const double MinZoom = 0.5;
    private const double MaxZoom = 4;

    // The document is stepped, restyled and edited on the UI thread and painted on the render thread: one at a time.
    private readonly Lock _documentLock = new();

    private HtmlViewRenderContext? _context;
    private HtmlDocument? _document;
    private double[] _pageTops = [];
    private double[] _pageHeights = [];
    private double _contentWidth;
    private double _contentHeight;
    private double _scrollX;
    private double _scrollY;
    private double _zoom = 1;
    private double _dpi = 1;
    private double _loadedWidth = -1;
    private double _loadedViewport = -1;
    private int _loadVersion;
    private bool _attached;
    private DispatcherTimer? _reflowTimer;

    static HtmlView()
    {
        FocusableProperty.OverrideDefaultValue<HtmlView>(true);
        ClipToBoundsProperty.OverrideDefaultValue<HtmlView>(true);
        AffectsRender<HtmlView>(PageBackgroundProperty);
        HtmlProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnContentChanged());
        SourceProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnContentChanged());
        SelectionColorProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnOptionsChanged());
        UserStyleSheetProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnOptionsChanged());
        DocumentMarginProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnOptionsChanged());
        AllowNetworkAccessProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnOptionsChanged());
        DocumentBaseUriProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnOptionsChanged());
        AnimationsEnabledProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnOptionsChanged());
        IsLayoutSynchronousProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnOptionsChanged());
        ScrollModeProperty.Changed.AddClassHandler<HtmlView>((view, _) => view.OnScrollModeChanged());
    }

    /// <summary>Creates a control with nothing in it.</summary>
    public HtmlView()
    {
        if (Environment.GetEnvironmentVariable("PEACH_NO_ENGINE_WARMUP") is null)
            _ = HtmlDocument.WarmUpAsync();
    }

    /// <summary>Identifies <see cref="Html"/>.</summary>
    public static readonly StyledProperty<string?> HtmlProperty = AvaloniaProperty.Register<HtmlView, string?>(nameof(Html));

    /// <summary>Identifies <see cref="Source"/>.</summary>
    public static readonly StyledProperty<Uri?> SourceProperty = AvaloniaProperty.Register<HtmlView, Uri?>(nameof(Source));

    /// <summary>Identifies <see cref="ScrollMode"/>.</summary>
    public static readonly StyledProperty<HtmlViewScrollMode> ScrollModeProperty = AvaloniaProperty.Register<HtmlView, HtmlViewScrollMode>(nameof(ScrollMode));

    /// <summary>Identifies <see cref="IsZoomEnabled"/>.</summary>
    public static readonly StyledProperty<bool> IsZoomEnabledProperty = AvaloniaProperty.Register<HtmlView, bool>(nameof(IsZoomEnabled), true);

    /// <summary>Identifies <see cref="IsLayoutSynchronous"/>.</summary>
    public static readonly StyledProperty<bool> IsLayoutSynchronousProperty = AvaloniaProperty.Register<HtmlView, bool>(nameof(IsLayoutSynchronous));

    /// <summary>Identifies <see cref="SelectionColor"/>.</summary>
    public static readonly StyledProperty<Color> SelectionColorProperty = AvaloniaProperty.Register<HtmlView, Color>(nameof(SelectionColor), Color.FromRgb(0xAD, 0xD6, 0xFF));

    /// <summary>Identifies <see cref="UserStyleSheet"/>.</summary>
    public static readonly StyledProperty<string?> UserStyleSheetProperty = AvaloniaProperty.Register<HtmlView, string?>(nameof(UserStyleSheet));

    /// <summary>Identifies <see cref="DocumentMargin"/>.</summary>
    public static readonly StyledProperty<double> DocumentMarginProperty = AvaloniaProperty.Register<HtmlView, double>(nameof(DocumentMargin));

    /// <summary>Identifies <see cref="AllowNetworkAccess"/>.</summary>
    public static readonly StyledProperty<bool> AllowNetworkAccessProperty = AvaloniaProperty.Register<HtmlView, bool>(nameof(AllowNetworkAccess));

    /// <summary>Identifies <see cref="DocumentBaseUri"/>.</summary>
    public static readonly StyledProperty<Uri?> DocumentBaseUriProperty = AvaloniaProperty.Register<HtmlView, Uri?>(nameof(DocumentBaseUri));

    /// <summary>Identifies <see cref="AnimationsEnabled"/>.</summary>
    public static readonly StyledProperty<bool> AnimationsEnabledProperty = AvaloniaProperty.Register<HtmlView, bool>(nameof(AnimationsEnabled));

    /// <summary>Identifies <see cref="PageBackground"/>.</summary>
    public static readonly StyledProperty<Color> PageBackgroundProperty = AvaloniaProperty.Register<HtmlView, Color>(nameof(PageBackground), Colors.White);

    /// <summary>Identifies <see cref="ContentHeight"/>.</summary>
    public static readonly DirectProperty<HtmlView, double> ContentHeightProperty =
        AvaloniaProperty.RegisterDirect<HtmlView, double>(nameof(ContentHeight), view => view._publishedContentHeight);

    private double _publishedContentHeight;

    /// <summary>The HTML to show. Setting it loads and lays out the document. When both this and <see cref="Source"/> are set, this one is shown.</summary>
    public string? Html
    {
        get => GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    /// <summary>The address of an HTML document to show: a <c>file:</c> address, or an <c>http</c> or <c>https</c> one when <see cref="AllowNetworkAccess"/> is on.</summary>
    public Uri? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Whether the control scrolls and zooms the document itself (the default) or is as tall as the document, for a parent that scrolls.</summary>
    public HtmlViewScrollMode ScrollMode
    {
        get => GetValue(ScrollModeProperty);
        set => SetValue(ScrollModeProperty, value);
    }

    /// <summary>Whether the document can be zoomed with Ctrl and the wheel, Ctrl and plus or minus, or a pinch. On by default.</summary>
    public bool IsZoomEnabled
    {
        get => GetValue(IsZoomEnabledProperty);
        set => SetValue(IsZoomEnabledProperty, value);
    }

    /// <summary>Whether the document is loaded and laid out on the UI thread before the next frame instead of in the background. Only documents that need no fetching can load so.</summary>
    public bool IsLayoutSynchronous
    {
        get => GetValue(IsLayoutSynchronousProperty);
        set => SetValue(IsLayoutSynchronousProperty, value);
    }

    /// <summary>The color behind selected text. Takes effect on the next load.</summary>
    public Color SelectionColor
    {
        get => GetValue(SelectionColorProperty);
        set => SetValue(SelectionColorProperty, value);
    }

    /// <summary>A style sheet that applies on top of the document's own, as a reader's style sheet. Takes effect on the next load.</summary>
    public string? UserStyleSheet
    {
        get => GetValue(UserStyleSheetProperty);
        set => SetValue(UserStyleSheetProperty, value);
    }

    /// <summary>The space around the document, in device independent pixels. Takes effect on the next load.</summary>
    public double DocumentMargin
    {
        get => GetValue(DocumentMarginProperty);
        set => SetValue(DocumentMarginProperty, value);
    }

    /// <summary>
    /// Whether the document may load images, style sheets and fonts from <c>http</c> and <c>https</c> addresses. Off by default, so a document cannot make
    /// the application call out; <c>data:</c> URIs and files are always read. Takes effect on the next load.
    /// </summary>
    public bool AllowNetworkAccess
    {
        get => GetValue(AllowNetworkAccessProperty);
        set => SetValue(AllowNetworkAccessProperty, value);
    }

    /// <summary>The address relative addresses in the document resolve against. By default that is <see cref="Source"/>, or nothing for <see cref="Html"/>.</summary>
    public Uri? DocumentBaseUri
    {
        get => GetValue(DocumentBaseUriProperty);
        set => SetValue(DocumentBaseUriProperty, value);
    }

    /// <summary>Whether CSS animations, transitions and animated images play. Off by default; the document is a still image of its first frame. Takes effect on the next load.</summary>
    public bool AnimationsEnabled
    {
        get => GetValue(AnimationsEnabledProperty);
        set => SetValue(AnimationsEnabledProperty, value);
    }

    /// <summary>The color behind the page.</summary>
    public Color PageBackground
    {
        get => GetValue(PageBackgroundProperty);
        set => SetValue(PageBackgroundProperty, value);
    }

    /// <summary>The height of the document at the control's width, in device independent pixels.</summary>
    public double ContentHeight => _publishedContentHeight;

    private InputCapabilities? _input;

    /// <summary>
    /// The pointing devices the document's <c>hover</c> and <c>pointer</c> media queries report. <see langword="null"/> (the default) reports a mouse on a desktop
    /// and a touch screen on a phone or tablet.
    /// </summary>
    public InputCapabilities? Input
    {
        get => _input;
        set
        {
            _input = value;
            OnOptionsChanged();
        }
    }

    private FontSet? _fontSet;

    /// <summary>The fonts documents may use besides the installed ones. <see langword="null"/> (the default) uses <see cref="HtmlViewFonts.Shared"/>.</summary>
    public FontSet? FontSet
    {
        get => _fontSet;
        set
        {
            if (ReferenceEquals(_fontSet, value))
                return;

            _fontSet = value;
            var old = _context;
            _context = null;
            RetireContext(old);
            OnOptionsChanged();
        }
    }

    /// <summary>The loaded document, or <see langword="null"/> before one has loaded. It is replaced when the control reloads.</summary>
    public HtmlDocument? Document => _document;

    /// <summary>How far the page is zoomed: 1 is one CSS pixel to one device independent pixel.</summary>
    public double Zoom => _zoom;

    /// <summary>The text that is selected, or an empty string.</summary>
    public string SelectedText => _document is { } document && document.Selection.HasSelection ? document.Selection.GetText() : string.Empty;

    /// <summary>Raised when a document has loaded and laid out.</summary>
    public event EventHandler? DocumentLoaded;

    /// <summary>Raised when a document could not be loaded.</summary>
    public event EventHandler<HtmlLoadFailedEventArgs>? DocumentFailed;

    /// <summary>Raised when a submit button of a form is activated, with the form as it would be submitted. The control sends nothing itself.</summary>
    public event EventHandler<HtmlFormSubmittingEventArgs>? FormSubmitting;

    /// <summary>
    /// Raised instead of <see cref="FormSubmitting"/> when a submit was tried and a control's value does not satisfy its constraints (<c>required</c>,
    /// <c>pattern</c>, <c>min</c>... unless the form has <c>novalidate</c>). The first such control has been given the focus.
    /// </summary>
    public event EventHandler<HtmlFormInvalidEventArgs>? FormInvalid;

    /// <summary>Raised before a link is followed. Set <see cref="HtmlLinkActivatedEventArgs.Cancel"/> to handle it yourself.</summary>
    public event EventHandler<HtmlLinkActivatedEventArgs>? LinkActivated;

    /// <summary>Raised when the page is clicked or tapped on something that is not a link or a control.</summary>
    public event EventHandler<HtmlPageClickedEventArgs>? PageClicked;

    /// <summary>Raised when the scroll position changes.</summary>
    public event EventHandler? ScrollChanged;

    /// <summary>Raised when the zoom changes.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Raised when <see cref="ContentHeight"/> changes.</summary>
    public event EventHandler? ContentHeightChanged;

    /// <summary>Raised when the document's animations and transitions start, end or are cancelled.</summary>
    public event EventHandler<HtmlAnimationEventsEventArgs>? AnimationEvents;

    /// <summary>Puts the selected text on the clipboard.</summary>
    public void CopySelection()
    {
        var text = SelectedText;
        if (text.Length == 0)
            return;

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            _ = clipboard.SetTextAsync(text);
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
                ScrollToDocumentY(_pageTops[page] + (bounds.Y * PixelsPerPoint));
                return true;
            }
        }

        return false;
    }

    /// <summary>Takes in the result of a change made to the document through its own API (a restyle, an edit, a form state change): the layout is measured again and what changed is painted.</summary>
    /// <param name="result">what the document reported</param>
    public void ApplyUpdate(UpdateResult result)
    {
        if (!result.Applied)
            return;

        UpdateMetrics();
        InvalidateRegions(result.DirtyRegions);
        if (_document is { } document)
            RefreshAccessibility(document);
    }

    private double Scale => _dpi * _zoom;

    private bool IsAutoHeight => ScrollMode == HtmlViewScrollMode.Disabled;

    // A change to what the document is made of: the old one is let go of at once and the new one loads.
    private void OnContentChanged()
    {
        _loadVersion++;
        if (_attached)
            StartReload();
    }

    // A change to how the document is loaded: the same document loads again, once things have settled.
    private void OnOptionsChanged()
    {
        if (_attached && (Html is not null || Source is not null))
            QueueReflow();
    }

    private void OnScrollModeChanged()
    {
        _scrollX = _scrollY = 0;
        InvalidateMeasure();
        OnOptionsChanged();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        ActualThemeVariantChanged += OnThemeChanged;
        TrackScaling();
        EnsureContext();
        StartReload();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ActualThemeVariantChanged -= OnThemeChanged;
        _loadVersion++;
        _reflowTimer?.Stop();
        UntrackScaling();
        StopAnimationLoop();
        lock (_documentLock)
        {
            ResetTiles();
            _document?.Dispose();
            _document = null;
        }

        RetireContext(_context);
        _context = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeChanged(object? sender, EventArgs e) => OnOptionsChanged();

    private TopLevel? _topLevel;

    private void TrackScaling()
    {
        _topLevel = TopLevel.GetTopLevel(this);
        _dpi = _topLevel?.RenderScaling ?? 1;
        if (_topLevel is not null)
            _topLevel.ScalingChanged += OnScalingChanged;
    }

    private void UntrackScaling()
    {
        if (_topLevel is not null)
            _topLevel.ScalingChanged -= OnScalingChanged;

        _topLevel = null;
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        _dpi = _topLevel?.RenderScaling ?? _dpi;
        if (_attached)
            QueueReflow();
    }

    /// <inheritdoc />
    protected override global::Avalonia.Size MeasureOverride(global::Avalonia.Size availableSize)
    {
        if (!IsAutoHeight)
            return base.MeasureOverride(availableSize);

        // As tall as the document at the width offered; a document that has not been laid out for this width yet is, and the control measures again.
        var width = double.IsInfinity(availableSize.Width) ? Math.Max(Bounds.Width, 1) : availableSize.Width;
        if (_document is not null && Math.Abs(_loadedWidth - width) > 0.5 && _attached)
            QueueReflow();

        return new global::Avalonia.Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, Math.Max(_publishedContentHeight, 0));
    }

    /// <inheritdoc />
    protected override global::Avalonia.Size ArrangeOverride(global::Avalonia.Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        // The control's height is the viewport `vh` resolves against, so it reflows the document as well as the width.
        if (_attached && (Html is not null || Source is not null)
            && (Math.Abs(size.Width - _loadedWidth) > 0.5 || (!IsAutoHeight && _loadedViewport > 0 && Math.Abs(size.Height - _loadedViewport) > 0.5)))
        {
            QueueReflow();
        }

        if (_document is not null)
        {
            ScrollTo(_scrollX, _scrollY);
            UpdateViewport();
        }

        return size;
    }

    // A resize or a theme change reflows the document, but only once it has settled.
    private void QueueReflow()
    {
        _reflowTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _reflowTimer.Stop();
        _reflowTimer.Tick -= OnReflowTick;
        _reflowTimer.Tick += OnReflowTick;
        _reflowTimer.Start();
    }

    private void OnReflowTick(object? sender, EventArgs e)
    {
        _reflowTimer?.Stop();
        StartReload();
    }

    private void StartReload() => _ = ReloadAsync();

    private HtmlViewRenderContext EnsureContext() =>
        _context ??= new HtmlViewRenderContext(() => AllowNetworkAccess, () => DocumentBaseUri ?? Source, _fontSet ?? HtmlViewFonts.Shared) { PixelsPerPoint = PixelsPerPoint };

    private async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        var widthDip = Bounds.Width;
        if (widthDip < 1 || (Html is null && Source is null))
            return;

        var context = EnsureContext();
        BeginLoad(context);
        try
        {
            var html = Html ?? await ReadSourceAsync(Source!, AllowNetworkAccess);
            if (version != _loadVersion)
                return;

            var options = BuildOptions(widthDip);
            HtmlDocument document;
            if (IsLayoutSynchronous)
            {
                document = LoadSynchronously(html, context, options);
            }
            else
            {
                // Reading the fonts, parsing, styling and laying out is seconds of work on a cold runtime: it runs on a pool thread and the control goes on taking input.
                document = await Task.Run(() => LoadDocumentAsync(html, context, options));
            }

            if (version != _loadVersion)
            {
                document.Dispose();
                return;
            }

            Adopt(document, widthDip);
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (version == _loadVersion && exception is not OutOfMemoryException)
        {
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
        finally
        {
            EndLoad(context);
        }
    }

    private static async Task<string> ReadSourceAsync(Uri source, bool allowNetwork)
    {
        if (source.IsFile)
            return await File.ReadAllTextAsync(source.LocalPath);

        if (!allowNetwork)
            throw new InvalidOperationException("The source is not a file and AllowNetworkAccess is off.");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(HostResourceLoader.UserAgent);
        return await client.GetStringAsync(source);
    }

    // The part of a load that does not touch the control: it runs on a pool thread, so it uses only what it is handed.
    private static async Task<HtmlDocument> LoadDocumentAsync(string html, SkiaRenderContext context, HtmlDocumentOptions options)
    {
        var document = await HtmlDocument.LoadAsync(html, context, options);
        try
        {
            using var measuring = new MeasuringCanvas(context);
            await document.LayoutAsync(measuring.Canvas);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static HtmlDocument LoadSynchronously(string html, SkiaRenderContext context, HtmlDocumentOptions options)
    {
        options.SynchronousOnly = true;
        var document = HtmlDocument.Load(html, context, options);
        try
        {
            using var measuring = new MeasuringCanvas(context);
            document.Layout(measuring.Canvas);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    // Takes a laid-out document in and shows it.
    private void Adopt(HtmlDocument document, double widthDip)
    {
        StopAnimationLoop();
        lock (_documentLock)
        {
            _document?.Dispose();
            _document = document;
            ResetTiles();
        }

        _loadedWidth = widthDip;
        document.Selection.SelectionChanged += OnSelectionChanged;
        document.AnimationsWoke += OnAnimationsWoke;
        UpdateMetrics();
        UpdateViewport();
        RefreshAccessibility(document);
        ScrollTo(_scrollX, _scrollY);
        InvalidateContent();
        EnsureAnimationLoop();
    }

    private void OnSelectionChanged(object? sender, PeachBrackets.Selection.SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _document?.Selection))
            return;

        PostToUi(() => InvalidateRegions(e.DirtyRegions));
    }

    // A load that is running uses its render context from a pool thread, so a context the control lets go of (it left the tree) is only disposed once the
    // last load that holds it has finished. All of this runs on the UI thread.
    private readonly Dictionary<HtmlViewRenderContext, int> _loadsUsing = [];
    private readonly HashSet<HtmlViewRenderContext> _retiredContexts = [];

    private void BeginLoad(HtmlViewRenderContext context) => _loadsUsing[context] = _loadsUsing.GetValueOrDefault(context) + 1;

    private void EndLoad(HtmlViewRenderContext context)
    {
        if (--_loadsUsing[context] > 0)
            return;

        _loadsUsing.Remove(context);
        if (_retiredContexts.Remove(context))
            context.Dispose();
    }

    private void RetireContext(HtmlViewRenderContext? context)
    {
        if (context is null)
            return;

        if (_loadsUsing.ContainsKey(context))
            _retiredContexts.Add(context);
        else
            context.Dispose();
    }

    private HtmlDocumentOptions BuildOptions(double widthDip)
    {
        var margin = Math.Max(0, DocumentMargin) / PixelsPerPoint;
        var plays = PlaysAnimations;
        var selection = SelectionColor;
        return new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = widthDip / PixelsPerPoint,
            ViewportHeightPoints = PinViewport() is { } viewport ? viewport / PixelsPerPoint : null,
            MarginTopPoints = margin,
            MarginRightPoints = margin,
            MarginBottomPoints = margin,
            MarginLeftPoints = margin,
            UserStyleSheet = UserStyleSheet,
            SelectionBackground = PaintColor.FromArgb(selection.A, selection.R, selection.G, selection.B),
            RecordTextRuns = true,
            EnableSelectionStyling = true,
            SkipOffscreenContent = true,
            CollectFormFields = true,
            PaintFormControls = true,
            ColorScheme = ActualThemeVariant == ThemeVariant.Dark ? ColorSchemePreference.Dark : ColorSchemePreference.Light,
            Input = Input ?? DetectInput(),
            DevicePixelRatio = ImageScale.DevicePixelRatio(_dpi),
            AnimationsEnabled = plays,
            Preferences = plays ? new UserPreferences { ReducedMotion = false } : null,
        };
    }

    // The height `vh` and a percentage root height resolve against, pinned when a document is loaded so it never follows the content.
    private double? PinViewport()
    {
        _loadedViewport = !IsAutoHeight && Bounds.Height > 1 ? Bounds.Height : -1;
        return _loadedViewport > 0 ? _loadedViewport : null;
    }

    // A screen document is one surface, so there is a single "page" at the top and no gap around it.
    private void UpdateMetrics()
    {
        var pages = _document?.Pages ?? [];
        var tops = new double[pages.Count];
        var heights = new double[pages.Count];
        var top = 0d;
        var width = 0d;
        for (var i = 0; i < pages.Count; i++)
        {
            tops[i] = top;
            heights[i] = pages[i].HeightPoints * PixelsPerPoint;
            top += heights[i];
            width = Math.Max(width, pages[i].WidthPoints * PixelsPerPoint);
        }

        lock (_documentLock)
        {
            _pageTops = tops;
            _pageHeights = heights;
            _contentWidth = width;
            _contentHeight = top;
        }

        PublishContentHeight(top);
    }

    private void PublishContentHeight(double height)
    {
        void Publish()
        {
            if (Math.Abs(_publishedContentHeight - height) < 0.01)
                return;

            var old = _publishedContentHeight;
            _publishedContentHeight = height;
            RaisePropertyChanged(ContentHeightProperty, old, height);
            if (IsAutoHeight)
                InvalidateMeasure();

            ContentHeightChanged?.Invoke(this, EventArgs.Empty);
        }

        PostToUi(Publish);
    }

    // Tells the document which part of it is showing, so a fixed box stays where it was put in the view and a sticky one follows the top of it.
    private void UpdateViewport()
    {
        if (_document is not { } document || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var perPoint = Scale * PixelsPerPoint;
        document.SetViewport(new PeachDrawing.Core.Rect(_scrollX / perPoint, _scrollY / perPoint, Bounds.Width * _dpi / perPoint, Bounds.Height * _dpi / perPoint));

        // What came near is shown and what went away skipped, for an element with `content-visibility: auto`.
        if (document.ContentVisibilityNeedsUpdate)
            _ = UpdateContentVisibilityAsync(document);

        // A scroll moves what an animation on a scroll or view timeline plays at.
        EnsureAnimationLoop();
    }

    private double ViewportWidthPx => Bounds.Width * _dpi;

    private double ViewportHeightPx => Bounds.Height * _dpi;

    private void ScrollTo(double x, double y)
    {
        var newX = ViewportMath.ClampScroll(x, _contentWidth * Scale, ViewportWidthPx);
        var newY = ViewportMath.ClampScroll(y, _contentHeight * Scale, ViewportHeightPx);
        if (newX == _scrollX && newY == _scrollY)
            return;

        _scrollX = newX;
        _scrollY = newY;
        UpdateViewport();
        RequestPaint();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
        OnScrolledForAccessibility();
    }

    // Scrolls so a position in the document (DIPs from its top, at zoom 1) is at the top of the view.
    private void ScrollToDocumentY(double documentY) => ScrollTo(_scrollX, documentY * Scale);

    /// <summary>Sets how far the page is zoomed, keeping the point <paramref name="focus"/> (in the control's coordinates) where it is.</summary>
    /// <param name="zoom">the new zoom; it is held between 0.5 and 4</param>
    /// <param name="focus">the point to keep still, or <see langword="null"/> for the middle of the control</param>
    public void SetZoom(double zoom, Point? focus = null)
    {
        if (!IsZoomEnabled || IsAutoHeight)
            return;

        var newZoom = ViewportMath.ClampZoom(zoom, MinZoom, MaxZoom);
        if (newZoom == _zoom)
            return;

        var at = focus ?? new Point(Bounds.Width / 2, Bounds.Height / 2);
        var newX = ViewportMath.ScrollAfterZoom(_scrollX, _zoom, newZoom, at.X * _dpi);
        var newY = ViewportMath.ScrollAfterZoom(_scrollY, _zoom, newZoom, at.Y * _dpi);
        _zoom = newZoom;
        _scrollX = newX;
        _scrollY = newY;
        _scrollX = ViewportMath.ClampScroll(_scrollX, _contentWidth * Scale, ViewportWidthPx);
        _scrollY = ViewportMath.ClampScroll(_scrollY, _contentHeight * Scale, ViewportHeightPx);
        UpdateViewport();
        RequestPaint();
        QueueImageUpgrade();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    // Runs an action on the UI thread: now when already there.
    private static void PostToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    // Raises FormSubmitting - or, when a value is not valid, gives the first such control the focus and raises FormInvalid.
    private bool Submit(HtmlDocument document, DocumentElement trigger, FormSubmission? submission)
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

    // Submits the form a button belongs to (at a point, for an image button); false when the element is not a submit button in a form.
    private bool SubmitForm(DocumentElement submitter, int page = -1, double x = 0, double y = 0) =>
        _document is { } document
        && Submit(document, submitter, page >= 0 ? document.CreateFormSubmission(submitter, page, x, y) : document.CreateFormSubmission(submitter));

    // Submits the form a field belongs to, as Enter does; false when that does not submit it.
    private bool SubmitFromField(DocumentElement field) =>
        _document is { } document && Submit(document, field, document.CreateImplicitSubmission(field));

    private void RaiseLinkActivated(HtmlLinkActivatedEventArgs args) => LinkActivated?.Invoke(this, args);

    private void RaisePageClicked(int page, double x, double y) => PageClicked?.Invoke(this, new HtmlPageClickedEventArgs(page, x, y));

    private void RaiseAnimationEvents(IReadOnlyList<AnimationEventInfo> events) => AnimationEvents?.Invoke(this, new HtmlAnimationEventsEventArgs(events));

    private void RaiseDocumentFailed(Exception exception) => DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));

    // The canvas a layout or a restyle measures text on: nothing is drawn on it.
    internal sealed class MeasuringCanvas : IDisposable
    {
        private readonly SKSurface _surface;
        private readonly SkiaComposer _composer;

        public MeasuringCanvas(SkiaRenderContext context)
        {
            _surface = SKSurface.Create(new SKImageInfo(1, 1));
            _composer = new SkiaComposer(context, _surface, 1, 1);
        }

        public PeachDrawing.Core.Canvas Canvas => _composer.Canvas;

        public void Dispose()
        {
            _composer.Dispose();
            _surface.Dispose();
        }
    }
}

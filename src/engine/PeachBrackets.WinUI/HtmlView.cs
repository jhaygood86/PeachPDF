using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using PeachDrawing.Win2D;
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using PointerDeviceType = Microsoft.UI.Input.PointerDeviceType;
using Windows.Foundation;

using Windows.UI;

namespace PeachBrackets.WinUI;

/// <summary>
/// A WinUI 3 control that shows an HTML document laid out by PeachBrackets as one scrolling, zoomable surface. It follows what the
/// document asks for - the CSS <c>cursor</c>, the <c>:hover</c>, <c>:active</c> and <c>:focus</c> rules, <c>pointer-events</c>, <c>touch-action</c>
/// and <c>user-select</c> - and turns links, text selection and copy into the behaviour a reader expects.
/// </summary>
/// <remarks>
/// <para>
/// Set <see cref="Html"/> (or <see cref="Source"/>) and the view loads it, lays it out for its own width and paints only the part in view. One CSS
/// pixel is one device-independent pixel. The document is laid out again when the view is resized, so it reflows the way a web page does.
/// </para>
/// <para>
/// Limits of this first version: form controls are painted but not interactive, there is no keyboard navigation between links, text is not exposed to
/// UI Automation, a custom <c>url()</c> cursor is shown as its keyword, and zooming scales the rendered surface rather than re-rendering it.
/// </para>
/// </remarks>
public sealed partial class HtmlView : UserControl
{
    private const double PixelsPerPoint = 96.0 / 72.0;
    private const double ClickSlop = 4;

    /// <summary>Identifies the <see cref="Html"/> property.</summary>
    public static readonly DependencyProperty HtmlProperty =
        DependencyProperty.Register(nameof(Html), typeof(string), typeof(HtmlView), new PropertyMetadata(null, (d, e) => ((HtmlView)d).OnContentChanged(e.OldValue, e.NewValue)));

    /// <summary>Identifies the <see cref="Source"/> property.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(HtmlView), new PropertyMetadata(null, (d, e) => ((HtmlView)d).OnContentChanged(e.OldValue, e.NewValue)));

    private readonly ScrollViewer _scroller = new();
    private readonly HtmlViewHost _host;
    private readonly CanvasVirtualControl _canvas = new();

    // A fixed or stuck box is drawn here, over the scroller, and not into the canvas the scroller moves: the scroller moves the canvas on its own,
    // ahead of anything this view can do about it, so a box drawn into the canvas would scroll away with the content and snap back a frame later.
    private readonly CanvasControl _overlay = new() { IsHitTestVisible = false };
    private readonly Grid _layers = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _reflowTimer;
    private Win2dRenderContext? _context;
    private Win2dTextRendering _textRendering = Win2dTextRendering.Auto;
    private Color _pageBackground = Microsoft.UI.Colors.White;
    private HtmlDocument? _document;
    private double[] _pageTops = [];
    private double[] _pageHeights = [];
    private int _loadVersion;
    private double _loadedWidth = -1;
    private bool _loaded;
    private bool _needsReload;

    /// <summary>How many regions the view has painted, for a test to tell a row that was never painted from one that was.</summary>
    internal int PaintedRegionCount { get; private set; }
    private bool _measuring;

    /// <summary>Creates a view with nothing in it.</summary>
    public HtmlView()
    {
        IsTabStop = true;
        _canvas.HorizontalAlignment = HorizontalAlignment.Left;
        _canvas.VerticalAlignment = VerticalAlignment.Top;
        _canvas.ClearColor = _pageBackground;
        _canvas.RegionsInvalidated += OnRegionsInvalidated;
        _host = new HtmlViewHost(this);
        _scroller.Content = _canvas;
        _overlay.Draw += OnOverlayDraw;
        _layers.Children.Add(_scroller);
        _layers.Children.Add(_overlay);
        _host.Content = _layers;
        _scroller.ZoomMode = ZoomMode.Enabled;
        _scroller.ViewChanged += (_, _) => UpdateViewport();
        _scroller.MinZoomFactor = 0.5f;
        _scroller.MaxZoomFactor = 4f;
        _scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Content = _host;

        Loaded += (_, _) => OnLoaded();
        Unloaded += (_, _) => OnUnloaded();
        SizeChanged += (_, _) => QueueReflow();
        ActualThemeChanged += (_, _) => OnOptionsChanged();
        WireInput();
    }

    /// <summary>The HTML to show.</summary>
    public string? Html
    {
        get => (string?)GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    /// <summary>The address (an <c>http</c>, <c>https</c> or <c>file</c> URI) to load the HTML from, used when <see cref="Html"/> is not set.</summary>
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// The colour behind the page. While it is opaque (the default) the view's surface has no alpha channel (the control follows its clear colour
    /// in this), which is what lets LCD text be blended against what is behind it; a colour with transparency keeps the surface translucent,
    /// and text is drawn as grayscale.
    /// </summary>
    public Color PageBackground
    {
        get => _pageBackground;
        set
        {
            _pageBackground = value;
            ApplyRenderMode();
            _canvas.Invalidate();
            _overlay.Invalidate();
        }
    }

    // True while the view paints the visible part of the page itself, on every scroll step, from the layer above the scroller (see ApplyRenderMode).
    private bool _viewportMode;

    internal bool IsViewportMode => _viewportMode;

    // A document with a fixed-attachment background cannot let the scroller move what is painted: the picture is part of the content it sits
    // under, so the compositor would carry it away with the content until the next paint put it back. Such a document is painted from the
    // non-scrolling layer instead, which draws the page as it is at the current offset, so nothing is ever shown in the wrong place. The scrolled canvas
    // then only gives the scroller its extent, and every pointer handler stays on it. Everything else keeps the cheaper path (the compositor
    // scrolls what was painted, and only the boxes the viewport holds in place are drawn above it).
    private void ApplyRenderMode()
    {
        _viewportMode = _scrollMode != HtmlViewScrollMode.Disabled && _document is { } document && document.Pages.Count > 0 && document.HasViewportBackgrounds;
        _canvas.ClearColor = _viewportMode ? Microsoft.UI.Colors.Transparent : _pageBackground;
        _overlay.ClearColor = _viewportMode ? _pageBackground : Microsoft.UI.Colors.Transparent;
    }

    /// <summary>
    /// How the text is drawn; see <see cref="Win2dTextRendering"/>. <see cref="Win2dTextRendering.Auto"/> (the default) draws body text hinted and
    /// with the weight Windows gives it, with ClearType's colour stripes when the user has ClearType on and what is behind the text is opaque,
    /// and leaves large text as crisp vectors.
    /// </summary>
    public Win2dTextRendering TextRendering
    {
        get => _textRendering;
        set
        {
            if (_textRendering == value)
                return;

            _textRendering = value;
            if (_context is not null)
            {
                _context.TextRendering = value;
                _canvas.Invalidate();
            }
        }
    }

    /// <summary>
    /// The pointing devices the document's <c>hover</c> and <c>pointer</c> media queries report. <see langword="null"/> (the default) asks the
    /// system which are attached.
    /// </summary>
    public InputCapabilities? Input { get; set; }

    /// <summary>The loaded document, or <see langword="null"/> before one has loaded. It is replaced when the view reloads.</summary>
    public HtmlDocument? Document => _document;

    /// <summary>The text that is selected, or an empty string.</summary>
    public string SelectedText => _document is { } document && document.Selection.HasSelection ? document.Selection.GetText() : string.Empty;

    /// <summary>Raised when a document has loaded and laid out.</summary>
    public event EventHandler? DocumentLoaded;

    /// <summary>Raised when a document could not be loaded.</summary>
    public event EventHandler<HtmlLoadFailedEventArgs>? DocumentFailed;

    /// <summary>Raised when a submit button of a form is clicked or invoked, with the form as it would be submitted. The view sends nothing itself.</summary>
    public event EventHandler<HtmlFormSubmittingEventArgs>? FormSubmitting;

    /// <summary>
    /// Raised instead of <see cref="FormSubmitting"/> when a submit was tried and a control's value does not satisfy its constraints (<c>required</c>,
    /// <c>pattern</c>, <c>min</c>... unless the form has <c>novalidate</c>). The first such control has been given the focus.
    /// </summary>
    public event EventHandler<HtmlFormInvalidEventArgs>? FormInvalid;

    // Submits the form a button belongs to (at a point, for an image button); false when the element is not a submit button in a form.
    internal bool SubmitForm(PeachBrackets.Updates.DocumentElement submitter, int page = -1, double x = 0, double y = 0) =>
        _document is { } document
        && Submit(document, submitter, page >= 0 ? document.CreateFormSubmission(submitter, page, x, y) : document.CreateFormSubmission(submitter));

    // Submits the form a field belongs to, as Enter in the field does; false when Enter does not submit it.
    internal bool SubmitFromField(PeachBrackets.Updates.DocumentElement field) =>
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

    /// <summary>Raised before a link is followed. Set <see cref="HtmlLinkClickedEventArgs.Cancel"/> to handle it yourself.</summary>
    public event EventHandler<HtmlLinkClickedEventArgs>? LinkClicked;

    /// <summary>Puts the selected text on the clipboard.</summary>
    public void CopySelection()
    {
        var text = SelectedText;
        if (text.Length == 0)
            return;

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
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
                ScrollToY(_pageTops[page] + (bounds.Y * PixelsPerPoint));
                return true;
            }
        }

        return false;
    }

    private void OnLoaded()
    {
        _loaded = true;
        EnsureContext();
        _reflowTimer ??= DispatcherQueue.CreateTimer();
        _reflowTimer.Interval = TimeSpan.FromMilliseconds(150);
        _reflowTimer.IsRepeating = false;
        _reflowTimer.Tick += (_, _) => StartReload(ActualWidth);
        if (_scrollMode == HtmlViewScrollMode.Disabled)
            InvalidateMeasure();
        else
            QueueReload();
    }

    // The context is made when the view first needs it, which for an auto-height view is its first measure, before it is loaded.
    private void EnsureContext()
    {
        if (_context is null)
        {
            _context = new HtmlViewRenderContext(() => _allowNetworkAccess, () => _baseUri ?? Source, CanvasDevice.GetSharedDevice(), FontSet ?? HtmlViewFonts.Shared)
            {
                PixelsPerPoint = PixelsPerPoint,
                TextRendering = _textRendering,
            };
        }

        // Fonts a host added to the shared set since this context last looked.
        _context.SyncFonts();
    }

    private void OnUnloaded()
    {
        _loaded = false;
        _loadVersion++;
        _reflowTimer?.Stop();
        ReleaseCursors();
        _document?.Dispose();
        _document = null;
        _loadedWidth = -1;
        _inFlightWidth = -1;
        _context?.Dispose();
        _context = null;
    }

    // The HTML or the address changed (a list gave a recycled view new content): what was shown is not this view's content any more, so it goes now rather
    // than staying until the new document has laid out.
    private void OnContentChanged(object? oldValue, object? newValue)
    {
        if (Equals(oldValue, newValue))
            return;

        _loadVersion++;
        _inFlightWidth = -1;
        if (_document is not null)
        {
            _document.Dispose();
            _document = null;
            _pageTops = [];
            _pageHeights = [];
            ApplyRenderMode();
            _canvas.Invalidate();
            _overlay.Invalidate();
        }

        _needsReload = true;
        if (_scrollMode == HtmlViewScrollMode.Disabled)
        {
            InvalidateMeasure();
            return;
        }

        QueueReload();
    }

    // A setting that changes how the document is laid out or painted (theme, selection colour, style sheet, margin): it is loaded again, and what is
    // shown stays until the new document is ready.
    private void OnOptionsChanged()
    {
        _needsReload = true;
        if (_scrollMode == HtmlViewScrollMode.Disabled)
            InvalidateMeasure();
        else
            QueueReload();
    }

    private void QueueReload()
    {
        if (_loaded)
            StartReload(ActualWidth);
    }

    // A resize reflows the document, but only once the width has settled. An auto-height view is told its width by the layout pass and reflows at once.
    private void QueueReflow()
    {
        if (_scrollMode == HtmlViewScrollMode.Disabled)
        {
            if (_loaded && Math.Abs(ActualWidth - _loadedWidth) > 0.5)
                InvalidateMeasure();
            return;
        }

        // A viewer's height is the viewport `vh` resolves against, so a change of height reflows it too.
        var heightChanged = ViewportHeightDips() is { } viewport && _loadedViewport is { } loaded && Math.Abs(viewport - loaded) > 0.5;
        if (_loaded && _reflowTimer is not null && (Math.Abs(ActualWidth - _loadedWidth) > 0.5 || heightChanged))
        {
            _reflowTimer.Stop();
            _reflowTimer.Start();
        }
    }

    // Loads the document for a width: all at once when the view was told layout needs nothing asynchronous, otherwise in the background.
    private void StartReload(double width)
    {
        if (_context is null || width < 1)
            return;

        _needsReload = false;
        if (_layoutSynchronous)
            LoadSynchronously(width);
        else
            _ = ReloadAsync(width);
    }

    private async Task ReloadAsync(double width)
    {
        var version = ++_loadVersion;
        if (_context is null || width < 1)
            return;

        _inFlightWidth = width;
        try
        {
            var html = Html ?? (Source is { } source ? await ReadSourceAsync(source) : null);
            if (html is null || version != _loadVersion)
                return;

            var options = BuildOptions(width);
            var document = await HtmlDocument.LoadAsync(html, _context, options);
            using (var measuring = new Win2dComposer(_context, 1, 1))
                await document.LayoutAsync(measuring.Canvas);

            if (version != _loadVersion)
            {
                document.Dispose();
                return;
            }

            Adopt(document, width);
        }
        catch (Exception exception) when (version == _loadVersion)
        {
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
        finally
        {
            if (version == _loadVersion)
                _inFlightWidth = -1;
        }
    }

    // The same without waiting for anything: what the document asks for that would need to wait is an InvalidOperationException, reported through
    // DocumentFailed, since this runs inside the layout pass and an exception there would end the application.
    private void LoadSynchronously(double width)
    {
        var version = ++_loadVersion;
        if (_context is null || width < 1)
            return;

        try
        {
            var html = Html ?? (Source is { } source ? ReadSourceSynchronously(source) : null);
            if (html is null)
                return;

            var options = BuildOptions(width);
            options.SynchronousOnly = true;
            var document = HtmlDocument.Load(html, _context, options);
            using (var measuring = new Win2dComposer(_context, 1, 1))
                document.Layout(measuring.Canvas);

            Adopt(document, width);
        }
        catch (Exception exception) when (version == _loadVersion)
        {
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
    }

    // Makes a laid-out document the one the view shows.
    private void Adopt(HtmlDocument document, double width)
    {
        _document?.Dispose();
        _document = document;
        _loadedWidth = width;
        document.Selection.SelectionChanged += (_, args) =>
        {
            foreach (var region in args.DirtyRegions)
                InvalidateRegion(region.PageIndex, region.Bounds);
        };

        UpdateMetrics();
        ApplyRenderMode();
        UpdateViewport();
        RefreshAccessibility(document);
        _canvas.Invalidate();
        RaiseOutsideMeasure(() => DocumentLoaded?.Invoke(this, EventArgs.Empty));
    }

    // Tells the document which part of it is showing, so a fixed box stays where it was put in the view and a sticky one follows the top of it. An
    // auto-height view has no scroller of its own: it is the whole of its document at the top, which is where the document starts.
    private void UpdateViewport()
    {
        if (_document is not { } document || _scrollMode == HtmlViewScrollMode.Disabled)
            return;

        var zoom = Math.Max(_scroller.ZoomFactor, 0.01);
        var viewport = new PeachDrawing.Core.Rect(
            _scroller.HorizontalOffset / zoom / PixelsPerPoint, _scroller.VerticalOffset / zoom / PixelsPerPoint,
            _scroller.ViewportWidth / zoom / PixelsPerPoint, _scroller.ViewportHeight / zoom / PixelsPerPoint);

        foreach (var region in document.SetViewport(viewport))
            InvalidateRegion(region.PageIndex, region.Bounds);

        _overlay.Invalidate();
    }

    // The layer over the scroller: the boxes the viewport holds in place, drawn where the viewport is, so they are already where they belong on the
    // frame the scroller moved.
    private void OnOverlayDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_document is not { } document || _context is not { } context || _scrollMode == HtmlViewScrollMode.Disabled || document.Pages.Count == 0)
            return;

        var zoom = _scroller.ZoomFactor;
        var session = args.DrawingSession;
        session.Transform = Matrix3x2.CreateScale(zoom) * Matrix3x2.CreateTranslation((float)-_scroller.HorizontalOffset, (float)-_scroller.VerticalOffset);
        var size = document.Pages[0];
        using var canvas = Win2dCanvas.FromSession(context, session, new PeachDrawing.Core.Rect(0, 0, size.WidthPoints * PixelsPerPoint, size.HeightPoints * PixelsPerPoint));
        if (!_viewportMode)
        {
            document.PaintPageAsync(0, canvas, DocumentPaintLayer.ViewportAttached).GetAwaiter().GetResult();
            return;
        }

        // The whole page as it is at this offset: only the part in view is painted, a little over so a shadow or outline at its edge is whole.
        var scale = zoom * PixelsPerPoint;
        const double bleed = 8;
        var left = Math.Max(0, (_scroller.HorizontalOffset / scale) - bleed);
        var top = Math.Max(0, (_scroller.VerticalOffset / scale) - bleed);
        var region = new PeachDrawing.Core.Rect(left, top, (_scroller.ViewportWidth / scale) + (2 * bleed), (_scroller.ViewportHeight / scale) + (2 * bleed));
        document.PaintPageRegionAsync(0, canvas, region, DocumentPaintLayer.All).GetAwaiter().GetResult();
    }

    private static string ReadSourceSynchronously(Uri source) =>
        source.IsFile
            ? System.IO.File.ReadAllText(source.LocalPath)
            : throw new InvalidOperationException($"The view was told layout needs nothing asynchronous, but '{source}' has to be fetched; set Html instead, or clear IsLayoutSynchronous.");

    private static async Task<string> ReadSourceAsync(Uri source)
    {
        if (source.IsFile)
            return await System.IO.File.ReadAllTextAsync(source.LocalPath);

        using var client = new System.Net.Http.HttpClient();
        return await client.GetStringAsync(source);
    }

    private HtmlDocumentOptions BuildOptions(double widthDips) => new()
    {
        MediaType = PeachBrackets.MediaType.Screen,
        PageWidthPoints = widthDips / PixelsPerPoint,
        ViewportHeightPoints = PinViewport() is { } viewport ? viewport / PixelsPerPoint : null,
        MarginTopPoints = DocumentMargin / PixelsPerPoint,
        MarginRightPoints = DocumentMargin / PixelsPerPoint,
        MarginBottomPoints = DocumentMargin / PixelsPerPoint,
        MarginLeftPoints = DocumentMargin / PixelsPerPoint,
        UserStyleSheet = string.IsNullOrWhiteSpace(UserStyleSheet) ? null : UserStyleSheet,
        SelectionBackground = PaintColor.FromArgb(SelectionColor.A, SelectionColor.R, SelectionColor.G, SelectionColor.B),
        RecordTextRuns = true,
        EnableSelectionStyling = true,
        CollectFormFields = true,
        PaintFormControls = true,
        ColorScheme = ActualTheme == ElementTheme.Dark ? ColorSchemePreference.Dark : ColorSchemePreference.Light,
        Input = Input ?? DetectInput(),
    };

    private static InputCapabilities DetectInput()
    {
        var devices = Windows.Devices.Input.PointerDevice.GetPointerDevices();
        return HtmlViewLogic.Capabilities(
            devices.Any(d => d.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse),
            devices.Any(d => d.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Pen),
            devices.Any(d => d.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Touch));
    }

    // The height `vh` and a percentage root height resolve against, pinned when a document is loaded so it never follows the content. A viewer is as tall as
    // it is shown; an auto-height view has none of its own, so it takes the finite height its host offered when it was first measured (a list row
    // measured against the list's height), and none when the host offered an unbounded one.
    private double? ViewportHeightDips()
    {
        var height = _scrollMode == HtmlViewScrollMode.Disabled ? _offeredHeight : ActualHeight;
        return height is > 1 and < 1e7 ? height : null;
    }

    private double? PinViewport() => _loadedViewport = ViewportHeightDips();

    // A screen document is one surface, so there is one "page" at the top of the canvas and no gap around it.
    private void UpdateMetrics()
    {
        var pages = _document?.Pages ?? [];
        _pageTops = new double[pages.Count];
        _pageHeights = new double[pages.Count];

        var gap = 0d;
        var top = 0d;
        var width = 0d;
        for (var i = 0; i < pages.Count; i++)
        {
            _pageTops[i] = top;
            _pageHeights[i] = pages[i].HeightPoints * PixelsPerPoint;
            top += _pageHeights[i] + gap;
            width = Math.Max(width, pages[i].WidthPoints * PixelsPerPoint);
        }

        var contentHeight = _document is null ? 0 : MeasureContentHeight(_document);
        _canvas.Width = Math.Max(1, width);
        _canvas.Height = Math.Max(1, _scrollMode == HtmlViewScrollMode.Disabled ? contentHeight : top);
        SetContentHeight(contentHeight);
    }

    private void InvalidateRegion(int page, PeachDrawing.Core.Rect pointsBounds)
    {
        if ((uint)page >= (uint)_pageTops.Length)
            return;

        if (_viewportMode)
        {
            // What is shown is painted by the layer above the scroller, from the document as it is now.
            _overlay.Invalidate();
            return;
        }

        try
        {
            _canvas.Invalidate(new Windows.Foundation.Rect(
                pointsBounds.X * PixelsPerPoint, _pageTops[page] + (pointsBounds.Y * PixelsPerPoint),
                pointsBounds.Width * PixelsPerPoint, pointsBounds.Height * PixelsPerPoint));
        }
        catch (ArgumentException)
        {
            // The canvas refuses a region outside the size it has been laid out at, as when the document has just grown; all of it is painted again.
            _canvas.Invalidate();
        }
    }

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        var document = _document;
        var context = _context;
        PaintedRegionCount += args.InvalidatedRegions.Count();
        foreach (var region in args.InvalidatedRegions)
        {
            using var session = sender.CreateDrawingSession(region);
            session.Clear(_viewportMode ? Microsoft.UI.Colors.Transparent : _pageBackground);
            if (document is null || context is null || _viewportMode)
                continue;

            for (var page = 0; page < _pageTops.Length; page++)
            {
                if (_pageTops[page] + _pageHeights[page] < region.Top || _pageTops[page] > region.Bottom)
                    continue;

                var size = document.Pages[page];
                session.Transform = Matrix3x2.CreateTranslation(0, (float)_pageTops[page]);
                using (var canvas = Win2dCanvas.FromSession(context, session, new PeachDrawing.Core.Rect(0, 0, size.WidthPoints * PixelsPerPoint, size.HeightPoints * PixelsPerPoint)))
                {
                    // Painting finishes without waiting for anything (what it draws was resolved during layout), so the task is already complete.
                    document.PaintPageAsync(page, canvas, _scrollMode == HtmlViewScrollMode.Disabled ? DocumentPaintLayer.All : DocumentPaintLayer.Content).GetAwaiter().GetResult();
                }

                session.Transform = Matrix3x2.Identity;
            }
        }
    }
}

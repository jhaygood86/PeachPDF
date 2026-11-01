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
    private const double PageGap = 8;
    private const double ContinuousPageHeightPoints = 14000;
    private const double ClickSlop = 4;

    /// <summary>Identifies the <see cref="Html"/> property.</summary>
    public static readonly DependencyProperty HtmlProperty =
        DependencyProperty.Register(nameof(Html), typeof(string), typeof(HtmlView), new PropertyMetadata(null, (d, _) => ((HtmlView)d).QueueReload()));

    /// <summary>Identifies the <see cref="Source"/> property.</summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(HtmlView), new PropertyMetadata(null, (d, _) => ((HtmlView)d).QueueReload()));

    private readonly ScrollViewer _scroller = new();
    private readonly CanvasVirtualControl _canvas = new();
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _reflowTimer;
    private Win2dRenderContext? _context;
    private HtmlDocument? _document;
    private double[] _pageTops = [];
    private double[] _pageHeights = [];
    private int _loadVersion;
    private double _loadedWidth = -1;
    private bool _loaded;

    /// <summary>Creates a view with nothing in it.</summary>
    public HtmlView()
    {
        IsTabStop = true;
        _canvas.HorizontalAlignment = HorizontalAlignment.Left;
        _canvas.VerticalAlignment = VerticalAlignment.Top;
        _canvas.RegionsInvalidated += OnRegionsInvalidated;
        _scroller.Content = _canvas;
        _scroller.ZoomMode = ZoomMode.Enabled;
        _scroller.MinZoomFactor = 0.5f;
        _scroller.MaxZoomFactor = 4f;
        _scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Content = _scroller;

        Loaded += (_, _) => OnLoaded();
        Unloaded += (_, _) => OnUnloaded();
        SizeChanged += (_, _) => QueueReflow();
        ActualThemeChanged += (_, _) => QueueReload();
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

    /// <summary>The colour behind the page.</summary>
    public Color PageBackground { get; set; } = Microsoft.UI.Colors.White;

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
                _scroller.ChangeView(null, (_pageTops[page] + (bounds.Y * PixelsPerPoint)) * _scroller.ZoomFactor, null);
                return true;
            }
        }

        return false;
    }

    private void OnLoaded()
    {
        _loaded = true;
        _context ??= new Win2dRenderContext(CanvasDevice.GetSharedDevice()) { PixelsPerPoint = PixelsPerPoint };
        _reflowTimer ??= DispatcherQueue.CreateTimer();
        _reflowTimer.Interval = TimeSpan.FromMilliseconds(150);
        _reflowTimer.IsRepeating = false;
        _reflowTimer.Tick += (_, _) => _ = ReloadAsync();
        QueueReload();
    }

    private void OnUnloaded()
    {
        _loaded = false;
        _loadVersion++;
        _reflowTimer?.Stop();
        _document?.Dispose();
        _document = null;
        _context?.Dispose();
        _context = null;
    }

    private void QueueReload()
    {
        if (_loaded)
            _ = ReloadAsync();
    }

    // A resize reflows the document, but only once the width has settled.
    private void QueueReflow()
    {
        if (_loaded && _reflowTimer is not null && Math.Abs(ActualWidth - _loadedWidth) > 0.5)
        {
            _reflowTimer.Stop();
            _reflowTimer.Start();
        }
    }

    private async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        var width = ActualWidth;
        if (_context is null || width < 1)
            return;

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

            _document?.Dispose();
            _document = document;
            _loadedWidth = width;
            document.Selection.SelectionChanged += (_, args) =>
            {
                foreach (var region in args.DirtyRegions)
                    InvalidateRegion(region.PageIndex, region.Bounds);
            };

            UpdateMetrics();
            _canvas.Invalidate();
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (version == _loadVersion)
        {
            DocumentFailed?.Invoke(this, new HtmlLoadFailedEventArgs(exception));
        }
    }

    private static async Task<string> ReadSourceAsync(Uri source)
    {
        if (source.IsFile)
            return await System.IO.File.ReadAllTextAsync(source.LocalPath);

        using var client = new System.Net.Http.HttpClient();
        return await client.GetStringAsync(source);
    }

    private HtmlDocumentOptions BuildOptions(double widthDips) => new()
    {
        Media = "screen",
        PageWidthPoints = widthDips / PixelsPerPoint,
        PageHeightPoints = ContinuousPageHeightPoints,
        MarginTopPoints = 0,
        MarginRightPoints = 0,
        MarginBottomPoints = 0,
        MarginLeftPoints = 0,
        RecordTextRuns = true,
        EnableSelectionStyling = true,
        CollectFormFields = true,
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

    // The pages stack one under another with a small gap; the canvas is as big as all of them.
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

        _canvas.Width = Math.Max(1, width);
        _canvas.Height = Math.Max(1, top);
    }

    private void InvalidateRegion(int page, PeachDrawing.Core.Rect pointsBounds)
    {
        if ((uint)page >= (uint)_pageTops.Length)
            return;

        _canvas.Invalidate(new Windows.Foundation.Rect(
            pointsBounds.X * PixelsPerPoint, _pageTops[page] + (pointsBounds.Y * PixelsPerPoint),
            pointsBounds.Width * PixelsPerPoint, pointsBounds.Height * PixelsPerPoint));
    }

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        var document = _document;
        var context = _context;
        foreach (var region in args.InvalidatedRegions)
        {
            using var session = sender.CreateDrawingSession(region);
            session.Clear(PageBackground);
            if (document is null || context is null)
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
                    document.PaintPageAsync(page, canvas).GetAwaiter().GetResult();
                }

                session.Transform = Matrix3x2.Identity;
            }
        }
    }
}

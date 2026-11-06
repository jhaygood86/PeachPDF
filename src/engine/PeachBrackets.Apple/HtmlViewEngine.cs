using CoreAnimation;
using CoreGraphics;
using Foundation;
using PeachBrackets.Accessibility;
using PeachBrackets.Apple.Logic;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using PeachDrawing.CoreGraphics;
using Rect = PeachDrawing.Core.Rect;

namespace PeachBrackets.Apple;

/// <summary>What the engine needs from the AppKit or UIKit view that hosts it.</summary>
internal interface IHtmlViewHost
{
    /// <summary>Device pixels per point of the screen the view is on.</summary>
    double BackingScale { get; }

    /// <summary>The width the document is laid out for, in CSS pixels (points of the view).</summary>
    double ViewportWidth { get; }

    /// <summary>The height of the view, which <c>vh</c> resolves against.</summary>
    double ViewportHeight { get; }

    bool IsDark { get; }

    bool AllowNetworkAccess { get; }

    Uri? DocumentBaseUri { get; }

    /// <summary>Whether the host asked for the document to play its animations.</summary>
    bool AnimationsEnabled { get; }

    /// <summary>Whether the system lets things move: false when it asks for reduced motion.</summary>
    bool SystemAllowsMotion { get; }

    /// <summary>Starts a clock that calls <paramref name="tick"/> on the main thread once a display frame, with the time in seconds from a fixed origin; disposing it stops it.</summary>
    IDisposable StartFrameClock(Action<TimeSpan> tick);

    /// <summary>The animation and transition events that happened since the last frame.</summary>
    void AnimationEvents(IReadOnlyList<AnimationEventInfo> events);

    /// <summary>The content got a new size (an animation laid the document out again): the host sizes what scrolls.</summary>
    void ContentResized();

    InputCapabilities? Input { get; }

    bool UseGpu { get; }

    CGColor PageBackground { get; }

    /// <summary>The pointing devices attached, when <see cref="Input"/> is not set.</summary>
    InputCapabilities DetectInput();

    /// <summary>A document has been adopted: the host sizes its content and asks for tiles.</summary>
    void DocumentAdopted();

    /// <summary>The document's content changed (a state, a selection, a restyle): the host asks for tiles again.</summary>
    void ContentInvalidated();

    void LoadFailed(Exception exception);

    /// <summary>A form is being submitted (a submit button was activated, or Enter was pressed in a field) with the values it holds.</summary>
    void FormSubmitting(FormSubmission submission);

    /// <summary>A submit was tried and some controls' values do not satisfy their constraints; the first has been given the focus.</summary>
    void FormInvalid(IReadOnlyList<ValidationProblem> problems);

    /// <summary>A text field took the focus or lost it: the host shows or hides its keyboard and routes key input to the engine.</summary>
    void EditingChanged(bool editing);

    /// <summary>The part of the page with the keyboard focus changed.</summary>
    void FocusChanged();

    /// <summary>Scrolls so that a rectangle of the content (CSS pixels) shows.</summary>
    void Reveal(CGRect frame);

    /// <summary>Lets the person choose one of a <c>&lt;select&gt;</c>'s options, near a rectangle of the content; calls <paramref name="chosen"/> with its index, or not at all when dismissed.</summary>
    void PickOption(IReadOnlyList<SelectOption> options, CGRect anchor, Action<int> chosen);

    /// <summary>Lets the person choose a date, a time, both, or a colour for a control (<paramref name="kind"/> is <c>date</c>, <c>time</c>, <c>datetime</c> or <c>color</c>); calls <paramref name="chosen"/> with the new value in the control's own format, or not at all when dismissed.</summary>
    void PickValue(string kind, string current, CGRect anchor, Action<string> chosen);

    /// <summary>Lets the person choose files for a file control; calls <paramref name="chosen"/> with them, or not at all when dismissed.</summary>
    void PickFiles(bool multiple, IReadOnlyList<string> mimeTypes, IReadOnlyList<string> extensions, Action<IReadOnlyList<ChosenFile>> chosen);

    /// <summary>The parts of the page an assistive technology stops on were taken again (a document loaded or changed); <paramref name="announcements"/> are live-region texts to speak.</summary>
    void AccessibilityRefreshed(IReadOnlyList<AccessibilityEntry> entries, IReadOnlyList<string> announcements);
}

/// <summary>A part of the page an assistive technology stops on, with where it is in the content (CSS pixels from the top-left).</summary>
/// <param name="Node">the part</param>
/// <param name="Frame">its rectangle in the content</param>
internal sealed record AccessibilityEntry(AccessibilityNode Node, CGRect Frame);

/// <summary>
/// What the AppKit and UIKit views share: loading and laying out the document off the main thread, the tiles the page is painted into (each its own
/// <see cref="CALayer"/>, so scrolling and zooming are composited by the graphics hardware and never repaint on the CPU), the layer that carries what is
/// attached to the viewport, and the pointer state the document's <c>:hover</c> rules read. Everything here is in CSS pixels, which are the points of
/// the view.
/// </summary>
internal sealed partial class HtmlViewEngine(IHtmlViewHost host) : IDisposable
{
    public const double PixelsPerPoint = 96.0 / 72.0;
    private const int MaxCachedTiles = 24;

    private HtmlViewRenderContext? _context;
    private HtmlDocument? _document;
    private double[] _pageTops = [];
    private double[] _pageHeights = [];
    private int _loadVersion;
    private double _loadedWidth = -1;
    private double _loadedViewport = -1;
    private NSTimer? _reflowTimer;
    private bool _attached;

    private TileCache<CALayer>? _tiles;
    private readonly List<CALayer> _stale = [];
    private HtmlDocument? _tilesDocument;
    private double _tilesScale;
    private bool _tilesInvalid;
    private (int Page, double X, double Y, bool Pressed)? _pendingPointer;
    private bool _clearPending;
    private bool _pumping;
    private AccessibilityTree? _accessibilityTree;
    private double _imagesChosenForZoom = 1;
    private NSTimer? _upgradeTimer;

    /// <summary>Holds one layer for each tile of the page, in CSS pixels from the top-left of the content.</summary>
    public CALayer ContentLayer { get; } = CALayer.Create();

    /// <summary>Holds what is attached to the viewport (fixed and sticky boxes, viewport backgrounds), painted again as the view scrolls.</summary>
    public CALayer OverlayLayer { get; } = CALayer.Create();

    public string? Html { get; set; }

    public HtmlDocument? Document => _document;

    public HtmlViewRenderContext? Context => _context;

    public double ContentWidth { get; private set; }

    public double ContentHeight { get; private set; }

    /// <summary>How many tiles were painted since the view was made, for the scroll benchmark.</summary>
    public int TilesPainted { get; private set; }

    public string SelectedText => _document is { } document && document.Selection.HasSelection ? document.Selection.GetText() : string.Empty;

    public void Attach()
    {
        _attached = true;
        _context ??= new HtmlViewRenderContext(() => host.AllowNetworkAccess, () => host.DocumentBaseUri, host.UseGpu) { PixelsPerPoint = PixelsPerPoint };
        _ = ReloadAsync();
    }

    public void Detach()
    {
        _attached = false;
        _loadVersion++;
        _reflowTimer?.Invalidate();
        _reflowTimer = null;
        _upgradeTimer?.Invalidate();
        _upgradeTimer = null;
        StopAnimationLoop();
        _accessibilityTree = null;
        _imagesChosenForZoom = 1;
        ResetInteractionState();
        ResetTiles();
        RetireDocument(_document);
        _document = null;
        RetireContext(_context);
        _context = null;
    }

    // ---- lifetimes: what async work holds is not disposed under it ----

    private int _busy;
    private readonly List<HtmlDocument> _retiredDocuments = [];

    // The document is disposed once no queued or running change is using it.
    private void RetireDocument(HtmlDocument? document)
    {
        if (document is null)
            return;

        if (_busy > 0)
            _retiredDocuments.Add(document);
        else
            document.Dispose();
    }

    private void BeginWork()
    {
        _busy++;
        if (_context is { } context)
            BeginLoad(context);
    }

    private void EndWork(HtmlViewRenderContext? context)
    {
        if (context is not null)
            EndLoad(context);

        if (--_busy == 0 && _retiredDocuments.Count > 0)
        {
            foreach (var document in _retiredDocuments)
                document.Dispose();

            _retiredDocuments.Clear();
        }
    }

    public void Dispose() => Detach();

    /// <summary>Reflows after a resize or a theme change, once it has settled.</summary>
    public void QueueReflow()
    {
        if (!_attached)
            return;

        _reflowTimer?.Invalidate();
        _reflowTimer = NSTimer.CreateScheduledTimer(0.15, timer => { _ = ReloadAsync(); });
    }

    /// <summary>Whether the view's size differs enough from the size the document was laid out for to lay it out again.</summary>
    public bool NeedsReflow() =>
        _attached && (Math.Abs(host.ViewportWidth - _loadedWidth) > 0.5 || (_loadedViewport > 0 && Math.Abs(host.ViewportHeight - _loadedViewport) > 0.5));

    public async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        var width = host.ViewportWidth;
        if (_context is not { } context || Html is not { } html || width < 1)
            return;

        var options = BuildOptions(width);
        // The size this load is for, recorded now: a load that is slow or fails must not look like a document that needs laying out again at every pass.
        _loadedWidth = width;
        BeginLoad(context);
        try
        {
            // Reading the fonts, parsing, styling and laying the document out is work for a pool thread; the view goes on taking input meanwhile.
            var document = await Task.Run(() => LoadDocumentAsync(html, context, options));
            if (version != _loadVersion)
            {
                document.Dispose();
                return;
            }

            ResetInteractionState();
            RetireDocument(_document);
            _document = document;
            document.Selection.SelectionChanged += (_, e) => Invalidate(e.DirtyRegions);
            StopAnimationLoop();
            _lastRealTime = null;
            UpdateMetrics();
            Invalidate();
            // The accessibility tree first, so what a DocumentLoaded handler asks of the view already includes it.
            RefreshAccessibility();
            host.DocumentAdopted();
            AdoptAnimations(document);
        }
        catch (Exception exception) when (version == _loadVersion)
        {
            host.LoadFailed(exception);
        }
        finally
        {
            EndLoad(context);
        }
    }

    private static async Task<HtmlDocument> LoadDocumentAsync(string html, HtmlViewRenderContext context, HtmlDocumentOptions options)
    {
        var document = await HtmlDocument.LoadAsync(html, context, options);
        try
        {
            using var measuring = new CoreGraphicsComposer(context, 1, 1, 1, 1);
            await document.LayoutAsync(measuring.Canvas);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    // A load that is running uses its render context from a pool thread, so a context the view lets go of is only disposed once the last load
    // that holds it has finished. All of this runs on the main thread.
    private readonly Dictionary<HtmlViewRenderContext, int> _loadsUsing = [];
    private readonly HashSet<HtmlViewRenderContext> _retired = [];

    private void BeginLoad(HtmlViewRenderContext context) => _loadsUsing[context] = _loadsUsing.GetValueOrDefault(context) + 1;

    private void EndLoad(HtmlViewRenderContext context)
    {
        if (--_loadsUsing[context] > 0)
            return;

        _loadsUsing.Remove(context);
        if (_retired.Remove(context))
            context.Dispose();
    }

    private void RetireContext(HtmlViewRenderContext? context)
    {
        if (context is null)
            return;

        if (_loadsUsing.ContainsKey(context))
            _retired.Add(context);
        else
            context.Dispose();
    }

    private HtmlDocumentOptions BuildOptions(double width)
    {
        _loadedViewport = host.ViewportHeight > 1 ? host.ViewportHeight : -1;
        var options = new HtmlDocumentOptions
        {
            MediaType = MediaType.Screen,
            PageWidthPoints = width / PixelsPerPoint,
            ViewportHeightPoints = _loadedViewport > 0 ? _loadedViewport / PixelsPerPoint : null,
            MarginTopPoints = 0,
            MarginRightPoints = 0,
            MarginBottomPoints = 0,
            MarginLeftPoints = 0,
            RecordTextRuns = true,
            EnableSelectionStyling = true,
            CollectFormFields = true,
            PaintFormControls = true,
            ColorScheme = host.IsDark ? ColorSchemePreference.Dark : ColorSchemePreference.Light,
            Input = host.Input ?? host.DetectInput(),
            DevicePixelRatio = ImageScale.DevicePixelRatio(host.BackingScale),
        };
        ApplyAnimationOptions(options);
        return options;
    }

    // A screen document is one surface, so there is a single "page" at the top and no gap around it.
    private void UpdateMetrics()
    {
        var pages = _document?.Pages ?? [];
        _pageTops = new double[pages.Count];
        _pageHeights = new double[pages.Count];
        double top = 0, width = 0;
        for (var i = 0; i < pages.Count; i++)
        {
            _pageTops[i] = top;
            _pageHeights[i] = pages[i].HeightPoints * PixelsPerPoint;
            top += _pageHeights[i];
            width = Math.Max(width, pages[i].WidthPoints * PixelsPerPoint);
        }

        ContentWidth = width;
        ContentHeight = top;
    }

    // ---- painting ----

    /// <summary>Marks every tile out of date: the next <see cref="UpdateTiles"/> paints what shows again.</summary>
    public void Invalidate()
    {
        _tilesInvalid = true;
        _contentVersion++;
        ScheduleContentUpdate();
    }

    /// <summary>
    /// Marks only the tiles that regions of the page touch as out of date (a typed character, a hover, a caret blink, a selection step touch a few), and leaves the rest
    /// as they are. A change that moved the content (the page got taller) touches everything below it, so that is a full <see cref="Invalidate()"/>.
    /// </summary>
    public void Invalidate(IReadOnlyList<DirtyRegion> regions)
    {
        if (regions.Count == 0)
            return;

        if (_tiles is null || _tilesInvalid || !ReferenceEquals(_tilesDocument, _document) || ContentWidth != _tilesContentWidth || ContentHeight != _tilesContentHeight)
        {
            Invalidate();
            return;
        }

        var scale = _tilesScale;
        foreach (var region in regions)
        {
            if ((uint)region.PageIndex >= (uint)_pageTops.Length)
            {
                Invalidate();
                return;
            }

            var bounds = region.Bounds;
            var left = (bounds.X * PixelsPerPoint * scale) - 2;
            var top = (((_pageTops[region.PageIndex]) + (bounds.Y * PixelsPerPoint)) * scale) - 2;
            var right = left + (bounds.Width * PixelsPerPoint * scale) + 4;
            var bottom = top + (bounds.Height * PixelsPerPoint * scale) + 4;
            var lastColumn = (int)Math.Ceiling(ContentWidth * scale / TileSize) - 1;
            var lastRow = (int)Math.Ceiling(ContentHeight * scale / TileSize) - 1;
            for (var row = Math.Max(0, (int)Math.Floor(top / TileSize)); row <= Math.Min(lastRow, (int)Math.Floor(bottom / TileSize)); row++)
            {
                for (var column = Math.Max(0, (int)Math.Floor(left / TileSize)); column <= Math.Min(lastColumn, (int)Math.Floor(right / TileSize)); column++)
                    _dirtyTiles.Add(new TileKey(column, row));
            }
        }

        _contentVersion++;
        ScheduleContentUpdate();
    }

    // One update a turn of the run loop however many changes came (an edit, the caret, a selection step all ask), not one for each.
    private void ScheduleContentUpdate()
    {
        if (_invalidatePending)
            return;

        _invalidatePending = true;
        NSRunLoop.Main.BeginInvokeOnMainThread(() =>
        {
            _invalidatePending = false;
            host.ContentInvalidated();
        });
    }

    private const int TileSize = 512;
    private readonly HashSet<TileKey> _dirtyTiles = [];
    private double _tilesContentWidth;
    private double _tilesContentHeight;

    private bool _invalidatePending;
    private int _contentVersion;

    /// <summary>Tells the document which part of it shows, so a fixed box stays where it was put in the view and a sticky one follows the top of it.</summary>
    public void SetViewport(CGRect visible)
    {
        if (_document is not { } document)
            return;

        var dirty = document.SetViewport(new Rect(visible.X / PixelsPerPoint, visible.Y / PixelsPerPoint, visible.Width / PixelsPerPoint, visible.Height / PixelsPerPoint));

        // What is attached to the viewport changed (a sticky box began to stick, a fixed one moved): the layer over the tiles is painted again, and if a box moved
        // between the layers, the tiles too.
        if (dirty.Count > 0)
            _overlayStale = true;

        if (document.ViewportLayersChanged)
            _tilesInvalid = true;
    }

    private bool _overlayStale = true;
    private double _overlayScale;
    private (int, int) _overlaySize;
    private int _overlayVersion = -1;

    /// <summary>
    /// Makes the tiles that hold <paramref name="visible"/> (in CSS pixels of the content) at a zoom, painting those not already held, and puts what is
    /// attached to the viewport over them.
    /// </summary>
    public void UpdateTiles(CGRect visible, double zoom)
    {
        if (_document is not { } document || _context is not { } context || zoom <= 0)
            return;

        var scale = host.BackingScale * zoom;
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        try
        {
            if (document.HasViewportBackgrounds)
            {
                // What is attached to the viewport changes under every scroll step, so the page cannot be held in tiles: it is painted straight.
                ResetTiles();
                PaintOverlay(document, context, visible, scale, DocumentPaintLayer.All);
                return;
            }

            if (_tiles is null || _tilesInvalid || !ReferenceEquals(_tilesDocument, document) || _tilesScale != scale)
            {
                _dirtyTiles.Clear();
                // Old tiles stay until the new ones are in, so a repaint does not flash.
                RetireTiles();
                _tiles = new TileCache<CALayer>(MaxCachedTiles, static layer => layer.RemoveFromSuperLayer());
                _tilesDocument = document;
                _tilesScale = scale;
                _tilesInvalid = false;
            }

            var contentWidth = ContentWidth * scale;
            var contentHeight = ContentHeight * scale;
            // A margin of a quarter of a screen each way, so the tile a scroll is about to reveal is already there.
            var marginX = visible.Width * scale * 0.25;
            var marginY = visible.Height * scale * 0.25;
            var around = TileGrid.Visible(visible.X * scale - marginX, visible.Y * scale - marginY, (visible.Width * scale) + (2 * marginX),
                (visible.Height * scale) + (2 * marginY), contentWidth, contentHeight, TileSize);
            var shown = TileGrid.Visible(visible.X * scale, visible.Y * scale, visible.Width * scale, visible.Height * scale, contentWidth, contentHeight, TileSize);
            _tiles.EnsureCapacity(around.Count + 2);

            // What shows first, then what the scroll is about to reveal. A tile costs about as much as a frame, so only as many are painted in one go as fit a small
            // budget (always at least one); the rest are painted on the following turns of the run loop, so a step that crosses into new tiles never holds the main thread
            // for a whole row of them.
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var complete = true;
            foreach (var key in shown.Concat(around.Where(k => !shown.Contains(k))))
            {
                // A tile is painted when it is missing, or when a change touched it (the old one stays until its replacement is in).
                if (_tiles.TryGet(key, out _) && !_dirtyTiles.Contains(key))
                    continue;

                if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= TileBudgetMilliseconds)
                {
                    complete = false;
                    break;
                }

                var bounds = TileGrid.Bounds(key, contentWidth, contentHeight, TileSize);
                var image = RenderImage(document, context, bounds.X, bounds.Y, bounds.Width, bounds.Height, scale, DocumentPaintLayer.Content);
                if (image is null)
                    continue;

                var layer = CALayer.Create();
                layer.AnchorPoint = CGPoint.Empty;
                layer.Frame = new CGRect(bounds.X / scale, bounds.Y / scale, bounds.Width / scale, bounds.Height / scale);
                layer.Contents = image;
                layer.Opaque = true;
                layer.MagnificationFilter = CALayer.FilterLinear;
                layer.MinificationFilter = CALayer.FilterTrilinear;
                ContentLayer.AddSublayer(layer);
                _tiles.Add(key, layer); // replaces, and so removes, the tile this one repaints
                _dirtyTiles.Remove(key);
                TilesPainted++;
            }

            _tilesContentWidth = ContentWidth;
            _tilesContentHeight = ContentHeight;
            if (complete)
            {
                // Old tiles went only now that the new ones are all in, so a repaint does not flash.
                foreach (var layer in _stale)
                    layer.RemoveFromSuperLayer();

                _stale.Clear();
            }
            else
            {
                ContinueTilesLater();
            }

            PaintOverlay(document, context, visible, scale, DocumentPaintLayer.ViewportAttached);
        }
        finally
        {
            CATransaction.Commit();
        }
    }

    private const double TileBudgetMilliseconds = 6;
    private bool _continuePending;

    private void ContinueTilesLater()
    {
        if (_continuePending)
            return;

        _continuePending = true;
        NSRunLoop.Main.BeginInvokeOnMainThread(() =>
        {
            _continuePending = false;
            host.ContentInvalidated();
        });
    }

    private void PaintOverlay(HtmlDocument document, HtmlViewRenderContext context, CGRect visible, double scale, DocumentPaintLayer layer)
    {
        var width = (int)Math.Ceiling(visible.Width * scale);
        var height = (int)Math.Ceiling(visible.Height * scale);
        if (width < 1 || height < 1)
            return;

        // The layer is placed at a whole device pixel so text is not smeared.
        var originX = Math.Round(visible.X * scale);
        var originY = Math.Round(visible.Y * scale);

        // What is attached to the viewport looks the same wherever the view is scrolled to, so the layer is painted again only when that changed (the document, the
        // size, the zoom, or the page being painted straight, which does move under the scroll) and otherwise only moved with the viewport.
        var straight = layer == DocumentPaintLayer.All;
        if (straight || _overlayStale || _overlayVersion != _contentVersion || _overlayScale != scale || _overlaySize != (width, height) || OverlayLayer.Contents is null)
        {
            var image = RenderImage(document, context, originX, originY, width, height, scale, layer, transparent: layer == DocumentPaintLayer.ViewportAttached);
            OverlayLayer.Contents = image;
            _overlayStale = false;
            _overlayVersion = _contentVersion;
            _overlayScale = scale;
            _overlaySize = (width, height);
        }

        OverlayLayer.AnchorPoint = CGPoint.Empty;
        OverlayLayer.Frame = new CGRect(originX / scale, originY / scale, width / scale, height / scale);
        OverlayLayer.MagnificationFilter = CALayer.FilterLinear;
    }

    private void RetireTiles()
    {
        if (_tiles is null)
            return;

        // Take the layers out of the cache without removing them from the view yet.
        foreach (var layer in ContentLayer.Sublayers ?? [])
            _stale.Add(layer);

        _tiles = null;
    }

    private void ResetTiles()
    {
        foreach (var layer in ContentLayer.Sublayers ?? [])
            layer.RemoveFromSuperLayer();

        _stale.Clear();
        _dirtyTiles.Clear();
        _tiles = null;
        _tilesDocument = null;
    }

    // One device-pixel window of the page painted into an image: the part that starts at (originX, originY) device pixels and is widthPx by heightPx.
    private CGImage? RenderImage(HtmlDocument document, HtmlViewRenderContext context, double originX, double originY, int widthPx, int heightPx,
        double scale, DocumentPaintLayer layer, bool transparent = false)
    {
        using var colorSpace = CGColorSpace.CreateSrgb();
        using var bitmap = new CGBitmapContext(IntPtr.Zero, widthPx, heightPx, 8, 0, colorSpace, CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little);
        if (!transparent)
        {
            bitmap.SetFillColor(host.PageBackground);
            bitmap.FillRect(new CGRect(0, 0, widthPx, heightPx));
        }

        // Y down, from the window's top-left, as the canvas draws.
        bitmap.TranslateCTM(0, heightPx);
        bitmap.ScaleCTM(1, -1);
        PaintWindow(bitmap, document, context, originX, originY, widthPx, heightPx, scale, layer);
        return bitmap.ToImage();
    }

    private void PaintWindow(CGContext cg, HtmlDocument document, HtmlViewRenderContext context, double originX, double originY, double widthPx, double heightPx,
        double scale, DocumentPaintLayer layer)
    {
        var visibleTop = originY / scale;
        var visibleBottom = (originY + heightPx) / scale;
        var visibleLeft = originX / scale;
        var visibleRight = (originX + widthPx) / scale;

        for (var page = 0; page < _pageTops.Length; page++)
        {
            var top = _pageTops[page];
            if (top + _pageHeights[page] < visibleTop || top > visibleBottom)
                continue;

            var size = document.Pages[page];
            cg.SaveState();
            cg.TranslateCTM((nfloat)(-originX), (nfloat)(-originY));
            cg.ScaleCTM((nfloat)scale, (nfloat)scale);
            cg.TranslateCTM(0, (nfloat)top);

            // Only the part of the page in view, in points from the top-left of the sheet.
            var region = new Rect(
                Math.Max(0, visibleLeft) / PixelsPerPoint, Math.Max(0, visibleTop - top) / PixelsPerPoint,
                (visibleRight - visibleLeft) / PixelsPerPoint, (visibleBottom - Math.Max(visibleTop, top)) / PixelsPerPoint);

            using (var target = CoreGraphicsCanvas.FromContext(context, cg, new Rect(0, 0, size.WidthPoints * PixelsPerPoint, size.HeightPoints * PixelsPerPoint),
                       originAtBottomLeft: false))
            {
                // Painting finishes without waiting for anything (what it draws was resolved during layout), so the task is already complete. The layer of what the
                // viewport holds is painted whole, not by region: a stuck box is painted where the viewport is, but its layout position is still where it would be if
                // it scrolled, so a region around what is in view would cull it.
                if (layer == DocumentPaintLayer.ViewportAttached)
                    document.PaintPageAsync(page, target, layer).GetAwaiter().GetResult();
                else
                    document.PaintPageRegionAsync(page, target, region, layer).GetAwaiter().GetResult();
            }

            cg.RestoreState();
        }
    }

    // ---- where things are ----

    /// <summary>The page a point of the content is on, and the point in points from the page's top-left; false between pages.</summary>
    public bool TryLocate(double x, double y, out int page, out double pageX, out double pageY)
    {
        page = PageLayout.PageAt(_pageTops, _pageHeights, y, out var localY);
        pageX = x / PixelsPerPoint;
        pageY = localY / PixelsPerPoint;
        return page >= 0 && _document is not null;
    }

    /// <summary>The top of an element in CSS pixels of the content, or null when there is no such element.</summary>
    public double? ElementTop(string elementId)
    {
        if (_document?.GetElementBounds(elementId) is not { } bounds)
            return null;

        for (var page = 0; page < _pageTops.Length; page++)
        {
            if (bounds.Y * PixelsPerPoint < _pageHeights[page])
                return _pageTops[page] + (bounds.Y * PixelsPerPoint);
        }

        return null;
    }

    /// <summary>Where an anchor or a destination of a link is, in CSS pixels from the top of the content; null when it is nowhere.</summary>
    public double? LinkTargetTop(PeachBrackets.Annotations.PageLink link)
    {
        if (link.Anchor is { } anchor && _document?.GetScrollOffset(anchor) is { } offset && _pageTops.Length > 0)
            return _pageTops[0] + (offset * PixelsPerPoint);

        if (link.Destination is { } destination && (uint)destination.PageIndex < (uint)_pageTops.Length)
            return _pageTops[destination.PageIndex] + (destination.Top * PixelsPerPoint);

        return null;
    }

    public PeachBrackets.Annotations.PageLink? LinkAt(int page, double x, double y) =>
        _document?.Annotations.Links.LastOrDefault(l => l.PageIndex == page && l.Bounds.Contains(x, y));

    // ---- the pointer state the document's :hover and :active rules read ----

    public void RequestState(int page, double x, double y, bool pressed)
    {
        _pendingPointer = (page, x, y, pressed);
        _clearPending = false;
        _ = PumpStateAsync();
    }

    public void RequestClear()
    {
        _pendingPointer = null;
        _clearPending = true;
        _ = PumpStateAsync();
    }

    /// <summary>Takes in the result of a change the document made to itself (a restyle, a form value).</summary>
    public void ApplyUpdate(UpdateResult result)
    {
        if (!result.Applied || result.DirtyRegions.Count == 0)
            return;

        UpdateMetrics();
        Invalidate(result.DirtyRegions);
        RefreshAccessibility();
    }

    // One state change at a time: a restyle lays the document out again, and only the pointer's latest position matters.
    private async Task PumpStateAsync()
    {
        if (_pumping || _stepping)
            return;

        _pumping = true;
        BeginWork();
        var held = _context;
        try
        {
            while (_document is { } document && _context is { } context && (_pendingPointer is not null || _clearPending))
            {
                UpdateResult result;
                using var measuring = new CoreGraphicsComposer(context, 1, 1, 1, 1);
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

                // A hover or a press restyles the page; it does not change what an assistive technology reads, so only the tiles are painted again.
                if (ReferenceEquals(document, _document) && result.Applied && result.DirtyRegions.Count > 0)
                {
                    UpdateMetrics();
                    Invalidate(result.DirtyRegions);
                }
            }
        }
        finally
        {
            _pumping = false;
            EndWork(held);
        }
    }

    // ---- accessibility ----

    /// <summary>Takes the accessibility tree again and tells the host which parts of the page to stop on and what to announce.</summary>
    public void RefreshAccessibility()
    {
        if (_document is not { } document)
            return;

        var before = _accessibilityTree;
        var after = _accessibilityTree = document.GetAccessibilityTree();
        var diff = before is null ? null : AccessibilityTreeDiff.Compare(before, after);
        var entries = new List<AccessibilityEntry>();

        void Add(AccessibilityNode node)
        {
            var children = node.ReadingChildren.ToList();
            var text = node.ReadingText;
            if (AppleAccessibility.IsElement(node.Role, children.Count > 0, !string.IsNullOrWhiteSpace(text)) && FrameOf(node) is { } frame)
                entries.Add(new AccessibilityEntry(node, frame));

            foreach (var child in children)
                Add(child);
        }

        foreach (var child in after.Root.ReadingChildren)
            Add(child);

        var announcements = diff?.Announcements.Select(a => a.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
        host.AccessibilityRefreshed(entries, announcements);
    }

    // A node's rectangle in the content, in CSS pixels. A node that crosses a page break is reported where it starts.
    private CGRect? FrameOf(AccessibilityNode node)
    {
        if (node.Bounds.Count == 0 || (uint)node.Bounds[0].PageIndex >= (uint)_pageTops.Length)
            return null;

        var (page, bounds) = node.Bounds[0];
        return new CGRect(bounds.X * PixelsPerPoint, _pageTops[page] + (bounds.Y * PixelsPerPoint), bounds.Width * PixelsPerPoint, bounds.Height * PixelsPerPoint);
    }

    // ---- images ----

    /// <summary>
    /// After a zoom has settled, asks the document for denser images (<c>srcset</c>, <c>image-set()</c>) for how large they are shown now. The document keeps its
    /// layout while it loads them, and the tiles are painted again with what changed.
    /// </summary>
    public void QueueImageUpgrade(double zoom)
    {
        if (_document is null || !ImageScale.NeedsUpgrade(_imagesChosenForZoom, zoom))
            return;

        _upgradeTimer?.Invalidate();
        _upgradeTimer = NSTimer.CreateScheduledTimer(ImageScale.SettleMilliseconds / 1000.0, timer => { _ = UpgradeImagesAsync(zoom); });
    }

    private async Task UpgradeImagesAsync(double zoom)
    {
        if (_document is not { } document)
            return;

        document.SetRenderScale(zoom, ImageScale.DevicePixelRatio(host.BackingScale));
        _imagesChosenForZoom = Math.Max(_imagesChosenForZoom, zoom);
        try
        {
            var result = await document.UpgradeImagesAsync();
            if (ReferenceEquals(document, _document) && result.DirtyRegions.Count > 0)
            {
                UpdateMetrics();
                Invalidate(result.DirtyRegions);
            }
        }
        catch (Exception exception) when (ReferenceEquals(document, _document))
        {
            host.LoadFailed(exception);
        }
    }
}

using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using PeachDrawing.Core;
using PeachDrawing.Skia;
using SkiaSharp;

namespace PeachBrackets.Avalonia;

public sealed partial class HtmlView
{
    // The page painted so far, as textures on the graphics hardware: the part of the page that does not move with the scroll (the content layer),
    // cut into tiles at the current zoom. A scroll draws the tiles it holds, and only a tile it does not hold is painted. What moves with the
    // viewport (a fixed or stuck box) is painted over them on every frame, as the document's own layers separate it.
    private const int MaxCachedTiles = 16;

    private TileCache<SKImage>? _tiles;
    private HtmlDocument? _tilesDocument;
    private GRContext? _tilesContext;
    private double _tilesScale;

    // Counts what has changed in the tiles' content. A paint that rebuilds the tiles remembers the count it read before it began, so a change that comes in while
    // it paints is still seen by the next one (a flag the render thread cleared after painting would lose it).
    private int _tilesGeneration;
    private int _tilesBuiltGeneration = -1;
    private double _tilesLastOriginY;
    private int _tilesHeading;

    // Counts the frames the render thread painted, for tests and diagnostics.
    private int _framesPainted;

    internal int FramesPainted => Volatile.Read(ref _framesPainted);

    /// <summary>How many changes to what the tiles show have been asked for. For tests.</summary>
    internal int TilesGeneration => Volatile.Read(ref _tilesGeneration);

    // What a frame is painted from, read once on the UI thread when the frame is recorded: the render thread then uses these and never the properties that move
    // under it (a scroll or a zoom between the recording and the painting would otherwise mix two frames).
    private readonly record struct PaintState(double ScrollX, double ScrollY, double Scale, SKColor Background);

    private bool _paintFailureReported;

    /// <summary>Paints through the tiles even with no graphics context, into raster surfaces, so a test can run the tile path a headless session has no GPU for.</summary>
    internal bool TilesOnCpuForTests { get; set; }

    /// <summary>
    /// Captures what a render would, on the calling (UI) thread, and returns the paint a render thread would run with it, so a test can run it from a thread of
    /// its own onto a canvas of its own.
    /// </summary>
    internal Action<SKCanvas, double, double> CapturePaintForTests()
    {
        var background = PageBackground;
        var state = new PaintState(_scrollX, _scrollY, Scale, new SKColor(background.R, background.G, background.B, background.A));
        return (canvas, widthPx, heightPx) => PaintFrame(canvas, state, widthPx, heightPx, null);
    }

    /// <summary>Holds tiles the way a paint on the graphics hardware does, so a test can see which ones a change drops without a graphics context.</summary>
    internal TileCache<SKImage> SeedTilesForTests()
    {
        lock (_documentLock)
        {
            ResetTiles();
            _tiles = new TileCache<SKImage>(MaxCachedTiles, static image => image.Dispose());
            _tilesDocument = _document;
            _tilesScale = Scale;
            return _tiles;
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new global::Avalonia.Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var background = PageBackground;
        var state = new PaintState(_scrollX, _scrollY, Scale, new SKColor(background.R, background.G, background.B, background.A));
        context.Custom(new HtmlDrawOperation(this, bounds, state));
        PaintScrollbarIndicator(context);
    }

    /// <summary>Asks for the control to be drawn again with what it holds, as a scroll or a zoom does.</summary>
    private void RequestPaint() => PostToUi(InvalidateVisual);

    /// <summary>Asks for the control to be drawn again after something it shows changed (a state, a selection, a caret, a form value): the tiles are painted again.</summary>
    private void InvalidateContent()
    {
        MarkTilesStale();
        RequestPaint();

        // What changed may have started a transition.
        EnsureAnimationLoop();
    }

    // What the tiles show changed as a whole (a state, the page's background): the next frame paints them again.
    private void MarkTilesStale() => Interlocked.Increment(ref _tilesGeneration);

    /// <summary>Asks for the tiles that hold any of the given regions to be painted again, and for nothing else.</summary>
    private void InvalidateRegions(IReadOnlyList<PeachBrackets.Updates.DirtyRegion> regions)
    {
        if (regions.Count == 0)
            return;

        InvalidateTiles(regions);
        RequestPaint();
        EnsureAnimationLoop();
    }

    private void InvalidateTiles(IReadOnlyList<PeachBrackets.Updates.DirtyRegion> regions)
    {
        // The render thread reads the tiles while it paints.
        lock (_documentLock)
        {
            if (_tiles is not { } tiles)
                return;

            var scale = Scale;
            var contentWidth = _contentWidth * scale;
            var contentHeight = _contentHeight * scale;
            foreach (var region in regions)
            {
                if ((uint)region.PageIndex >= (uint)_pageTops.Length)
                    continue;

                var (left, top, width, height) = AnimationLogic.RegionToPixels(region.Bounds, _pageTops[region.PageIndex], PixelsPerPoint, scale);
                foreach (var key in TileGrid.Visible(left, top, width, height, contentWidth, contentHeight))
                    tiles.Remove(key);
            }
        }
    }

    // Called on the render thread with the Skia canvas of the lease, in device pixels, for a control `widthPx` by `heightPx` pixels.
    private void PaintFrame(SKCanvas canvas, PaintState state, double widthPx, double heightPx, GRContext? gpu)
    {
        canvas.Clear(state.Background);
        Interlocked.Increment(ref _framesPainted);

        // An exception that left the render callback would be the renderer's to deal with; it is the document's failure, told to the application on the UI thread.
        try
        {
            lock (_documentLock)
                PaintFrameLocked(canvas, gpu, state, widthPx, heightPx);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_paintFailureReported)
            {
                _paintFailureReported = true;
                PostToUi(() => RaiseDocumentFailed(exception));
            }
        }
    }

    private void PaintFrameLocked(SKCanvas canvas, GRContext? gpu, PaintState state, double widthPx, double heightPx)
    {
        var document = _document;
        var context = _context;
        if (document is null || context is null)
            return;

        // The context uploads each image to the hardware once, for the one the control draws with.
        context.GpuContext = gpu;

        // Whole pixels, so a tile (or the page painted straight) lands on the pixel grid and text is not smeared.
        var originX = Math.Round(state.ScrollX);
        var originY = Math.Round(state.ScrollY);

        // A page whose background is attached to the viewport changes under every scroll step, so it cannot be held in tiles.
        if ((gpu is not null || TilesOnCpuForTests) && !document.HasViewportBackgrounds && TryPaintTiles(canvas, gpu, document, state, originX, originY, widthPx, heightPx))
        {
            // The boxes that only move or fade are not in the tiles: each frame draws them over, and the boxes the viewport holds in place are over those.
            PaintWindow(canvas, document, state.Scale, originX, originY, widthPx, heightPx, DocumentPaintLayer.Animated);
            PaintWindow(canvas, document, state.Scale, originX, originY, widthPx, heightPx, DocumentPaintLayer.ViewportAttached);
            return;
        }

        // Painted straight, the layers are still drawn apart: what the viewport holds is drawn whole, because a stuck box is painted where the viewport is but its
        // layout position is where it would be if it scrolled, so painting everything by the region in view would cull it. (A background attached to the
        // viewport is part of the one pass.)
        if (document.HasViewportBackgrounds)
        {
            PaintWindow(canvas, document, state.Scale, originX, originY, widthPx, heightPx, DocumentPaintLayer.All);
            return;
        }

        PaintWindow(canvas, document, state.Scale, originX, originY, widthPx, heightPx, DocumentPaintLayer.Content);
        PaintWindow(canvas, document, state.Scale, originX, originY, widthPx, heightPx, DocumentPaintLayer.Animated);
        PaintWindow(canvas, document, state.Scale, originX, originY, widthPx, heightPx, DocumentPaintLayer.ViewportAttached);
    }

    // Draws the tiles that hold the part of the page in view, painting the ones that are not held. False when a tile cannot be made, in which case
    // nothing of the page has been drawn and the caller paints it straight.
    private bool TryPaintTiles(SKCanvas canvas, GRContext? gpu, HtmlDocument document, PaintState state, double originX, double originY, double widthPx, double heightPx)
    {
        var scale = state.Scale;
        var generation = Volatile.Read(ref _tilesGeneration);
        if (_tiles is null || generation != _tilesBuiltGeneration || !ReferenceEquals(_tilesDocument, document) || !ReferenceEquals(_tilesContext, gpu) || _tilesScale != scale)
        {
            ResetTiles();
            _tiles = new TileCache<SKImage>(MaxCachedTiles, static image => image.Dispose());
            _tilesDocument = document;
            _tilesContext = gpu;
            _tilesScale = scale;
            _tilesBuiltGeneration = generation;
        }

        var contentWidth = _contentWidth * scale;
        var contentHeight = _contentHeight * scale;
        var visible = TileGrid.Visible(originX, originY, widthPx, heightPx, contentWidth, contentHeight);
        _tiles.EnsureCapacity(visible.Count + 2);

        var painted = 0;
        foreach (var key in visible)
        {
            var bounds = TileGrid.Bounds(key, contentWidth, contentHeight);
            if (!_tiles.TryGet(key, out var tile))
            {
                tile = RenderTile(gpu, document, state, bounds);
                if (tile is null)
                    return false;

                _tiles.Add(key, tile);
                painted++;
            }

            canvas.DrawImage(tile, (float)(bounds.X - originX), (float)(bounds.Y - originY));
        }

        // A frame that found every tile it needed has time to spare: paint one just past the edge the scroll is heading for, so the frame that
        // reaches it finds it there. One tile a frame, and another frame is asked for.
        var moved = Math.Sign(originY - _tilesLastOriginY);
        _tilesLastOriginY = originY;
        if (moved != 0)
            _tilesHeading = moved;

        if (painted == 0 && _tilesHeading != 0 && visible.Count > 0 && PrefetchTile(gpu, document, state, visible, _tilesHeading, contentWidth, contentHeight))
            RequestPaint();

        return true;
    }

    // Paints the first tile missing from the row beyond the visible ones in the direction of the scroll. False when there is none to paint.
    private bool PrefetchTile(GRContext? gpu, HtmlDocument document, PaintState state, IReadOnlyList<TileKey> visible, int heading, double contentWidth, double contentHeight)
    {
        var edgeRow = heading > 0 ? visible.Max(k => k.Row) : visible.Min(k => k.Row);
        var row = edgeRow + heading;
        var lastRow = (int)Math.Ceiling(contentHeight / TileGrid.TileSize) - 1;
        if (row < 0 || row > lastRow)
            return false;

        foreach (var column in visible.Select(k => k.Column).Distinct())
        {
            var key = new TileKey(column, row);
            if (_tiles!.TryGet(key, out _))
                continue;

            var tile = RenderTile(gpu, document, state, TileGrid.Bounds(key, contentWidth, contentHeight));
            if (tile is null)
                return false;

            _tiles.Add(key, tile);
            return true;
        }

        return false;
    }

    // One tile: the content layer of the part of the page it covers, on the page's background, in a texture of the hardware.
    private SKImage? RenderTile(GRContext? gpu, HtmlDocument document, PaintState state, TileBounds bounds)
    {
        var info = new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = gpu is null ? SKSurface.Create(info) : SKSurface.Create(gpu, true, info);
        if (surface is null)
            return null;

        surface.Canvas.Clear(state.Background);
        PaintWindow(surface.Canvas, document, state.Scale, bounds.X, bounds.Y, bounds.Width, bounds.Height, DocumentPaintLayer.Content);
        return surface.Snapshot();
    }

    // Lets go of every tile, so the next frame paints what it needs again. Called with the document lock held, or while nothing paints.
    private void ResetTiles()
    {
        _tiles?.Clear();
        _tiles = null;
        _tilesDocument = null;
        _tilesContext = null;
        _tilesLastOriginY = 0;
        _tilesHeading = 0;
    }

    // Paints one layer of the document for the window of the page that starts at (originX, originY) device pixels and is widthPx by heightPx, onto a
    // canvas whose origin is that window's top-left.
    private void PaintWindow(SKCanvas canvas, HtmlDocument document, double pageScale, double originX, double originY, double widthPx, double heightPx, DocumentPaintLayer layer)
    {
        if (_context is not { } context)
            return;

        var scale = (float)pageScale;
        var visibleTop = originY / pageScale;
        var visibleBottom = (originY + heightPx) / pageScale;
        var visibleLeft = originX / pageScale;
        var visibleRight = (originX + widthPx) / pageScale;

        // The metrics are taken again after a layout, and the document's pages can be ahead of them for a moment: only the pages both know are painted.
        var pageCount = Math.Min(Math.Min(_pageTops.Length, _pageHeights.Length), document.Pages.Count);
        for (var page = 0; page < pageCount; page++)
        {
            var top = _pageTops[page];
            if (top + _pageHeights[page] < visibleTop || top > visibleBottom)
                continue;

            var size = document.Pages[page];
            canvas.Save();
            canvas.Translate((float)-originX, (float)-originY);
            canvas.Scale(scale);
            canvas.Translate(0, (float)top);

            // Only the part of the page in view, in points from the top-left of the sheet.
            var region = new PeachDrawing.Core.Rect(
                Math.Max(0, visibleLeft) / PixelsPerPoint, Math.Max(0, visibleTop - top) / PixelsPerPoint,
                (visibleRight - visibleLeft) / PixelsPerPoint, (visibleBottom - Math.Max(visibleTop, top)) / PixelsPerPoint);

            using (var target = SkiaCanvas.FromCanvas(context, canvas, new PeachDrawing.Core.Rect(0, 0, size.WidthPoints * PixelsPerPoint, size.HeightPoints * PixelsPerPoint)))
            {
                // Painting finishes without waiting for anything (what it draws was resolved during layout), so the task is already complete.
                // The layer of what the viewport holds is painted whole, not by region (see PaintFrameLocked).
                if (layer == DocumentPaintLayer.ViewportAttached)
                    document.PaintPageAsync(page, target, layer).GetAwaiter().GetResult();
                else
                    document.PaintPageRegionAsync(page, target, region, layer).GetAwaiter().GetResult();
            }

            canvas.Restore();
        }
    }

    // The custom draw operation Avalonia hands to its render thread: it paints the page onto the Skia canvas the renderer leases.
    private sealed class HtmlDrawOperation(HtmlView view, global::Avalonia.Rect bounds, PaintState state) : ICustomDrawOperation
    {
        public global::Avalonia.Rect Bounds => bounds;

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(global::Avalonia.Point p) => bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;

            // The pixels per device independent pixel the renderer draws this control at (the window's scale, which is what hit-testing uses as well).
            var scale = Math.Max(canvas.TotalMatrix.ScaleX, 0.01f);

            // Work in device pixels: the clip is the control in the units it is laid out in, and the page is painted at one device pixel to one canvas unit.
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            canvas.Scale(1f / scale);
            try
            {
                view.PaintFrame(canvas, state, bounds.Width * scale, bounds.Height * scale, lease.GrContext);
            }
            finally
            {
                canvas.Restore();
            }
        }
    }
}

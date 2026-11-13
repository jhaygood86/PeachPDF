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
    private volatile bool _tilesStale;
    private double _tilesLastOriginY;
    private int _tilesHeading;

    // Counts the frames the render thread painted, for tests and diagnostics.
    private int _framesPainted;

    internal int FramesPainted => Volatile.Read(ref _framesPainted);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new global::Avalonia.Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var background = PageBackground;
        context.Custom(new HtmlDrawOperation(this, bounds, _scrollX, _scrollY, new SKColor(background.R, background.G, background.B, background.A)));
        PaintScrollbarIndicator(context);
    }

    /// <summary>Asks for the control to be drawn again with what it holds, as a scroll or a zoom does.</summary>
    private void RequestPaint() => PostToUi(InvalidateVisual);

    /// <summary>Asks for the control to be drawn again after something it shows changed (a state, a selection, a caret, a form value): the tiles are painted again.</summary>
    private void InvalidateContent()
    {
        _tilesStale = true;
        RequestPaint();

        // What changed may have started a transition.
        EnsureAnimationLoop();
    }

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
    private void PaintFrame(SKCanvas canvas, SKColor background, double originX, double originY, double widthPx, double heightPx, GRContext? gpu)
    {
        canvas.Clear(background);
        Interlocked.Increment(ref _framesPainted);

        lock (_documentLock)
            PaintFrameLocked(canvas, gpu, originX, originY, widthPx, heightPx);
    }

    private void PaintFrameLocked(SKCanvas canvas, GRContext? gpu, double scrollX, double scrollY, double widthPx, double heightPx)
    {
        var document = _document;
        var context = _context;
        if (document is null || context is null)
            return;

        // The context uploads each image to the hardware once, for the one the control draws with.
        context.GpuContext = gpu;

        // Whole pixels, so a tile (or the page painted straight) lands on the pixel grid and text is not smeared.
        var originX = Math.Round(scrollX);
        var originY = Math.Round(scrollY);

        // A page whose background is attached to the viewport changes under every scroll step, so it cannot be held in tiles.
        if (gpu is not null && !document.HasViewportBackgrounds && TryPaintTiles(canvas, gpu, document, originX, originY, widthPx, heightPx))
        {
            // The boxes that only move or fade are not in the tiles: each frame draws them over, and the boxes the viewport holds in place are over those.
            PaintWindow(canvas, document, originX, originY, widthPx, heightPx, DocumentPaintLayer.Animated);
            PaintWindow(canvas, document, originX, originY, widthPx, heightPx, DocumentPaintLayer.ViewportAttached);
            return;
        }

        PaintWindow(canvas, document, originX, originY, widthPx, heightPx, DocumentPaintLayer.All);
    }

    // Draws the tiles that hold the part of the page in view, painting the ones that are not held. False when a tile cannot be made, in which case
    // nothing of the page has been drawn and the caller paints it straight.
    private bool TryPaintTiles(SKCanvas canvas, GRContext gpu, HtmlDocument document, double originX, double originY, double widthPx, double heightPx)
    {
        var scale = Scale;
        if (_tiles is null || _tilesStale || !ReferenceEquals(_tilesDocument, document) || !ReferenceEquals(_tilesContext, gpu) || _tilesScale != scale)
        {
            ResetTiles();
            _tiles = new TileCache<SKImage>(MaxCachedTiles, static image => image.Dispose());
            _tilesDocument = document;
            _tilesContext = gpu;
            _tilesScale = scale;
            _tilesStale = false;
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
                tile = RenderTile(gpu, document, bounds);
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

        if (painted == 0 && _tilesHeading != 0 && visible.Count > 0 && PrefetchTile(gpu, document, visible, _tilesHeading, contentWidth, contentHeight))
            RequestPaint();

        return true;
    }

    // Paints the first tile missing from the row beyond the visible ones in the direction of the scroll. False when there is none to paint.
    private bool PrefetchTile(GRContext gpu, HtmlDocument document, IReadOnlyList<TileKey> visible, int heading, double contentWidth, double contentHeight)
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

            var tile = RenderTile(gpu, document, TileGrid.Bounds(key, contentWidth, contentHeight));
            if (tile is null)
                return false;

            _tiles.Add(key, tile);
            return true;
        }

        return false;
    }

    // One tile: the content layer of the part of the page it covers, on the page's background, in a texture of the hardware.
    private SKImage? RenderTile(GRContext gpu, HtmlDocument document, TileBounds bounds)
    {
        using var surface = SKSurface.Create(gpu, true, new SKImageInfo(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
            return null;

        var background = PageBackground;
        surface.Canvas.Clear(new SKColor(background.R, background.G, background.B, background.A));
        PaintWindow(surface.Canvas, document, bounds.X, bounds.Y, bounds.Width, bounds.Height, DocumentPaintLayer.Content);
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
    private void PaintWindow(SKCanvas canvas, HtmlDocument document, double originX, double originY, double widthPx, double heightPx, DocumentPaintLayer layer)
    {
        if (_context is not { } context)
            return;

        var scale = (float)Scale;
        var visibleTop = originY / Scale;
        var visibleBottom = (originY + heightPx) / Scale;
        var visibleLeft = originX / Scale;
        var visibleRight = (originX + widthPx) / Scale;

        for (var page = 0; page < _pageTops.Length; page++)
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
                document.PaintPageRegionAsync(page, target, region, layer).GetAwaiter().GetResult();
            }

            canvas.Restore();
        }
    }

    // The custom draw operation Avalonia hands to its render thread: it paints the page onto the Skia canvas the renderer leases.
    private sealed class HtmlDrawOperation(HtmlView view, global::Avalonia.Rect bounds, double scrollX, double scrollY, SKColor background) : ICustomDrawOperation
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
            var scale = Math.Max(canvas.TotalMatrix.ScaleX, 0.01f);

            // Work in device pixels: the clip is the control in the units it is laid out in, and the page is painted at one device pixel to one canvas unit.
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            canvas.Scale(1f / scale);
            try
            {
                view.PaintFrame(canvas, background, scrollX, scrollY, bounds.Width * scale, bounds.Height * scale, lease.GrContext);
            }
            finally
            {
                canvas.Restore();
            }
        }
    }
}

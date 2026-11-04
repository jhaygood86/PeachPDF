using Android.Views;
using PeachBrackets.Android.Logic;
using PeachDrawing.Core;
using PeachDrawing.Skia;
using SkiaSharp;
using SkiaSharp.Views.Android;

namespace PeachBrackets.Android;

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

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e) => PaintFrame(e.Surface, e.Info, null);

    private void OnPaintGlSurface(object? sender, SKPaintGLSurfaceEventArgs e) => PaintFrame(e.Surface, e.Info, _glSurface?.GRContext);

    /// <summary>Asks for the view to be drawn again with what it holds, as a scroll or a zoom does.</summary>
    private void RequestPaint()
    {
        if (_glSurface is not null)
            _glSurface.RequestRender();
        else
            _surface.Invalidate();
    }

    /// <summary>Asks for the view to be drawn again after something it shows changed (a state, a selection, a caret, a form value): the tiles are painted again.</summary>
    private void InvalidateContent()
    {
        _tilesStale = true;
        RequestPaint();
    }

    private void PaintFrame(SKSurface surface, SKImageInfo info, GRContext? gpu)
    {
        var canvas = surface.Canvas;
        canvas.Clear(PageBackground);
        var document = _document;
        var context = _context;
        if (document is null || context is null)
            return;

        // The context uploads each image to the hardware once, for the one the view draws with.
        context.GpuContext = gpu;

        // Whole pixels, so a tile (or the page painted straight) lands on the pixel grid and text is not smeared.
        var originX = Math.Round(_scrollX);
        var originY = Math.Round(_scrollY);

        // A page whose background is attached to the viewport changes under every scroll step, so it cannot be held in tiles.
        if (gpu is not null && !document.HasViewportBackgrounds && TryPaintTiles(canvas, info, gpu, document, originX, originY))
        {
            PaintWindow(canvas, document, originX, originY, info.Width, info.Height, DocumentPaintLayer.ViewportAttached);
            return;
        }

        PaintWindow(canvas, document, originX, originY, info.Width, info.Height, DocumentPaintLayer.All);
    }

    // Draws the tiles that hold the part of the page in view, painting the ones that are not held. False when a tile cannot be made, in which case
    // nothing of the page has been drawn and the caller paints it straight.
    private bool TryPaintTiles(SKCanvas canvas, SKImageInfo info, GRContext gpu, HtmlDocument document, double originX, double originY)
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
        var visible = TileGrid.Visible(originX, originY, info.Width, info.Height, contentWidth, contentHeight);
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
        // reaches it finds it there instead of painting it while the finger is moving. One tile a frame, and another frame is asked for.
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

        surface.Canvas.Clear(PageBackground);
        PaintWindow(surface.Canvas, document, bounds.X, bounds.Y, bounds.Width, bounds.Height, DocumentPaintLayer.Content);
        return surface.Snapshot();
    }

    // Lets go of every tile, so the next frame paints what it needs again.
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
}

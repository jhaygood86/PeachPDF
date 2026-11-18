namespace PeachReader.Core;

/// <summary>One square of a page painted at a zoom: where it is on the page and how big, in device pixels.</summary>
/// <param name="Column">the tile's column, 0 at the left of the page</param>
/// <param name="Row">the tile's row, 0 at the top of the page</param>
/// <param name="X">the left edge from the left of the page, in pixels</param>
/// <param name="Y">the top edge from the top of the page, in pixels</param>
/// <param name="Width">the width in pixels; less than <see cref="PageTiles.TileSize"/> at the right edge of the page</param>
/// <param name="Height">the height in pixels; less than <see cref="PageTiles.TileSize"/> at the bottom edge of the page</param>
public readonly record struct PageTile(int Column, int Row, int X, int Y, int Width, int Height);

/// <summary>
/// How a page is cut into tiles for a view that paints on the graphics hardware: a texture has a size limit, and a page zoomed in is far larger than
/// one, so each tile is a texture of its own, kept while it is near the view and painted when it comes near.
/// </summary>
public static class PageTiles
{
    /// <summary>The side of a tile, in pixels.</summary>
    public const int TileSize = 1024;

    /// <summary>The tiles of a page that a window onto it touches.</summary>
    /// <param name="pageWidthPx">the width of the whole page at the zoom, in pixels</param>
    /// <param name="pageHeightPx">the height of the whole page at the zoom, in pixels</param>
    /// <param name="left">the left of the window, in pixels from the left of the page</param>
    /// <param name="top">the top of the window, in pixels from the top of the page</param>
    /// <param name="right">the right of the window</param>
    /// <param name="bottom">the bottom of the window</param>
    /// <returns>the tiles, row by row; none when the window does not touch the page</returns>
    public static IReadOnlyList<PageTile> Visible(double pageWidthPx, double pageHeightPx, double left, double top, double right, double bottom)
    {
        int width = (int)Math.Ceiling(pageWidthPx), height = (int)Math.Ceiling(pageHeightPx);
        if (width <= 0 || height <= 0 || right <= 0 || bottom <= 0 || left >= width || top >= height || right <= left || bottom <= top) return [];

        int firstColumn = (int)Math.Floor(Math.Max(0, left) / TileSize), lastColumn = (int)Math.Floor((Math.Min(width, right) - 1e-6) / TileSize);
        int firstRow = (int)Math.Floor(Math.Max(0, top) / TileSize), lastRow = (int)Math.Floor((Math.Min(height, bottom) - 1e-6) / TileSize);
        var tiles = new List<PageTile>();
        for (int row = firstRow; row <= lastRow; row++)
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                int x = column * TileSize, y = row * TileSize;
                tiles.Add(new PageTile(column, row, x, y, Math.Min(TileSize, width - x), Math.Min(TileSize, height - y)));
            }

        return tiles;
    }

    /// <summary>The number of rows of tiles a page has.</summary>
    /// <param name="pageHeightPx">the height of the whole page, in pixels</param>
    /// <returns>the count, at least 1</returns>
    public static int RowCount(double pageHeightPx) => Math.Max(1, (int)Math.Ceiling(Math.Ceiling(pageHeightPx) / TileSize));
}

namespace PeachBrackets.Apple.Logic;

/// <summary>A tile's place in the grid that covers the page: its column and row, counted from the page's top-left.</summary>
/// <param name="Column">the column, from 0</param>
/// <param name="Row">the row, from 0</param>
internal readonly record struct TileKey(int Column, int Row);

/// <summary>A tile's pixels: where it is on the page and how big, in the page's device pixels.</summary>
/// <param name="X">the left edge</param>
/// <param name="Y">the top edge</param>
/// <param name="Width">the width, less than the tile size at the page's right edge</param>
/// <param name="Height">the height, less than the tile size at the page's bottom edge</param>
internal readonly record struct TileBounds(int X, int Y, int Width, int Height);

/// <summary>
/// The grid of square tiles a page's content is cut into, so a scroll draws the tiles it already has instead of painting the page again. The
/// tiles are in the page's device pixels at the current zoom; a different zoom is a different grid.
/// </summary>
internal static class TileGrid
{
    /// <summary>The side of a tile, in device pixels: big enough that a screen is a handful of tiles, small enough that one is cheap to paint and hold (4 MB).</summary>
    public const int TileSize = 1024;

    /// <summary>The tiles that hold any of the part of the page showing, in row order. Nothing is returned when nothing of the page shows.</summary>
    /// <param name="originX">the left edge of what shows, in the page's pixels</param>
    /// <param name="originY">the top edge of what shows</param>
    /// <param name="viewWidth">how wide what shows is</param>
    /// <param name="viewHeight">how tall what shows is</param>
    /// <param name="contentWidth">how wide the page is</param>
    /// <param name="contentHeight">how tall the page is</param>
    /// <param name="tileSize">the side of a tile</param>
    public static IReadOnlyList<TileKey> Visible(double originX, double originY, double viewWidth, double viewHeight,
        double contentWidth, double contentHeight, int tileSize = TileSize)
    {
        if (!(viewWidth > 0) || !(viewHeight > 0) || !(contentWidth > 0) || !(contentHeight > 0))
            return [];

        var lastColumn = (int)Math.Ceiling(contentWidth / tileSize) - 1;
        var lastRow = (int)Math.Ceiling(contentHeight / tileSize) - 1;
        var firstColumn = Math.Max(0, (int)Math.Floor(originX / tileSize));
        var firstRow = Math.Max(0, (int)Math.Floor(originY / tileSize));
        var endColumn = Math.Min(lastColumn, (int)Math.Floor((originX + viewWidth - 1e-6) / tileSize));
        var endRow = Math.Min(lastRow, (int)Math.Floor((originY + viewHeight - 1e-6) / tileSize));

        var tiles = new List<TileKey>();
        for (var row = firstRow; row <= endRow; row++)
        {
            for (var column = firstColumn; column <= endColumn; column++)
                tiles.Add(new TileKey(column, row));
        }

        return tiles;
    }

    /// <summary>Where a tile is on the page and how big, cut to the page's own edges.</summary>
    /// <param name="key">the tile</param>
    /// <param name="contentWidth">how wide the page is</param>
    /// <param name="contentHeight">how tall the page is</param>
    /// <param name="tileSize">the side of a tile</param>
    public static TileBounds Bounds(TileKey key, double contentWidth, double contentHeight, int tileSize = TileSize)
    {
        var x = key.Column * tileSize;
        var y = key.Row * tileSize;
        var width = (int)Math.Min(tileSize, Math.Ceiling(contentWidth) - x);
        var height = (int)Math.Min(tileSize, Math.Ceiling(contentHeight) - y);
        return new TileBounds(x, y, Math.Max(width, 1), Math.Max(height, 1));
    }
}

/// <summary>
/// The tiles kept, most recently used first, up to a limit. The one that has gone unused longest is let go when a new one does not fit; what is
/// let go (and what <see cref="Clear"/> removes) is handed to <c>release</c> so its native memory is freed.
/// </summary>
/// <typeparam name="T">what is kept for a tile</typeparam>
internal sealed class TileCache<T>(int capacity, Action<T> release) where T : class
{
    private readonly Dictionary<TileKey, LinkedListNode<(TileKey Key, T Value)>> _index = [];
    private readonly LinkedList<(TileKey Key, T Value)> _order = new();
    private int _capacity = Math.Max(1, capacity);

    /// <summary>How many tiles are kept.</summary>
    public int Count => _index.Count;

    /// <summary>The most tiles kept.</summary>
    public int Capacity => _capacity;

    /// <summary>Raises the limit (never lowers it), so every tile a frame needs can be held at once.</summary>
    /// <param name="needed">how many tiles must fit</param>
    public void EnsureCapacity(int needed) => _capacity = Math.Max(_capacity, needed);

    /// <summary>The tile, marked as just used, or false when it is not kept.</summary>
    /// <param name="key">the tile</param>
    /// <param name="value">what is kept for it</param>
    public bool TryGet(TileKey key, out T value)
    {
        if (_index.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = null!;
        return false;
    }

    /// <summary>Keeps a tile (replacing, and releasing, what was kept for it), letting go of the least recently used ones that no longer fit.</summary>
    /// <param name="key">the tile</param>
    /// <param name="value">what to keep for it</param>
    public void Add(TileKey key, T value)
    {
        if (_index.Remove(key, out var existing))
        {
            _order.Remove(existing);
            release(existing.Value.Value);
        }

        _index[key] = _order.AddFirst((key, value));
        while (_index.Count > _capacity && _order.Last is { } oldest)
        {
            _order.RemoveLast();
            _index.Remove(oldest.Value.Key);
            release(oldest.Value.Value);
        }
    }

    /// <summary>Lets go of every tile.</summary>
    public void Clear()
    {
        foreach (var entry in _order)
            release(entry.Value);

        _order.Clear();
        _index.Clear();
    }
}

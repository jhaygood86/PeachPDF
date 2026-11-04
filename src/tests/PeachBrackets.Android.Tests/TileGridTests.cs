using PeachBrackets.Android.Logic;

namespace PeachBrackets.Android.Tests;

public class TileGridTests
{
    [Fact]
    public void A_view_inside_one_tile_needs_only_that_tile()
    {
        var tiles = TileGrid.Visible(100, 200, 500, 800, 3000, 9000);

        Assert.Equal([new TileKey(0, 0)], tiles);
    }

    [Fact]
    public void A_view_across_tile_edges_needs_every_tile_it_touches_in_row_order()
    {
        // Showing x 900..1500 and y 1000..2100: columns 0 and 1, rows 0, 1 and 2.
        var tiles = TileGrid.Visible(900, 1000, 600, 1100, 3000, 9000);

        Assert.Equal(
            [new TileKey(0, 0), new TileKey(1, 0), new TileKey(0, 1), new TileKey(1, 1), new TileKey(0, 2), new TileKey(1, 2)],
            tiles);
    }

    [Fact]
    public void A_view_that_ends_exactly_on_a_tile_edge_does_not_need_the_next_tile()
    {
        var tiles = TileGrid.Visible(0, 0, TileGrid.TileSize, TileGrid.TileSize, 5000, 5000);

        Assert.Equal([new TileKey(0, 0)], tiles);
    }

    [Fact]
    public void Tiles_past_the_pages_edges_are_not_asked_for()
    {
        // A page 1500 x 1200 is two columns and two rows; a view that reaches beyond it needs no more than those.
        var tiles = TileGrid.Visible(0, 0, 4000, 4000, 1500, 1200);

        Assert.Equal(4, tiles.Count);
        Assert.Equal(new TileKey(1, 1), tiles[^1]);
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(100, 0, 100, 100)]
    [InlineData(100, 100, 0, 100)]
    [InlineData(100, 100, 100, 0)]
    [InlineData(double.NaN, 100, 100, 100)]
    public void Nothing_is_asked_for_when_nothing_shows(double viewWidth, double viewHeight, double contentWidth, double contentHeight)
    {
        Assert.Empty(TileGrid.Visible(0, 0, viewWidth, viewHeight, contentWidth, contentHeight));
    }

    [Fact]
    public void A_view_scrolled_past_the_page_needs_no_tiles()
    {
        Assert.Empty(TileGrid.Visible(0, 20000, 500, 500, 1000, 1000));
    }

    [Fact]
    public void A_tiles_bounds_are_cut_to_the_page()
    {
        Assert.Equal(new TileBounds(0, 0, 1024, 1024), TileGrid.Bounds(new TileKey(0, 0), 3000, 9000));
        Assert.Equal(new TileBounds(2048, 0, 904, 1024), TileGrid.Bounds(new TileKey(2, 0), 2951.2, 9000));
        Assert.Equal(new TileBounds(0, 8192, 1024, 808), TileGrid.Bounds(new TileKey(0, 8), 3000, 9000));
    }

    [Fact]
    public void The_cache_lets_go_of_the_least_recently_used_tile_when_full()
    {
        var released = new List<string>();
        var cache = new TileCache<string>(2, released.Add);

        cache.Add(new TileKey(0, 0), "a");
        cache.Add(new TileKey(1, 0), "b");
        Assert.True(cache.TryGet(new TileKey(0, 0), out _)); // a is now the most recent
        cache.Add(new TileKey(2, 0), "c");

        Assert.Equal(["b"], released);
        Assert.False(cache.TryGet(new TileKey(1, 0), out _));
        Assert.True(cache.TryGet(new TileKey(0, 0), out var a));
        Assert.Equal("a", a);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Replacing_a_tile_releases_what_it_held_and_clearing_releases_the_rest()
    {
        var released = new List<string>();
        var cache = new TileCache<string>(4, released.Add);

        cache.Add(new TileKey(0, 0), "old");
        cache.Add(new TileKey(0, 0), "new");
        Assert.Equal(["old"], released);
        Assert.Equal(1, cache.Count);

        cache.Add(new TileKey(1, 0), "other");
        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(3, released.Count);
        Assert.Contains("new", released);
        Assert.Contains("other", released);
    }

    [Fact]
    public void The_capacity_grows_to_hold_every_tile_a_frame_needs_and_never_shrinks()
    {
        var cache = new TileCache<string>(2, _ => { });

        cache.EnsureCapacity(6);
        Assert.Equal(6, cache.Capacity);

        cache.EnsureCapacity(3);
        Assert.Equal(6, cache.Capacity);
    }
}

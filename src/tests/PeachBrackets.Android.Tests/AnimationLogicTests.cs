using PeachBrackets.Android.Logic;
using PeachDrawing.Core;

namespace PeachBrackets.Android.Tests;

public class AnimationLogicTests
{
    [Theory]
    [InlineData(1f, true)]
    [InlineData(0.5f, true)]
    [InlineData(10f, true)]
    [InlineData(0f, false)]
    public void Motion_plays_unless_the_system_removes_animations(float scale, bool expected)
    {
        Assert.Equal(expected, AnimationLogic.SystemAllowsMotion(scale));
    }

    [Fact]
    public void The_first_frame_is_stepped_at_the_time_the_display_reports()
    {
        var clock = new AnimationLogic.FrameClock();

        Assert.Equal(TimeSpan.FromSeconds(5), clock.Step(TimeSpan.FromSeconds(5)));
        Assert.Equal(TimeSpan.FromSeconds(5.016), clock.Step(TimeSpan.FromSeconds(5.016)));
    }

    [Fact]
    public void The_time_a_stopped_loop_was_idle_is_left_out_when_it_starts_again()
    {
        var clock = new AnimationLogic.FrameClock();
        clock.Step(TimeSpan.FromSeconds(1));
        var last = clock.Step(TimeSpan.FromSeconds(1.5));

        // Stopped for ten seconds: the display's clock went on, the animations' did not.
        clock.Resume();
        var restarted = clock.Step(TimeSpan.FromSeconds(11.5));

        Assert.Equal(TimeSpan.FromSeconds(1.5), last);
        Assert.Equal(TimeSpan.FromSeconds(1.5), restarted);
        Assert.Equal(TimeSpan.FromSeconds(1.516), clock.Step(TimeSpan.FromSeconds(11.516)));
    }

    [Fact]
    public void Resuming_before_the_first_frame_leaves_nothing_out()
    {
        var clock = new AnimationLogic.FrameClock();
        clock.Resume();

        Assert.Equal(TimeSpan.FromSeconds(3), clock.Step(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void Frame_times_are_taken_from_nanoseconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(16.6), AnimationLogic.FromFrameNanos(16_600_000));
        Assert.Equal(TimeSpan.Zero, AnimationLogic.FromFrameNanos(0));
    }

    [Fact]
    public void A_region_of_a_page_is_in_the_pixels_the_tiles_are_cut_from()
    {
        // 10 pt at 4/3 dp per point, on a page that starts 300 dp down, at a density of 2.5 and zoom 2 (scale 5).
        var (left, top, width, height) = AnimationLogic.RegionToPixels(new Rect(30, 60, 90, 45), 300, 4.0 / 3.0, 5);

        Assert.Equal(200, left, 6);
        Assert.Equal((300 + 80) * 5, top, 6);
        Assert.Equal(600, width, 6);
        Assert.Equal(300, height, 6);
    }

    [Fact]
    public void A_repainted_region_names_only_the_tiles_it_touches()
    {
        // Showing 1800 x 3000 pixels of page: a region across the corner of four tiles.
        var (left, top, width, height) = AnimationLogic.RegionToPixels(new Rect(750, 750, 100, 100), 0, 1, 1.5);
        var tiles = TileGrid.Visible(left, top, width, height, 1800, 3000);

        Assert.Equal([new TileKey(1, 1)], tiles);

        var across = TileGrid.Visible(900, 900, 300, 300, 1800, 3000);
        Assert.Equal([new TileKey(0, 0), new TileKey(1, 0), new TileKey(0, 1), new TileKey(1, 1)], across);
    }

    [Fact]
    public void A_tile_can_be_let_go_without_touching_the_others()
    {
        var released = new List<string>();
        var cache = new TileCache<string>(4, released.Add);
        cache.Add(new TileKey(0, 0), "a");
        cache.Add(new TileKey(1, 0), "b");

        Assert.True(cache.Remove(new TileKey(0, 0)));
        Assert.False(cache.Remove(new TileKey(0, 0)));

        Assert.Equal(["a"], released);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(new TileKey(1, 0), out var kept));
        Assert.Equal("b", kept);

        // What is left is still ordered by use: filling the cache lets go of the oldest, not of a tile that was removed.
        cache.Add(new TileKey(2, 0), "c");
        cache.Add(new TileKey(3, 0), "d");
        cache.Add(new TileKey(4, 0), "e");
        cache.Add(new TileKey(5, 0), "f");
        Assert.Equal(["a", "b"], released);
    }
}

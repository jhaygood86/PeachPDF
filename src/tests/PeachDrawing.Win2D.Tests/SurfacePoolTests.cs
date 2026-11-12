using Microsoft.Graphics.Canvas;
using PeachDrawing.Win2D;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D.Tests;

/// <summary>The render targets geometry clips draw into are made for the clip, kept between paints, and counted only while a canvas has them.</summary>
public class SurfacePoolTests
{
    private const DirectXPixelFormat Format = DirectXPixelFormat.B8G8R8A8UIntNormalized;

    [Fact]
    public void ATargetGivenBackIsTheOneTheNextClipOfThatSizeGets_AndCountsOnlyWhileItIsInUse()
    {
        var counts = new Win2dResourceCounts();
        var device = CanvasDevice.GetSharedDevice(); // shared with every other test: not disposed here
        var pool = new Win2dSurfacePool(counts);

        var first = pool.Take(device, 96, Format, 100, 50, 2000, 2000);
        Assert.Equal(1, counts.ClipSurfaces);
        Assert.True(first.SizeInPixels.Width >= 100 && first.SizeInPixels.Width <= 128, first.SizeInPixels.Width.ToString());

        pool.Return(first);
        Assert.Equal(0, counts.ClipSurfaces);

        // Smaller or equal: the same target. Bigger than it has: another.
        var again = pool.Take(device, 96, Format, 90, 40, 2000, 2000);
        Assert.Same(first, again);
        var bigger = pool.Take(device, 96, Format, 400, 400, 2000, 2000);
        Assert.NotSame(first, bigger);
        Assert.Equal(2, counts.ClipSurfaces);

        pool.Return(again);
        pool.Return(bigger);
        pool.Clear();
        Assert.Equal(0, counts.Total);
    }

    [Fact]
    public void ATargetIsNeverBiggerThanTheOneItIsACopyOf()
    {
        var counts = new Win2dResourceCounts();
        var device = CanvasDevice.GetSharedDevice(); // shared with every other test: not disposed here
        var pool = new Win2dSurfacePool(counts);

        var target = pool.Take(device, 96, Format, 300, 300, 200, 150);

        Assert.True(target.SizeInPixels.Width <= 200 && target.SizeInPixels.Height <= 150, $"{target.SizeInPixels.Width}x{target.SizeInPixels.Height}");
        pool.Return(target);
        pool.Clear();
    }

    [Fact]
    public void WhatTheBudgetCannotHold_IsReleased_OldestFirst()
    {
        var counts = new Win2dResourceCounts();
        var device = CanvasDevice.GetSharedDevice(); // shared with every other test: not disposed here
        var pool = new Win2dSurfacePool(counts);

        // Eight targets of 4 MB each are 32 MB, over the pool's 24.
        var taken = Enumerable.Range(0, 8).Select(_ => pool.Take(device, 96, Format, 1024, 1024, 2000, 2000)).ToList();
        foreach (var target in taken)
            pool.Return(target);

        var reused = Enumerable.Range(0, 8).Select(_ => pool.Take(device, 96, Format, 1024, 1024, 2000, 2000)).ToList();
        Assert.Contains(reused, t => taken.IndexOf(t) >= 0);
        Assert.True(reused.Count(t => taken.IndexOf(t) >= 0) < 8, "everything was kept");
        Assert.DoesNotContain(taken.Take(2), t => reused.Contains(t));

        foreach (var target in reused)
            pool.Return(target);

        pool.Clear();
        Assert.Equal(0, counts.Total);
    }
}

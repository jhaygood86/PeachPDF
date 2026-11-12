using Microsoft.Graphics.Canvas;
using PeachDrawing.Win2D;

namespace PeachDrawing.Win2D.Tests;

/// <summary>The render targets the GPU text blend works in follow the area the queued text covers, and let go of a size one large flush made them.</summary>
public class LcdScratchTests
{
    [Fact]
    public void TheTargetsAreMadeForTheAreaAskedFor_NotLargerThanAStep()
    {
        var counts = new Win2dResourceCounts();
        var device = CanvasDevice.GetSharedDevice(); // shared with every other test: not disposed here
        var scratch = new Win2dLcdScratch(counts);

        scratch.Ensure(device, 96, 300, 40);

        Assert.True(scratch.ByteCount <= 4L * 512 * 256 * 4, scratch.Describe());
        Assert.Equal(4, counts.LcdLayers);

        scratch.Clear();
        Assert.Equal(0, counts.LcdLayers);
    }

    [Fact]
    public void TargetsGrownForOneLargeFlush_AreMadeSmallerAfterARunOfSmallOnes()
    {
        var counts = new Win2dResourceCounts();
        var device = CanvasDevice.GetSharedDevice(); // shared with every other test: not disposed here
        var scratch = new Win2dLcdScratch(counts);

        scratch.Ensure(device, 96, 1500, 2500);
        var large = scratch.ByteCount;

        // A few small flushes keep them (a view alternates between sizes); a long run does not.
        for (var i = 0; i < 10; i++)
            scratch.Ensure(device, 96, 300, 40);
        Assert.Equal(large, scratch.ByteCount);

        for (var i = 0; i < 100; i++)
            scratch.Ensure(device, 96, 300, 40);
        Assert.True(scratch.ByteCount < large / 8, $"still {scratch.ByteCount} of {large}");

        // And a large one grows them again.
        scratch.Ensure(device, 96, 1500, 2500);
        Assert.Equal(large, scratch.ByteCount);

        scratch.Clear();
        Assert.Equal(0, counts.LcdLayers);
    }
}

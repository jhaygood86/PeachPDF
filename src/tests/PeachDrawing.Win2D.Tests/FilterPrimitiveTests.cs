using PeachDrawing.Core;
using PeachDrawing.Win2D;

namespace PeachDrawing.Win2D.Tests;

/// <summary>The filter primitives a layer can apply that Win2D has an effect for (component transfer, convolution), held to what the raster canvas draws.</summary>
public class FilterPrimitiveTests
{
    [Fact]
    public void A_component_transfer_maps_each_channel_through_its_table()
    {
        var invertRed = Enumerable.Range(0, 256).Select(i => (byte)(255 - i)).ToArray();
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Effects: new LayerEffect[] { new ComponentTransferEffect(invertRed, null, null, null) }))!)
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 5, 5, 20, 20);

        var inside = scene.Px(15, 15);
        Assert.Equal(0, inside.R);
        Assert.Equal(255, inside.A);
        Assert.True(scene.IsClear(1, 1));
        Assert.Equal(0, scene.Context.Counts.Total);
    }

    [Fact]
    public void A_convolution_turns_its_kernel_round_and_puts_its_target_over_the_pixel()
    {
        // A kernel that reads the pixel one to the right, so the shape's left edge moves one pixel left.
        using var scene = Scene.Create();
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Effects: new LayerEffect[] { new ConvolveMatrixEffect(3, 1, new double[] { 1, 0, 0 }, 1, 0, 1, 0, FilterEdgeMode.None, false) }))!)
            layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 10, 10);

        Assert.Equal(255, scene.Px(9, 15).A);
        Assert.True(scene.IsClear(19, 15));
        Assert.Equal(255, scene.Px(18, 15).A);
        Assert.Equal(0, scene.Context.Counts.Total);
    }
}

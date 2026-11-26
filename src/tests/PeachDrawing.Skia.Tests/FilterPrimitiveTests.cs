using PeachDrawing.Core;
using PeachDrawing.Skia;

namespace PeachDrawing.Skia.Tests;

/// <summary>The filter primitives a layer can apply (component transfer, convolution, turbulence), drawn by Skia itself.</summary>
public class FilterPrimitiveTests
{
    [Fact]
    public void A_component_transfer_maps_each_channel_through_its_table()
    {
        var invertRed = Enumerable.Range(0, 256).Select(i => (byte)(255 - i)).ToArray();
        using var scene = Scene.Create(60, 60);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60), Effects: [new ComponentTransferEffect(invertRed, null, null, null)])))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 10, 10, 30, 30);

        var inside = scene.Px(20, 20);
        Assert.Equal(0, inside.R);
        Assert.Equal(255, inside.A);
        Assert.True(scene.IsClear(5, 5));
    }

    [Fact]
    public void A_convolution_turns_its_kernel_round_and_puts_its_target_over_the_pixel()
    {
        // A kernel that reads the pixel one to the right (as SVG convolves: the kernel is turned half way round), so the shape's left edge moves one pixel left.
        using var scene = Scene.Create(60, 60);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60),
            Effects: [new ConvolveMatrixEffect(3, 1, [1, 0, 0], 1, 0, 1, 0, FilterEdgeMode.None, false)])))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 20, 20, 20, 20);

        Assert.Equal(255, scene.Px(19, 30).A);
        Assert.True(scene.IsClear(39, 30));
        Assert.Equal(255, scene.Px(38, 30).A);
    }

    [Fact]
    public void Turbulence_replaces_the_layer_with_noise()
    {
        using var scene = Scene.Create(60, 60);
        using (var layer = scene.Canvas.BeginLayer(new LayerOptions(Bounds: new Rect(0, 0, 60, 60), Effects: [new TurbulenceEffect(0.1, 0.1, 2, 7, false, false)])))
            layer!.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 0, 0, 5, 5);

        var alphas = new HashSet<byte>();
        for (var x = 20; x < 60; x += 5)
            alphas.Add((byte)scene.Px(x, 40).A);

        Assert.True(alphas.Count > 3, "the noise varies across the layer, outside the shape that was drawn");
    }
}

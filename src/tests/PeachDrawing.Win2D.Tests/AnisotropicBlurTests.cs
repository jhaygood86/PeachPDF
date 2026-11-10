using PeachDrawing.Core;
using PeachDrawing.Win2D;

namespace PeachDrawing.Win2D.Tests;

/// <summary>
/// A blur or shadow with different horizontal and vertical standard deviations blurs each direction by its own: Win2D's effects take one
/// blur amount, so an unequal pair has to be done as a horizontal blur and a vertical one, not blurred by their mean in both.
/// </summary>
public class AnisotropicBlurTests
{
    // A 30 x 30 square at (35, 35) on a 100 x 100 page: its edges are 35 and 65 along both axes.
    private static void Square(Scene scene, LayerEffect effect)
    {
        using var layer = scene.Canvas.BeginLayer(new LayerOptions(Effects: new[] { effect }))!;
        layer.Canvas.DrawRectangle(scene.Solid(255, 0, 0), 35, 35, 30, 30);
    }

    [Fact]
    public void A_wide_horizontal_and_narrow_vertical_blur_spreads_sideways_but_hardly_up()
    {
        using var scene = Scene.Create(100, 100);
        Square(scene, new BlurEffect(8, 0.5));

        // Six pixels outside the left edge: a sigma of 8 leaves about 23% there (alpha near 58); the mean sigma of 4.25 would leave 8% (about 20).
        Assert.True(scene.Px(29, 50).A > 40, $"left of the square: {scene.Px(29, 50)}");

        // Six pixels above the top edge: a sigma of 0.5 leaves nothing; the mean sigma would leave the same 8% it leaves sideways.
        Assert.True(scene.Px(50, 29).A < 8, $"above the square: {scene.Px(50, 29)}");
    }

    [Fact]
    public void A_narrow_horizontal_and_wide_vertical_blur_spreads_up_but_hardly_sideways()
    {
        using var scene = Scene.Create(100, 100);
        Square(scene, new BlurEffect(0.5, 8));

        Assert.True(scene.Px(50, 29).A > 40, $"above the square: {scene.Px(50, 29)}");
        Assert.True(scene.Px(29, 50).A < 8, $"left of the square: {scene.Px(29, 50)}");
    }

    [Fact]
    public void A_blur_with_no_vertical_deviation_blurs_only_sideways()
    {
        using var scene = Scene.Create(100, 100);
        Square(scene, new BlurEffect(8, 0));

        Assert.True(scene.Px(29, 50).A > 40, $"left of the square: {scene.Px(29, 50)}");
        Assert.True(scene.Px(50, 29).A < 8, $"above the square: {scene.Px(50, 29)}");
        Assert.True(scene.Px(50, 50).A > 220, "the middle stays nearly opaque (a sigma of 8 leaves about 94% there)");
    }

    [Fact]
    public void Equal_deviations_blur_the_same_in_both_directions()
    {
        using var scene = Scene.Create(100, 100);
        Square(scene, new BlurEffect(5));

        var left = scene.Px(29, 50).A;
        var above = scene.Px(50, 29).A;
        Assert.InRange(left, above - 3, above + 3);
        Assert.True(left > 10, $"some spread: {left}");
    }

    [Fact]
    public void A_drop_shadow_with_unequal_deviations_blurs_each_direction_by_its_own()
    {
        using var scene = Scene.Create(100, 100);
        Square(scene, new DropShadowEffect(0, 0, 8, 0.5, PaintColor.FromArgb(255, 0, 0, 255)));

        // The shadow is blue under the red square; look for it where the square is not.
        Assert.True(scene.Px(29, 50).B > 40, $"left of the square: {scene.Px(29, 50)}");
        Assert.True(scene.Px(50, 29).B < 8, $"above the square: {scene.Px(50, 29)}");
        Assert.True(scene.IsRed(50, 50));
    }
}

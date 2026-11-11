using PeachDrawing.Core;

namespace PeachDrawing.Core.Tests;

/// <summary>How far a layer's effects spread ink past its content: three deviations of each blur and shadow, and a shadow's offset.</summary>
public class LayerEffectMarginsTests
{
    [Fact]
    public void ABlurSpreadsThreeDeviationsEachWay()
    {
        var (x, y) = LayerEffectMargins.InkMargin(new LayerEffect[] { new BlurEffect(4, 2) });

        Assert.Equal((12.0, 6.0), (x, y));
    }

    [Fact]
    public void AShadowAddsItsOffsetToItsBlur()
    {
        var (x, y) = LayerEffectMargins.InkMargin(new LayerEffect[] { new DropShadowEffect(-5, 3, 2, 1, PaintColor.Black) });

        Assert.Equal((5 + 6.0, 3 + 3.0), (x, y));
    }

    [Fact]
    public void EffectsAddUp_AndAColorMatrixAddsNothing()
    {
        var (x, y) = LayerEffectMargins.InkMargin(new LayerEffect[]
        {
            new BlurEffect(1), new ColorMatrixEffect(ColorMatrix.Identity), new BlurEffect(2),
        });

        Assert.Equal((9.0, 9.0), (x, y));
    }

    [Fact]
    public void ANegativeOrNotANumberDeviationOrOffsetAddsNothing()
    {
        var (x, y) = LayerEffectMargins.InkMargin(new LayerEffect[]
        {
            new BlurEffect(-3, double.NaN), new DropShadowEffect(double.NaN, double.NaN, double.NaN, -1, PaintColor.Black),
        });

        Assert.Equal((0.0, 0.0), (x, y));
    }
}

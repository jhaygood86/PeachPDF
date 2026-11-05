using PeachBrackets.WinUI;

namespace PeachBrackets.WinUI.Tests;

public class ImageScaleTests
{
    [Theory]
    [InlineData(1.5, 1.5)]
    [InlineData(2, 2)]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(double.NaN, 1)]
    public void DevicePixelRatio_IsTheRasterizationScaleOrOne(double scale, double expected) =>
        Assert.Equal(expected, ImageScale.DevicePixelRatio(scale));

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 1.04, false)]
    [InlineData(1, 1.06, true)]
    [InlineData(1, 4, true)]
    [InlineData(4, 0.5, false)]
    [InlineData(2, 2.05, false)]
    [InlineData(1, double.PositiveInfinity, false)]
    public void NeedsUpgrade_OnlyWhenTheZoomGrewPastTheStep(double chosenFor, double zoom, bool expected) =>
        Assert.Equal(expected, ImageScale.NeedsUpgrade(chosenFor, zoom));
}

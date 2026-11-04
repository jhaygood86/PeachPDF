using PeachBrackets.Android.Logic;

namespace PeachBrackets.Android.Tests;

public class ImageScaleTests
{
    [Theory]
    [InlineData(2.625, 2.625)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void DevicePixelRatio_IsThePlatformScaleOrOne(double scale, double expected) =>
        Assert.Equal(expected, ImageScale.DevicePixelRatio(scale));

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 1.04, false)]
    [InlineData(1, 1.06, true)]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, false)]
    [InlineData(2, 2.05, false)]
    [InlineData(2, 2.2, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, double.NaN, false)]
    public void NeedsUpgrade_OnlyWhenTheZoomGrewPastTheStep(double chosenFor, double zoom, bool expected) =>
        Assert.Equal(expected, ImageScale.NeedsUpgrade(chosenFor, zoom));
}

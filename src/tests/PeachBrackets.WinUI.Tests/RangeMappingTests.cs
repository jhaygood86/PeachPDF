using PeachBrackets.Accessibility;
using PeachBrackets.WinUI;

namespace PeachBrackets.WinUI.Tests;

public class RangeMappingTests
{
    [Theory]
    [InlineData(AccessibilityRole.ProgressBar, true)]
    [InlineData(AccessibilityRole.Meter, true)]
    [InlineData(AccessibilityRole.Slider, true)]
    [InlineData(AccessibilityRole.ScrollBar, true)]
    [InlineData(AccessibilityRole.SpinButton, true)]
    [InlineData(AccessibilityRole.TextBox, false)]
    [InlineData(AccessibilityRole.Button, false)]
    [InlineData(AccessibilityRole.Paragraph, false)]
    public void OnlyTheRangeRolesAreReadAsRanges(AccessibilityRole role, bool expected) =>
        Assert.Equal(expected, HtmlViewLogic.IsRangeRole(role));

    [Theory]
    [InlineData(0, 100, 1, 10)]
    [InlineData(10, 20, 0.1, 1)]
    [InlineData(5, 5, 0, 0)]
    [InlineData(9, 3, 0, 0)]
    public void ASmallStepIsAHundredthOfTheExtentAndALargeOneATenth(double minimum, double maximum, double small, double large)
    {
        var (s, l) = HtmlViewLogic.RangeSteps(minimum, maximum);

        Assert.Equal(small, s, 6);
        Assert.Equal(large, l, 6);
    }
}

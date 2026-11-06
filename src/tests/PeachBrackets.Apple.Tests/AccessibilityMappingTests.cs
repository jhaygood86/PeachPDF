using PeachBrackets.Accessibility;
using PeachBrackets.Apple.Logic;

namespace PeachBrackets.Apple.Tests;

public class AccessibilityMappingTests
{
    [Theory]
    [InlineData(AccessibilityRole.Link, "Link")]
    [InlineData(AccessibilityRole.Button, "Button")]
    [InlineData(AccessibilityRole.CheckBox, "Button")]
    [InlineData(AccessibilityRole.Heading, "Header")]
    [InlineData(AccessibilityRole.Image, "Image")]
    [InlineData(AccessibilityRole.Slider, "Adjustable")]
    [InlineData(AccessibilityRole.SearchBox, "SearchField")]
    [InlineData(AccessibilityRole.Paragraph, "StaticText")]
    public void RolesGetTheirTraits(AccessibilityRole role, string expected) =>
        Assert.Equal(expected, AppleAccessibility.Traits(role, false, false).ToString());

    [Fact]
    public void StateAddsTraits() =>
        Assert.Equal(AppleTraits.Button | AppleTraits.Selected | AppleTraits.NotEnabled, AppleAccessibility.Traits(AccessibilityRole.Tab, true, true));

    [Theory]
    [InlineData(AccessibilityRole.Link, "AXLink")]
    [InlineData(AccessibilityRole.Heading, "AXHeading")]
    [InlineData(AccessibilityRole.TextBox, "AXTextField")]
    [InlineData(AccessibilityRole.Switch, "AXCheckBox")]
    [InlineData(AccessibilityRole.Radio, "AXRadioButton")]
    [InlineData(AccessibilityRole.Paragraph, "AXStaticText")]
    public void RolesGetTheirMacRole(AccessibilityRole role, string expected) => Assert.Equal(expected, AppleAccessibility.MacRole(role));

    [Fact]
    public void OnlyLeavesAndControlsAreElements()
    {
        Assert.True(AppleAccessibility.IsElement(AccessibilityRole.Paragraph, hasChildren: false, hasText: true));
        Assert.False(AppleAccessibility.IsElement(AccessibilityRole.Paragraph, hasChildren: true, hasText: true));
        Assert.True(AppleAccessibility.IsElement(AccessibilityRole.Link, hasChildren: true, hasText: true));
        Assert.False(AppleAccessibility.IsElement(AccessibilityRole.Image, hasChildren: false, hasText: false));
    }

    [Fact]
    public void LinksAreActivatableAndSplitIntoTargets()
    {
        Assert.True(AppleAccessibility.IsActivatable(AccessibilityRole.Link));
        Assert.False(AppleAccessibility.IsActivatable(AccessibilityRole.Heading));
        Assert.Equal(("https://a.example/", null), AppleAccessibility.LinkTarget("https://a.example/"));
        Assert.Equal((null, "top"), AppleAccessibility.LinkTarget("#top"));
    }
}

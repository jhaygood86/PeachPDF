using PeachBrackets.Accessibility;

namespace PeachBrackets.Tests;

public class AccessibilityRangeTests
{
    private static async Task<AccessibilityNode> NodeAsync(string body, AccessibilityRole role)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>@page { margin: 0 }</style>"), new HtmlDocumentOptions { Media = "screen" });
        return document.GetAccessibilityTree().Nodes.First(n => n.Role == role);
    }

    [Fact]
    public async Task AProgressBarIsAFractionOfOneUnlessItSaysMore()
    {
        var node = await NodeAsync("<progress value=0.25></progress>", AccessibilityRole.ProgressBar);
        Assert.Equal((0.25, 0.0, 1.0), (node.Value, node.ValueMin, node.ValueMax));

        node = await NodeAsync("<progress value=30 max=60></progress>", AccessibilityRole.ProgressBar);
        Assert.Equal((30.0, 0.0, 60.0), (node.Value, node.ValueMin, node.ValueMax));
    }

    [Fact]
    public async Task ABarWithNoValueIsIndeterminate()
    {
        var node = await NodeAsync("<progress></progress>", AccessibilityRole.ProgressBar);

        Assert.Null(node.Value);
    }

    [Fact]
    public async Task AMeterHasItsOwnRange()
    {
        var node = await NodeAsync("<meter min=10 max=20 value=15></meter>", AccessibilityRole.Meter);

        Assert.Equal((15.0, 10.0, 20.0), (node.Value, node.ValueMin, node.ValueMax));
    }

    [Fact]
    public async Task ARangeInputStartsAtTheMiddleAndIsKeptInItsRange()
    {
        var node = await NodeAsync("<input type=range min=0 max=10>", AccessibilityRole.Slider);
        Assert.Equal(5.0, node.Value);

        node = await NodeAsync("<input type=range min=0 max=10 value=99>", AccessibilityRole.Slider);
        Assert.Equal(10.0, node.Value);
    }

    [Fact]
    public async Task AnAriaSliderReadsItsAriaValuesAndText()
    {
        var node = await NodeAsync("<div role=slider aria-valuenow=3 aria-valuemin=1 aria-valuemax=5 aria-valuetext='medium'></div>", AccessibilityRole.Slider);

        Assert.Equal((3.0, 1.0, 5.0, "medium"), (node.Value, node.ValueMin, node.ValueMax, node.ValueText));
    }

    [Fact]
    public async Task AMaxBelowTheMinIsRaisedToIt()
    {
        var node = await NodeAsync("<div role=slider aria-valuenow=3 aria-valuemin=5 aria-valuemax=1></div>", AccessibilityRole.Slider);

        Assert.Equal((5.0, 5.0, 5.0), (node.Value, node.ValueMin, node.ValueMax));
    }

    [Fact]
    public async Task OtherNodesHaveNoValue()
    {
        var node = await NodeAsync("<p>text</p>", AccessibilityRole.Paragraph);

        Assert.Null(node.Value);
    }
}

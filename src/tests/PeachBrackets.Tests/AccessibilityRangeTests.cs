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

    [Fact]
    public async Task ANodeReportsTheFontSizeWeightSlantAndColoursOfItsText()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p id=a style='font-size:20pt;font-weight:bold;font-style:italic;color:rgb(255,0,0);background:rgb(0,0,255)'>styled</p><p id=b>plain</p>",
                "<style>@page { margin: 0 }</style>"), new HtmlDocumentOptions { Media = "screen" });
        var tree = document.GetAccessibilityTree();

        var styled = tree.Nodes.First(n => n.ElementId == "a");
        var plain = tree.Nodes.First(n => n.ElementId == "b");

        Assert.Equal(20, styled.FontSize, 1);
        Assert.Equal(700, styled.FontWeight);
        Assert.True(styled.IsItalic);
        Assert.Equal(unchecked((int)0xFFFF0000), styled.ForegroundColor);
        Assert.Equal(unchecked((int)0xFF0000FF), styled.BackgroundColor);
        Assert.False(string.IsNullOrEmpty(styled.FontFamily));
        Assert.Equal(400, plain.FontWeight);
        Assert.False(plain.IsItalic);
        Assert.Null(plain.BackgroundColor);
        Assert.NotNull(plain.ForegroundColor);
        Assert.All(styled.DescendantsAndSelf(), n => Assert.Equal(700, n.FontWeight));
    }

    [Fact]
    public async Task ANodeKnowsWhetherItsTextIsUnderlinedOrStruckThroughByItselfOrAnElementAround()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p id=u style='text-decoration:underline'>under <span id=s>inside</span></p><p id=l style='text-decoration:underline line-through'>both</p><p id=n>none</p>",
                "<style>@page { margin: 0 }</style>"), new HtmlDocumentOptions { Media = "screen" });
        var nodes = document.GetAccessibilityTree().Nodes;

        var under = nodes.First(n => n.ElementId == "u");
        var both = nodes.First(n => n.ElementId == "l");
        var none = nodes.First(n => n.ElementId == "n");

        Assert.Equal((true, false), (under.Underline, under.Strikethrough));
        Assert.All(under.DescendantsAndSelf(), n => Assert.True(n.Underline));
        Assert.Equal((true, true), (both.Underline, both.Strikethrough));
        Assert.Equal((false, false), (none.Underline, none.Strikethrough));
    }
}

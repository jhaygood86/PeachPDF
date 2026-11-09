using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// The HTML Standard's rendering section maps the obsolete <c>clear</c> attribute of <c>br</c> to the CSS <c>clear</c> property: what follows the
/// break goes below the floats on that side. Chrome: the block after the break in the first sample is at the float's bottom, and the line after the
/// break in the second is.
/// </summary>
public class BrClearTests
{
    private static async Task<CssBox> LayoutAsync(string content)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(content), pageHeight: 600);

        return root;
    }

    private static CssBox Box(CssBox root, string id) => LayoutHarness.FindById(root, id)!;

    private const string Float = "<div id='f' style='float:left;width:75pt;height:52.5pt'></div>";

    [Theory]
    [InlineData("clear=\"all\"", 52.5)]
    [InlineData("clear=\"left\"", 52.5)]
    [InlineData("clear=\"both\"", 52.5)]
    [InlineData("clear=\"right\"", 30)]
    [InlineData("clear=\"none\"", 30)]
    [InlineData("clear=\"nonsense\"", 30)]
    [InlineData("", 30)]
    public async Task BrAfterABlock_PutsTheNextBlockBelowTheFloat(string attribute, double expectedBelowTop)
    {
        var root = await LayoutAsync(
            $"<div id='w' style='width:300pt'>{Float}<div style='height:15pt'></div><br {attribute}><div id='n' style='height:7.5pt'></div></div>");

        var w = Box(root, "w");
        Assert.Equal(w.Location.Y + expectedBelowTop, Box(root, "n").Location.Y, 1);
    }

    [Fact]
    public async Task BrStyleClear_IsTheSameAsTheAttribute()
    {
        var root = await LayoutAsync(
            $"<div id='w' style='width:300pt'>{Float}<div style='height:15pt'></div><br style='clear:both'><div id='n' style='height:7.5pt'></div></div>");

        Assert.Equal(Box(root, "w").Location.Y + 52.5, Box(root, "n").Location.Y, 1);
    }

    [Fact]
    public async Task AnAuthorStyleOverridesTheAttribute()
    {
        var root = await LayoutAsync(
            $"<div id='w' style='width:300pt'>{Float}<div style='height:15pt'></div><br clear='all' style='clear:none'><div id='n' style='height:7.5pt'></div></div>");

        Assert.Equal(Box(root, "w").Location.Y + 30, Box(root, "n").Location.Y, 1);
    }

    [Fact]
    public async Task BrInsideAnInline_StartsTheNextLineBelowTheFloat()
    {
        var root = await LayoutAsync(
            $"<div id='w' style='width:300pt'>{Float}<span>x<br clear='left'>y</span><div id='n' style='height:7.5pt'></div></div>");

        var w = Box(root, "w");
        var n = Box(root, "n");
        Assert.True(n.Location.Y >= w.Location.Y + 52.5 + 5, $"the line after the break must start at the float's bottom, n at {n.Location.Y - w.Location.Y}");
    }

    [Fact]
    public async Task BrClear_SeesAFloatBeforeAnAncestor()
    {
        var root = await LayoutAsync(
            $"<div id='w' style='width:300pt'>{Float}<div><div style='height:15pt'></div><br clear='all'><div id='n' style='height:7.5pt'></div></div></div>");

        Assert.Equal(Box(root, "w").Location.Y + 52.5, Box(root, "n").Location.Y, 1);
    }

    [Fact]
    public async Task BrClear_DoesNotSeeAFloatInsideAFormattingContextRoot()
    {
        var root = await LayoutAsync(
            $"<div id='w' style='width:300pt'><div style='overflow:hidden;height:7.5pt'>{Float}</div><br clear='all'><div id='n' style='height:7.5pt'></div></div>");

        Assert.True(Box(root, "n").Location.Y - Box(root, "w").Location.Y < 40);
    }

    // Chrome: a break at the end of the content still has the block after it below the float (the line after it is cleared).
    [Theory]
    [InlineData("x<br clear='left'>")]
    [InlineData("<span>x<br clear='left'></span>")]
    [InlineData("<b>x</b><br clear='all'>")]
    public async Task BrClearAtTheEndOfTheContent_StillClearsTheBlockAfterIt(string inner)
    {
        var root = await LayoutAsync($"<div id='w' style='width:300pt'>{Float}{inner}<div id='n' style='height:7.5pt'></div></div>");

        Assert.Equal(Box(root, "w").Location.Y + 52.5, Box(root, "n").Location.Y, 1);
    }

    [Theory]
    [InlineData("x<br clear='left'>y")]
    [InlineData("<b>x</b><br clear='all'>y")]
    public async Task BrClearBetweenText_PutsTheTextAfterItBelowTheFloat(string inner)
    {
        var root = await LayoutAsync($"<div id='w' style='width:300pt'>{Float}{inner}<div id='n' style='height:7.5pt'></div></div>");

        var below = Box(root, "n").Location.Y - Box(root, "w").Location.Y;
        Assert.InRange(below, 52.5 + 5, 52.5 + 25);
    }
}

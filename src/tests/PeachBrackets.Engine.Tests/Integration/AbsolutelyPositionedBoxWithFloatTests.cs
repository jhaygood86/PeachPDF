using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 section 9.7: <c>float</c> computes to <c>none</c> on an absolutely or fixed positioned box. The box was still found by the float
/// scans, so the lines beside it were narrowed for a box that takes no room in them.
/// </summary>
public class AbsolutelyPositionedBoxWithFloatTests
{
    private static IEnumerable<CssRect> WordsOf(CssBox box) => box.Words.Concat(box.Boxes.SelectMany(WordsOf));

    private static async Task<(CssBox C, CssBox A, CssBox T)> LayoutAsync(string abs)
    {
        var html = LayoutHarness.Wrap(
            $"<div id='c' style='position:relative;width:300pt;height:150pt'>{abs}<div id='t'>text</div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        return (LayoutHarness.FindById(root, "c")!, LayoutHarness.FindById(root, "a")!, LayoutHarness.FindById(root, "t")!);
    }

    // The words stay at the container's left edge, where the box that would have narrowed them is (or would be, at its static position).
    [Theory]
    [InlineData("position:absolute;float:left;width:75pt;height:52.5pt")]
    [InlineData("position:absolute;float:left;left:0;top:0;width:75pt;height:52.5pt")]
    [InlineData("position:fixed;float:left;width:75pt;height:52.5pt")]
    [InlineData("position:absolute;float:right;right:0;top:0;width:75pt;height:52.5pt")]
    [InlineData("position:absolute;float:inline-start;width:75pt;height:52.5pt")]
    public async Task TextBesideAnAbsolutelyPositionedBoxWithFloat_IsNotNarrowed(string style)
    {
        var (c, a, t) = await LayoutAsync($"<div id='a' style='{style}'></div>");

        Assert.False(a.IsFloated, "float computes to none on a positioned box");
        Assert.Equal(c.Location.X, WordsOf(t).Min(w => w.Left), 1);
        Assert.True(WordsOf(t).Max(w => w.Right) < c.Location.X + 75, "the words run from the container's edge");
    }

    // A page-float value is computed to none as well: the box was collected as a page float and reserved its height at the top of the page,
    // which moved the container and the text down although the box takes no room.
    [Theory]
    [InlineData("float:top")]
    [InlineData("float:bottom")]
    [InlineData("float:top-bottom")]
    [InlineData("float:snap")]
    public async Task AbsolutelyPositionedBoxWithAPageFloat_TakesNoRoomAtTheTopOfThePage(string pageFloat)
    {
        var (_, _, without) = await LayoutAsync("");
        var (_, a, withBox) = await LayoutAsync($"<div id='a' style='position:absolute;{pageFloat};top:75pt;left:37.5pt;width:75pt;height:20pt'></div>");

        Assert.False(a.IsPageFloated);
        Assert.Equal(without.Location.Y, withBox.Location.Y, 1);
    }

    // A box that is only positioned relatively keeps its float.
    [Fact]
    public async Task RelativelyPositionedFloat_StaysAFloat()
    {
        var (c, a, t) = await LayoutAsync("<div id='a' style='position:relative;float:left;width:75pt;height:52.5pt'></div>");

        Assert.True(a.IsFloated);
        Assert.Equal(c.Location.X + 75, WordsOf(t).Min(w => w.Left), 1);
    }

    // The box itself is where its insets put it, as for any absolutely positioned box.
    [Fact]
    public async Task AbsolutelyPositionedBoxWithFloat_IsPlacedByItsInsets()
    {
        var (c, a, _) = await LayoutAsync("<div id='a' style='position:absolute;float:left;top:7.5pt;left:37.5pt;width:75pt;height:52.5pt'></div>");

        Assert.Equal(c.Location.X + 37.5, a.Location.X, 1);
        Assert.Equal(c.Location.Y + 7.5, a.Location.Y, 1);
    }
}

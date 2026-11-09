using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.5.1 rules 7 and 8: a float is placed as high as it can be. One that does not fit beside the floats where it is goes to the first
/// bottom edge that makes room for it, not below the lowest float it met on the way. Every position is Chrome's, in pt (a 400px container is 300pt).
/// </summary>
public class FloatDropsToTheFirstBottomThatMakesRoomTests
{
    private static async Task<(CssBox C, CssBox Root)> LayoutAsync(string floats)
    {
        var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap($"<div id='c' style='width:300pt'>{floats}</div>"), pageHeight: 600);

        return (LayoutHarness.FindById(root, "c")!, root);
    }

    private static (double X, double Y) At(CssBox root, CssBox c, string id)
    {
        var box = LayoutHarness.FindById(root, id)!;

        return (box.Location.X - c.Location.X, box.Location.Y - c.Location.Y);
    }

    [Fact]
    public async Task LeftFloat_GoesBesideTheClearedFloatAsSoonAsTheRightFloatEnds()
    {
        var (c, root) = await LayoutAsync(
            "<div style='float:left;width:75pt;height:22.5pt'></div>" +
            "<div style='float:right;width:187.5pt;height:37.5pt'></div>" +
            "<div style='float:left;clear:left;width:75pt;height:22.5pt'></div>" +
            "<div id='f' style='float:left;width:75pt;height:22.5pt'></div>");

        var (x, y) = At(root, c, "f");
        Assert.Equal(75, x, 1);
        Assert.Equal(37.5, y, 1);
    }

    [Fact]
    public async Task RightFloat_GoesBesideTheClearedFloatAsSoonAsTheLeftFloatEnds()
    {
        var (c, root) = await LayoutAsync(
            "<div style='float:right;width:75pt;height:22.5pt'></div>" +
            "<div style='float:left;width:187.5pt;height:37.5pt'></div>" +
            "<div style='float:right;clear:right;width:75pt;height:22.5pt'></div>" +
            "<div id='f' style='float:right;width:75pt;height:22.5pt'></div>");

        var (x, y) = At(root, c, "f");
        Assert.Equal(300 - 75 - 75, x, 1);
        Assert.Equal(37.5, y, 1);
    }

    // The control: nothing frees room before the lowest blocker ends, so the float still goes below it.
    [Fact]
    public async Task LeftFloat_ThatFitsBesideNothingBeforeTheLastBlockerEnds_GoesBelowIt()
    {
        var (c, root) = await LayoutAsync(
            "<div style='float:left;width:75pt;height:15pt'></div>" +
            "<div style='float:right;width:150pt;height:30pt'></div>" +
            "<div id='f' style='float:left;width:187.5pt;height:7.5pt'></div>");

        var (x, y) = At(root, c, "f");
        Assert.Equal(0, x, 1);
        Assert.Equal(30, y, 1);
    }

    // Still the highest place: beside the right float once the left one ends, when there is room between them.
    [Fact]
    public async Task LeftFloat_GoesToTheFirstBlockerBottomWhenItFitsThere()
    {
        var (c, root) = await LayoutAsync(
            "<div style='float:left;width:75pt;height:15pt'></div>" +
            "<div style='float:right;width:150pt;height:30pt'></div>" +
            "<div id='f' style='float:left;width:112.5pt;height:7.5pt'></div>");

        var (x, y) = At(root, c, "f");
        Assert.Equal(0, x, 1);
        Assert.Equal(15, y, 1);
    }

    [Fact]
    public async Task RightFloat_GoesToTheFirstBlockerBottomWhenItFitsThere()
    {
        var (c, root) = await LayoutAsync(
            "<div style='float:right;width:75pt;height:15pt'></div>" +
            "<div style='float:left;width:150pt;height:30pt'></div>" +
            "<div id='f' style='float:right;width:112.5pt;height:7.5pt'></div>");

        var (x, y) = At(root, c, "f");
        Assert.Equal(300 - 112.5, x, 1);
        Assert.Equal(15, y, 1);
    }
}

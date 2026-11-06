using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS 2.1 §9.5.1 rule 5: the outer top of a float may not be higher than the outer top of any float before it. A float that would fit in
/// a gap higher up, beside a float an earlier float pushed down, goes to the pushed-down float's top instead.
/// </summary>
public class FloatTopNotAboveAnEarlierFloatTests
{
    [Fact]
    public async Task ThirdLeftFloat_SitsBesideTheSecondNotBesideTheFirst()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'>" +
            "<div id='a' style='float:left;width:225pt;height:40pt'></div>" +
            "<div id='b' style='float:left;width:150pt;height:40pt'></div>" +
            "<div id='d' style='float:left;width:38pt;height:40pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var a = LayoutHarness.FindById(root, "a")!;
        var b = LayoutHarness.FindById(root, "b")!;
        var d = LayoutHarness.FindById(root, "d")!;

        Assert.Equal(a.ActualBottom, b.Location.Y, 1);
        Assert.Equal(b.Location.Y, d.Location.Y, 1);
        Assert.Equal(b.ActualRight, d.Location.X, 1);
    }

    [Fact]
    public async Task LeftFloatAfterARightAndAWiderLeftFloat_DoesNotGoBackUpBesideTheFirst()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'>" +
            "<div id='r' style='float:right;width:225pt;height:40pt'></div>" +
            "<div id='b' style='float:left;width:150pt;height:40pt'></div>" +
            "<div id='d' style='float:left;width:75pt;height:40pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var r = LayoutHarness.FindById(root, "r")!;
        var b = LayoutHarness.FindById(root, "b")!;
        var d = LayoutHarness.FindById(root, "d")!;

        Assert.Equal(r.ActualBottom, b.Location.Y, 1);
        Assert.Equal(b.Location.Y, d.Location.Y, 1);
        Assert.Equal(b.ActualRight, d.Location.X, 1);
    }

    [Fact]
    public async Task ThirdRightFloat_SitsBesideTheSecondNotBesideTheFirst()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'>" +
            "<div id='a' style='float:right;width:225pt;height:40pt'></div>" +
            "<div id='b' style='float:right;width:150pt;height:40pt'></div>" +
            "<div id='d' style='float:right;width:38pt;height:40pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var b = LayoutHarness.FindById(root, "b")!;
        var d = LayoutHarness.FindById(root, "d")!;

        Assert.Equal(b.Location.Y, d.Location.Y, 1);
        Assert.Equal(b.Location.X, d.ActualRight, 1);
    }

    // The earlier floats are looked for in the preceding siblings of every ancestor, not only of the float's own parent.
    [Fact]
    public async Task EarlierFloatsBeforeTheFloatsParent_AreCounted()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'>" +
            "<div id='a' style='float:left;width:225pt;height:40pt'></div>" +
            "<div id='b' style='float:left;width:150pt;height:40pt'></div>" +
            "<div id='w'><div id='d' style='float:left;width:38pt;height:40pt'></div></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "b")!.Location.Y, LayoutHarness.FindById(root, "d")!.Location.Y, 1);
    }

    // A float in a box that is a formatting context of its own does not see the floats outside it.
    [Fact]
    public async Task FloatInsideAFormattingContextRoot_IgnoresFloatsOutsideIt()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'>" +
            "<div id='a' style='float:left;width:225pt;height:40pt'></div>" +
            "<div id='b' style='float:left;width:150pt;height:40pt'></div>" +
            "<div id='w' style='overflow:hidden'><div id='d' style='float:left;width:38pt;height:40pt'></div></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "w")!.Location.Y, LayoutHarness.FindById(root, "d")!.Location.Y, 1);
    }

    // What a float does inside a box that is a formatting context of its own stays inside it, so it is no earlier float to this one.
    [Fact]
    public async Task FloatInsideAnOverflowHiddenSibling_DoesNotMoveALaterFloat()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'>" +
            "<div style='overflow:hidden;height:10pt'><div style='height:30pt'></div><div style='float:left;width:75pt;height:40pt'></div></div>" +
            "<div id='d' style='float:left;width:75pt;height:20pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "c")!.Location.Y + 10, LayoutHarness.FindById(root, "d")!.Location.Y, 1);
    }

    // The float is placed again once clearance has moved it, and that placement keeps the rule too: clearing the left float puts it at the
    // top of the first row, beside nothing, but the right floats were pushed down and it may not start above them.
    [Fact]
    public async Task FloatPlacedAgainAfterClearance_IsNotAboveAnEarlierFloat()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'>" +
            "<div id='a' style='float:left;width:100pt;height:10pt'></div>" +
            "<div id='r1' style='float:right;width:250pt;height:30pt'></div>" +
            "<div id='r2' style='float:right;width:250pt;height:30pt'></div>" +
            "<div id='l' style='float:left;clear:left;width:20pt;height:20pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.True(LayoutHarness.FindById(root, "l")!.Location.Y >= LayoutHarness.FindById(root, "r2")!.Location.Y - 0.5,
            $"l is at y={LayoutHarness.FindById(root, "l")!.Location.Y}, above r2 at y={LayoutHarness.FindById(root, "r2")!.Location.Y}");
    }

    // The earlier float's margin edge is above its border top by its margin-top, and the later float has none.
    [Fact]
    public async Task EarlierFloatsPositiveMarginTop_ComparesMarginEdges()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'>" +
            "<div id='a' style='float:left;width:75pt;height:20pt;margin-top:10pt'></div>" +
            "<div id='b' style='float:left;width:75pt;height:20pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "c")!.Location.Y, LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }

    // Margin edges are compared, so an earlier float's negative margin-top does not lower the next one.
    [Fact]
    public async Task EarlierFloatsNegativeMarginTop_ComparesMarginEdges()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt;padding-top:40pt'>" +
            "<div id='a' style='float:left;width:75pt;height:22pt;margin-top:-15pt'></div>" +
            "<div id='b' style='float:left;width:75pt;height:25pt;margin-top:-30pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "c")!.ClientTop - 30, LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }
}

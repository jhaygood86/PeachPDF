using PeachBrackets.Html.Dom;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// A float that clears earlier floats goes below their bottom outer edges by its own outer edge: its margin-top sits between the two, so
/// two floats with `margin: 6pt 0` and `clear: both` are 12pt apart, not 6pt (CSS 2.1 §9.5.1 rules 1-3 keep the outer edges of floats from
/// overlapping). The §9.5.2 rule that puts a block-level box's top border edge at the bottom outer edge is not for floats.
/// </summary>
public class FloatClearanceMarginTests
{
    private const string Chat = "float:{0};width:300pt;height:30pt;padding:6pt 9pt;margin:6pt 0;clear:both";

    private static async Task<(CssBox C, CssBox A, CssBox B, CssBox D)> LayoutAsync(string a, string b, string d)
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:750pt'>" +
            $"<div id='a' style='{string.Format(Chat, a)}'></div><div id='b' style='{string.Format(Chat, b)}'></div>" +
            $"<div id='d' style='{string.Format(Chat, d)}'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        return (LayoutHarness.FindById(root, "c")!, LayoutHarness.FindById(root, "a")!, LayoutHarness.FindById(root, "b")!, LayoutHarness.FindById(root, "d")!);
    }

    // The 42pt tall boxes (30pt and 12pt of padding) start at 6, 60 and 114: each is 6pt of margin, 42pt of box and 6pt of margin below the
    // one before it, and then its own 6pt of margin-top.
    [Theory]
    [InlineData("left", "left", "right")]
    [InlineData("left", "right", "left")]
    [InlineData("right", "right", "right")]
    public async Task ClearedFloatsWithMargins_AreAFullMarginBoxApart(string a, string b, string d)
    {
        var (c, boxA, boxB, boxD) = await LayoutAsync(a, b, d);

        Assert.Equal(c.Location.Y + 6, boxA.Location.Y, 1);
        Assert.Equal(c.Location.Y + 60, boxB.Location.Y, 1);
        Assert.Equal(c.Location.Y + 114, boxD.Location.Y, 1);
    }

    [Fact]
    public async Task ClearedFloatWithANegativeMarginTop_ReachesBackAboveTheFloatItClears()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:750pt'><div id='a' style='float:left;width:300pt;height:30pt;margin:6pt 0 15pt'></div>" +
            "<div id='b' style='float:left;width:300pt;height:30pt;margin:-4.5pt 0 3pt;clear:left'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var c = LayoutHarness.FindById(root, "c")!;

        // `a` ends 51pt down with its margin; the margin box of `b` starts there, so its border box starts 4.5pt above.
        Assert.Equal(c.Location.Y + 46.5, LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }

    // A float with `clear` and nothing to clear is not held to the container's top: its negative margin-top lets it start above it, and the
    // next float (rule 5) is placed against that top, not a clamped one.
    [Fact]
    public async Task ClearedFloatWithANegativeMarginTopAndNothingToClear_IsNotClampedToTheContainerTop()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:750pt'><div id='a' style='float:left;width:150pt;height:30pt;margin:-4.5pt 0 0;clear:both'></div>" +
            "<div id='b' style='float:left;width:150pt;height:30pt;margin:6pt 0 0'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var c = LayoutHarness.FindById(root, "c")!;

        Assert.Equal(c.Location.Y - 4.5, LayoutHarness.FindById(root, "a")!.Location.Y, 1);
        Assert.Equal(c.Location.Y + 6, LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }

    // Floats conflict by margin box (CSS 2.1 §9.5.1 rules 1-3). A margin-top starts the blocker above its border box...
    [Fact]
    public async Task FloatThatDoesNotFitBesideAFloatWithAMarginTop_GoesBelowItsMarginBox()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:300pt'><div style='float:right;width:187.5pt;height:22.5pt;margin-top:4.5pt'></div>" +
            "<div id='b' style='float:right;width:187.5pt;height:22.5pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "c")!.Location.Y + 27, LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }

    // ...and a negative margin-bottom ends it above its border box, where the next float, here one that clears the other side, may start.
    [Fact]
    public async Task FloatAfterABlockerWithANegativeMarginBottom_StartsAtItsMarginEdge()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:450pt'><div style='float:right;width:300pt;height:22.5pt;margin-bottom:-7.5pt'></div>" +
            "<div id='b' style='float:left;width:225pt;height:22.5pt;clear:right'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "c")!.Location.Y + 15, LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }

    // A float that clears the other side, with a negative margin-top, is not put over an earlier float whose margin-top starts it lower.
    [Fact]
    public async Task ClearedFloatWithANegativeMarginTop_IsNotPutOverAnEarlierFloatWithAMarginTop()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:450pt;padding-top:30pt'><div style='float:left;width:300pt;height:22.5pt;margin-top:6pt'></div>" +
            "<div id='b' style='float:left;width:225pt;height:22.5pt;margin-top:-11.25pt;clear:right'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "c")!.Location.Y + 47.25, LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }

    // The margin term is for floats. A block that clears a float gets §9.5.2's clearance, which puts its top border edge at the float's bottom
    // outer edge whatever its own margin-top is.
    [Fact]
    public async Task BlockWithClearAndAMarginTop_IsStillClearedToTheFloatsBottomOuterEdge()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:750pt'><div style='float:left;width:300pt;height:30pt;margin:6pt 0'></div>" +
            "<div id='b' style='clear:both;margin-top:15pt;height:10pt'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        Assert.Equal(LayoutHarness.FindById(root, "c")!.Location.Y + 42, LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }

    // A cleared float is still never above the float it does not clear, and clearing one side leaves the other alone.
    [Fact]
    public async Task ClearRightOfAFloatOnTheLeft_ClearsNothingAndKeepsRuleFive()
    {
        var html = LayoutHarness.Wrap(
            "<div id='c' style='width:750pt'><div id='a' style='float:left;width:300pt;height:30pt;margin:6pt 0'></div>" +
            "<div id='b' style='float:right;width:150pt;height:30pt;margin:9pt 0;clear:right'></div></div>");
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600);

        var c = LayoutHarness.FindById(root, "c")!;

        // Nothing on the right to clear, but the float's outer top (0pt) may not be above `a`'s (0pt): its border box is its margin-top down.
        Assert.Equal(c.Location.Y + 9, LayoutHarness.FindById(root, "b")!.Location.Y, 1);
    }
}

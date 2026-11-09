using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachDrawing.Core;
using PeachBrackets.Html.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// css-overflow-4 §3.1: <c>overflow-clip-margin</c> pushes the overflow clip edge of an axis that is cut without scrolling (<c>overflow: clip</c>, or paint
/// containment) out by a length from the edge of a visual box (the padding box unless another is named); the rounded corners grow with it, a square corner
/// staying square. An axis that is hidden, scroll or auto ignores it. Numbers are in pt so no px conversion is involved.
/// </summary>
public class OverflowClipMarginTests
{
    private static async Task<CssBox> BoxAsync(string style)
    {
        var html = $"<!DOCTYPE html><html><body style='margin:0'><div id='t' style='width:100pt;height:60pt;padding:6pt;border:4pt solid;box-sizing:content-box;{style}'><div style='width:300pt;height:200pt'></div></div></body></html>";
        var (root, _) = await LayoutHarness.LayoutAsync(html, margin: 0);

        return LayoutHarness.FindById(root, "t")!;
    }

    private static (Rect Clip, BorderRadii? Radii, bool AxisOpen) Geometry(CssBox box) => RenderUtils.OverflowClipGeometryOf(box, box.Bounds);

    // The box is 100pt of content with 6pt of padding and 4pt of border: the border box is 120 x 80, the padding box 112 x 72, the content box 100 x 60.
    [Theory]
    [InlineData("overflow:clip", 4, 4, 116, 76)]
    [InlineData("overflow:clip;overflow-clip-margin:10pt", -6, -6, 126, 86)]
    [InlineData("overflow:clip;overflow-clip-margin:0", 4, 4, 116, 76)]
    [InlineData("overflow:clip;overflow-clip-margin:padding-box 5pt", -1, -1, 121, 81)]
    [InlineData("overflow:clip;overflow-clip-margin:border-box", 0, 0, 120, 80)]
    [InlineData("overflow:clip;overflow-clip-margin:border-box 3pt", -3, -3, 123, 83)]
    [InlineData("overflow:clip;overflow-clip-margin:content-box", 10, 10, 110, 70)]
    [InlineData("overflow:clip;overflow-clip-margin:2pt content-box", 8, 8, 112, 72)]
    [InlineData("contain:paint;overflow-clip-margin:10pt", -6, -6, 126, 86)]
    [InlineData("contain:content;overflow-clip-margin:border-box", 0, 0, 120, 80)]
    [InlineData("content-visibility:auto;overflow-clip-margin:1pt", 3, 3, 117, 77)]
    public async Task TheClipEdgeIsTheVisualBoxsEdgePushedOutByTheMargin(string style, double left, double top, double right, double bottom)
    {
        var box = await BoxAsync(style);
        var (clip, _, axisOpen) = Geometry(box);

        Assert.False(axisOpen);
        Assert.Equal(left, clip.Left, 6);
        Assert.Equal(top, clip.Top, 6);
        Assert.Equal(right, clip.Right, 6);
        Assert.Equal(bottom, clip.Bottom, 6);
    }

    [Theory]
    [InlineData("overflow:hidden;overflow-clip-margin:10pt")]
    [InlineData("overflow:scroll;overflow-clip-margin:border-box 10pt")]
    [InlineData("overflow:auto;overflow-clip-margin:10pt")]
    [InlineData("overflow:visible;overflow-clip-margin:10pt")]
    [InlineData("overflow-x:clip;overflow-y:hidden;overflow-clip-margin:10pt")]
    [InlineData("contain:layout;overflow-clip-margin:10pt")]
    [InlineData("contain:paint;overflow:hidden;overflow-clip-margin:10pt")]
    public async Task AnAxisThatScrollsOrIsNotClippedIgnoresTheMargin(string style)
    {
        var box = await BoxAsync(style);

        Assert.False(box.ClipsAxisAtTheOverflowClipEdge(horizontal: true));
        Assert.False(box.ClipsAxisAtTheOverflowClipEdge(horizontal: false));

        var padding = RenderUtils.PaddingEdgeOf(box, box.Bounds);
        Assert.Equal(padding, RenderUtils.OverflowClipEdgeOf(box, box.Bounds, padding));
    }

    [Fact]
    public async Task OnlyTheClippedAxisTakesTheMargin()
    {
        var box = await BoxAsync("overflow-x:clip;overflow-clip-margin:10pt");
        var padding = RenderUtils.PaddingEdgeOf(box, box.Bounds);
        var edge = RenderUtils.OverflowClipEdgeOf(box, box.Bounds, padding);

        Assert.True(box.ClipsAxisAtTheOverflowClipEdge(horizontal: true));
        Assert.False(box.ClipsAxisAtTheOverflowClipEdge(horizontal: false));
        Assert.Equal(padding.Left - 10, edge.Left, 6);
        Assert.Equal(padding.Right + 10, edge.Right, 6);
        Assert.Equal(padding.Top, edge.Top, 6);
        Assert.Equal(padding.Bottom, edge.Bottom, 6);

        // One axis open: the clip rectangle is open on the other, and no corner curve bounds it.
        var (clip, radii, axisOpen) = Geometry(box);
        Assert.True(axisOpen);
        Assert.Null(radii);
        Assert.Equal(edge.Left, clip.Left, 6);
        Assert.True(clip.Top < -1000 && clip.Bottom > 1000);
    }

    [Fact]
    public async Task TheRoundedCornersGrowWithTheMarginAndASquareCornerStaysSquare()
    {
        var plain = await BoxAsync("overflow:clip;border-width:0;padding:0;border-radius:20pt 20pt 20pt 0");
        var margin = await BoxAsync("overflow:clip;border-width:0;padding:0;border-radius:20pt 20pt 20pt 0;overflow-clip-margin:5pt");

        var before = Geometry(plain).Radii!.Value;
        var after = Geometry(margin).Radii!.Value;

        Assert.Equal(20, before.TLX, 6);
        Assert.Equal(25, after.TLX, 6);
        Assert.Equal(25, after.TLY, 6);
        Assert.Equal(25, after.TRX, 6);
        Assert.Equal(25, after.BRY, 6);
        Assert.Equal(0, after.BLX, 6);
        Assert.Equal(0, after.BLY, 6);
    }

    [Fact]
    public async Task TheCornersOfTheBorderBoxEdgeAreTheBorderRadius()
    {
        var box = await BoxAsync("overflow:clip;border-radius:16pt;overflow-clip-margin:border-box");
        var radii = Geometry(box).Radii!.Value;

        Assert.Equal(16, radii.TLX, 6);
        Assert.Equal(16, radii.BRY, 6);
    }

    [Fact]
    public async Task TheCornersOfThePaddingBoxEdgeAreTheBorderRadiusLessTheBorder()
    {
        var box = await BoxAsync("overflow:clip;border-radius:16pt");
        var radii = Geometry(box).Radii!.Value;

        Assert.Equal(12, radii.TLX, 6);
    }

    // ── a margin per side, logical and physical longhands as one cascade, negative lengths ──

    [Theory]
    [InlineData("overflow:clip;overflow-clip-margin-top:10pt", 4, -6, 116, 76)]
    [InlineData("overflow:clip;overflow-clip-margin-left:3pt;overflow-clip-margin-right:5pt", 1, 4, 121, 76)]
    [InlineData("overflow:clip;overflow-clip-margin-bottom:border-box 2pt", 4, 4, 116, 82)]
    [InlineData("overflow:clip;overflow-clip-margin:6pt;overflow-clip-margin-top:0", -2, 4, 122, 82)]
    [InlineData("overflow:clip;overflow-clip-margin-top:0;overflow-clip-margin:6pt", -2, -2, 122, 82)]
    [InlineData("overflow:clip;overflow-clip-margin:-3pt", 7, 7, 113, 73)]
    [InlineData("overflow:clip;overflow-clip-margin:border-box -2pt", 2, 2, 118, 78)]
    [InlineData("overflow:clip;overflow-clip-margin-inline:5pt", -1, 4, 121, 76)]
    [InlineData("overflow:clip;overflow-clip-margin-block:5pt", 4, -1, 116, 81)]
    [InlineData("overflow:clip;overflow-clip-margin-block-start:7pt;overflow-clip-margin-inline-end:2pt", 4, -3, 118, 76)]
    // The logical side that is declared after the physical one wins, and the other way round.
    [InlineData("overflow:clip;overflow-clip-margin-left:9pt;overflow-clip-margin-inline-start:2pt", 2, 4, 116, 76)]
    [InlineData("overflow:clip;overflow-clip-margin-inline-start:2pt;overflow-clip-margin-left:9pt", -5, 4, 116, 76)]
    // Under rtl the inline start is the right side; in a vertical writing mode the block start is the right side too.
    [InlineData("overflow:clip;direction:rtl;overflow-clip-margin-inline-start:6pt", 4, 4, 122, 76)]
    [InlineData("overflow:clip;writing-mode:vertical-rl;overflow-clip-margin-block-start:6pt", 4, 4, 122, 76)]
    public async Task EachSideTakesItsOwnMarginAndALogicalLonghandNamesTheSideItIsOnUnderTheWritingMode(string style, double left, double top, double right, double bottom)
    {
        var box = await BoxAsync(style);
        var (clip, _, _) = Geometry(box);

        Assert.Equal(left, clip.Left, 6);
        Assert.Equal(top, clip.Top, 6);
        Assert.Equal(right, clip.Right, 6);
        Assert.Equal(bottom, clip.Bottom, 6);
    }

    [Theory]
    // A radius less than the spread keeps its shape rather than jumping: r + s * (1 + (r/s - 1)^3).
    [InlineData(4, 10, 4 + (10 * (1 + (-0.6 * -0.6 * -0.6))))]
    [InlineData(20, 5, 25)]
    [InlineData(10, 10, 20)]
    [InlineData(10, -4, 6)]
    [InlineData(3, -5, 0)]
    public async Task TheRoundedCornersAreShapedAsABoxShadowSpreadByTheSameOffset(double radius, double margin, double expected)
    {
        var box = await BoxAsync($"overflow:clip;border-width:0;padding:0;border-radius:{radius}pt;overflow-clip-margin:{margin}pt");
        var radii = Geometry(box).Radii;

        Assert.Equal(expected, radii!.Value.TLX, 6);
    }
}

using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// CSS Containment 2 (§3.2, §3.4): layout containment and paint containment each make a box an independent formatting context (so it contains its
/// floats and its margins do not collapse through it), the containing block of its absolute and fixed descendants and a stacking context; paint containment
/// also clips to the padding edge; layout containment gives the box no baseline. <c>content-visibility: auto</c> turns on layout, style and paint
/// containment (§4). Neither applies to a non-atomic inline, an internal table box or a box with no principal box. Numbers are in pt so no px conversion is
/// involved.
/// </summary>
public class ContainmentTests
{
    private static async Task<CssBox> LayoutAsync(string body, string css = "")
    {
        var html = $"<!DOCTYPE html><html><head><style>{css}</style></head><body style='margin:0'>{body}</body></html>";
        var (root, _) = await LayoutHarness.LayoutAsync(html, pageHeight: 600, margin: 0);

        return root;
    }

    private static CssBox Box(CssBox root, string id) => LayoutHarness.FindById(root, id)!;

    private static double Height(CssBox box) => box.ActualBottom - box.Location.Y;

    private static double FirstWordTop(CssBox box) =>
        new[] { box }.Concat(LayoutHarness.Descendants(box)).SelectMany(b => b.Words).First().Top;

    private const string Float = "<div style='float:left;width:48pt;height:12pt'></div>";

    [Theory]
    [InlineData("contain:layout")]
    [InlineData("contain:paint")]
    [InlineData("contain:content")]
    [InlineData("contain:layout paint")]
    [InlineData("contain:size layout")]
    [InlineData("content-visibility:auto")]
    public async Task ABoxWithLayoutOrPaintContainment_ContainsItsFloats(string style)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'>{Float}</div>");

        Assert.Equal(12, Height(Box(root, "t")), 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contain:none")]
    [InlineData("contain:size")]
    [InlineData("contain:style")]
    [InlineData("contain:size style")]
    [InlineData("contain:nonsense")]
    [InlineData("contain:layout layout")]
    [InlineData("contain:strict layout")]
    [InlineData("content-visibility:visible")]
    public async Task ABoxWithoutLayoutOrPaintContainment_DoesNotContainItsFloats(string style)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'>{Float}</div>");

        Assert.Equal(0, Height(Box(root, "t")), 6);
    }

    [Theory]
    [InlineData("contain:strict")]
    [InlineData("content-visibility:hidden")]
    public async Task StrictAndHiddenIncludeLayoutAndPaintContainment(string style)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'></div>");

        Assert.True(DomUtils.EstablishesIndependentFormattingContext(Box(root, "t")));
        Assert.Equal(Containment.Strict, DomUtils.AppliedContainment(Box(root, "t")));
    }

    [Theory]
    [InlineData("<span id='t' style='contain:layout paint'>x</span>")]
    [InlineData("<div id='t' style='contain:layout paint;display:none'>x</div>")]
    public async Task ContainmentDoesNotApplyWhereThereIsNoBoxToApplyItTo(string markup)
    {
        var root = await LayoutAsync(markup);

        Assert.Equal(Containment.None, DomUtils.AppliedContainment(Box(root, "t")));
    }

    [Fact]
    public async Task ContainmentDoesApplyToAnAtomicInline()
    {
        var root = await LayoutAsync("<span id='t' style='display:inline-block;contain:layout'>x</span>");

        Assert.Equal(Containment.Layout, DomUtils.AppliedContainment(Box(root, "t")));
    }

    [Fact]
    public async Task ContainmentDoesNotApplyToAnInternalTableBoxButStyleStillDoes()
    {
        var root = await LayoutAsync("<table><tr id='t' style='contain:strict'><td>x</td></tr></table>");

        Assert.Equal(Containment.Style, DomUtils.AppliedContainment(Box(root, "t")));
    }

    [Fact]
    public async Task ContainmentAppliesToATableCellAndToATableButNotSizeContainmentToTheTable()
    {
        var root = await LayoutAsync("<table id='tb' style='contain:strict'><tr><td id='td' style='contain:layout'>x</td></tr></table>");

        Assert.Equal(Containment.Layout | Containment.Paint | Containment.Style, DomUtils.AppliedContainment(Box(root, "tb")));
        Assert.Equal(Containment.Layout, DomUtils.AppliedContainment(Box(root, "td")));
    }

    // A margin does not collapse through the box (the box is an independent formatting context): the wrapper starts at the top and its child 20pt in.
    [Theory]
    [InlineData("contain:layout")]
    [InlineData("contain:paint")]
    public async Task AMarginDoesNotCollapseThroughAContainedBox(string style)
    {
        var root = await LayoutAsync($"<div id='w' style='{style}'><div id='c' style='margin-top:20pt;height:10pt'></div></div>");

        Assert.Equal(0, Box(root, "w").Location.Y, 6);
        Assert.Equal(20, Box(root, "c").Location.Y - Box(root, "w").Location.Y, 6);
        Assert.Equal(30, Height(Box(root, "w")), 6);
    }

    [Fact]
    public async Task AMarginDoesCollapseThroughAnUncontainedBox()
    {
        var root = await LayoutAsync("<div id='w'><div id='c' style='margin-top:20pt;height:10pt'></div></div>");

        Assert.Equal(20, Box(root, "w").Location.Y, 6);
        Assert.Equal(20, Box(root, "c").Location.Y, 6);
    }

    [Theory]
    [InlineData("contain:layout")]
    [InlineData("contain:paint")]
    [InlineData("content-visibility:auto")]
    public async Task AContainedBoxIsTheContainingBlockOfItsAbsoluteDescendants(string style)
    {
        var root = await LayoutAsync(
            $"<div style='margin-left:100pt;height:50pt'><div id='c' style='{style};width:200pt;height:50pt;margin-left:7pt'>" +
            "<div style='height:5pt'></div><div id='a' style='position:absolute;left:10pt;top:3pt;width:20pt;height:4pt'></div></div></div>");

        var c = Box(root, "c");
        var a = Box(root, "a");
        Assert.Equal(c.Location.X + 10, a.Location.X, 6);
        Assert.Equal(c.Location.Y + 3, a.Location.Y, 6);
    }

    [Fact]
    public async Task AnUncontainedBoxIsNotTheContainingBlockOfAnAbsoluteDescendant()
    {
        var root = await LayoutAsync(
            "<div style='margin-left:100pt;height:50pt'><div id='c' style='width:200pt;height:50pt;margin-left:7pt'>" +
            "<div id='a' style='position:absolute;left:10pt;top:3pt;width:20pt;height:4pt'></div></div></div>");

        Assert.Equal(10, Box(root, "a").Location.X, 6);
    }

    [Theory]
    [InlineData("contain:layout")]
    [InlineData("contain:paint")]
    public async Task AFixedDescendantOfAContainedBoxIsPlacedAgainstItLikeAnAbsoluteOne(string style)
    {
        var root = await LayoutAsync(
            $"<div id='c' style='{style};margin-left:100pt;width:200pt;height:50pt'>" +
            "<div id='f' style='position:fixed;left:10pt;top:3pt;width:20pt;height:4pt'></div></div>");

        var c = Box(root, "c");
        var f = Box(root, "f");
        Assert.Equal(PositionMode.Absolute, f.Position.Value);
        Assert.Equal(c.Location.X + 10, f.Location.X, 6);
        Assert.Equal(c.Location.Y + 3, f.Location.Y, 6);
    }

    [Fact]
    public async Task AFixedBoxOutsideAnyContainedBoxStaysFixed()
    {
        var root = await LayoutAsync("<div style='margin-left:100pt'><div id='f' style='position:fixed;left:10pt;top:3pt;width:20pt;height:4pt'></div></div>");

        Assert.Equal(PositionMode.Fixed, Box(root, "f").Position.Value);
    }

    [Theory]
    [InlineData("contain:layout", true)]
    [InlineData("contain:paint", true)]
    [InlineData("content-visibility:auto", true)]
    [InlineData("contain:size style", false)]
    [InlineData("", false)]
    public async Task LayoutAndPaintContainmentCreateAStackingContext(string style, bool expected)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'>x</div>");

        Assert.Equal(expected, DomUtils.IsStackingContextBox(Box(root, "t")));
    }

    [Theory]
    [InlineData("contain:paint", true)]
    [InlineData("contain:content", true)]
    [InlineData("content-visibility:auto", true)]
    [InlineData("contain:layout", false)]
    [InlineData("contain:size style", false)]
    [InlineData("", false)]
    public async Task PaintContainmentClipsBothAxesToThePaddingEdge(string style, bool clips)
    {
        var root = await LayoutAsync($"<div id='t' style='{style};width:50pt;height:20pt'>x</div>");
        var t = Box(root, "t");

        Assert.Equal(clips, DomUtils.ClipsItsOverflow(t));
        Assert.Equal(clips, t.ClipsWithoutScrolling);

        if (clips)
        {
            Assert.True(t.ClipsOverflowHorizontally);
            Assert.True(t.ClipsOverflowVertically);
        }
    }

    // Only a visible axis becomes clip: a box that is already a scroll container is left alone, and is not turned into a clipping non-scroller.
    [Fact]
    public async Task PaintContainmentLeavesAScrollContainerAlone()
    {
        var root = await LayoutAsync("<div id='t' style='contain:paint;overflow:scroll;width:50pt;height:20pt'>x</div>");

        Assert.False(Box(root, "t").ClipsWithoutScrolling);
        Assert.Equal(Overflow.Scroll, Box(root, "t").Overflow.Value);
    }

    [Fact]
    public async Task PaintContainmentLeavesAnOverflowClipOnOneAxisAndClipsTheOther()
    {
        var root = await LayoutAsync("<div id='t' style='contain:paint;overflow-x:clip;width:50pt;height:20pt'>x</div>");
        var t = Box(root, "t");

        Assert.True(t.ClipsOverflowHorizontally);
        Assert.True(t.ClipsOverflowVertically);
        Assert.True(DomUtils.ClipsItsOverflow(t));
    }

    [Fact]
    public async Task AContainedInlineBlockIsAlignedByItsBottomMarginEdge()
    {
        const string Line = "<div id='w' style='font:12pt sans-serif'><span id='xs'>x</span><span style='display:inline-block;height:40pt;{0}'>y</span></div>";

        var plain = await LayoutAsync(string.Format(Line, ""));
        var contained = await LayoutAsync(string.Format(Line, "contain:layout"));

        // Uncontained, the box is aligned by the baseline of its own last line; contained, it has none and its bottom margin edge sits on the line's baseline,
        // which is the width of the box's descent lower than its own text's baseline was.
        var plainText = FirstWordTop(Box(plain, "xs")) - Box(plain, "w").Location.Y;
        var containedText = FirstWordTop(Box(contained, "xs")) - Box(contained, "w").Location.Y;
        Assert.True(containedText > plainText + 1, $"contained text at {containedText}, uncontained at {plainText}");
    }

    [Fact]
    public async Task ALegendIsABlockByDefault()
    {
        var root = await LayoutAsync("<fieldset><legend id='l'>x</legend></fieldset>");

        Assert.Equal(Keywords.Block, Box(root, "l").DerivedStyle.ActualDisplay);
    }

    // css-contain-2 §2: layout or paint containment on html or body stops body's background and overflow propagating to the canvas and the viewport.
    private static async Task<(CssBox Root, HtmlContainerInt Container)> DocumentAsync(string css) =>
        await LayoutHarness.LayoutAsync($"<!DOCTYPE html><html><head><style>{css}</style></head><body><div>text</div></body></html>");

    [Theory]
    [InlineData("body{background:red}", "body")]
    [InlineData("html{background:blue} body{background:red}", "html")]
    [InlineData("body{background:red;contain:paint}", null)]
    [InlineData("body{background:red;contain:layout}", null)]
    [InlineData("html{contain:layout} body{background:red}", null)]
    [InlineData("html{contain:paint} body{background:red}", null)]
    [InlineData("body{background:red;content-visibility:auto}", null)]
    [InlineData("html{contain:paint;background:blue} body{background:red}", "html")]
    [InlineData("body{background:red;contain:size style}", "body")]
    public async Task ContainmentOnHtmlOrBody_StopsBodyBackgroundPropagating(string css, string? expectedCanvasTag)
    {
        var (_, container) = await DocumentAsync(css);

        Assert.Equal(expectedCanvasTag, container.CanvasBackgroundBox?.HtmlTag?.Name);
    }

    [Fact]
    public async Task ABodyWhoseBackgroundDoesNotPropagate_PaintsItOnItsOwnBox()
    {
        var (root, container) = await DocumentAsync("body{background:red;contain:paint}");
        var body = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name == "body");

        Assert.Null(container.CanvasBackgroundBox);
        Assert.False(body.SuppressOwnBackgroundPaint);
    }

    [Theory]
    [InlineData("body{overflow:hidden;height:50pt}", false)]
    [InlineData("body{overflow:hidden;height:50pt;contain:paint}", true)]
    [InlineData("body{overflow:hidden;height:50pt;contain:layout}", true)]
    [InlineData("html{contain:layout} body{overflow:hidden;height:50pt}", true)]
    [InlineData("body{overflow:hidden;height:50pt;contain:style}", false)]
    public async Task ContainmentOnHtmlOrBody_StopsBodyOverflowPropagating(string css, bool bodyIsAScrollContainer)
    {
        var (root, _) = await DocumentAsync(css);
        var body = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name == "body");

        Assert.Equal(bodyIsAScrollContainer, MonolithicContent.IsScrollContainer(body));
    }

    [Fact]
    public async Task TheRootElementsOwnOverflowStillGoesToTheViewportWhenItIsContained()
    {
        var (root, _) = await DocumentAsync("html{overflow:hidden;contain:paint}");
        var html = LayoutHarness.Descendants(root).First(b => b.HtmlTag?.Name == "html");

        Assert.False(MonolithicContent.IsScrollContainer(html));
    }
}

using PeachBrackets;
using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Rendering;
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

    // ── size containment (css-contain-2 §3.1) and contain-intrinsic-* (css-sizing-4 §5.1) ──

    private const string Tall = "<div style='height:40pt;width:90pt'></div>";

    [Theory]
    [InlineData("contain:size", 0d)]
    [InlineData("contain:strict", 0d)]
    [InlineData("content-visibility:hidden", 0d)]
    [InlineData("contain:size;padding:5pt", 10d)]
    [InlineData("contain:size;contain-intrinsic-height:30pt", 30d)]
    [InlineData("contain:size;contain-intrinsic-height:30pt;padding:5pt;border:2pt solid", 44d)]
    [InlineData("contain:size;contain-intrinsic-size:20pt 25pt", 25d)]
    [InlineData("contain:size;contain-intrinsic-size:15pt", 15d)]
    [InlineData("contain:size;contain-intrinsic-size:auto 20pt auto 35pt", 35d)]
    [InlineData("contain:size;contain-intrinsic-height:auto 22pt", 22d)]
    [InlineData("contain:size;contain-intrinsic-height:none", 0d)]
    [InlineData("contain:size;height:50pt", 50d)]
    [InlineData("contain:size;height:50pt;contain-intrinsic-height:30pt", 50d)]
    [InlineData("contain:size;min-height:12pt", 12d)]
    // Without size containment, none of it applies.
    [InlineData("contain-intrinsic-height:30pt", 40d)]
    [InlineData("contain:layout paint style", 40d)]
    public async Task SizeContainmentGivesAnAutoHeightBoxTheHeightOfAnEmptyOne(string style, double expected)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'>{Tall}</div>");

        Assert.Equal(expected, Height(Box(root, "t")), 6);
    }

    [Fact]
    public async Task TheContentsOfASizeContainedBoxAreLaidOutNormallyAndOverflowIt()
    {
        var root = await LayoutAsync("<div id='t' style='contain:size;width:50pt'><div id='c' style='height:40pt'></div></div>");

        Assert.Equal(0, Height(Box(root, "t")), 6);
        Assert.Equal(40, Height(Box(root, "c")), 6);
        Assert.Equal(Box(root, "t").Location.Y, Box(root, "c").Location.Y, 6);
    }

    [Theory]
    [InlineData("contain:size", 0d)]
    [InlineData("contain:size;contain-intrinsic-width:70pt", 70d)]
    [InlineData("contain:size;contain-intrinsic-size:60pt 5pt", 60d)]
    [InlineData("contain:size;padding:0 4pt;contain-intrinsic-width:70pt", 78d)]
    [InlineData("contain:size;width:33pt;contain-intrinsic-width:70pt", 33d)]
    [InlineData("", 26d)]
    public async Task SizeContainmentGivesAShrinkToFitBoxTheWidthOfAnEmptyOne(string style, double expected)
    {
        var root = await LayoutAsync($"<div id='t' style='float:left;{style}'><div style='width:26pt;height:5pt'></div></div>");
        var t = Box(root, "t");

        Assert.Equal(expected, t.ActualRight - t.Location.X, 6);
    }

    [Fact]
    public async Task TheLogicalAndThePhysicalContainIntrinsicLonghandsAreOneCascade_TheLaterWins()
    {
        var logicalLast = await LayoutAsync(
            "<div id='t' style='contain:size;contain-intrinsic-height:30pt;contain-intrinsic-block-size:10pt'>x</div>");
        var physicalLast = await LayoutAsync(
            "<div id='t' style='contain:size;contain-intrinsic-block-size:10pt;contain-intrinsic-height:30pt'>x</div>");

        Assert.Equal(10, Height(Box(logicalLast, "t")), 6);
        Assert.Equal(30, Height(Box(physicalLast, "t")), 6);
    }

    [Fact]
    public async Task ABlockSizeIsTheHeightInAHorizontalWritingModeAndTheWidthInAVerticalOne()
    {
        var horizontal = await LayoutAsync("<div id='t' style='contain:size;contain-intrinsic-block-size:18pt;contain-intrinsic-inline-size:7pt'>x</div>");
        var vertical = await LayoutAsync("<div id='t' style='writing-mode:vertical-rl;contain:size;contain-intrinsic-block-size:44pt;contain-intrinsic-inline-size:12pt'>x</div>");

        Assert.Equal(18, Box(horizontal, "t").ContainIntrinsicLength(widthAxis: false));
        Assert.Equal(7, Box(horizontal, "t").ContainIntrinsicLength(widthAxis: true));
        Assert.Equal(44, Box(vertical, "t").ContainIntrinsicLength(widthAxis: true));
        Assert.Equal(12, Box(vertical, "t").ContainIntrinsicLength(widthAxis: false));
    }

    [Fact]
    public async Task AReplacedElementWithSizeContainmentHasNoNaturalSize()
    {
        const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var plain = await LayoutAsync($"<img id='t' src='{Png}'>");
        var contained = await LayoutAsync($"<img id='t' src='{Png}' style='contain:size'>");
        var intrinsic = await LayoutAsync($"<img id='t' src='{Png}' style='contain:size;contain-intrinsic-size:30pt 20pt'>");
        var authored = await LayoutAsync($"<img id='t' src='{Png}' style='contain:size;width:15pt;height:9pt'>");

        double Width(CssBox root) => new[] { Box(root, "t") }.Concat(LayoutHarness.Descendants(Box(root, "t"))).SelectMany(b => b.Words).First().Width;
        double WordHeight(CssBox root) => new[] { Box(root, "t") }.Concat(LayoutHarness.Descendants(Box(root, "t"))).SelectMany(b => b.Words).First().Height;

        Assert.True(Width(plain) > 0);
        Assert.Equal(0, Width(contained), 6);
        Assert.Equal(0, WordHeight(contained), 6);
        Assert.Equal(30, Width(intrinsic), 6);
        Assert.Equal(20, WordHeight(intrinsic), 6);
        Assert.Equal(15, Width(authored), 6);
        Assert.Equal(9, WordHeight(authored), 6);
    }

    [Theory]
    [InlineData("contain:size", true)]
    [InlineData("contain:strict", true)]
    [InlineData("contain:layout", false)]
    [InlineData("", false)]
    public async Task ASizeContainedBoxIsMonolithicWhenFragmenting(string style, bool monolithic)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'><p>text</p></div>");

        Assert.Equal(monolithic, MonolithicContent.IsMonolithic(Box(root, "t")));
    }

    [Fact]
    public async Task SizeContainmentDoesNotChangeTheBaseline()
    {
        const string Line = "<div id='w' style='font:12pt sans-serif'><span id='xs'>x</span><span style='display:inline-block;{0}'>y</span></div>";

        var plain = await LayoutAsync(string.Format(Line, ""));
        var contained = await LayoutAsync(string.Format(Line, "contain:size"));

        // Not asserting its size: only that the inline-block still sits on the line by its own text's baseline, as it does without containment.
        Assert.Equal(FirstWordTop(Box(plain, "xs")), FirstWordTop(Box(contained, "xs")), 6);
    }

    // ── content-visibility: hidden skips its contents (css-contain-2 §4) ──

    [Fact]
    public async Task HiddenContentsAreSkippedAndTheBoxIsAsTallAsAnEmptyOne()
    {
        var root = await LayoutAsync("<div id='t' style='content-visibility:hidden;padding:3pt'><div id='c' style='height:40pt'>words</div></div><div id='n' style='height:5pt'></div>");

        Assert.Equal(6, Height(Box(root, "t")), 6);
        Assert.Equal(Keywords.None, Box(root, "c").DerivedStyle.ActualDisplay);
        Assert.Equal(Box(root, "t").ActualBottom, Box(root, "n").Location.Y, 6);
        Assert.DoesNotContain(LayoutHarness.Descendants(Box(root, "t")), b => b.Words.Count > 0);
    }

    [Fact]
    public async Task TheTextOfAHiddenBoxIsSkippedToo()
    {
        var root = await LayoutAsync("<div id='t' style='content-visibility:hidden'>some text<span>more</span></div>");

        Assert.Equal(0, Height(Box(root, "t")), 6);
        Assert.DoesNotContain(LayoutHarness.Descendants(Box(root, "t")), b => b.Words.Count > 0);
    }

    [Fact]
    public async Task AHiddenBoxTakesItsContainIntrinsicSize()
    {
        var root = await LayoutAsync("<div id='t' style='content-visibility:hidden;contain-intrinsic-size:auto 80pt auto 30pt;float:left'><div style='height:40pt'></div></div>");

        Assert.Equal(30, Height(Box(root, "t")), 6);
        Assert.Equal(80, Box(root, "t").ActualRight - Box(root, "t").Location.X, 6);
    }

    [Theory]
    [InlineData("content-visibility:visible")]
    [InlineData("content-visibility:auto")]
    [InlineData("")]
    public async Task ContentsThatAreNotHiddenAreLaidOutAsUsual(string style)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'><div id='c' style='height:40pt'>words</div></div>");

        Assert.Equal(40, Height(Box(root, "t")), 6);
        Assert.NotEqual(Keywords.None, Box(root, "c").DerivedStyle.ActualDisplay);
    }

    [Fact]
    public async Task ContentVisibilityHiddenDoesNothingToAnInlineBoxContainmentCannotApplyTo()
    {
        var root = await LayoutAsync("<div><span id='t' style='content-visibility:hidden'>kept</span></div>");

        Assert.Contains(LayoutHarness.Descendants(Box(root, "t")), b => b.Words.Count > 0);
    }

    [Fact]
    public async Task AHiddenBoxInsideAHiddenBoxIsSkippedWithTheOuterOne()
    {
        var root = await LayoutAsync("<div id='t' style='content-visibility:hidden'><div id='u' style='content-visibility:hidden'><div style='height:30pt'></div></div></div>");

        Assert.Equal(0, Height(Box(root, "t")), 6);
        Assert.Equal(Keywords.None, Box(root, "u").DerivedStyle.ActualDisplay);
    }

    [Fact]
    public async Task TheCountersAndQuotesOfSkippedContentsDoNotCount()
    {
        var root = await LayoutAsync(
            "<div style='counter-reset:c'><div style='content-visibility:hidden'><p id='i' style='counter-increment:c'>x</p></div><p id='a' style='counter-increment:c'>y</p></div>");

        Assert.Equal(1, CssCounterEngine.GetCounter(Box(root, "a"), "c")!.Value);
    }

    // ── inline-size containment (css-contain-3) and what container-type implies ──

    [Theory]
    [InlineData("contain:inline-size", 0d, 40d)]
    [InlineData("contain:inline-size;contain-intrinsic-inline-size:70pt", 70d, 40d)]
    [InlineData("contain:inline-size;contain-intrinsic-width:60pt;contain-intrinsic-height:10pt", 60d, 40d)]
    [InlineData("contain:inline-size layout paint style", 0d, 40d)]
    [InlineData("container-type:inline-size", 0d, 40d)]
    [InlineData("container-type:size", 0d, 0d)]
    [InlineData("container-type:normal", 26d, 40d)]
    [InlineData("contain:strict", 0d, 0d)]
    [InlineData("contain:content", 26d, 40d)]
    [InlineData("contain:size inline-size", 26d, 40d)]
    public async Task InlineSizeContainmentSizesTheInlineAxisAsAnEmptyBoxAndTheBlockAxisByItsContents(string style, double width, double height)
    {
        var root = await LayoutAsync($"<div id='t' style='float:left;{style}'><div style='width:26pt;height:40pt'></div></div>");
        var t = Box(root, "t");

        Assert.Equal(width, t.ActualRight - t.Location.X, 6);
        Assert.Equal(height, Height(t), 6);
    }

    [Theory]
    [InlineData("contain:inline-size", "InlineSize")]
    [InlineData("contain:inline-size layout", "InlineSize, Layout")]
    [InlineData("container-type:inline-size", "InlineSize, Style")]
    [InlineData("container-type:size", "Size, Style")]
    [InlineData("container-type:size;contain:layout", "Size, Layout, Style")]
    [InlineData("container-type:normal", "None")]
    public async Task AContainerTypeAppliesStyleContainmentAndSizeContainmentOnBothAxesOrTheInlineOne(string style, string expected)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'>x</div>");

        Assert.Equal(Enum.Parse<Containment>(expected), DomUtils.AppliedContainment(Box(root, "t")));
    }

    [Fact]
    public async Task InlineSizeContainmentIsTheInlineAxisOfTheBoxsWritingMode()
    {
        var horizontal = await LayoutAsync("<div id='t' style='contain:inline-size'>x</div>");
        var vertical = await LayoutAsync("<div id='t' style='writing-mode:vertical-rl;contain:inline-size'>x</div>");

        Assert.True(Box(horizontal, "t").AppliesSizeContainmentInAxis(widthAxis: true));
        Assert.False(Box(horizontal, "t").AppliesSizeContainmentInAxis(widthAxis: false));
        Assert.False(Box(vertical, "t").AppliesSizeContainmentInAxis(widthAxis: true));
        Assert.True(Box(vertical, "t").AppliesSizeContainmentInAxis(widthAxis: false));
        Assert.False(Box(horizontal, "t").AppliesSizeContainment);
    }

    [Theory]
    [InlineData("contain:inline-size", false)]
    [InlineData("container-type:inline-size", false)]
    [InlineData("container-type:size", true)]
    public async Task OnlyFullSizeContainmentMakesABoxMonolithic(string style, bool monolithic)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'><p>text</p></div>");

        Assert.Equal(monolithic, MonolithicContent.IsMonolithic(Box(root, "t")));
    }

    [Fact]
    public async Task InlineSizeContainmentDoesNotApplyToATableCell()
    {
        var root = await LayoutAsync("<table><tr><td id='t' style='contain:inline-size'>x</td></tr></table>");

        Assert.Equal(Containment.None, DomUtils.AppliedContainment(Box(root, "t")));
    }

    [Fact]
    public async Task AReplacedElementWithInlineSizeContainmentHasNoNaturalInlineSizeAndNoRatio()
    {
        const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        var plain = await LayoutAsync($"<img id='t' src='{Png}' style='height:20pt'>");
        var contained = await LayoutAsync($"<img id='t' src='{Png}' style='contain:inline-size;height:20pt'>");

        double WordWidth(CssBox root) => new[] { Box(root, "t") }.Concat(LayoutHarness.Descendants(Box(root, "t"))).SelectMany(b => b.Words).First().Width;
        double WordHeight(CssBox root) => new[] { Box(root, "t") }.Concat(LayoutHarness.Descendants(Box(root, "t"))).SelectMany(b => b.Words).First().Height;

        Assert.True(WordWidth(plain) > 0);
        Assert.Equal(0, WordWidth(contained), 6);
        Assert.Equal(20, WordHeight(contained), 6);
    }

    [Theory]
    [InlineData("contain:size;contain-intrinsic-height:30pt", 30d)]
    [InlineData("contain:size;contain-intrinsic-height:30pt;padding:2pt", 34d)]
    [InlineData("contain:size", 0d)]
    [InlineData("content-visibility:hidden;contain-intrinsic-size:auto 80pt auto 25pt", 25d)]
    public async Task WhatFollowsASizeContainedBoxStartsBelowTheSizeItWasGiven(string style, double expectedTop)
    {
        var root = await LayoutAsync($"<div id='t' style='{style}'><div style='height:90pt'></div></div><div id='n' style='height:5pt'></div>");

        Assert.Equal(expectedTop, Box(root, "n").Location.Y, 6);
        Assert.Equal(Box(root, "t").ActualBottom, Box(root, "n").Location.Y, 6);
    }

    // The rewrite keeps what a fixed box has that an absolute one lacks: its own stacking context (css-position-3 §3.5), which an absolute box with z-index auto does not.
    [Fact]
    public async Task AFixedBoxInAContainedBoxStillHasItsOwnStackingContext()
    {
        var root = await LayoutAsync(
            "<div style='contain:layout;width:100pt;height:50pt'>" +
            "<div id='f' style='position:fixed;width:10pt;height:10pt'></div><div id='a' style='position:absolute;width:10pt;height:10pt'></div></div>");

        Assert.True(Box(root, "f").FixedInContainment);
        Assert.True(DomUtils.IsStackingContextBox(Box(root, "f")));
        Assert.False(Box(root, "a").FixedInContainment);
        Assert.False(DomUtils.IsStackingContextBox(Box(root, "a")));
    }

    [Fact]
    public async Task AFixedBoxOutsideAContainedBoxIsNotMarked()
    {
        var root = await LayoutAsync("<div id='f' style='position:fixed;width:10pt;height:10pt'></div>");

        Assert.False(Box(root, "f").FixedInContainment);
        Assert.Equal(PositionMode.Fixed, Box(root, "f").Position.Value);
    }

    // A change of `contain` or `content-visibility` is never applied in place (it changes what a descendant is positioned against), so the document is loaded
    // again and the build-time normalisation runs over the new styles: the fixed box follows the containment on and off.
    [Theory]
    [InlineData("contain:layout")]
    [InlineData("contain:paint")]
    [InlineData("content-visibility:auto")]
    public async Task AFixedBoxFollowsItsAncestorsContainmentWhenAHoverTurnsItOnAndOff(string hovered)
    {
        var html = "<!DOCTYPE html><html><head><style>body{margin:0} #c{margin-left:100px;margin-top:50px;width:200px;height:100px} " +
                   $"#c:hover{{{hovered}}} #f{{position:fixed;left:10px;top:5px;width:20px;height:10px}}</style></head><body><div id=c><div id=f></div></div></body></html>";
        var context = new HtmlRenderContext();
        var size = new Size(800, 600);
        var document = await HtmlDocument.LoadAsync(html, context, new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = size.Width, PageHeightPoints = size.Height });
        var canvas = PdfCanvas.CreateMeasuring(context, size);
        await document.LayoutAsync(canvas);

        var before = document.GetElementBounds("f")!.Value;
        await document.SetPointerStateAsync(0, 150, 80, pressed: false, canvas);
        var during = document.GetElementBounds("f")!.Value;
        await document.ClearPointerStateAsync(canvas);
        var after = document.GetElementBounds("f")!.Value;

        Assert.True(during.X > before.X + 50 && during.Y > before.Y + 25, $"fixed to the page at {before}, in the contained box at {during}");
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
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

namespace PeachBrackets.Tests;

/// <summary>
/// Absolutely and relatively positioned boxes through the public API: the containing block of a positioned inline, the static position of an absolute box among inline
/// content, in a flex container and in a vertical box, and relative offsets on table parts. Every box is read back through <see cref="HtmlDocument.GetElementBounds"/>.
/// </summary>
public class PositionedLayoutCoverageTests
{
    private static HtmlDocumentOptions Screen()
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 600 };
        options.SetMargins(0);
        return options;
    }

    private static async Task<HtmlDocument> LoadAsync(string css, string body, HtmlDocumentOptions? options = null)
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>body {{ margin: 0 }} {css}</style>"), options ?? Screen());
        return document;
    }

    private static PeachDrawing.Core.Rect Bounds(HtmlDocument document, string id) =>
        document.GetElementBounds(id) ?? throw new Xunit.Sdk.XunitException($"no box #{id}");

    // --- the containing block of a positioned inline ---

    [Fact]
    public async Task APositionedInlineSplitByABlock_HasAContainingBlockThatBeginsWhereItsFirstPieceDoes()
    {
        var document = await LoadAsync("#d { width: 200pt; line-height: 20pt } #a { position: absolute; inset: 0 }",
            "<div id=d>aaa <span id=s style='position: relative'>xx<div style='height: 30pt'></div>yy<span id=a></span></span></div>");

        var d = Bounds(document, "d");
        var a = Bounds(document, "a");

        // The first fragment is the line of xx, at the top of the block; the last the line of yy, at its bottom.
        Assert.InRange(a.Y, d.Y, d.Y + 4);
        Assert.InRange(a.Bottom, d.Bottom - 4, d.Bottom + 1);
        Assert.True(a.Height > 50, $"the containing block spans the block in the middle, {a.Height}pt");
    }

    [Fact]
    public async Task APositionedInlineThatBeginsWithABlock_HasItsFirstFragmentAtTheEndOfTheLineItBeganOn()
    {
        var document = await LoadAsync("#d { width: 200pt; line-height: 20pt } #a { position: absolute; inset: 0 }",
            "<div id=d>aaa <span id=s style='position: relative'><div style='height: 30pt'></div>yy<span id=a></span></span></div>");

        var d = Bounds(document, "d");
        var a = Bounds(document, "a");

        Assert.InRange(a.Y, d.Y, d.Y + 4);
        Assert.True(a.X > d.X + 5, $"the box starts after aaa, at {a.X}");
        Assert.InRange(a.Bottom, d.Bottom - 4, d.Bottom + 1);
    }

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public async Task APositionedInlineWhoseFirstWordWrapped_HasAnEmptyFirstFragmentAtTheEndOfTheLineItBeganOn(string direction)
    {
        var document = await LoadAsync($"#d {{ width: 100pt; line-height: 20pt; direction: {direction} }} #a {{ position: absolute; inset: 0 }}",
            "<div id=d>BBBBBBBBBB<span id=s style='position: relative'><span id=a></span> AAAAAA AAAAAA</span></div>");

        var d = Bounds(document, "d");
        var a = Bounds(document, "a");

        // The inline's first fragment is on the first line, where nothing of it fit; its last is on the line below.
        Assert.Equal(d.Y, a.Y, 1);
        Assert.True(a.Height >= 35, $"the containing block spans two lines, {a.Height}pt");
    }

    [Theory]
    [InlineData("vertical-rl", "ltr")]
    [InlineData("vertical-lr", "ltr")]
    [InlineData("vertical-rl", "rtl")]
    [InlineData("vertical-lr", "rtl")]
    public async Task APositionedInlineInAVerticalBox_ContainsAnAbsoluteChildAsLargeAsItsPaddingBox(string mode, string direction)
    {
        var document = await LoadAsync($"#d {{ writing-mode: {mode}; direction: {direction}; height: 200pt; width: 100pt; line-height: 20pt }} #s {{ position: relative; border: 2pt solid }} #a {{ position: absolute; inset: 0 }}",
            "<div id=d>aa <span id=s>inline<span id=a></span></span> bb</div>");

        var s = Bounds(document, "s");
        var a = Bounds(document, "a");

        Assert.Equal(s.X + 2, a.X, 1.5);
        Assert.Equal(s.Y + 2, a.Y, 1.5);
        Assert.Equal(s.Right - 2, a.Right, 1.5);
        Assert.Equal(s.Bottom - 2, a.Bottom, 1.5);
    }

    [Theory]
    [InlineData("vertical-rl", "ltr")]
    [InlineData("vertical-lr", "ltr")]
    [InlineData("vertical-lr", "rtl")]
    public async Task APositionedInlineWrappedInAVerticalBox_ContainsAnAbsoluteChildFromItsFirstFragmentToItsLast(string mode, string direction)
    {
        var document = await LoadAsync($"#d {{ writing-mode: {mode}; direction: {direction}; height: 70pt; width: 100pt; line-height: 20pt }} #s {{ position: relative }} #a {{ position: absolute; inset: 0 }}",
            "<div id=d><span id=s>aaaa bbbb cccc<span id=a></span></span></div>");

        var d = Bounds(document, "d");
        var a = Bounds(document, "a");

        Assert.True(a.Width >= 30, $"the containing block spans two columns, {a.Width}pt");
        Assert.True(a.Height >= 20 && a.Height <= d.Height + 0.5, $"{a.Height}pt along the column");
    }

    [Theory]
    [InlineData("right: 0; bottom: 0")]
    [InlineData("left: 0; bottom: 0")]
    [InlineData("right: 0; top: 0")]
    public async Task APositionedInlineThatContinuesOnTheNextPage_PlacesItsRightAndBottomChildAgainstItsLastFragment(string insets)
    {
        var options = new HtmlDocumentOptions { PageWidthPoints = 200, PageHeightPoints = 100 };
        options.SetMargins(0);
        var words = string.Join(' ', Enumerable.Repeat("word", 40));
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            $"<div style='line-height: 20pt'><div style='height: 60pt'></div>BBBB <span id=s style='position: relative'><span id=a style='position: absolute; {insets}; width: 10pt; height: 10pt; background: red'></span>{words}<i id=z style='display: inline-block; width: 2pt; height: 2pt'></i></span></div>",
            "<style>body { margin: 0 }</style>"), options);

        var a = Bounds(document, "a");
        var z = Bounds(document, "z");

        Assert.Equal(2, document.Pages.Count);
        if (insets.EndsWith("bottom: 0", StringComparison.Ordinal))
        {
            // The last fragment is on the second page, whose last line z ends.
            Assert.InRange(a.Bottom, z.Bottom, z.Bottom + 8);
            Assert.True(a.Y > 100, $"the box is on the second page, at {a.Y}");
        }
        else
        {
            Assert.InRange(a.Y, 60, 80);
        }

        if (insets.StartsWith("right", StringComparison.Ordinal)) Assert.InRange(a.Right, z.X - 1, z.Right + 1);
        else Assert.Equal(Bounds(document, "s").X, a.X, 1);
    }

    // --- an inline-block holding a forced line break ---

    [Theory]
    [InlineData("", 36)]
    [InlineData("columns: 2; ", 24)]
    public async Task AnInlineBlockHoldingABreak_LaysOutItsLinesInsideItself(string containerStyle, double minimumHeight)
    {
        var document = await LoadAsync("#c { display: inline-block; width: 100pt; border: 2pt solid; background: blue } #x { line-height: 12pt }",
            $"<div id=x style='{containerStyle}'>before <div id=c>aa<br>bb<br>cc</div> <i id=m style='display: inline-block; width: 10pt; height: 10pt'></i></div>");

        var x = Bounds(document, "x");
        var c = Bounds(document, "c");
        var m = Bounds(document, "m");

        Assert.True(x.Height >= minimumHeight, $"three lines, {x.Height}pt");
        if (containerStyle.Length == 0) Assert.True(m.Y >= c.Y - 0.5 && m.Y <= c.Bottom, "the marker after it is on the same line as it");
    }

    [Fact]
    public async Task AnInlineBlockHoldingABreak_ThatWouldStraddleAPageBreak_IsFlowedIntoItsParent()
    {
        var options = new HtmlDocumentOptions { PageWidthPoints = 300, PageHeightPoints = 100 };
        options.SetMargins(0);
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            "<style>@page { size: 300pt 100pt; margin: 0 }</style><div style='height: 60pt'></div><div id=d style='line-height: 20pt'>x <span style='display: inline-block; width: 100pt'>aa<br>bb<br>cc<br>dd<br>ee</span></div>", "<style>body { margin: 0 }</style>"), options);

        Assert.True(document.Pages.Count >= 1);
        Assert.True(document.GetElementBounds("d") is { Height: > 20 });
    }

    // --- an inline-level absolute box among block-level siblings ---

    private static async Task<(PeachDrawing.Core.Rect Container, PeachDrawing.Core.Rect Box)> LineBoxAsync(string container, string floated)
    {
        var document = await LoadAsync("#c { position: relative; width: 100pt; height: 100pt } #f { width: 50pt; height: 50pt } #a { display: inline; position: absolute; width: 50pt; height: 50pt }",
            $"<div id=c style='{container}'><div style='height: 50pt'></div><div id=f style='{floated}'></div><div id=a></div></div>");
        return (Bounds(document, "c"), Bounds(document, "a"));
    }

    [Theory]
    [InlineData("direction:ltr;text-align:left", "float:left", 50)]
    [InlineData("direction:ltr;text-align:center", "float:left", 75)]
    [InlineData("direction:ltr;text-align:right", "float:left", 100)]
    [InlineData("direction:ltr;text-align:left", "float:right", 0)]
    [InlineData("direction:ltr;text-align:center", "float:right", 25)]
    [InlineData("direction:ltr;text-align:right", "float:right", 50)]
    [InlineData("direction:ltr;text-align:justify", "float:left", 50)]
    [InlineData("direction:ltr;text-align:start", "float:left", 50)]
    public async Task TheBoxStartsWhereTheAlignedLineBesideTheFloatDoes_InALeftToRightBlock(string container, string floated, double expectedLeft)
    {
        var (c, a) = await LineBoxAsync(container, floated);

        Assert.Equal(c.X + expectedLeft, a.X, 0.5);
        Assert.Equal(c.Y + 50, a.Y, 0.5);
    }

    [Theory]
    [InlineData("direction:rtl;text-align:left", "float:left", 50)]
    [InlineData("direction:rtl;text-align:left", "float:right", 0)]
    [InlineData("direction:rtl;text-align:right", "float:left", 100)]
    [InlineData("direction:rtl;text-align:justify", "float:right", 50)]
    public async Task InARightToLeftBlock_TheBoxsRightEdgeIsWhereTheLineStarts(string container, string floated, double expectedRight)
    {
        var (c, a) = await LineBoxAsync(container, floated);

        Assert.Equal(c.X + expectedRight, a.Right, 0.5);
    }

    // --- an absolute box among inline content: bidi level of its place ---

    [Theory]
    [InlineData("direction: ltr", "aaa <b>&#1488;&#1489;&#1490;</b><i id=a></i> ccc")]
    [InlineData("direction: ltr", "aaa<i id=a></i> &#1488;&#1489;&#1490; ccc")]
    [InlineData("direction: ltr", "&#1488;&#1489;&#1490;<i id=a></i> aaa")]
    [InlineData("direction: ltr", "&#1488;&#1489;&#1490; <i id=a></i>aaa &#1488;&#1489;&#1490;")]
    [InlineData("direction: ltr", "<i id=a></i>&#1488;&#1489;&#1490; aaa")]
    [InlineData("direction: rtl", "<i id=a></i>aaa bbb")]
    [InlineData("direction: rtl", "")]
    [InlineData("direction: ltr", "")]
    [InlineData("direction: rtl", "aaa <b>&#1488;&#1489;&#1490;</b><i id=a></i> ccc")]
    [InlineData("direction: rtl", "aaa<i id=a></i> &#1488;&#1489;&#1490; ccc")]
    [InlineData("direction: rtl", "&#1488;&#1489;&#1490; aaa<i id=a></i> bbb &#1488;&#1489;&#1490;")]
    [InlineData("direction: rtl", "aaa bbb<i id=a></i> ccc ddd")]
    public async Task AnAbsoluteBoxAmongMixedDirectionText_SitsOnTheLineAtAPlaceInsideTheText(string style, string content)
    {
        var document = await LoadAsync($"#d {{ width: 300pt; line-height: 20pt; {style} }} #a {{ position: absolute; width: 6pt; height: 6pt; background: red }}", $"<div id=d>{(content.Length == 0 ? "<i id=a></i>" : content)}</div>");

        var d = Bounds(document, "d");
        var a = Bounds(document, "a");

        Assert.True(a.X >= d.X - 0.5 && a.Right <= d.Right + 0.5, $"the box is at {a.X}..{a.Right} in {d.X}..{d.Right}");
        Assert.Equal(d.Y, a.Y, 1);
    }

    // --- an absolute box among the inline content of a vertical box ---

    private static Task<HtmlDocument> VerticalAsync(string style, string body) =>
        LoadAsync($"#c {{ {style}; height: 300pt; line-height: 20pt }} #a {{ position: absolute; width: 4pt; height: 4pt }}", $"<div id=c>{body}</div>");

    [Theory]
    [InlineData("writing-mode: vertical-lr")]
    [InlineData("writing-mode: vertical-rl")]
    public async Task AnInlineLevelBoxInAVerticalBox_HasItsTopEdgeWhereTheWordBeforeItEnds(string style)
    {
        var document = await VerticalAsync(style, "<span id=w>aaa</span><span id=a></span><span>bbb</span>");
        var a = Bounds(document, "a");
        var w = Bounds(document, "w");

        Assert.Equal(w.Bottom, a.Y, 1);
        Assert.Equal(style.EndsWith("rl", StringComparison.Ordinal) ? w.Right : w.X, style.EndsWith("rl", StringComparison.Ordinal) ? a.Right : a.X, 1.5);
    }

    [Fact]
    public async Task InADirectionRtlColumn_TheBoxAfterTheRunIsAboveIt()
    {
        var document = await VerticalAsync("writing-mode: vertical-lr; direction: rtl", "<span id=w>aaa</span> <span>bbb</span><span id=a></span>");

        Assert.Equal(Bounds(document, "w").Y, Bounds(document, "a").Bottom, 1);
    }

    [Theory]
    [InlineData("writing-mode: vertical-lr")]
    [InlineData("writing-mode: vertical-rl")]
    public async Task ABlockLevelBoxInAVerticalBox_StartsTheNextColumnAtTheStartOfTheInlineAxis(string style)
    {
        var document = await VerticalAsync(style, "aaa<div id=a></div><br>bbb");

        Assert.Equal(Bounds(document, "c").Y, Bounds(document, "a").Y, 1);
    }

    [Fact]
    public async Task ABlockLevelBoxInADirectionRtlColumn_HasItsBottomEdgeOnTheBottomOfTheContent()
    {
        var document = await VerticalAsync("writing-mode: vertical-lr; direction: rtl", "aaa<div id=a></div><br>bbb");

        Assert.Equal(Bounds(document, "c").Bottom, Bounds(document, "a").Bottom, 1);
    }

    [Theory]
    [InlineData("writing-mode: vertical-lr; direction: rtl", "aaa <b>&#1488;&#1489;&#1490;</b><i id=a></i> ccc")]
    [InlineData("writing-mode: vertical-lr; direction: rtl", "aaa<i id=a></i> &#1488;&#1489;&#1490; ccc")]
    [InlineData("writing-mode: vertical-lr; direction: rtl", "&#1488;&#1489;&#1490; aaa<i id=a></i> bbb &#1488;&#1489;&#1490;")]
    [InlineData("writing-mode: vertical-lr; direction: ltr", "aaa <b>&#1488;&#1489;&#1490;</b><i id=a></i> ccc")]
    [InlineData("writing-mode: vertical-lr; direction: ltr", "aaa<i id=a></i> &#1488;&#1489;&#1490; ccc")]
    [InlineData("writing-mode: vertical-lr; direction: ltr", "&#1488;&#1489;&#1490;<i id=a></i> aaa")]
    [InlineData("writing-mode: vertical-rl; direction: ltr", "&#1488;&#1489;&#1490; <i id=a></i>aaa &#1488;&#1489;&#1490;")]
    [InlineData("writing-mode: vertical-lr; direction: rtl", "aaa <b>&#1488;&#1489;&#1490;</b> bbb <b>&#1488;&#1489;&#1490;</b> ccc")]
    public async Task AnAbsoluteBoxAmongMixedDirectionTextInAVerticalBox_IsPlacedAlongTheColumn(string style, string content)
    {
        var document = await VerticalAsync(style, content);
        var c = Bounds(document, "c");

        if (content.Contains("id=a", StringComparison.Ordinal))
        {
            var a = Bounds(document, "a");
            Assert.True(a.Y >= c.Y - 0.5 && a.Bottom <= c.Bottom + 0.5, $"the box is at {a.Y}..{a.Bottom} in {c.Y}..{c.Bottom}");
        }
        else
        {
            Assert.True(c.Height > 0);
        }
    }

    // --- an absolute child of a flex container ---

    private static async Task<(PeachDrawing.Core.Rect Flex, PeachDrawing.Core.Rect Child)> FlexAsync(string flexStyle, string childStyle = "")
    {
        var document = await LoadAsync("", $"<div id=f style='display: flex; position: relative; width: 100pt; height: 60pt; {flexStyle}'><div id=c style='position: absolute; width: 20pt; height: 10pt; {childStyle}'></div></div>");
        return (Bounds(document, "f"), Bounds(document, "c"));
    }

    [Fact]
    public async Task AnAbsoluteChildOfAFlexContainer_DefaultsToTheStartCorner()
    {
        var (flex, child) = await FlexAsync("");

        Assert.Equal(flex.X, child.X, 1);
        Assert.Equal(flex.Y, child.Y, 1);
    }

    [Fact]
    public async Task JustifyContentCenterAndAlignItemsCenter_CentreAnAbsoluteChildInARow()
    {
        var (flex, child) = await FlexAsync("justify-content: center; align-items: center");

        Assert.Equal(flex.X + 40, child.X, 1);
        Assert.Equal(flex.Y + 25, child.Y, 1);
    }

    [Theory]
    [InlineData("flex-direction: column; justify-content: flex-end; align-items: center", 40, 50)]
    [InlineData("flex-direction: column; justify-content: center; align-items: flex-end", 80, 25)]
    [InlineData("flex-direction: column-reverse; justify-content: flex-start; align-items: flex-start", 0, 50)]
    [InlineData("flex-direction: row; justify-content: flex-end; align-items: flex-end", 80, 50)]
    [InlineData("flex-direction: row; justify-content: right; align-items: end", 80, 50)]
    [InlineData("flex-direction: row; justify-content: left; align-items: start", 0, 0)]
    [InlineData("flex-direction: row; justify-content: space-around; align-items: self-end", 40, 50)]
    [InlineData("flex-direction: column; justify-content: end; align-items: self-start", 0, 50)]
    [InlineData("flex-direction: column; justify-content: right; align-items: start", 0, 0)]
    [InlineData("flex-wrap: wrap-reverse; align-items: flex-start", 0, 50)]
    [InlineData("direction: rtl; justify-content: end", 0, 0)]
    [InlineData("direction: rtl; flex-direction: column; align-items: end", 0, 0)]
    [InlineData("direction: rtl; flex-direction: column; align-items: start", 80, 0)]
    public async Task AnAbsoluteChildOfAFlexContainer_IsJustifiedAlongTheMainAxis_AndAlignedAcrossIt(string style, double left, double top)
    {
        var (flex, child) = await FlexAsync(style);

        Assert.Equal(flex.X + left, child.X, 1);
        Assert.Equal(flex.Y + top, child.Y, 1);
    }

    [Fact]
    public async Task ARowReverse_StartsAtTheRight_AndAnAlignSelfOnTheChildOutranksAlignItems()
    {
        var (flex, child) = await FlexAsync("flex-direction: row-reverse; align-items: flex-end", "align-self: flex-start");

        Assert.Equal(flex.X + 80, child.X, 1);
        Assert.Equal(flex.Y, child.Y, 1);
    }

    [Theory]
    [InlineData("end")]
    [InlineData("safe end")]
    [InlineData("unsafe end")]
    public async Task AnOverflowPositionKeywordInFrontOfEnd_DoesNotChangeWhereThereIsRoom(string alignSelf)
    {
        var (flex, child) = await FlexAsync("", $"align-self: {alignSelf}");

        Assert.Equal(flex.Y + 50, child.Y, 1);
    }

    [Fact]
    public async Task SafeEnd_FallsBackToStart_WhenTheBoxOverflowsTheContainer()
    {
        var (flex, child) = await FlexAsync("", "align-self: safe end; height: 80pt");

        Assert.Equal(flex.Y, child.Y, 1);
    }

    [Fact]
    public async Task AnAxisWithAnInset_IsLeftToThatInset()
    {
        var (flex, child) = await FlexAsync("justify-content: center; align-items: center", "left: 5pt");

        Assert.Equal(flex.X + 5, child.X, 1);
        Assert.Equal(flex.Y + 25, child.Y, 1);
    }

    // --- an absolute table ---

    [Fact]
    public async Task AnAbsoluteTableWithTopAndBottomInsetsAndAutoMargins_IsCentredAtItsContentHeight()
    {
        var document = await LoadAsync("",
            "<div id=cb style='position: relative; width: 100pt; height: 200pt'><div id=t style='display: table; position: absolute; top: 0; bottom: 0; margin: auto; width: 100pt'>"
            + "<div style='width: 100pt; height: 100pt'></div></div></div>");

        var t = Bounds(document, "t");

        Assert.Equal(100, t.Height, 1);
        Assert.Equal(Bounds(document, "cb").Y + 50, t.Y, 1);
    }

    // --- position: relative on the parts of a table ---

    private const string Cells = "<td id=a style='padding: 0'><div id=ad style='width: 50pt; height: 50pt'></div></td><td id=b style='padding: 0'><div style='width: 50pt; height: 50pt'></div></td>";

    [Theory]
    [InlineData("<table style='border-spacing: 0'><tbody id=g {0}><tr id=r>" + Cells + "</tr></tbody></table>")]
    [InlineData("<table style='border-spacing: 0'><tbody><tr id=g {0}>" + Cells + "</tr></tbody></table>")]
    public async Task ARelativeRowGroupOrRow_MovesWithEveryCellItHolds(string template)
    {
        var plain = await LoadAsync("", string.Format(template, ""));
        var shifted = await LoadAsync("", string.Format(template, "style='position: relative; left: 30pt; top: 12pt'"));

        foreach (var id in new[] { "g", "a", "b", "ad" })
        {
            Assert.Equal(Bounds(plain, id).X + 30, Bounds(shifted, id).X, 1);
            Assert.Equal(Bounds(plain, id).Y + 12, Bounds(shifted, id).Y, 1);
        }
    }

    [Fact]
    public async Task ARelativeCell_MovesAloneAndTheOthersStay()
    {
        var plain = await LoadAsync("", "<table style='border-spacing: 0'><tr>" + Cells + "</tr></table>");
        var shifted = await LoadAsync("", "<table style='border-spacing: 0'><tr><td id=a style='padding: 0; position: relative; left: 7pt; top: 3pt'><div id=ad style='width: 50pt; height: 50pt'></div></td>"
                                          + "<td id=b style='padding: 0'><div style='width: 50pt; height: 50pt'></div></td></tr></table>");

        Assert.Equal(Bounds(plain, "a").X + 7, Bounds(shifted, "a").X, 1);
        Assert.Equal(Bounds(plain, "ad").Y + 3, Bounds(shifted, "ad").Y, 1);
        Assert.Equal(Bounds(plain, "b").X, Bounds(shifted, "b").X, 1);
        Assert.Equal(Bounds(plain, "b").Y, Bounds(shifted, "b").Y, 1);
    }

    [Fact]
    public async Task ARelativeRowAndItsRelativeCell_HaveTheirOffsetsAdded()
    {
        var plain = await LoadAsync("", "<table style='border-spacing: 0'><tr>" + Cells + "</tr></table>");
        var shifted = await LoadAsync("", "<table style='border-spacing: 0'><tr style='position: relative; left: 10pt'>"
                                          + "<td id=a style='padding: 0; position: relative; left: 5pt'><div id=ad style='width: 50pt; height: 50pt'></div></td>"
                                          + "<td id=b style='padding: 0'><div style='width: 50pt; height: 50pt'></div></td></tr></table>");

        Assert.Equal(Bounds(plain, "a").X + 15, Bounds(shifted, "a").X, 1);
        Assert.Equal(Bounds(plain, "ad").X + 15, Bounds(shifted, "ad").X, 1);
        Assert.Equal(Bounds(plain, "b").X + 10, Bounds(shifted, "b").X, 1);
    }

    [Fact]
    public async Task ARelativeCaption_MovesAloneAndKeepsTheTableWhereItWas()
    {
        var plain = await LoadAsync("", "<table style='border-spacing: 0'><caption id=cap style='height: 10pt'>x</caption><tr>" + Cells + "</tr></table>");
        var shifted = await LoadAsync("", "<table style='border-spacing: 0'><caption id=cap style='height: 10pt; position: relative; left: 8pt; top: 4pt'>x</caption><tr>" + Cells + "</tr></table>");

        Assert.Equal(Bounds(plain, "cap").X + 8, Bounds(shifted, "cap").X, 1);
        Assert.Equal(Bounds(plain, "cap").Y + 4, Bounds(shifted, "cap").Y, 1);
        Assert.Equal(Bounds(plain, "a").X, Bounds(shifted, "a").X, 1);
        Assert.Equal(Bounds(plain, "a").Y, Bounds(shifted, "a").Y, 1);
    }

    [Fact]
    public async Task APercentageTopOnARow_ResolvesAgainstTheHeightItsRowGroupDeclares()
    {
        var plain = await LoadAsync("", "<table style='border-spacing: 0'><tbody style='height: 20pt'><tr id=r>" + Cells + "</tr></tbody></table>");
        var shifted = await LoadAsync("", "<table style='border-spacing: 0'><tbody style='height: 20pt'><tr id=r style='position: relative; top: 50%'>" + Cells + "</tr></tbody></table>");

        Assert.Equal(Bounds(plain, "r").Y + 10, Bounds(shifted, "r").Y, 1);
    }

    [Fact]
    public async Task ACaptionThatDeclaresAWidth_WidensTheTableToIt()
    {
        var document = await LoadAsync("", "<table style='border-spacing: 0'><caption id=cap style='width: 80pt; height: 10pt'></caption><tr><td id=a style='padding: 0; width: 20pt'><div style='height: 5pt'></div></td></tr></table>");

        Assert.Equal(80, Bounds(document, "cap").Width, 1);
        Assert.True(Bounds(document, "a").Width >= 79, $"the cell is {Bounds(document, "a").Width}pt wide");
    }

    [Fact]
    public async Task AnAbsoluteBoxWhoseContainingBlockIsARelativeRowGroup_IsPlacedAgainstTheGroupWhereItEndsUp()
    {
        const string Box = "<div id=x style='position: absolute; left: 20pt; top: 4pt; width: 5pt; height: 5pt'></div>";
        var plain = await LoadAsync("", $"<table style='border-spacing: 0'><tbody id=g style='position: relative'><tr><td style='padding: 0'>{Box}<div style='width: 50pt; height: 50pt'></div></td></tr></tbody></table>");
        var shifted = await LoadAsync("", $"<table style='border-spacing: 0'><tbody id=g style='position: relative; left: 9pt'><tr><td style='padding: 0'>{Box}<div style='width: 50pt; height: 50pt'></div></td></tr></tbody></table>");

        Assert.Equal(Bounds(plain, "g").X + 20, Bounds(plain, "x").X, 1);
        Assert.Equal(Bounds(plain, "g").Y + 4, Bounds(plain, "x").Y, 1);
        Assert.Equal(Bounds(plain, "x").X + 9, Bounds(shifted, "x").X, 1);
    }

    // --- the columns of a vertical multicol container ---

    private static Task<HtmlDocument> VerticalColumnsAsync(string body) =>
        LoadAsync("body { writing-mode: vertical-lr } .m { column-count: 2; column-fill: auto; column-gap: 0; block-size: 100pt; inline-size: 160pt } .c { background: green; inline-size: 80pt; block-size: 100pt }", body);

    [Fact]
    public async Task SiblingsOfOneClassInAVerticalContainer_AllGetTheirPhysicalSize()
    {
        var document = await VerticalColumnsAsync("<div id=m class=m><div id=a class=c>X</div><div id=b class=c>X</div><div id=c class=c>X</div></div>");
        var m = Bounds(document, "m");

        foreach (var (id, strip) in new[] { ("a", 0), ("b", 1), ("c", 2) })
        {
            var box = Bounds(document, id);
            Assert.Equal(100, box.Width, 1);
            Assert.Equal(80, box.Height, 1);
            Assert.Equal(m.Y + strip * 80, box.Y, 1);
        }
    }

    [Fact]
    public async Task ABlockWithOnlyASize_TallerThanAColumn_ContinuesIntoTheColumnsBelow()
    {
        var document = await VerticalColumnsAsync("<div id=m class=m><div id=t style='inline-size: 80pt; block-size: 600pt'></div></div>");
        var m = Bounds(document, "m");
        var t = Bounds(document, "t");

        Assert.True(t.Bottom >= m.Y + 6 * 80 - 1, $"the box ends at {t.Bottom}, six strips down from {m.Y}");
    }

    // --- sticky boxes held inside a scroll container ---

    private static async Task<(int X0, int Y0, int X1, int Y1)?> RedBoundsAsync(PeachDrawing.RasterRenderContext context, HtmlDocument document)
    {
        var canvas = await TestDocument.PaintAsync(context, document);
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (var y = 0; y < (int)document.Pages[0].HeightPoints; y++)
        {
            for (var x = 0; x < (int)document.Pages[0].WidthPoints; x++)
            {
                var p = TestDocument.Pixel(canvas, x, y);
                if (p.R > 200 && p.G < 60 && p.B < 60)
                {
                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x);
                    y1 = Math.Max(y1, y);
                }
            }
        }

        return x1 < 0 ? null : (x0, y0, x1, y1);
    }

    [Fact]
    public async Task AStickyBoxHeldInsideAClippingContainer_CarriesItsContentWithIt()
    {
        const string Html = "<div style='width:100pt;height:100pt;overflow:hidden'><div style='display:flex;flex-direction:column;height:500pt'>"
            + "<div style='position:sticky;bottom:0;margin-top:auto;width:100pt;height:100pt'><div style='height:100pt;background:red'></div></div></div></div>";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Html), Screen());

        document.SetViewport(document.Viewport);

        Assert.Equal((0, 0, 99, 99), await RedBoundsAsync(context, document));
    }

    [Fact]
    public async Task AStickyBoxWithABottomInset_BelowTheViewport_IsNotPulledUpOutOfItsContainingBlock()
    {
        const string Html = "<div style='height:700pt'></div><div style='height:100pt'><div id=s style='position:sticky;bottom:0;height:100pt;background:red'></div></div>";
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Html), Screen());

        document.SetViewport(document.Viewport);

        Assert.Equal(700, document.GetElementBounds("s")!.Value.Y, 1);
    }
}

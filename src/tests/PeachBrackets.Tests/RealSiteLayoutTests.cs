using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// Layout defects found by rendering real sites (slashdot.org, osnews.com, webkit.org, mad4mod.space) beside Chromium, written against the public API: each page is laid
/// out as a screen document and measured by element id (points: 1px is 0.75pt). The engine's own tests assert on the box tree; these are what a consumer sees.
/// </summary>
public class RealSiteLayoutTests
{
    private const double Px = 0.75;

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string body, string style = "")
    {
        var html = $"<!DOCTYPE html><html><head><style>@page {{ margin: 0 }} html, body {{ margin: 0 }} body {{ font: 13px {TestDocument.Family} }} {style}</style></head><body>{body}</body></html>";
        return await TestDocument.LoadAsync(html, new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 900, ViewportHeightPoints = 842 });
    }

    private static async Task<Rect> BoundsAsync(string body, string id, string style = "")
    {
        var (_, document) = await LoadAsync(body, style);
        using (document)
        {
            return document.GetElementBounds(id)!.Value;
        }
    }

    // ---- parsing and style sheets ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task NoscriptContent_IsParsedOnce_AndLaidOut()
    {
        var bounds = await BoundsAsync("<noscript><p id='p'>inside</p></noscript><div id='d'>after</div>", "p");

        Assert.True(bounds.Height > 0);
    }

    [Fact]
    public async Task ARuleWithItsBraceMissing_DoesNotDropTheRulesAfterIt()
    {
        // slashdot.org's classic.ssl.css: the "{" after the selector list is missing, and the rest of the sheet was lost.
        var bounds = await BoundsAsync("<div id='b'>x</div>", "b", "#editor span,\ndisplay:none !important;\n}\n#dropped { color: red }\n#b { width: 50px; display: block }");

        Assert.Equal(50 * Px, bounds.Width, 0.5);
    }

    // ---- svg ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnSvgWithOnlyAViewBox_TakesTheWidthOfItsBlock_AndTheHeightOfItsRatio()
    {
        var bounds = await BoundsAsync("<div style='width:200px'><svg id='s' viewBox='0 0 2 1' style='display:block'></svg></div>", "s");

        Assert.Equal(200 * Px, bounds.Width, 0.5);
        Assert.Equal(100 * Px, bounds.Height, 0.5);
    }

    [Fact]
    public async Task AnSvgWithNoSizeAtAll_IsTheDefaultSize()
    {
        var bounds = await BoundsAsync("<svg id='s' xmlns='http://www.w3.org/2000/svg' style='display:block'></svg>", "s");

        Assert.Equal(300 * Px, bounds.Width, 0.5);
        Assert.Equal(150 * Px, bounds.Height, 0.5);
    }

    [Theory]
    [InlineData("<svg id='s' width='80' viewBox='0 0 2 1'></svg>", 80, 40)]
    [InlineData("<svg id='s' height='40' viewBox='0 0 2 1'></svg>", 80, 40)]
    [InlineData("<svg id='s' width='80' xmlns='http://www.w3.org/2000/svg'></svg>", 80, 150)]
    public async Task AnSvgWithASizeInOneAxis_TakesTheOtherFromItsViewBox(string svg, double width, double height)
    {
        var bounds = await BoundsAsync($"<div>{svg}</div>", "s");

        Assert.Equal(width * Px, bounds.Width, 0.5);
        Assert.Equal(height * Px, bounds.Height, 0.5);
    }

    [Theory]
    [InlineData("<div style='float:left'><svg id='s' viewBox='0 0 2 1'></svg></div>")]
    [InlineData("<span style='display:inline-block'><svg id='s' viewBox='0 0 2 1'></svg></span>")]
    [InlineData("<div style='display:flex'><div><svg id='s' viewBox='0 0 2 1'></svg></div></div>")]
    public async Task ARatioOnlySvg_InAContextSizedToItsContent_ContributesNoWidth(string html)
    {
        // The box has no width to give it: it is as wide as its content, and an svg with only a ratio is none (it is 0 wide in Chromium and WebKit).
        var bounds = await BoundsAsync(html, "s");

        Assert.True(bounds.Width < 1, $"expected no width, got {bounds.Width}pt");
    }

    [Fact]
    public async Task ARatioOnlySvg_AsAGridItem_FillsItsArea()
    {
        // An item with only an aspect ratio stretches under any alignment (css-grid-2 section 6.2), unlike the contexts sized to their content.
        var bounds = await BoundsAsync("<div style='display:grid;width:200px'><svg id='s' viewBox='0 0 2 1'></svg></div>", "s");

        Assert.Equal(200 * Px, bounds.Width, 0.5);
    }

    [Theory]
    [InlineData("svg { display: block; width: 0; height: 0 }")]
    [InlineData("svg { display: block; width: 0px; height: 0px }")]
    public async Task AnSvgSizedZero_TakesNoRoom(string rule)
    {
        // webkit.org hides its filter definitions this way; read as "no size" the svg was 150px tall and pushed the page down.
        var bounds = await BoundsAsync("<svg xmlns='http://www.w3.org/2000/svg'><filter id='f'/></svg><div id='d' style='height:20px'></div>", "d", rule);

        Assert.Equal(0, bounds.Y, 0.01);
    }

    // ---- floats, relative positioning and shrink-to-fit ----------------------------------------------------------------------------

    [Fact]
    public async Task ARelativelyOffsetFloat_IsMoved_AndWhatFollowsFlowsAgainstItsStaticBox()
    {
        var (_, document) = await LoadAsync(
            "<div id='f' style='float:left;position:relative;left:20px;top:10px;width:30px;height:30px'></div><div id='n' style='float:left;width:30px;height:30px'></div>");
        using (document)
        {
            var moved = document.GetElementBounds("f")!.Value;
            var next = document.GetElementBounds("n")!.Value;

            Assert.Equal(20 * Px, moved.X, 0.5);
            Assert.Equal(10 * Px, moved.Y, 0.5);
            Assert.Equal(30 * Px, next.X, 0.5);
            Assert.Equal(0, next.Y, 0.5);
        }
    }

    [Fact]
    public async Task AShrinkToFitBox_HoldsTheSpaceAfterAnInlineBlock_OnOneLine()
    {
        // `<i class="icon"></i> <span>Software</span>`: the markup's space is part of the max-content width, or the float is a space too narrow and wraps.
        var bounds = await BoundsAsync(
            "<div style='width:400px'><div id='f' style='float:left'><i style='display:inline-block;width:20px;height:10px'></i> <span>Software</span></div></div>", "f");
        var single = await BoundsAsync("<div id='d'>Software</div>", "d");

        Assert.True(bounds.Height < single.Height * 1.5, $"expected one line, got {bounds.Height}pt against {single.Height}pt for a single line");
    }

    [Fact]
    public async Task ARowOfFloatedItems_InAFloatedMenu_StaysOnOneRow()
    {
        // osnews.com: nav and ul are floats, the li items are floats, and a clearing block follows.
        var (_, document) = await LoadAsync(
            "<nav id='n' style='float:left'><ul id='u' class='cf'><li id='a'><a>Home</a></li><li id='b'><a>Originals</a></li></ul></nav>",
            "ul { margin: 0; padding: 0; list-style: none } li { float: left } li a { display: inline-block; padding: 0 25px } .cf:after { content: ' '; display: table; clear: both }");
        using (document)
        {
            var a = document.GetElementBounds("a")!.Value;
            var b = document.GetElementBounds("b")!.Value;
            var nav = document.GetElementBounds("n")!.Value;

            Assert.Equal(a.Y, b.Y, 0.5);
            Assert.Equal(a.Width + b.Width, nav.Width, 0.5);
        }
    }

    [Fact]
    public async Task AFloatedListItem_IsAsWideAsItsText_WithListStyleNone()
    {
        var (_, document) = await LoadAsync(
            "<div id='d' style='float:left;clear:left'>Home</div><ul id='u' style='float:left;clear:left;margin:0;padding:0;list-style:none'><li id='l'>Home</li></ul>");
        using (document)
        {
            Assert.Equal(document.GetElementBounds("d")!.Value.Width, document.GetElementBounds("l")!.Value.Width, 0.2);
        }
    }

    [Fact]
    public async Task ALoneFloatBetweenBlocks_IsStillOneLineAmongTheirs()
    {
        var bounds = await BoundsAsync("<div id='c' style='float:left'><div style='width:50px'>a</div><div style='float:left;width:120px'>b</div><div style='width:80px'>c</div></div>", "c");

        Assert.Equal(120 * Px, bounds.Width, 0.5);
    }

    // ---- inline-level boxes ------------------------------------------------------------------------------------------------------

    private const string BarStyle = ".bar { float: left; width: 1174px; height: 36px } .q { display: inline-block; min-width: 478px; margin-left: 120px; margin-right: 336px } .q blockquote { width: 100%; margin: 0; float: left }";

    [Fact]
    public async Task AnInlineBlocksTopMargin_DoesNotMoveTheFloatBeforeIt()
    {
        var (_, document) = await LoadAsync("<div class='bar' id='f'></div><section class='q' id='q' style='margin-top:19px'><blockquote><p>x</p></blockquote></section>", BarStyle);
        using (document)
        {
            Assert.Equal(0, document.GetElementBounds("f")!.Value.Y, 0.5);
            Assert.Equal((36 + 19) * Px, document.GetElementBounds("q")!.Value.Y, 1);
        }
    }

    [Fact]
    public async Task AnInlineBlockTooWideBesideAFloat_GoesBelowIt_AtItsOwnLeftMargin()
    {
        var bounds = await BoundsAsync("<div class='bar'></div><section class='q' id='q'><blockquote><p>x</p></blockquote></section>", "q", BarStyle);

        Assert.Equal(120 * Px, bounds.X, 0.5);
        Assert.Equal(36 * Px, bounds.Y, 0.5);
    }

    [Fact]
    public async Task ALeftFloatAfterAnInlineBlockItDoesNotFitBeside_GoesBelowTheLine()
    {
        var (_, document) = await LoadAsync(
            "<div class='bar'></div><section class='q' id='q' style='margin-top:19px'><blockquote><p style='margin:0'>x</p></blockquote></section><div id='ft' style='float:left;width:1157px;clear:both'>footer</div>",
            BarStyle);
        using (document)
        {
            var q = document.GetElementBounds("q")!.Value;
            Assert.True(document.GetElementBounds("ft")!.Value.Y >= q.Bottom - 0.5);
        }
    }

    [Fact]
    public async Task ARelativeOffsetOnAnInlineBlock_MovesIt_AndNotTheLine()
    {
        var (_, plain) = await LoadAsync("<div id='d'>a <span id='s' style='display:inline-block;width:60px;height:12px'>ib</span> b</div><div id='after'>x</div>");
        var (_, moved) = await LoadAsync("<div id='d'>a <span id='s' style='position:relative;left:20px;top:10px;display:inline-block;width:60px;height:12px'>ib</span> b</div><div id='after'>x</div>");
        using (plain)
        using (moved)
        {
            var before = plain.GetElementBounds("s")!.Value;
            var after = moved.GetElementBounds("s")!.Value;

            Assert.Equal(before.X + 15, after.X, 0.5);
            Assert.Equal(before.Y + 7.5, after.Y, 0.5);
            Assert.Equal(plain.GetElementBounds("after")!.Value.Y, moved.GetElementBounds("after")!.Value.Y, 0.01);
        }
    }

    [Fact]
    public async Task ARelativeOffsetOnAnInlineBox_MovesIt()
    {
        var (_, plain) = await LoadAsync("<div>a <span id='s'>word</span> b</div>");
        var (_, moved) = await LoadAsync("<div>a <span id='s' style='position:relative;left:20px;top:-3px'>word</span> b</div>");
        using (plain)
        using (moved)
        {
            var before = plain.GetElementBounds("s")!.Value;
            var after = moved.GetElementBounds("s")!.Value;

            Assert.Equal(before.X + 15, after.X, 0.5);
            Assert.Equal(before.Y - 2.25, after.Y, 0.5);
        }
    }

    [Theory]
    [InlineData("<span style='display:inline-block;width:60px;height:30px'></span>")]
    [InlineData("<span style='display:inline-block;width:60px'><div style='height:30px'></div></span>")]
    public async Task AnInlineBlockAloneOnItsLine_HasTheStrutsDescentBelowIt(string content)
    {
        // A 30px box alone on a 13px line is a 33px line in Chromium: the strut hangs below the baseline the box sits on.
        var bounds = await BoundsAsync($"<div id='d'>{content}</div>", "d");

        Assert.True(bounds.Height / Px > 31, $"the line ends at the box's own bottom edge ({bounds.Height / Px}px)");
    }

    [Fact]
    public async Task TheMarginOfAnInlineBoxWhoseFirstWordWrapsToANewLine_StartsThatLine()
    {
        var (_, document) = await LoadAsync(
            "<ul style='margin:0;padding:0;width:200px'>"
            + "<li style='list-style:none;display:inline;margin:0 10px'><a id='w1'>First</a></li>\n<li style='list-style:none;display:inline;margin:0 10px'><a id='w2'>Second</a></li>\n"
            + "<li style='list-style:none;display:inline;margin:0 10px'><a id='w3'>Third</a></li>\n<li style='list-style:none;display:inline;margin:0 10px'><a id='w4'>Fourth</a></li></ul>");
        using (document)
        {
            var first = document.GetElementBounds("w1")!.Value;
            var wrapped = document.GetElementBounds("w4")!.Value;

            Assert.True(wrapped.Y > first.Y + 1, "the fourth item is expected on a second line");
            Assert.Equal(first.X, wrapped.X, 0.5);
        }
    }

    [Fact]
    public async Task RightAlignedInlineBlocks_DoNotOverlap_WhateverOutOfFlowContentTheyHold()
    {
        // webkit.org's navigation: an inline-block holding an absolutely positioned sub-menu was aligned against its container's width and shifted twice.
        var (_, document) = await LoadAsync(
            "<div style='width:600px;text-align:right'><span id='a' style='display:inline-block;padding-left:15px'>Downloads</span> "
            + "<span id='b' style='display:inline-block;padding-left:15px'>Status<i style='position:absolute;top:0;left:0'></i></span> <span id='c' style='display:inline-block'>Docs</span></div>");
        using (document)
        {
            var a = document.GetElementBounds("a")!.Value;
            var b = document.GetElementBounds("b")!.Value;
            var c = document.GetElementBounds("c")!.Value;

            Assert.True(a.Right <= b.X + 0.5, "Downloads overlaps Status");
            Assert.True(b.Right <= c.X + 0.5, "Status overlaps Docs");
            Assert.Equal(600 * Px, c.Right, 0.5);
        }
    }

    // ---- flex items and replaced elements ----------------------------------------------------------------------------------------

    [Fact]
    public async Task AFlexItemShrunkByItsContainer_IsAsTallAsTheContainerLeftIt()
    {
        // mad4mod.space's gallery cards: a figure with `flex: 1 1 auto; min-height: 0; overflow: hidden` holding a photo taller than the card.
        var bounds = await BoundsAsync(
            "<div style='display:flex;flex-direction:column;width:100px;height:100px'><div id='i' style='flex:1 1 auto;min-height:0;overflow:hidden;background:#fc0'><div style='height:400px'></div></div></div>", "i");

        Assert.Equal(100 * Px, bounds.Height, 0.5);
    }

    [Fact]
    public async Task AFlexItemWithADefiniteHeight_IsThatTall_WhateverItsContentDoes()
    {
        var bounds = await BoundsAsync(
            "<div style='display:flex;flex-direction:column'><div id='i' style='height:20pt;background:#cde'><p style='margin:0;height:80pt'>a</p></div></div>", "i");

        Assert.Equal(20, bounds.Height, 0.5);
    }

    // A 2x1 px PNG.
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEklEQVR4nGNkYGD4z8DAwMAAAAcAAQ0Ym6QAAAAASUVORK5CYII=";

    [Fact]
    public async Task AnImageWithPercentageSizes_FillsAFlexFigureTheContainerSized()
    {
        var bounds = await BoundsAsync(
            "<div style='display:flex;flex-direction:column;width:200px;height:220px'><div style='flex:1 1 auto;min-height:0;overflow:hidden'>"
            + $"<img id='i' src='{Png}' style='width:100%;height:100%'></div><div style='flex:none;height:20px'>caption</div></div>", "i");

        Assert.Equal(200 * Px, bounds.Width, 0.5);
        Assert.Equal(200 * Px, bounds.Height, 0.5);
    }

    [Fact]
    public async Task AnImageWithAPercentageWidthAndAPixelHeight_KeepsItsHeight()
    {
        var bounds = await BoundsAsync($"<div style='width:200px'><img id='i' src='{Png}' style='width:100%;height:50px'></div>", "i");

        Assert.Equal(200 * Px, bounds.Width, 0.5);
        Assert.Equal(50 * Px, bounds.Height, 0.5);
    }
}

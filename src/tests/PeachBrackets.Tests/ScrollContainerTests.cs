using PeachBrackets.Updates;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>Scroll containers (CSS Overflow 3 §2): a box with <c>overflow</c> auto, scroll or hidden clips its content to its padding box and scrolls it.</summary>
public class ScrollContainerTests
{
    private static HtmlDocumentOptions Screen() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 300 };

    private const string Page =
        "#s { width: 100pt; height: 100pt; overflow: auto; margin: 10pt } .a { height: 100pt; background: rgb(255,0,0) } .b { height: 100pt; background: rgb(0,255,0) } .c { height: 100pt; background: rgb(0,0,255) }";

    private const string Body = "<div id=s><div class=a id=a></div><div class=b id=b></div><div class=c id=c></div></div><div id=after style=\"height:150pt;background:rgb(255,255,0)\"></div>";

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string css = Page, string body = Body, HtmlDocumentOptions? options = null) =>
        await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>body {{ margin: 0 }} {css}</style>"), options ?? Screen());

    private static async Task<(byte R, byte G, byte B)> PixelAsync(RasterRenderContext context, HtmlDocument document, double x, double y)
    {
        using var canvas = await TestDocument.PaintAsync(context, document);
        var pixel = TestDocument.Pixel(canvas, (int)x, (int)y);
        return (pixel.R, pixel.G, pixel.B);
    }

    private static bool Is((byte R, byte G, byte B) pixel, int r, int g, int b) => Math.Abs(pixel.R - r) < 12 && Math.Abs(pixel.G - g) < 12 && Math.Abs(pixel.B - b) < 12;

    [Fact]
    public async Task AScrollContainer_ClipsItsContent_AndScrollsIt()
    {
        var (context, document) = await LoadAsync();
        var scroller = document.GetElement("s")!;
        Assert.True(document.IsScrollContainer(scroller));

        // At the start it shows its first block, and nothing past its padding box.
        Assert.True(Is(await PixelAsync(context, document, 60, 60), 255, 0, 0));
        Assert.True(Is(await PixelAsync(context, document, 60, 150), 255, 255, 0) || Is(await PixelAsync(context, document, 60, 150), 255, 255, 255));
        Assert.False(Is(await PixelAsync(context, document, 60, 130), 0, 255, 0));

        var regions = document.SetScrollPosition(scroller, 0, 100);
        Assert.NotEmpty(regions);
        Assert.True(Is(await PixelAsync(context, document, 60, 60), 0, 255, 0));
        Assert.False(Is(await PixelAsync(context, document, 60, 130), 0, 0, 255));

        document.SetScrollPosition(scroller, 0, 200);
        Assert.True(Is(await PixelAsync(context, document, 60, 60), 0, 0, 255));
    }

    [Fact]
    public async Task TheScrollPosition_IsKeptWithinWhatTheContentAllows()
    {
        var (_, document) = await LoadAsync();
        var scroller = document.GetElement("s")!;

        var metrics = document.GetScrollMetrics(scroller)!.Value;
        Assert.Equal(200, metrics.MaxY, 1);
        Assert.Equal(0, metrics.MaxX, 1);
        Assert.Equal(300, metrics.ContentHeight, 1);
        Assert.Equal(100, metrics.Viewport.Height, 1);

        document.SetScrollPosition(scroller, 50, 1000);
        var after = document.GetScrollMetrics(scroller)!.Value;
        Assert.Equal(200, after.Y, 1);
        Assert.Equal(0, after.X, 1);

        document.SetScrollPosition(scroller, 0, -30);
        Assert.Equal(0, document.GetScrollMetrics(scroller)!.Value.Y, 1);

        // NaN leaves an axis where it is.
        document.SetScrollPosition(scroller, 0, 80);
        document.SetScrollPosition(scroller, double.NaN, double.NaN);
        Assert.Equal(80, document.GetScrollMetrics(scroller)!.Value.Y, 1);
    }

    [Fact]
    public async Task ScrollBy_MovesFromWhereItIs_AndReportsTheScrollport()
    {
        var (_, document) = await LoadAsync();
        var scroller = document.GetElement("s")!;
        ScrollPositionChangedEventArgs? seen = null;
        document.ScrollPositionChanged += (_, e) => seen = e;

        document.ScrollBy(scroller, 0, 30);
        var regions = document.ScrollBy(scroller, 0, 20);

        Assert.Equal(50, document.GetScrollMetrics(scroller)!.Value.Y, 1);
        Assert.Equal(50, seen!.Y, 1);
        Assert.Equal("s", seen.Element.Id);

        // The scrollport is what needs painting, and only that.
        var port = document.GetScrollMetrics(scroller)!.Value.Viewport;
        var region = Assert.Single(regions);
        Assert.True(region.Bounds.X <= port.X && region.Bounds.Right >= port.Right && region.Bounds.Y <= port.Y && region.Bounds.Bottom >= port.Bottom);
        Assert.True(region.Bounds.Height < port.Height + 10);

        // Nothing moved, so nothing to paint and nothing said.
        seen = null;
        Assert.Empty(document.SetScrollPosition(scroller, 0, 50));
        Assert.Null(seen);
    }

    [Fact]
    public async Task OnlyABoxWithAScrollingOverflow_IsAScrollContainer()
    {
        var (_, document) = await LoadAsync(
            "div { width: 50pt; height: 50pt } .v { overflow: visible } .h { overflow: hidden } .au { overflow: auto } .sc { overflow: scroll } .cl { overflow: clip }",
            "<div class=v id=v><p style=\"height:100pt\">x</p></div><div class=h id=h><p style=\"height:100pt\">x</p></div><div class=au id=au><p style=\"height:100pt\">x</p></div>" +
            "<div class=sc id=sc><p style=\"height:100pt\">x</p></div><div class=cl id=cl><p style=\"height:100pt\">x</p></div>");

        Assert.False(document.IsScrollContainer(document.GetElement("v")!));
        Assert.True(document.IsScrollContainer(document.GetElement("h")!));
        Assert.True(document.IsScrollContainer(document.GetElement("au")!));
        Assert.True(document.IsScrollContainer(document.GetElement("sc")!));
        Assert.False(document.IsScrollContainer(document.GetElement("cl")!));
        Assert.Null(document.GetScrollMetrics(document.GetElement("v")!));
        Assert.Empty(document.SetScrollPosition(document.GetElement("v")!, 0, 10));
    }

    [Fact]
    public async Task APagedDocument_HasNoScrollContainers_AndShowsAllOfTheContent()
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Print };
        options.SetMargins(0);
        var (context, document) = await LoadAsync(body: "<div id=s><div class=a id=a></div><div class=b id=b></div><div class=c id=c></div></div>", options: options);

        Assert.False(document.IsScrollContainer(document.GetElement("s")!));
        Assert.Null(document.GetScrollMetrics(document.GetElement("s")!));

        // Print lets the content of an auto box run on past its box, as it always has.
        var bounds = document.GetElementBounds("c")!.Value;
        var pp = await PixelAsync(context, document, bounds.X + 5, bounds.Y + 5); Assert.True(Is(pp, 0, 0, 255), $"{pp} at {bounds}");
    }

    [Fact]
    public async Task AnElementInsideAScrollContainer_IsWhereItIsDrawn()
    {
        var (_, document) = await LoadAsync();
        var before = document.GetElementBounds("b")!.Value;

        document.SetScrollPosition(document.GetElement("s")!, 0, 60);
        var after = document.GetElementBounds("b")!.Value;

        Assert.Equal(before.Y - 60, after.Y, 1);
        Assert.Equal(before.X, after.X, 1);

        // What follows the container is not moved by it.
        Assert.Equal(document.GetElementBounds("after")!.Value.Y, document.GetElementBounds("after")!.Value.Y, 1);
    }

    [Fact]
    public async Task ScrollIntoView_ScrollsTheContainersThatHoldAnElement()
    {
        var (context, document) = await LoadAsync();

        var regions = document.ScrollIntoView(document.GetElement("c")!);

        Assert.NotEmpty(regions);
        var metrics = document.GetScrollMetrics(document.GetElement("s")!)!.Value;
        Assert.Equal(200, metrics.Y, 1);
        Assert.True(Is(await PixelAsync(context, document, 60, 60), 0, 0, 255));

        // An element that is in view moves nothing.
        Assert.Empty(document.ScrollIntoView(document.GetElement("c")!));
        document.SetScrollPosition(document.GetElement("s")!, 0, 0);
        Assert.Empty(document.ScrollIntoView(document.GetElement("a")!));
    }

    [Fact]
    public async Task TheContainerUnderAPoint_IsTheInnermostThatCanScrollTheWayAsked()
    {
        var (_, document) = await LoadAsync(
            "#outer { width: 150pt; height: 120pt; overflow: auto; margin: 0 } #inner { width: 100pt; height: 60pt; overflow: auto } .t { height: 200pt } .u { height: 40pt }",
            "<div id=outer><div id=inner><div class=t></div></div><div class=t></div></div>");
        var outer = document.GetElement("outer")!;
        var inner = document.GetElement("inner")!;

        Assert.Equal("inner", document.GetScrollContainerAt(0, 20, 20)!.Id);
        Assert.Equal("inner", document.GetScrollContainerAt(0, 20, 20, 0, 1)!.Id);

        // A point past the inner one is only on the outer one.
        Assert.Equal("outer", document.GetScrollContainerAt(0, 20, 100)!.Id);

        // The inner one at the end of its content passes the wheel on to the outer one; at the start it keeps it for a turn toward its end.
        document.SetScrollPosition(inner, 0, 1000);
        Assert.Equal("outer", document.GetScrollContainerAt(0, 20, 20, 0, 1)!.Id);
        Assert.Equal("inner", document.GetScrollContainerAt(0, 20, 20, 0, -1)!.Id);

        document.SetScrollPosition(outer, 0, 1000);
        Assert.Null(document.GetScrollContainerAt(0, 20, 20, 0, 1));

        // Nowhere near any of them.
        Assert.Null(document.GetScrollContainerAt(0, 380, 280));
    }

    [Fact]
    public async Task ANestedScrollContainer_ClipsToItsOwnPortInsideTheOuterOne()
    {
        var (context, document) = await LoadAsync(
            "#outer { width: 120pt; height: 100pt; overflow: auto } #inner { width: 100pt; height: 50pt; overflow: auto; margin-top: 60pt } .r { height: 100pt; background: rgb(255,0,0) } .g { height: 100pt; background: rgb(0,255,0) }",
            "<div id=outer><div id=inner><div class=r></div><div class=g></div></div><div style=\"height:100pt;background:rgb(0,0,255)\"></div></div>");

        // Scrolling the outer one carries the inner one with its clip.
        document.SetScrollPosition(document.GetElement("outer")!, 0, 40);
        var inner = document.GetElementBounds("inner")!.Value;
        Assert.True(Is(await PixelAsync(context, document, inner.X + 20, inner.Y + 20), 255, 0, 0));
        Assert.False(Is(await PixelAsync(context, document, inner.X + 20, inner.Y + inner.Height + 8), 255, 0, 0));

        // And the inner one scrolls inside it.
        document.SetScrollPosition(document.GetElement("inner")!, 0, 100);
        var px = await PixelAsync(context, document, inner.X + 20, inner.Y + 20); Assert.True(Is(px, 0, 255, 0), $"{px} inner={inner} metrics={document.GetScrollMetrics(document.GetElement("inner")!)}");
    }

    [Fact]
    public async Task APointerHitsWhatIsDrawnUnderIt_AndNotWhatTheScrollportClips()
    {
        var (_, document) = await LoadAsync(
            "#s { width: 100pt; height: 100pt; overflow: auto } .p { height: 100pt }",
            "<div id=s><div class=p></div><div class=p><a id=l href=\"#x\" style=\"display:block;height:100pt\">link</a></div></div><div style=\"height:200pt\"></div>");
        var scroller = document.GetElement("s")!;

        // The link is below the scrollport and clipped away.
        Assert.NotEqual(CursorShape.Pointer, document.GetCursorAt(0, 50, 50).Shape);
        Assert.NotEqual(CursorShape.Pointer, document.GetCursorAt(0, 50, 150).Shape);

        // Scrolled to it, the pointer is over it where it is drawn, and not over the place it was laid out at.
        document.SetScrollPosition(scroller, 0, 100);
        Assert.Equal(CursorShape.Pointer, document.GetCursorAt(0, 50, 50).Shape);

        // Below the scrollport the link is still clipped, though the box of the link is there when it is laid out.
        document.SetScrollPosition(scroller, 0, 0);
        Assert.NotEqual(CursorShape.Pointer, document.GetCursorAt(0, 50, 150).Shape);
    }

    [Fact]
    public async Task AScrollContainerThatOverflows_DrawsAThumbWhereItIsScrolledTo()
    {
        var (context, document) = await LoadAsync();
        var scroller = document.GetElement("s")!;
        var port = document.GetScrollMetrics(scroller)!.Value.Viewport;
        var barX = port.Right - 5;

        // At the start the thumb is at the top of the right edge, over the red.
        var top = await PixelAsync(context, document, barX, port.Y + 15);
        var bottom = await PixelAsync(context, document, barX, port.Bottom - 5);
        Assert.True(top.R < 200 && bottom.R > top.R + 40, $"top {top}, bottom {bottom}");

        // Scrolled to the end it is at the bottom, over the blue.
        document.SetScrollPosition(scroller, 0, 200);
        var end = await PixelAsync(context, document, barX, port.Bottom - 8);
        var start = await PixelAsync(context, document, barX, port.Y + 15);
        Assert.True(end.B < 200 && start.B > end.B + 40, $"end {end}, start {start}");
    }

    [Fact]
    public async Task AnAutoContainerWithNothingToScrollTo_DrawsNoScrollbar_AndOverflowHiddenNever()
    {
        var (context, document) = await LoadAsync(
            "#s { width: 100pt; height: 100pt; overflow: auto } #h { width: 100pt; height: 100pt; overflow: hidden } .a { height: 50pt; background: rgb(255,0,0) } .t { height: 300pt; background: rgb(255,0,0) }",
            "<div id=s><div class=a></div></div><div id=h><div class=t></div></div>");

        var s = document.GetElementBounds("s")!.Value;
        var h = document.GetElementBounds("h")!.Value;

        // The short content leaves white beside nothing; the bar would be at the right edge, over the red of the block.
        var shortContent = await PixelAsync(context, document, s.Right - 5, s.Y + 25);
        Assert.True(shortContent.R > 240 && shortContent.G < 12, $"{shortContent}");

        // overflow: hidden scrolls by script and shows no bar, though it holds more than it shows.
        var hidden = await PixelAsync(context, document, h.Right - 5, h.Y + 20);
        Assert.True(hidden.R > 240 && hidden.G < 12, $"{hidden}");
    }

    // The animations on a scroll container's own timeline (Scroll-driven Animations 1): they follow how far it is scrolled, not the document.
    private static HtmlDocumentOptions Animating() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = 300, AnimationsEnabled = true };

    private const string Fade = "@keyframes fade { from { opacity: 0 } to { opacity: 1 } } @keyframes tint { from { background: rgb(255,0,0) } to { background: rgb(0,0,255) } } ";

    private static async Task<double> OpacityOfAsync(RasterRenderContext context, HtmlDocument document, string id)
    {
        var box = document.GetElementBounds(id)!.Value;
        var pixel = await PixelAsync(context, document, box.X + 3, box.Y + 3);
        return 1 - (pixel.G / 255.0);
    }

    [Fact]
    public async Task ANamedScrollTimelineOfAScrollContainer_DrivesAnElementOutsideIt()
    {
        var (context, document) = await LoadAsync(
            Fade + "body { timeline-scope: --box } #s { width: 100pt; height: 100pt; overflow: auto; scroll-timeline: --box; margin: 0 } .t { height: 300pt } " +
            "#out { width: 50pt; height: 20pt; background: rgb(255,0,0); animation: fade linear both; animation-timeline: --box }",
            "<div id=s><div class=t></div></div><div id=out></div>", Animating());
        var scroller = document.GetElement("s")!;
        document.AdvanceAnimations(TimeSpan.Zero);

        // 0 at the start, half-way through the 200pt it can scroll, 1 at the end.
        Assert.InRange(await OpacityOfAsync(context, document, "out"), 0, 0.05);
        document.SetScrollPosition(scroller, 0, 100);
        Assert.True(document.HasRunningAnimations);
        document.AdvanceAnimations(TimeSpan.FromSeconds(1));
        Assert.InRange(await OpacityOfAsync(context, document, "out"), 0.4, 0.6);

        document.SetScrollPosition(scroller, 0, 200);
        document.AdvanceAnimations(TimeSpan.FromSeconds(2));
        Assert.InRange(await OpacityOfAsync(context, document, "out"), 0.97, 1.0);
        Assert.False(document.AdvanceAnimations(TimeSpan.FromSeconds(3)).IsRunning);
    }

    [Fact]
    public async Task ScrollSelf_FollowsTheContainersOwnScrolling()
    {
        var (context, document) = await LoadAsync(
            Fade + "#s { width: 100pt; height: 100pt; overflow: auto; animation: tint linear both; animation-timeline: scroll(self) } .t { width: 50pt; height: 300pt }",
            "<div id=s><div class=t></div></div>", Animating());
        var scroller = document.GetElement("s")!;
        var box = document.GetElementBounds("s")!.Value;
        document.AdvanceAnimations(TimeSpan.Zero);

        // Beside the content (which is narrower than the container) the container's own background shows.
        Assert.True((await PixelAsync(context, document, box.X + 70, box.Y + 20)).R > 240);
        document.SetScrollPosition(scroller, 0, 220);
        document.AdvanceAnimations(TimeSpan.FromSeconds(1));
        Assert.True((await PixelAsync(context, document, box.X + 70, box.Y + 20)).B > 240);
    }

    [Fact]
    public async Task AViewTimelineInsideAScrollContainer_FollowsWhereTheElementIsInItsScrollport()
    {
        var (context, document) = await LoadAsync(
            Fade + "#s { width: 100pt; height: 100pt; overflow: auto; margin: 0 } .pad { height: 150pt } #v { height: 20pt; background: rgb(255,0,0); animation: fade linear both; animation-timeline: view() } .after { height: 300pt }",
            "<div id=s><div class=pad></div><div id=v></div><div class=after></div></div>", Animating());
        var scroller = document.GetElement("s")!;
        document.AdvanceAnimations(TimeSpan.Zero);

        // The element is 20pt tall at 150: it covers the scrollport from a scroll of 50 to one of 170, and is half-way through at 110.
        document.SetScrollPosition(scroller, 0, 110);
        document.AdvanceAnimations(TimeSpan.FromSeconds(1));
        Assert.InRange(await OpacityOfAsync(context, document, "v"), 0.4, 0.6);

        document.SetScrollPosition(scroller, 0, 60);
        document.AdvanceAnimations(TimeSpan.FromSeconds(2));
        Assert.InRange(await OpacityOfAsync(context, document, "v"), 0.0, 0.15);
    }
}

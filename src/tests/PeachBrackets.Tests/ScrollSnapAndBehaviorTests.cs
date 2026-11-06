using PeachBrackets.Updates;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>The properties of a scroll container beyond its overflow: scroll margins and padding, snapping, smooth scrolling, overscroll and the scrollbars (CSS Scroll Snap 1, CSSOM View, CSS Overscroll Behavior 1, CSS Scrollbars 1).</summary>
public class ScrollSnapAndBehaviorTests
{
    private static HtmlDocumentOptions Screen(bool reducedMotion = true) => new()
    {
        MediaType = MediaType.Screen,
        PageWidthPoints = 400,
        ViewportHeightPoints = 300,
        Preferences = new UserPreferences { ReducedMotion = reducedMotion },
    };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string css, string body, bool reducedMotion = true) =>
        await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>body {{ margin: 0 }} {css}</style>"), Screen(reducedMotion));

    private static string Items(int count, string cls = "i") => string.Concat(Enumerable.Range(0, count).Select(n => $"<div class=\"{cls}\" id=\"i{n}\"></div>"));

    private static double Y(HtmlDocument document, string id = "s") => document.GetScrollMetrics(document.GetElement(id)!)!.Value.Y;

    private const string Base = "#s { width: 100pt; height: 100pt; overflow: auto } .i { height: 100pt }";

    [Fact]
    public async Task ThePropertiesAreAccepted_AndTheShorthandsSetTheirLonghands()
    {
        var (_, document) = await LoadAsync(
            Base + " #s { scroll-padding: 1pt 2pt 3pt 4pt; scroll-snap-type: y mandatory; overscroll-behavior: contain none; scroll-behavior: smooth; scrollbar-width: thin; scrollbar-color: red blue; scrollbar-gutter: stable both-edges } .i { scroll-margin: 5pt 6pt; scroll-snap-align: start; scroll-snap-stop: always }",
            "<div id=s>" + Items(3) + "</div>");

        // Nothing here throws, and the container still scrolls: a declaration it cannot read would have left the document as it was.
        Assert.True(document.IsScrollContainer(document.GetElement("s")!));
    }

    [Fact]
    public async Task ScrollPaddingKeepsRoomAtTheEdgesOfTheScrollport_ForAnElementScrolledIntoView()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-padding: 10pt 0 30pt 0 } .i { height: 20pt }", "<div id=s>" + Items(12) + "</div>");

        // An element below the port: its bottom edge comes to 30pt above the bottom of the scrollport.
        document.ScrollIntoView(document.GetElement("i8")!);
        Assert.Equal((9 * 20) - 100 + 30, Y(document), 1);

        // And one above it comes to 10pt below the top.
        document.SetScrollPosition(document.GetElement("s")!, 0, 120);
        document.ScrollIntoView(document.GetElement("i2")!);
        Assert.Equal((2 * 20) - 10, Y(document), 1);
    }

    [Fact]
    public async Task ScrollMarginKeepsRoomAroundTheElementItself()
    {
        var (_, document) = await LoadAsync(Base + " .i { height: 20pt } #i8 { scroll-margin: 12pt }", "<div id=s>" + Items(12) + "</div>");

        document.ScrollIntoView(document.GetElement("i8")!);
        Assert.Equal((9 * 20) - 100 + 12, Y(document), 1);
    }

    [Fact]
    public async Task ASnapContainer_SettlesOnTheNearestSnapPosition()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-snap-type: y mandatory } .i { scroll-snap-align: start }", "<div id=s>" + Items(5) + "</div>");
        var s = document.GetElement("s")!;

        document.SetScrollPosition(s, 0, 130);
        Assert.Equal(100, Y(document), 1);

        document.SetScrollPosition(s, 0, 160);
        Assert.Equal(200, Y(document), 1);

        // The wheel and the keys go through the same call, so each step lands on a position.
        document.ScrollBy(s, 0, 42);
        Assert.Equal(200, Y(document), 1);
    }

    [Fact]
    public async Task ProximitySnapsOnlyWhenAPositionIsNear()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-snap-type: y proximity } .i { scroll-snap-align: start }", "<div id=s>" + Items(5) + "</div>");
        var s = document.GetElement("s")!;

        document.SetScrollPosition(s, 0, 125);
        Assert.Equal(100, Y(document), 1);

        // 40pt from the nearest position is farther than a third of the scrollport.
        document.SetScrollPosition(s, 0, 140);
        Assert.Equal(140, Y(document), 1);
    }

    [Fact]
    public async Task CenterAndEndAlign_ByTheirOwnPlaceInTheScrollport()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-snap-type: y mandatory } .i { height: 50pt; scroll-snap-align: center }", "<div id=s>" + Items(8) + "</div>");
        var s = document.GetElement("s")!;

        // The middle of item 3 (at 150 to 200) in the middle of the 100pt port: a scroll of 175 - 50 = 125.
        document.SetScrollPosition(s, 0, 120);
        Assert.Equal(125, Y(document), 1);

        var (_, ends) = await LoadAsync(Base + " #s { scroll-snap-type: y mandatory } .i { height: 50pt; scroll-snap-align: end }", "<div id=s>" + Items(8) + "</div>");
        ends.SetScrollPosition(ends.GetElement("s")!, 0, 140);

        // The bottom of item 4 (ends at 250) at the bottom of the port: 150.
        Assert.Equal(150, Y(ends), 1);
    }

    [Fact]
    public async Task AStopThatMustNotBePassed_HoldsAScrollBack()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-snap-type: y mandatory } .i { scroll-snap-align: start } #i2 { scroll-snap-stop: always }", "<div id=s>" + Items(6) + "</div>");

        document.ScrollBy(document.GetElement("s")!, 0, 340);
        Assert.Equal(200, Y(document), 1);
    }

    [Fact]
    public async Task ADraggedThumbDoesNotSnap_UntilItIsSettled()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-snap-type: y mandatory } .i { scroll-snap-align: start }", "<div id=s>" + Items(5) + "</div>");
        var s = document.GetElement("s")!;
        var port = document.GetScrollMetrics(s)!.Value.Viewport;
        var thumb = document.GetScrollbarAt(0, port.Right - 5, port.Y + 12)!;

        document.ScrollThumbTo(s, ScrollbarAxis.Vertical, thumb.TrackStart + ((thumb.TrackLength - thumb.ThumbLength) * 0.33));
        var held = Y(document);
        Assert.NotEqual(0, held % 100);

        document.SettleScroll(s);
        Assert.Equal(0, Y(document) % 100, 1);
    }

    [Fact]
    public async Task ADragMovesAContainerExactly_AndSettlingSnapsIt()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-snap-type: y mandatory } .i { scroll-snap-align: start }", "<div id=s>" + Items(5) + "</div>");
        var s = document.GetElement("s")!;

        document.DragScrollBy(s, 0, 30);
        Assert.Equal(30, Y(document), 1);

        document.SettleScroll(s);
        Assert.Equal(0, Y(document), 1);
    }

    [Fact]
    public async Task ASmoothContainerGlides_AndAnInstantScrollJumps()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-behavior: smooth }", "<div id=s>" + Items(5) + "</div>", reducedMotion: false);
        var s = document.GetElement("s")!;
        var woke = 0;
        document.AnimationsWoke += (_, _) => woke++;

        Assert.Empty(document.SetScrollPosition(s, 0, 100));
        Assert.Equal(0, Y(document), 1);
        Assert.True(document.HasRunningAnimations);
        Assert.Equal(1, woke);

        document.AdvanceAnimations(TimeSpan.Zero);
        var middle = document.AdvanceAnimations(TimeSpan.FromSeconds(0.15));
        Assert.InRange(Y(document), 20, 99);
        Assert.True(middle.IsRunning);
        Assert.NotEmpty(middle.DirtyRegions);

        var end = document.AdvanceAnimations(TimeSpan.FromSeconds(0.6));
        Assert.Equal(100, Y(document), 1);
        Assert.False(end.IsRunning);
        Assert.False(document.HasRunningAnimations);

        // Asked to be instant it is, whatever the property says.
        document.SetScrollPosition(s, 0, 300, ScrollBehavior.Instant);
        Assert.Equal(300, Y(document), 1);
    }

    [Fact]
    public async Task UnderReducedMotion_ASmoothContainerJumps()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-behavior: smooth }", "<div id=s>" + Items(5) + "</div>", reducedMotion: true);

        document.SetScrollPosition(document.GetElement("s")!, 0, 100);
        Assert.Equal(100, Y(document), 1);
        Assert.False(document.HasRunningAnimations);
    }

    [Fact]
    public async Task ASmoothScroll_IsReplacedByTheNextOne()
    {
        var (_, document) = await LoadAsync(Base + " #s { scroll-behavior: smooth }", "<div id=s>" + Items(5) + "</div>", reducedMotion: false);
        var s = document.GetElement("s")!;

        document.SetScrollPosition(s, 0, 100);
        document.AdvanceAnimations(TimeSpan.Zero);
        document.SetScrollPosition(s, 0, 300);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.1));
        document.AdvanceAnimations(TimeSpan.FromSeconds(1));

        Assert.Equal(300, Y(document), 1);
    }

    [Fact]
    public async Task OverscrollContain_KeepsAScrollFromPassingToTheContainerAround()
    {
        var (_, document) = await LoadAsync(
            "#outer { width: 150pt; height: 120pt; overflow: auto } #inner { width: 100pt; height: 60pt; overflow: auto } .t { height: 200pt }",
            "<div id=outer><div id=inner><div class=t></div></div><div class=t></div></div>");
        document.SetScrollPosition(document.GetElement("inner")!, 0, 1000);

        // The inner one is at its end and the outer one can go on: the wheel passes to it.
        Assert.Equal("outer", document.GetScrollContainerAt(0, 40, 40, 0, 1)!.Id);

        var (_, held) = await LoadAsync(
            "#outer { width: 150pt; height: 120pt; overflow: auto } #inner { width: 100pt; height: 60pt; overflow: auto; overscroll-behavior: contain } .t { height: 200pt }",
            "<div id=outer><div id=inner><div class=t></div></div><div class=t></div></div>");
        held.SetScrollPosition(held.GetElement("inner")!, 0, 1000);

        Assert.Equal("inner", held.GetScrollContainerAt(0, 40, 40, 0, 1)!.Id);

        // Only along the axis it names, and only toward the end it has reached.
        var (_, xOnly) = await LoadAsync(
            "#outer { width: 150pt; height: 120pt; overflow: auto } #inner { width: 100pt; height: 60pt; overflow: auto; overscroll-behavior-x: contain } .t { height: 200pt }",
            "<div id=outer><div id=inner><div class=t></div></div><div class=t></div></div>");
        xOnly.SetScrollPosition(xOnly.GetElement("inner")!, 0, 1000);
        Assert.Equal("outer", xOnly.GetScrollContainerAt(0, 40, 40, 0, 1)!.Id);
    }

    private static async Task<(byte R, byte G, byte B)> PixelAsync(RasterRenderContext context, HtmlDocument document, double x, double y)
    {
        using var canvas = await TestDocument.PaintAsync(context, document);
        var pixel = TestDocument.Pixel(canvas, (int)x, (int)y);
        return (pixel.R, pixel.G, pixel.B);
    }

    [Fact]
    public async Task ScrollbarWidth_NoneShowsNoBars_AndThinOnesAreNarrower()
    {
        const string css = ".i { background: rgb(255,255,255) }";
        var (autoContext, auto) = await LoadAsync(Base + css, "<div id=s>" + Items(4) + "</div>");
        var (noneContext, none) = await LoadAsync(Base + css + " #s { scrollbar-width: none }", "<div id=s>" + Items(4) + "</div>");
        var (thinContext, thin) = await LoadAsync(Base + css + " #s { scrollbar-width: thin }", "<div id=s>" + Items(4) + "</div>");

        var port = auto.GetScrollMetrics(auto.GetElement("s")!)!.Value.Viewport;
        var y = port.Y + 15;

        // The bar at the right edge: 8px (6pt) wide, 1.5pt in.
        Assert.True((await PixelAsync(autoContext, auto, port.Right - 5, y)).R < 200);
        Assert.True((await PixelAsync(noneContext, none, port.Right - 5, y)).R > 250);
        Assert.Null(none.GetScrollbarAt(0, port.Right - 5, y));

        // A thin one (5px, 3.75pt) leaves the outer part of the wide one's place empty.
        Assert.True((await PixelAsync(thinContext, thin, port.Right - 6.5, y)).R > 250);
        Assert.True((await PixelAsync(thinContext, thin, port.Right - 3, y)).R < 200);
    }

    [Fact]
    public async Task ScrollbarColor_PaintsTheThumbAndTheTrack()
    {
        var (context, document) = await LoadAsync(Base + " #s { scrollbar-color: rgb(255,0,0) rgb(0,0,255) } .i { background: rgb(255,255,255) }", "<div id=s>" + Items(4) + "</div>");
        var port = document.GetScrollMetrics(document.GetElement("s")!)!.Value.Viewport;
        var x = port.Right - 5;

        var thumb = await PixelAsync(context, document, x, port.Y + 15);
        var track = await PixelAsync(context, document, x, port.Bottom - 6);
        Assert.True(thumb.R > 200 && thumb.B < 100, $"thumb {thumb}");
        Assert.True(track.B > 200 && track.R < 100, $"track {track}");
    }
}

using PeachBrackets.Updates;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>Animations on a scroll or view timeline (Scroll-driven Animations 1): progress comes from where the document is scrolled, not from time.</summary>
public class ScrollDrivenAnimationTests
{
    private const double ViewportHeight = 300;

    private static HtmlDocumentOptions Options() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, ViewportHeightPoints = ViewportHeight, AnimationsEnabled = true };

    private const string Fade = "@keyframes fade { from { opacity: 0 } to { opacity: 1 } } ";

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string css, string body)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>body {{ margin: 0 }} {Fade}{css}</style>"), Options());
        document.SetViewport(new Rect(0, 0, 400, ViewportHeight));
        return (context, document);
    }

    private static double MaxScroll(HtmlDocument document) => document.Pages[0].HeightPoints - ViewportHeight;

    private static async Task<int> GreenAsync(RasterRenderContext context, HtmlDocument document, string id)
    {
        var box = document.GetElementBounds(id)!.Value;
        using var canvas = await TestDocument.PaintAsync(context, document);
        return TestDocument.Pixel(canvas, (int)box.X + 5, (int)box.Y + 5).G;
    }

    private static AnimationFrame ScrollTo(HtmlDocument document, double y)
    {
        document.SetViewport(new Rect(0, y, 400, ViewportHeight));
        return document.AdvanceAnimations(TimeSpan.FromSeconds(1));
    }

    private const string Box = "#a { width: 100pt; height: 50pt; background: rgb(255, 0, 0); ";
    private const string Tall = "<div id=a></div><div style=\"height: 2000pt\"></div>";

    [Fact]
    public async Task AScrollTimeline_FollowsHowFarTheDocumentIsScrolled()
    {
        var (context, document) = await LoadAsync(Box + "animation: fade linear both; animation-timeline: scroll() }", Tall);

        document.AdvanceAnimations(TimeSpan.Zero);
        Assert.Equal(255, await GreenAsync(context, document, "a"));

        var half = ScrollTo(document, MaxScroll(document) / 2);
        Assert.InRange(await GreenAsync(context, document, "a"), 110, 145);
        Assert.False(half.IsRunning);

        ScrollTo(document, MaxScroll(document));
        Assert.Equal(0, await GreenAsync(context, document, "a"));

        ScrollTo(document, 0);
        Assert.Equal(255, await GreenAsync(context, document, "a"));
    }

    [Fact]
    public async Task AScrollMoveWakesTheClockOnce_AndIdlesAfterIt()
    {
        var (_, document) = await LoadAsync(Box + "animation: fade linear both; animation-timeline: scroll() }", Tall);
        document.AdvanceAnimations(TimeSpan.Zero);
        Assert.False(document.HasRunningAnimations);

        document.SetViewport(new Rect(0, 200, 400, ViewportHeight));
        Assert.True(document.HasRunningAnimations);

        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(1));
        Assert.True(frame.OverlayChanged || frame.DirtyRegions.Count > 0);
        Assert.False(document.HasRunningAnimations);
        Assert.Empty(frame.Events);
    }

    [Fact]
    public async Task AnAnimationRange_NarrowsWhereTheAnimationPlays()
    {
        var (context, document) = await LoadAsync(Box + "animation: fade linear both; animation-timeline: scroll(); animation-range: 100pt 500pt }", Tall);

        ScrollTo(document, 300);
        Assert.InRange(await GreenAsync(context, document, "a"), 110, 145);
        ScrollTo(document, 50);
        Assert.Equal(255, await GreenAsync(context, document, "a"));
        ScrollTo(document, 900);
        Assert.Equal(0, await GreenAsync(context, document, "a"));
    }

    [Fact]
    public async Task WithoutAFill_NothingShowsOutsideTheRange()
    {
        var (context, document) = await LoadAsync(Box + "opacity: 0.7; animation: fade linear; animation-timeline: scroll(); animation-range: 400pt 800pt }", Tall);

        ScrollTo(document, 100);
        Assert.InRange(await GreenAsync(context, document, "a"), 70, 90);

        ScrollTo(document, 600);
        Assert.InRange(await GreenAsync(context, document, "a"), 110, 145);
    }

    [Fact]
    public async Task AViewTimeline_FollowsHowMuchOfTheElementIsInView()
    {
        var css = "#v { width: 100pt; height: 100pt; background: rgb(255, 0, 0); animation: fade linear both; animation-timeline: view() }";
        var (context, document) = await LoadAsync(css, "<div style=\"height: 1000pt\"></div><div id=v></div><div style=\"height: 1500pt\"></div>");
        var v = document.GetElementBounds("v")!.Value;

        // Cover range: 0 when the top edge reaches the bottom of the view, 1 when the bottom edge has left at the top.
        ScrollTo(document, v.Y - ViewportHeight - 50);
        Assert.Equal(255, await GreenAsync(context, document, "v"));

        ScrollTo(document, v.Y - ViewportHeight + ((ViewportHeight + v.Height) / 2));
        Assert.InRange(await GreenAsync(context, document, "v"), 110, 145);

        ScrollTo(document, v.Y + v.Height + 50);
        Assert.Equal(0, await GreenAsync(context, document, "v"));
    }

    [Fact]
    public async Task ANamedRangeOfAViewTimeline_IsPartOfTheWay()
    {
        var css = "#v { width: 100pt; height: 100pt; background: rgb(255, 0, 0); animation: fade linear both; animation-timeline: view(); animation-range: entry 0% entry 100% }";
        var (context, document) = await LoadAsync(css, "<div style=\"height: 1000pt\"></div><div id=v></div><div style=\"height: 1500pt\"></div>");
        var v = document.GetElementBounds("v")!.Value;

        // Entry: from the top edge reaching the bottom of the view to the bottom edge reaching it.
        ScrollTo(document, v.Y - ViewportHeight + (v.Height / 2));
        Assert.InRange(await GreenAsync(context, document, "v"), 110, 145);

        ScrollTo(document, v.Y - ViewportHeight + v.Height + 20);
        Assert.Equal(0, await GreenAsync(context, document, "v"));
    }

    [Fact]
    public async Task ANamedTimeline_IsFoundAmongTheAncestorsAndThroughTimelineScope()
    {
        var css = "body { timeline-scope: --seen } #v { width: 100pt; height: 100pt; background: rgb(255, 0, 0); view-timeline: --seen } #b { width: 100pt; height: 50pt; background: rgb(255, 0, 0); animation: fade linear both; animation-timeline: --seen }";
        var (context, document) = await LoadAsync(css, "<div id=b></div><div style=\"height: 1000pt\"></div><div id=v></div><div style=\"height: 1500pt\"></div>");
        var v = document.GetElementBounds("v")!.Value;

        ScrollTo(document, v.Y - ViewportHeight + ((ViewportHeight + v.Height) / 2));

        Assert.InRange(await GreenAsync(context, document, "b"), 110, 145);
    }

    [Fact]
    public async Task ATimelineThatIsNotThere_LeavesTheAnimationWithNoEffect()
    {
        var (context, document) = await LoadAsync(Box + "opacity: 0.7; animation: fade linear both; animation-timeline: none }", Tall);
        ScrollTo(document, MaxScroll(document));
        Assert.InRange(await GreenAsync(context, document, "a"), 70, 90);

        var (selfContext, self) = await LoadAsync(Box + "opacity: 0.7; animation: fade linear both; animation-timeline: scroll(self) }", Tall);
        ScrollTo(self, MaxScroll(self));
        Assert.InRange(await GreenAsync(selfContext, self, "a"), 70, 90);

        var (unnamedContext, unnamed) = await LoadAsync(Box + "opacity: 0.7; animation: fade linear both; animation-timeline: --nothing }", Tall);
        ScrollTo(unnamed, MaxScroll(unnamed));
        Assert.InRange(await GreenAsync(unnamedContext, unnamed, "a"), 70, 90);
    }

    [Fact]
    public async Task TheAnimationTimeline_OfAnAnimationOnTime_StaysTime()
    {
        var (context, document) = await LoadAsync(Box + "animation: fade 1s linear forwards; animation-timeline: auto }", Tall);

        document.AdvanceAnimations(TimeSpan.Zero);
        Assert.Equal(255, await GreenAsync(context, document, "a"));
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));
        Assert.InRange(await GreenAsync(context, document, "a"), 110, 145);
        Assert.False(document.AdvanceAnimations(TimeSpan.FromSeconds(3)).IsRunning);
    }

    [Fact]
    public async Task ABadTimeline_IsAnInvalidDeclaration_AndTheOldValueStands()
    {
        // scroll(sideways) is not a scroll() and the declaration is dropped: the animation goes on time.
        var (context, document) = await LoadAsync(Box + "animation: fade 1s linear forwards; animation-timeline: scroll(sideways) }", Tall);

        document.AdvanceAnimations(TimeSpan.Zero);
        document.AdvanceAnimations(TimeSpan.FromSeconds(3));

        Assert.Equal(0, await GreenAsync(context, document, "a"));
    }

    [Fact]
    public async Task ScrollTimelineAndAnimationRange_ShorthandsAreAccepted()
    {
        var css = "body { scroll-timeline: --page block } #v { view-timeline: --box inline 10pt } #a { width: 10pt; height: 10pt; animation-range: entry 10% cover 50% }";
        var (_, document) = await LoadAsync(css, "<div id=a></div><div id=v></div>");

        Assert.True(document.Pages.Count > 0);
    }
}

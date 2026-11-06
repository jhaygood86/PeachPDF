using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// Changing an element's class or attributes, or the user's preferences, on a loaded document: the rules that read them match again, and a property that
/// has a different value afterwards runs to it when the element's <c>transition-*</c> names it.
/// </summary>
public class MutationTests
{
    private static HtmlDocumentOptions Options(bool animate = true) =>
        new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = animate, EnableIncrementalUpdates = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, Canvas Measuring)> LoadAsync(string css, string body, bool animate = true)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{css}</style>"), Options(animate));
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static Rect Bounds(HtmlDocument document, string id) => document.GetElementBounds(id)!.Value;

    private static async Task<(byte R, byte G, byte B, byte A)> PixelAtAsync(RasterRenderContext context, HtmlDocument document, int x, int y)
    {
        using var canvas = await TestDocument.PaintAsync(context, document);
        return TestDocument.Pixel(canvas, x, y);
    }

    [Fact]
    public async Task AddingAndRemovingAClass_MatchesTheRulesAgain()
    {
        var (context, document, measuring) = await LoadAsync("#a { width: 100pt; height: 100pt; background: rgb(255, 0, 0) } #a.on { background: rgb(0, 0, 255) }", "<div id=a></div>", animate: false);
        using var _ = measuring;
        var a = document.GetElement("a")!;

        var added = await document.AddClassAsync(a, "on", measuring);
        Assert.True(added.Applied);
        Assert.Equal("on", document.GetAttribute(a, "class"));
        Assert.Equal(255, (await PixelAtAsync(context, document, 50, 50)).B);

        var removed = await document.RemoveClassAsync(document.GetElement("a")!, "on", measuring);
        Assert.True(removed.Applied);
        Assert.Null(document.GetAttribute(document.GetElement("a")!, "class"));
        Assert.Equal(255, (await PixelAtAsync(context, document, 50, 50)).R);
    }

    [Fact]
    public async Task ToggleClass_FlipsItEachTime_AndSayingTheSameThingTwiceChangesNothing()
    {
        var (_, document, measuring) = await LoadAsync("#a { width: 100pt; height: 20pt } #a.wide { width: 300pt }", "<div id=a></div>", animate: false);
        using var _ = measuring;
        var a = document.GetElement("a")!;

        Assert.True((await document.ToggleClassAsync(a, "wide", measuring)).Applied);
        Assert.Equal(300, Bounds(document, "a").Width, 1);
        Assert.False((await document.AddClassAsync(document.GetElement("a")!, "wide", measuring)).Applied);
        Assert.True((await document.ToggleClassAsync(document.GetElement("a")!, "wide", measuring)).Applied);
        Assert.Equal(100, Bounds(document, "a").Width, 1);
    }

    [Fact]
    public async Task AnAttributeSelector_FollowsSetAttribute_AndAChangeSurvivesALaterReload()
    {
        var (_, document, measuring) = await LoadAsync(
            "#a { width: 100pt; height: 20pt } #a[data-state=open] { width: 250pt } #b { width: 50pt; height: 20pt } #b:hover { margin-top: 10pt }", "<div id=a></div><div id=b></div>", animate: false);
        using var _ = measuring;

        await document.SetAttributeAsync(document.GetElement("a")!, "data-state", "open", measuring);
        Assert.Equal(250, Bounds(document, "a").Width, 1);

        // A hover rule that moves something loads the document again; the attribute the host set is still there.
        var b = Bounds(document, "b");
        await document.SetPointerStateAsync(0, b.X + 5, b.Y + 5, false, measuring);
        Assert.Equal(250, Bounds(document, "a").Width, 1);
        Assert.Equal("open", document.GetAttribute(document.GetElement("a")!, "data-state"));

        await document.RemoveAttributeAsync(document.GetElement("a")!, "data-state", measuring);
        Assert.Equal(100, Bounds(document, "a").Width, 1);
    }

    [Fact]
    public async Task AClassChange_RunsAPaintTransitionToTheNewValue()
    {
        var (context, document, measuring) = await LoadAsync("#a { width: 100pt; height: 100pt; background: rgb(255, 0, 0); opacity: 1; transition: opacity 1s linear } #a.off { opacity: 0 }", "<div id=a></div>");
        using var _ = measuring;
        document.AdvanceAnimations(TimeSpan.Zero);

        await document.AddClassAsync(document.GetElement("a")!, "off", measuring);
        Assert.True(document.HasRunningAnimations);

        document.AdvanceAnimations(TimeSpan.FromSeconds(0.1));
        Assert.InRange((await PixelAtAsync(context, document, 50, 50)).G, 0, 60);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.6));
        Assert.InRange((await PixelAtAsync(context, document, 50, 50)).G, 90, 190);
        Assert.False(document.AdvanceAnimations(TimeSpan.FromSeconds(3)).IsRunning);
        Assert.Equal(255, (await PixelAtAsync(context, document, 50, 50)).G);
    }

    [Fact]
    public async Task AClassChange_RunsALayoutTransition_WhenTheHostLaysOutEachFrame()
    {
        var (_, document, measuring) = await LoadAsync("#a { width: 100pt; height: 20pt; transition: width 1s linear } #a.wide { width: 300pt }", "<div id=a></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);

        await document.AddClassAsync(document.GetElement("a")!, "wide", measuring);
        Assert.Equal(100, Bounds(document, "a").Width, 1);

        var frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);
        Assert.True(frame.LayoutChanged);
        Assert.Equal(200, Bounds(document, "a").Width, 1);

        Assert.False((await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(3), measuring)).IsRunning);
        Assert.Equal(300, Bounds(document, "a").Width, 1);
    }

    [Fact]
    public async Task AHoverThatChangesLayout_RunsItsTransition_ThroughTheReload()
    {
        var (_, document, measuring) = await LoadAsync("#a { width: 100pt; height: 20pt; transition: width 1s linear } #a:hover { width: 300pt }", "<div id=a></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);

        var a = Bounds(document, "a");
        var result = await document.SetPointerStateAsync(0, a.X + 5, a.Y + 5, false, measuring);
        Assert.True(result.Applied);
        Assert.Equal(100, Bounds(document, "a").Width, 1);

        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);
        Assert.Equal(200, Bounds(document, "a").Width, 1);
    }

    [Fact]
    public async Task AShorthandInTransitionProperty_CoversItsLonghands()
    {
        var (_, document, measuring) = await LoadAsync("#a { width: 100pt; height: 20pt; transition: margin 1s linear } #a.shift { margin-top: 40pt }", "<div id=a></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var before = Bounds(document, "a").Y;

        await document.AddClassAsync(document.GetElement("a")!, "shift", measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        Assert.Equal(before + 20, Bounds(document, "a").Y, 1);
    }

    [Fact]
    public async Task AChangeWithNoTransition_SnapsToTheNewValue()
    {
        var (_, document, measuring) = await LoadAsync("#a { width: 100pt; height: 20pt } #a.wide { width: 300pt }", "<div id=a></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);

        await document.AddClassAsync(document.GetElement("a")!, "wide", measuring);

        Assert.Equal(300, Bounds(document, "a").Width, 1);
        Assert.False(document.HasRunningAnimations && (await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.1), measuring)).IsRunning);
    }

    [Fact]
    public async Task SettingThePreferences_MatchesTheMediaRulesAgain()
    {
        var (context, document, measuring) = await LoadAsync(
            "#a { width: 100pt; height: 100pt; background: rgb(255, 0, 0) } @media (prefers-color-scheme: dark) { #a { background: rgb(0, 0, 255) } }", "<div id=a></div>", animate: false);
        using var _ = measuring;
        Assert.Equal(255, (await PixelAtAsync(context, document, 50, 50)).R);

        var result = await document.SetPreferencesAsync(null, ColorSchemePreference.Dark, measuring);

        Assert.True(result.Applied);
        Assert.NotEmpty(result.DirtyRegions);
        Assert.Equal(255, (await PixelAtAsync(context, document, 50, 50)).B);
        Assert.False((await document.SetPreferencesAsync(null, ColorSchemePreference.Dark, measuring)).Applied);
    }

    [Fact]
    public async Task ABadName_IsRefused_AndADocumentWithNoSourceIsNotChanged()
    {
        var (_, document, measuring) = await LoadAsync("#a { height: 20pt }", "<div id=a></div>", animate: false);
        using var _ = measuring;
        var a = document.GetElement("a")!;

        await Assert.ThrowsAsync<ArgumentException>(async () => await document.AddClassAsync(a, "two words", measuring));
        await Assert.ThrowsAsync<ArgumentException>(async () => await document.SetAttributeAsync(a, " ", "x", measuring));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await document.AddClassAsync(null!, "x", measuring));
    }

    [Fact]
    public async Task ATransitionWithNoDurationButADelay_HoldsTheOldValueThroughTheDelay()
    {
        var (context, document, measuring) = await LoadAsync(
            "#a { width: 100pt; height: 100pt; background: rgb(255, 0, 0); opacity: 1; transition: opacity 0s 1s } #a.on { opacity: 0.2 }", "<div id=a></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        await document.AddClassAsync(document.GetElement("a")!, "on", measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.1), measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.6), measuring);
        Assert.Equal(0, (await PixelAtAsync(context, document, 50, 50)).G);

        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(1.5), measuring);
        Assert.InRange((await PixelAtAsync(context, document, 50, 50)).G, 190, 215);
    }

    [Fact]
    public async Task AClassChangeBeforeTheFirstStep_StillRunsALayoutTransition_WhenTheHostThenLaysOut()
    {
        var (_, document, measuring) = await LoadAsync("#a { width: 100pt; height: 20pt; transition: width 1s linear } #a.wide { width: 300pt }", "<div id=a></div>");
        using var _ = measuring;

        await document.AddClassAsync(document.GetElement("a")!, "wide", measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        Assert.InRange(Bounds(document, "a").Width, 150, 250);
    }

    [Fact]
    public async Task AClassChangeBeforeTheFirstStep_EndsAtOnce_WhenTheHostStepsWithNoCanvas()
    {
        var (_, document, measuring) = await LoadAsync("#a { width: 100pt; height: 20pt; transition: width 1s linear } #a.wide { width: 300pt }", "<div id=a></div>");
        using var _ = measuring;

        await document.AddClassAsync(document.GetElement("a")!, "wide", measuring);
        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.1));

        Assert.False(frame.IsRunning);
        Assert.False(document.HasRunningAnimations);
    }

    [Fact]
    public async Task VisibilityCollapseOnATableRow_LaysTheTableOutAgain()
    {
        var (_, document, measuring) = await LoadAsync(
            "table { border-collapse: collapse } td { height: 30pt; padding: 0 } tr.c { visibility: collapse }",
            "<table id=t><tr><td>a</td></tr><tr id=r><td>b</td></tr></table>", animate: false);
        using var _ = measuring;
        var before = Bounds(document, "t").Height;

        await document.AddClassAsync(document.GetElement("r")!, "c", measuring);

        Assert.True(Bounds(document, "t").Height < before - 10, $"{before} -> {Bounds(document, "t").Height}");
    }
}

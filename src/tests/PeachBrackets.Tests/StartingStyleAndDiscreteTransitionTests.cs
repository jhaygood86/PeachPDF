using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary><c>@starting-style</c>, <c>transition-behavior: allow-discrete</c> with <c>display</c>, and how a transition is interrupted (CSS Transitions 1 and 2).</summary>
public class StartingStyleAndDiscreteTransitionTests
{
    private static HtmlDocumentOptions Options(bool animate = true) =>
        new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = animate, EnableIncrementalUpdates = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, Canvas Measuring)> LoadAsync(string css, string body, bool animate = true)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{css}</style>"), Options(animate));
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static Rect Bounds(HtmlDocument document, string id) => document.GetElementBounds(id)!.Value;

    private static async Task<byte> GreenAsync(RasterRenderContext context, HtmlDocument document, int x, int y)
    {
        using var canvas = await TestDocument.PaintAsync(context, document);
        return TestDocument.Pixel(canvas, x, y).G;
    }

    private const string Box = "width: 100pt; height: 100pt; background: rgb(255, 0, 0)";

    [Fact]
    public async Task AStartingStyle_IsNotPartOfTheOrdinaryStyle()
    {
        var (context, document, measuring) = await LoadAsync($"#a {{ {Box}; opacity: 1 }} @starting-style {{ #a {{ opacity: 0 }} }}", "<div id=a></div>", animate: false);
        using var _ = measuring;

        Assert.Equal(0, await GreenAsync(context, document, 50, 50));
    }

    [Fact]
    public async Task AStartingStyle_IsWhatATransitionRunsFromWhenTheDocumentAppears()
    {
        var (context, document, measuring) = await LoadAsync($"#a {{ {Box}; opacity: 1; transition: opacity 1s linear }} @starting-style {{ #a {{ opacity: 0 }} }}", "<div id=a></div>");
        using var _ = measuring;

        document.AdvanceAnimations(TimeSpan.Zero);
        Assert.Equal(255, await GreenAsync(context, document, 50, 50));

        document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));
        Assert.InRange(await GreenAsync(context, document, 50, 50), 100, 160);

        Assert.False(document.AdvanceAnimations(TimeSpan.FromSeconds(3)).IsRunning);
        Assert.Equal(0, await GreenAsync(context, document, 50, 50));
    }

    [Fact]
    public async Task AStartingStyleNestedInARule_IsTheSame()
    {
        var (context, document, measuring) = await LoadAsync($"#a {{ {Box}; opacity: 1; transition: opacity 1s linear; @starting-style {{ opacity: 0 }} }}", "<div id=a></div>");
        using var _ = measuring;

        document.AdvanceAnimations(TimeSpan.Zero);
        Assert.Equal(255, await GreenAsync(context, document, 50, 50));
        document.AdvanceAnimations(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await GreenAsync(context, document, 50, 50));
    }

    [Fact]
    public async Task AnElementShownAgain_RunsFromItsStartingStyle()
    {
        var css = $"#a {{ {Box}; opacity: 1; transition: opacity 1s linear }} #a.hide {{ display: none }} @starting-style {{ #a {{ opacity: 0 }} }}";
        var (context, document, measuring) = await LoadAsync(css, "<div id=a class=hide></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);

        await document.RemoveClassAsync(document.GetElement("a")!, "hide", measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        Assert.InRange(await GreenAsync(context, document, 50, 50), 100, 160);
    }

    [Fact]
    public async Task ADiscreteProperty_OnlyTransitionsWithAllowDiscrete()
    {
        var css = "#a { width: 100pt; height: 20pt; transition: display 1s linear } #a.gone { display: none } #b { width: 100pt; height: 10pt }";
        var (_, document, measuring) = await LoadAsync(css, "<div id=a></div><div id=b></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var before = Bounds(document, "b").Y;

        await document.AddClassAsync(document.GetElement("a")!, "gone", measuring);

        // normal behavior: display is a step, and it snapped.
        Assert.Equal(before - 20, Bounds(document, "b").Y, 1);
    }

    [Fact]
    public async Task Display_WithAllowDiscrete_KeepsTheBoxForTheWholeTransition_AndThenRemovesIt()
    {
        var css = "#a { width: 100pt; height: 20pt; transition: display 1s linear allow-discrete } #a.gone { display: none } #b { width: 100pt; height: 10pt }";
        var (_, document, measuring) = await LoadAsync(css, "<div id=a></div><div id=b></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var before = Bounds(document, "b").Y;

        await document.AddClassAsync(document.GetElement("a")!, "gone", measuring);
        Assert.Equal(before, Bounds(document, "b").Y, 1);

        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);
        Assert.Equal(before, Bounds(document, "b").Y, 1);

        var end = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(3), measuring);
        Assert.False(end.IsRunning);
        Assert.Equal(before - 20, Bounds(document, "b").Y, 1);
    }

    [Fact]
    public async Task AChangeBackToWhereATransitionStarted_ReversesIt_OverThePartAlreadyTravelled()
    {
        var css = "#a { width: 100pt; height: 20pt; transition: width 1s linear } #a.wide { width: 300pt }";
        var (_, document, measuring) = await LoadAsync(css, "<div id=a></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);

        await document.AddClassAsync(document.GetElement("a")!, "wide", measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.25), measuring);
        Assert.Equal(150, Bounds(document, "a").Width, 1);

        // Back to where it started, a quarter of the way: the way back takes a quarter of the duration, not all of it.
        await document.RemoveClassAsync(document.GetElement("a")!, "wide", measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.375), measuring);
        Assert.Equal(125, Bounds(document, "a").Width, 1);

        var end = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);
        Assert.Equal(100, Bounds(document, "a").Width, 1);
        Assert.False(end.IsRunning);
    }

    [Fact]
    public async Task ARestyleThatLeavesTheTarget_DoesNotRestartARunningTransition()
    {
        var css = "#a { width: 100pt; height: 20pt; transition: width 1s linear } #a.wide { width: 300pt } #a.tall { height: 40pt; transition: width 1s linear }";
        var (_, document, measuring) = await LoadAsync(css, "<div id=a></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);

        await document.AddClassAsync(document.GetElement("a")!, "wide", measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);
        Assert.Equal(200, Bounds(document, "a").Width, 1);

        // An unrelated change reloads the document; the width transition is on its way to the same 300pt and goes on, instead of starting again from 200pt.
        await document.AddClassAsync(document.GetElement("a")!, "tall", measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.75), measuring);
        Assert.Equal(250, Bounds(document, "a").Width, 1);
    }

    [Fact]
    public async Task TransitionBehavior_IsAcceptedInTheLonghandAndTheShorthand()
    {
        var css = "#a { width: 100pt; height: 20pt; transition: width 1s allow-discrete; } #b { width: 100pt; height: 20pt; transition-behavior: allow-discrete, normal }";
        var (_, document, measuring) = await LoadAsync(css, "<div id=a></div><div id=b></div>", animate: false);
        using var _ = measuring;

        Assert.True(document.Pages.Count > 0);
    }
}

using PeachBrackets.Updates;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>The animation and transition events a step reports, and the Web Animations-style control of the animations running on a document.</summary>
public class AnimationEventAndControlTests
{
    private static HtmlDocumentOptions Options() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = true, EnableIncrementalUpdates = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, Canvas Measuring)> LoadAsync(string css, string body = "<div id=a></div>")
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{css}</style>"), Options());
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static double Width(HtmlDocument document) => document.GetElementBounds("a")!.Value.Width;

    private static string Describe(IEnumerable<AnimationEventInfo> events) =>
        string.Join(", ", events.Select(e => $"{e.Kind}:{e.Name}@{e.ElapsedTime.TotalSeconds:0.##}"));

    private const string Fade = "@keyframes fade { from { opacity: 1 } to { opacity: 0 } } #a { width: 50pt; height: 10pt; background: red; ";
    private const string Grow = "@keyframes grow { from { width: 0pt } to { width: 100pt } } #a { height: 10pt; width: 50pt; ";

    [Fact]
    public async Task AnAnimation_ReportsItsStart_EachIteration_AndItsEnd()
    {
        var (_, document, measuring) = await LoadAsync(Fade + "animation: fade 1s linear 2 }");
        using var _ = measuring;

        Assert.Equal("AnimationStart:fade@0", Describe(document.AdvanceAnimations(TimeSpan.Zero).Events));
        Assert.Empty(document.AdvanceAnimations(TimeSpan.FromSeconds(0.5)).Events);
        Assert.Equal("AnimationIteration:fade@1", Describe(document.AdvanceAnimations(TimeSpan.FromSeconds(1.2)).Events));
        Assert.Equal("AnimationEnd:fade@2", Describe(document.AdvanceAnimations(TimeSpan.FromSeconds(3)).Events));
        Assert.Empty(document.AdvanceAnimations(TimeSpan.FromSeconds(4)).Events);
    }

    [Fact]
    public async Task ADelayedAnimation_StartsWhenItsDelayIsOver_AndANegativeDelayReportsTheTimeAlreadyGone()
    {
        var (_, delayed, measuring) = await LoadAsync(Fade + "animation: fade 1s linear 0.5s }");
        using var _ = measuring;
        Assert.Empty(delayed.AdvanceAnimations(TimeSpan.Zero).Events);
        Assert.Equal("AnimationStart:fade@0", Describe(delayed.AdvanceAnimations(TimeSpan.FromSeconds(0.6)).Events));

        var (_, early, measuringEarly) = await LoadAsync(Fade + "animation: fade 1s linear -0.5s }");
        using var __ = measuringEarly;
        Assert.Equal("AnimationStart:fade@0.5", Describe(early.AdvanceAnimations(TimeSpan.Zero).Events));
    }

    [Fact]
    public async Task AnAnimationTheElementStopsNaming_IsCancelled()
    {
        var (_, document, measuring) = await LoadAsync(Fade + "animation: fade 4s linear } #a.off { animation: none }");
        using var _ = measuring;
        document.AdvanceAnimations(TimeSpan.Zero);
        document.AdvanceAnimations(TimeSpan.FromSeconds(1));

        await document.AddClassAsync(document.GetElement("a")!, "off", measuring);

        Assert.Contains("AnimationCancel:fade@1", Describe(document.AdvanceAnimations(TimeSpan.FromSeconds(1.1)).Events));
    }

    [Fact]
    public async Task AnAnimationThatGoesOnThroughAReload_DoesNotStartAgain()
    {
        var (_, document, measuring) = await LoadAsync(Fade + "animation: fade 4s linear } #b { width: 10pt; height: 10pt } #b:hover { margin-top: 5pt }", "<div id=a></div><div id=b></div>");
        using var _ = measuring;
        Assert.Contains("AnimationStart", Describe(document.AdvanceAnimations(TimeSpan.Zero).Events));

        var b = document.GetElementBounds("b")!.Value;
        await document.SetPointerStateAsync(0, b.X + 2, b.Y + 2, false, measuring);

        Assert.DoesNotContain("AnimationStart", Describe(document.AdvanceAnimations(TimeSpan.FromSeconds(0.5)).Events));
    }

    [Fact]
    public async Task ATransition_ReportsRun_Start_AndEnd_AndACancelWhenItIsReplaced()
    {
        var (_, document, measuring) = await LoadAsync("#a { width: 100pt; height: 20pt; opacity: 1; transition: opacity 1s linear } #a.off { opacity: 0 } #a.half { opacity: 0.5 }");
        using var _ = measuring;
        document.AdvanceAnimations(TimeSpan.Zero);

        await document.AddClassAsync(document.GetElement("a")!, "off", measuring);
        Assert.Equal("TransitionRun:opacity@0, TransitionStart:opacity@0", Describe(document.AdvanceAnimations(TimeSpan.FromSeconds(0.1)).Events));

        await document.AddClassAsync(document.GetElement("a")!, "half", measuring);
        var replaced = Describe(document.AdvanceAnimations(TimeSpan.FromSeconds(0.2)).Events);
        Assert.Contains("TransitionCancel:opacity", replaced);
        Assert.Contains("TransitionRun:opacity@0", replaced);

        Assert.EndsWith("TransitionEnd:opacity@1", Describe(document.AdvanceAnimations(TimeSpan.FromSeconds(3)).Events));
    }

    [Fact]
    public async Task AnimationsOfTheDocument_AreListed_WithTheirNameAndState()
    {
        var (_, document, measuring) = await LoadAsync(Grow + "animation: grow 1s linear forwards }");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);

        var animation = Assert.Single(document.GetAnimations());

        Assert.Equal("grow", animation.Name);
        Assert.False(animation.IsTransition);
        Assert.Equal(AnimationPlayState.Running, animation.PlayState);
        Assert.Equal("a", animation.Element.Id);
        Assert.Single(document.GetAnimations(document.GetElement("a")!));
    }

    [Fact]
    public async Task Pause_HoldsTheAnimation_AndPlayLetsItGoOn()
    {
        var (_, document, measuring) = await LoadAsync(Grow + "animation: grow 1s linear forwards }");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var animation = document.GetAnimations()[0];

        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.25), measuring);
        animation.Pause();
        Assert.Equal(AnimationPlayState.Paused, animation.PlayState);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.75), measuring);
        Assert.Equal(25, Width(document), 1);

        animation.Play();
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(1.0), measuring);
        Assert.Equal(50, Width(document), 1);
    }

    [Fact]
    public async Task SettingCurrentTime_Seeks_EvenWhilePaused()
    {
        var (_, document, measuring) = await LoadAsync(Grow + "animation: grow 1s linear forwards }");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var animation = document.GetAnimations()[0];
        animation.Pause();

        animation.CurrentTime = TimeSpan.FromSeconds(0.75);
        Assert.True(document.HasRunningAnimations);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.1), measuring);

        Assert.Equal(75, Width(document), 1);
        Assert.Equal(0.75, animation.CurrentTime.TotalSeconds, 2);
    }

    [Fact]
    public async Task APlaybackRate_ScalesTheTimeThatPasses_AndReverseTurnsItAround()
    {
        var (_, document, measuring) = await LoadAsync(Grow + "animation: grow 1s linear forwards }");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var animation = document.GetAnimations()[0];

        animation.PlaybackRate = 2;
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.25), measuring);
        Assert.Equal(50, Width(document), 1);

        animation.Reverse();
        Assert.Equal(-2, animation.PlaybackRate);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.375), measuring);
        Assert.Equal(25, Width(document), 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => animation.PlaybackRate = 0);
    }

    [Fact]
    public async Task Finish_GoesToTheEnd_AndTheFinishedTaskCompletes()
    {
        var (_, document, measuring) = await LoadAsync(Grow + "animation: grow 1s linear forwards }");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var animation = document.GetAnimations()[0];

        animation.Finish();
        var frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.016), measuring);

        Assert.Equal(100, Width(document), 1);
        Assert.Contains(frame.Events, e => e.Kind == AnimationEventKind.AnimationEnd);
        Assert.True(animation.Finished.IsCompletedSuccessfully);
        Assert.Equal(AnimationPlayState.Finished, animation.PlayState);
    }

    [Fact]
    public async Task Finish_OfAnAnimationThatNeverEnds_IsRefused()
    {
        var (_, document, measuring) = await LoadAsync(Grow + "animation: grow 1s linear infinite }");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);

        Assert.Throws<InvalidOperationException>(() => document.GetAnimations()[0].Finish());
    }

    [Fact]
    public async Task Cancel_RemovesTheEffect_ReportsIt_AndCancelsTheFinishedTask()
    {
        var (_, document, measuring) = await LoadAsync(Grow + "animation: grow 1s linear forwards }");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var animation = document.GetAnimations()[0];
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);
        Assert.Equal(50, Width(document), 1);

        animation.Cancel();
        var frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.6), measuring);

        Assert.Equal(50, Width(document), 1);
        Assert.Contains(frame.Events, e => e.Kind == AnimationEventKind.AnimationCancel);
        Assert.Equal(AnimationPlayState.Idle, animation.PlayState);
        Assert.True(animation.Finished.IsCanceled);
        Assert.Empty(document.GetAnimations());
    }

    [Fact]
    public async Task ATransition_IsListedAndCanBeCancelled_WhichLeavesTheNewValue()
    {
        var (_, document, measuring) = await LoadAsync("#a { width: 100pt; height: 20pt; transition: width 1s linear } #a.wide { width: 300pt }");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        await document.AddClassAsync(document.GetElement("a")!, "wide", measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.25), measuring);

        var transition = Assert.Single(document.GetAnimations());
        Assert.True(transition.IsTransition);
        Assert.Equal("width", transition.Name);

        transition.Cancel();
        var frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.3), measuring);

        Assert.Equal(300, Width(document), 1);
        Assert.Contains(frame.Events, e => e.Kind == AnimationEventKind.TransitionCancel);
        Assert.True(transition.Finished.IsCanceled);
    }

    [Fact]
    public async Task TheObjectForAnAnimation_StaysItself_WhenTheDocumentIsLoadedAgain()
    {
        var (_, document, measuring) = await LoadAsync(Grow + "animation: grow 4s linear forwards } #b { width: 10pt; height: 10pt } #b:hover { margin-top: 5pt }", "<div id=a></div><div id=b></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var animation = document.GetAnimations()[0];
        animation.PlaybackRate = 2;

        var b = document.GetElementBounds("b")!.Value;
        await document.SetPointerStateAsync(0, b.X + 2, b.Y + 2, false, measuring);

        Assert.Equal(AnimationPlayState.Running, animation.PlayState);
        Assert.Equal(2, animation.PlaybackRate);
        Assert.Same(animation, document.GetAnimations()[0]);
        Assert.Equal("a", animation.Element.Id);
    }
}

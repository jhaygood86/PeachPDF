using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// Animations of properties that move or resize boxes (<c>width</c>, <c>margin</c>, <c>font-size</c>, ...): a step that changes one lays the document out again,
/// so the geometry of every frame is what a document written with that value would have.
/// </summary>
public class LayoutAnimationPlaybackTests
{
    private static HtmlDocumentOptions Options() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, Canvas Measuring)> LoadAsync(string css, string body)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{css}</style>"), Options());
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static Rect Bounds(HtmlDocument document, string id) => document.GetElementBounds(id)!.Value;

    [Fact]
    public async Task AWidthAnimation_ResizesTheBoxEachFrame_AndSaysTheLayoutChanged()
    {
        var (_, document, measuring) = await LoadAsync("@keyframes grow { from { width: 100pt } to { width: 300pt } } #a { height: 20pt; background: red; animation: grow 1s linear forwards }", "<div id=a></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        Assert.Equal(100, Bounds(document, "a").Width, 1);

        var frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);
        Assert.True(frame.LayoutChanged);
        Assert.True(frame.IsRunning);
        Assert.Equal(200, Bounds(document, "a").Width, 1);
        Assert.NotEmpty(frame.DirtyRegions);

        frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(2), measuring);
        Assert.False(frame.IsRunning);
        Assert.Equal(300, Bounds(document, "a").Width, 1);
    }

    [Fact]
    public async Task AHeightAnimation_MovesTheBoxesAfterIt()
    {
        var (_, document, measuring) = await LoadAsync("@keyframes tall { from { height: 50pt } to { height: 150pt } } #a { width: 100pt; animation: tall 1s linear forwards } #b { width: 100pt; height: 10pt }", "<div id=a></div><div id=b></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var before = Bounds(document, "b").Y;
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        Assert.Equal(before + 50, Bounds(document, "b").Y, 1);
    }

    [Fact]
    public async Task AWidthAnimation_RepaintsOnlyWhereBoxesMoved()
    {
        var (_, document, measuring) = await LoadAsync("@keyframes grow { from { width: 100pt } to { width: 300pt } } #a { height: 20pt; background: red; animation: grow 1s linear forwards } #b { width: 100pt; height: 600pt }", "<div id=a></div><div id=b></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        var a = Bounds(document, "a");
        Assert.True(frame.LayoutChanged);
        Assert.NotEmpty(frame.DirtyRegions);
        Assert.All(frame.DirtyRegions, region =>
        {
            Assert.True(region.Bounds.Bottom < a.Bottom + 20, $"{region.Bounds} reaches below the animated box {a}");
            Assert.True(region.Bounds.Right >= a.Right - 1, $"{region.Bounds} does not cover the new width of {a}");
        });
    }

    [Fact]
    public async Task AHeightAnimation_RepaintsTheBoxesItPushedDown()
    {
        var (_, document, measuring) = await LoadAsync("@keyframes tall { from { height: 50pt } to { height: 150pt } } #a { width: 100pt; animation: tall 1s linear forwards } #b { width: 100pt; height: 10pt; background: blue }", "<div id=a></div><div id=b></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        var b = Bounds(document, "b");
        Assert.Contains(frame.DirtyRegions, region => region.Bounds.Top <= b.Y && region.Bounds.Bottom >= b.Bottom);
    }

    [Fact]
    public async Task AMarginAnimation_ShiftsItsOwnBox()
    {
        var (_, document, measuring) = await LoadAsync("@keyframes push { from { margin-top: 0pt } to { margin-top: 80pt } } #a { width: 50pt; height: 20pt; animation: push 1s linear forwards }", "<div id=a></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var before = Bounds(document, "a").Y;
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        Assert.Equal(before + 40, Bounds(document, "a").Y, 1);
    }

    [Fact]
    public async Task AFontSizeAnimation_ReachesTheBoxesThatInheritIt()
    {
        var (_, document, measuring) = await LoadAsync("@keyframes size { from { font-size: 10pt } to { font-size: 30pt } } #a { animation: size 1s linear forwards } #c { width: 10em; height: 1em }", "<div id=a><div id=c></div></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        Assert.Equal(100, Bounds(document, "c").Width, 1);

        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        var c = Bounds(document, "c");
        Assert.Equal(200, c.Width, 1);
        Assert.Equal(20, c.Height, 1);
    }

    [Fact]
    public async Task WithoutACanvas_LayoutPropertiesStayWhereTheCascadeLeftThem()
    {
        var (_, document, measuring) = await LoadAsync("@keyframes grow { from { width: 100pt } to { width: 300pt } } #a { width: 100pt; height: 20pt; animation: grow 1s linear forwards }", "<div id=a></div>");
        using var _ = measuring;

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.False(frame.LayoutChanged);
        Assert.Equal(100, Bounds(document, "a").Width, 1);
    }

    [Fact]
    public async Task APaintAnimationBesideALayoutOne_StillPlaysBoth()
    {
        var (context, document, measuring) = await LoadAsync("@keyframes combo { from { width: 100pt; opacity: 1 } to { width: 200pt; opacity: 0 } } #a { height: 50pt; background: rgb(255, 0, 0); animation: combo 1s linear forwards }", "<div id=a></div>");
        using var _ = measuring;

        var first = await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var second = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        Assert.True(Math.Abs(Bounds(document, "a").Width - 150) < 1, $"t0 changed={first.LayoutChanged} t.5 changed={second.LayoutChanged} width={Bounds(document, "a").Width}");
        using var canvas = await TestDocument.PaintAsync(context, document);
        Assert.InRange(TestDocument.Pixel(canvas, 50, 25).G, 100, 160);
    }
}

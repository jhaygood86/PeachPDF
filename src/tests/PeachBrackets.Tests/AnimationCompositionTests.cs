using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary><c>animation-composition</c>, and the <c>-webkit-</c> spellings of the animation properties.</summary>
public class AnimationCompositionTests
{
    private static HtmlDocumentOptions Options() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, Canvas Measuring)> LoadAsync(string css, string body)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{css}</style>"), Options());
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static async Task<double> WidthAtAsync(string css, double seconds)
    {
        var (_, document, measuring) = await LoadAsync(css, "<div id=a></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(seconds), measuring);
        return document.GetElementBounds("a")!.Value.Width;
    }

    private const string Grow = "@keyframes grow { from { width: 0pt } to { width: 100pt } } #a { height: 10pt; width: 50pt; animation: grow 1s linear forwards; ";

    [Fact]
    public async Task Replace_IsTheDefault_AndTheUnderlyingValueIsNotUsed() =>
        Assert.Equal(50, await WidthAtAsync(Grow + "}", 0.5), 1);

    [Fact]
    public async Task Add_PutsTheKeyframeValueOnTopOfTheUnderlyingOne() =>
        Assert.Equal(100, await WidthAtAsync(Grow + "animation-composition: add }", 0.5), 1);

    [Fact]
    public async Task Add_ReachesTheSumAtTheEnd() =>
        Assert.Equal(150, await WidthAtAsync(Grow + "animation-composition: add }", 3), 1);

    [Fact]
    public async Task AKeyframeCanChooseItsOwnOperation()
    {
        // The 100% keyframe adds to the 50pt underneath; the 0% one replaces it.
        var css = "@keyframes mixed { from { width: 0pt } to { width: 100pt; animation-composition: add } } #a { height: 10pt; width: 50pt; animation: mixed 1s linear forwards }";
        Assert.Equal(150, await WidthAtAsync(css, 3), 1);
        Assert.Equal(75, await WidthAtAsync(css, 0.5), 1);
    }

    [Fact]
    public async Task Accumulate_SumsTheSameTransformFunctions_WhereAddPutsThemOneAfterTheOther()
    {
        // translateX(10pt) beneath translateX(20pt): accumulate is one translateX(30pt), add is the two in a row, which is the same place.
        var css = "@keyframes slide { from { transform: translateX(20pt) } to { transform: translateX(20pt) } } #a { width: 10pt; height: 10pt; background: red; transform: translateX(10pt); animation: slide 1s linear forwards; animation-composition: accumulate }";
        var (context, document, measuring) = await LoadAsync(css, "<div id=a></div>");
        using var _ = measuring;
        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        var a = document.GetElementBounds("a")!.Value;
        using var canvas = await TestDocument.PaintAsync(context, document);
        // The box is at x = a.X + 30 (20 + 10), not a.X + 20 (replace).
        Assert.Equal(0, TestDocument.Pixel(canvas, (int)(a.X + 35), (int)(a.Y + 5)).G);
        Assert.Equal(255, TestDocument.Pixel(canvas, (int)(a.X + 15), (int)(a.Y + 5)).G);
    }

    [Fact]
    public async Task TheWebkitSpellingsOfTheAnimationProperties_PlayTheSameAnimation()
    {
        var css = "@-webkit-keyframes grow { from { width: 0pt } to { width: 100pt } } #a { height: 10pt; -webkit-animation: grow 1s linear forwards }";
        Assert.Equal(50, await WidthAtAsync(css, 0.5), 1);
    }
}

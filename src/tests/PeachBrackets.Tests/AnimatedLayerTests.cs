using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>
/// <see cref="DocumentPaintLayer.Animated"/>: a box that only moves or fades is drawn apart from the page, so the content layer has nothing to repaint for
/// it - and a box that cannot be drawn apart without changing what is on top of what stays where it was.
/// </summary>
public class AnimatedLayerTests
{
    private const string Slide = "@keyframes slide { from { transform: translateX(0pt) } to { transform: translateX(100pt) } }";

    private static HtmlDocumentOptions Options() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string body, string extraCss = "") =>
        await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{Slide} #a {{ width: 50pt; height: 50pt; background: rgb(0, 0, 255); animation: slide 1s linear forwards }} {extraCss}</style>"), Options());

    private static async Task<RasterCanvas> LayerAsync(RasterRenderContext context, HtmlDocument document, DocumentPaintLayer layer)
    {
        var page = document.Pages[0];
        var canvas = context.CreateCanvas((int)Math.Ceiling(page.WidthPoints), (int)Math.Ceiling(page.HeightPoints));
        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, page.WidthPoints, page.HeightPoints);
        await document.PaintPageAsync(0, canvas, layer);
        return canvas;
    }

    private static bool IsBlue(RasterCanvas canvas, int x, int y) => TestDocument.Pixel(canvas, x, y) is { B: > 200, R: < 60, G: < 60 };

    [Fact]
    public async Task ATransformedBox_IsLeftOutOfTheContentLayer_AndDrawnByTheAnimatedOne()
    {
        var (context, document) = await LoadAsync("<div id=a></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.True(frame.OverlayChanged);
        using var content = await LayerAsync(context, document, DocumentPaintLayer.Content);
        using var animated = await LayerAsync(context, document, DocumentPaintLayer.Animated);
        Assert.False(IsBlue(content, 25, 25));
        Assert.False(IsBlue(content, 75, 25));
        Assert.True(IsBlue(animated, 75, 25));
        Assert.False(IsBlue(animated, 10, 25));
    }

    [Fact]
    public async Task TheContentAndAnimatedLayersTogether_AreWhatOnePassDraws()
    {
        var (context, document) = await LoadAsync("<p>some text before</p><div id=a></div><p>some text after</p>");

        document.AdvanceAnimations(TimeSpan.Zero);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        using var one = await LayerAsync(context, document, DocumentPaintLayer.All);
        using var two = await LayerAsync(context, document, DocumentPaintLayer.Content);
        await document.PaintPageAsync(0, two, DocumentPaintLayer.Animated);

        Assert.True(one.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(two.ToPixelBuffer().PremultipliedRgba.Span));
    }

    [Fact]
    public async Task AFrameThatOnlyMovesTheBox_HasNothingOfTheSurfaceToRepaint()
    {
        var (_, document) = await LoadAsync("<div id=a></div>");

        // The step that takes the box out of the surface repaints where it was; after that a step is only the animated layer.
        var first = document.AdvanceAnimations(TimeSpan.Zero);
        Assert.NotEmpty(first.DirtyRegions);

        var second = document.AdvanceAnimations(TimeSpan.FromSeconds(0.25));
        var third = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.Empty(second.DirtyRegions);
        Assert.Empty(third.DirtyRegions);
        Assert.True(second.OverlayChanged);
        Assert.True(third.OverlayChanged);
        Assert.True(third.IsRunning);
    }

    [Fact]
    public async Task TheAllLayer_StillDrawsTheBoxInPlace()
    {
        var (context, document) = await LoadAsync("<div id=a></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        using var all = await LayerAsync(context, document, DocumentPaintLayer.All);
        Assert.True(IsBlue(all, 75, 25));
        Assert.False(IsBlue(all, 10, 25));
    }

    [Fact]
    public async Task AnimatedLayer_IsEmptyBeforeAnythingPlays()
    {
        var (context, document) = await LoadAsync("<div id=a></div>");

        using var content = await LayerAsync(context, document, DocumentPaintLayer.Content);
        using var animated = await LayerAsync(context, document, DocumentPaintLayer.Animated);

        Assert.True(IsBlue(content, 25, 25));
        Assert.False(IsBlue(animated, 25, 25));
    }

    [Fact]
    public async Task ABoxBehindThePage_WithANegativeZIndex_StaysInTheContentLayer()
    {
        // It paints under the text that is not positioned: the animated layer, over all of the content, would put it over the text.
        var (context, document) = await LoadAsync("<p style='margin:0;font-size:40pt'>some text over it</p><div id=a style='position:relative;z-index:-1'></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.False(frame.OverlayChanged);
        using var animated = await LayerAsync(context, document, DocumentPaintLayer.Animated);
        Assert.False(IsBlue(animated, 75, 25));

        using var one = await LayerAsync(context, document, DocumentPaintLayer.All);
        using var two = await LayerAsync(context, document, DocumentPaintLayer.Content);
        await document.PaintPageAsync(0, two, DocumentPaintLayer.Animated);
        Assert.True(one.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(two.ToPixelBuffer().PremultipliedRgba.Span));
    }

    [Fact]
    public async Task ABoxInsideAClippingAncestor_IsDrawnApart_UnderTheAncestorsClip()
    {
        // The clip is 60pt wide: the box starts inside it, and at 0.5s it is at x=50..100, half of it past the clip.
        var (context, document) = await LoadAsync("<div style='overflow:hidden;width:80pt;height:60pt'><div id=a></div></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.True(frame.OverlayChanged);
        using var animated = await LayerAsync(context, document, DocumentPaintLayer.Animated);
        using var content = await LayerAsync(context, document, DocumentPaintLayer.Content);
        Assert.True(IsBlue(animated, 70, 25));
        Assert.False(IsBlue(animated, 100, 25));
        Assert.False(IsBlue(content, 70, 25));

        using var one = await LayerAsync(context, document, DocumentPaintLayer.All);
        await document.PaintPageAsync(0, content, DocumentPaintLayer.Animated);
        Assert.True(one.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(content.ToPixelBuffer().PremultipliedRgba.Span));
    }

    [Fact]
    public async Task ABoxInsideAClippingPositionedAncestor_IsDrawnApart_AndTheAncestorDoesNotCountAsOverlap()
    {
        var (context, document) = await LoadAsync("<div style='position:relative;overflow:hidden;width:200pt;height:60pt'><div id=a></div></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.True(frame.OverlayChanged);
        using var content = await LayerAsync(context, document, DocumentPaintLayer.Content);
        using var one = await LayerAsync(context, document, DocumentPaintLayer.All);
        await document.PaintPageAsync(0, content, DocumentPaintLayer.Animated);
        Assert.True(IsBlue(content, 75, 25));
        Assert.True(one.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(content.ToPixelBuffer().PremultipliedRgba.Span));
    }

    [Fact]
    public async Task ABoxThatAnotherMovesOver_GoesBackToTheContentLayer_AndTheFrameRepaintsIt()
    {
        const string css = "@keyframes rise { from { transform: translateY(0pt) } to { transform: translateY(-300pt) } } #b { position: absolute; left: 0; top: 300pt; width: 400pt; height: 50pt; background: rgb(255, 0, 0); animation: rise 1s linear forwards } ";
        var (context, document) = await LoadAsync("<div id=a></div><div id=b></div>", css);

        // Both are animated, in different places: both are drawn apart from the page.
        document.AdvanceAnimations(TimeSpan.Zero);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.25));
        using (var animated = await LayerAsync(context, document, DocumentPaintLayer.Animated))
            Assert.True(IsBlue(animated, 50, 25));

        // The red box reaches the blue one: the blue one goes back to the page, and where it is is repainted.
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(1));
        using (var animated = await LayerAsync(context, document, DocumentPaintLayer.Animated))
            Assert.False(IsBlue(animated, 125, 25));

        // Whatever the layers hold, they draw what one pass does.
        using var one = await LayerAsync(context, document, DocumentPaintLayer.All);
        using var two = await LayerAsync(context, document, DocumentPaintLayer.Content);
        await document.PaintPageAsync(0, two, DocumentPaintLayer.Animated);
        Assert.True(one.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(two.ToPixelBuffer().PremultipliedRgba.Span));
    }

    // Every case below must draw what one pass draws, whatever the layers hold.
    private static async Task AssertLayersEqualAllAsync(RasterRenderContext context, HtmlDocument document)
    {
        using var one = await LayerAsync(context, document, DocumentPaintLayer.All);
        using var two = await LayerAsync(context, document, DocumentPaintLayer.Content);
        await document.PaintPageAsync(0, two, DocumentPaintLayer.Animated);
        await document.PaintPageAsync(0, two, DocumentPaintLayer.ViewportAttached);
        Assert.True(one.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(two.ToPixelBuffer().PremultipliedRgba.Span));
    }

    [Fact]
    public async Task ABoxInsideAnOpacityGroup_IsDrawnWithTheGroup_ByTheAnimatedLayer()
    {
        var (context, document) = await LoadAsync("<div style='opacity:0.5;width:400pt;height:60pt;background:rgb(255,0,0)'><div id=a></div></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.True(frame.OverlayChanged);
        await AssertLayersEqualAllAsync(context, document);
        var next = document.AdvanceAnimations(TimeSpan.FromSeconds(0.75));
        Assert.Empty(next.DirtyRegions);
        await AssertLayersEqualAllAsync(context, document);
    }

    [Fact]
    public async Task ABoxInsideATransformedAncestor_IsDrawnWithIt_ByTheAnimatedLayer()
    {
        var (context, document) = await LoadAsync("<div style='transform:translate(20pt,10pt);width:400pt;height:60pt'><div id=a></div></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.True(frame.OverlayChanged);
        await AssertLayersEqualAllAsync(context, document);
    }

    [Fact]
    public async Task ABoxInsideAFilteredAncestor_IsDrawnWithIt_ByTheAnimatedLayer()
    {
        var (context, document) = await LoadAsync("<div style='filter:grayscale(1);width:400pt;height:60pt'><div id=a></div></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        await AssertLayersEqualAllAsync(context, document);
    }

    [Fact]
    public async Task ABoxInsideABlendingAncestor_StaysInTheContentLayer()
    {
        var (context, document) = await LoadAsync("<div style='mix-blend-mode:multiply;width:400pt;height:60pt;background:rgb(255,200,0)'><div id=a></div></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.False(frame.OverlayChanged);
        await AssertLayersEqualAllAsync(context, document);
    }

    [Fact]
    public async Task APositionedBoxPaintedOverIt_IsDrawnWithIt_AsACompanion()
    {
        // The red box is later in the tree and positioned, so it covers the blue one where they meet: the animated layer draws both, in that order.
        var (context, document) = await LoadAsync("<div id=a></div><div style='position:absolute;left:60pt;top:0;width:200pt;height:50pt;background:red'></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.True(frame.OverlayChanged);
        await AssertLayersEqualAllAsync(context, document);
        var next = document.AdvanceAnimations(TimeSpan.FromSeconds(0.75));
        Assert.Empty(next.DirtyRegions);
        Assert.True(next.OverlayChanged);
        await AssertLayersEqualAllAsync(context, document);
    }

    [Fact]
    public async Task AFixedBoxOverIt_NeedsNoFallback_AndOneUnderItDoes()
    {
        var (context, document) = await LoadAsync("<div id=a></div><div style='position:fixed;left:60pt;top:0;width:200pt;height:50pt;background:red;z-index:5'></div>");
        document.SetViewport(new Rect(0, 0, 400, 300));
        document.AdvanceAnimations(TimeSpan.Zero);
        Assert.True(document.AdvanceAnimations(TimeSpan.FromSeconds(0.5)).OverlayChanged);
        await AssertLayersEqualAllAsync(context, document);

        var (context2, under) = await LoadAsync("<div id=a style='position:relative;z-index:5'></div><div style='position:fixed;left:60pt;top:0;width:200pt;height:50pt;background:red;z-index:1'></div>");
        under.SetViewport(new Rect(0, 0, 400, 300));
        under.AdvanceAnimations(TimeSpan.Zero);
        under.AdvanceAnimations(TimeSpan.FromSeconds(0.5));
        using (var animated = await LayerAsync(context2, under, DocumentPaintLayer.Animated))
            Assert.False(IsBlue(animated, 100, 25));
    }

    [Fact]
    public async Task ANegativeZIndexBox_IsDrawnApart_WhenNothingInFlowPaintsOverIt()
    {
        var (context, document) = await LoadAsync("<div id=a style='position:relative;z-index:-1'></div>");
        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.True(frame.OverlayChanged);
        await AssertLayersEqualAllAsync(context, document);
    }
}

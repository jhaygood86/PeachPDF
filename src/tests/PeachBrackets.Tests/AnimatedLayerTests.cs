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
    public async Task APositionedBoxThatOverlapsIt_KeepsItInTheContentLayer()
    {
        // The red box is painted after the animated one (positioned, later in the tree) and covers it where it passes under: drawn apart, the animated box
        // would end up on top of it. It starts clear of it, so it is drawn apart at first and goes back to the content layer when it reaches the red box.
        var (context, document) = await LoadAsync("<div id=a></div><div style='position:absolute;left:60pt;top:0;width:200pt;height:50pt;background:red'></div>");

        var first = document.AdvanceAnimations(TimeSpan.Zero);
        Assert.True(first.OverlayChanged);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.True(frame.OverlayChanged);
        Assert.NotEmpty(frame.DirtyRegions);
        using var content = await LayerAsync(context, document, DocumentPaintLayer.Content);
        using var animated = await LayerAsync(context, document, DocumentPaintLayer.Animated);
        Assert.False(IsBlue(animated, 75, 25));
        using var all = await LayerAsync(context, document, DocumentPaintLayer.All);
        Assert.Equal(TestDocument.Pixel(all, 75, 25), TestDocument.Pixel(content, 75, 25));

        // It stays there while it overlaps: nothing is drawn apart and the surface is repainted where it is.
        var next = document.AdvanceAnimations(TimeSpan.FromSeconds(0.6));
        Assert.False(next.OverlayChanged);
        Assert.NotEmpty(next.DirtyRegions);
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
    public async Task ABoxInsideAClippingAncestor_KeepsItInTheContentLayer()
    {
        var (context, document) = await LoadAsync("<div style='overflow:hidden;width:400pt;height:60pt'><div id=a></div></div>");

        document.AdvanceAnimations(TimeSpan.Zero);
        var frame = document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        Assert.False(frame.OverlayChanged);
        using var animated = await LayerAsync(context, document, DocumentPaintLayer.Animated);
        using var content = await LayerAsync(context, document, DocumentPaintLayer.Content);
        Assert.False(IsBlue(animated, 75, 25));
        Assert.True(IsBlue(content, 75, 25));
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
        Assert.NotEmpty(frame.DirtyRegions);
        using (var animated = await LayerAsync(context, document, DocumentPaintLayer.Animated))
            Assert.False(IsBlue(animated, 125, 25));

        // Whatever the layers hold, they draw what one pass does.
        using var one = await LayerAsync(context, document, DocumentPaintLayer.All);
        using var two = await LayerAsync(context, document, DocumentPaintLayer.Content);
        await document.PaintPageAsync(0, two, DocumentPaintLayer.Animated);
        Assert.True(one.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(two.ToPixelBuffer().PremultipliedRgba.Span));
    }
}

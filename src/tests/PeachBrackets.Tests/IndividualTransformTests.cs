using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>The individual transform properties <c>translate</c>, <c>rotate</c> and <c>scale</c> (CSS Transforms 2 §3): painted in front of <c>transform</c>, and animated on their own.</summary>
public class IndividualTransformTests
{
    private static HtmlDocumentOptions Options() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string css)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div id=a></div>", $"<style>#a {{ width: 20pt; height: 20pt; margin: 40pt; background: rgb(255, 0, 0); {css} }}</style>"), Options());
        return (context, document);
    }

    private static async Task<bool> IsRedAsync(RasterRenderContext context, HtmlDocument document, double dx, double dy)
    {
        var a = document.GetElementBounds("a")!.Value;
        using var canvas = await TestDocument.PaintAsync(context, document);
        var pixel = TestDocument.Pixel(canvas, (int)Math.Round(a.X + dx), (int)Math.Round(a.Y + dy));
        return pixel.R == 255 && pixel.G == 0 && pixel.B == 0;
    }

    [Fact]
    public async Task Translate_MovesThePaintedBox_AndNotItsLayout()
    {
        var (context, document) = await LoadAsync("translate: 30pt 0");
        var layout = document.GetElementBounds("a")!.Value;

        Assert.True(await IsRedAsync(context, document, 40, 10));
        Assert.False(await IsRedAsync(context, document, 10, 10));
        Assert.Equal(layout, document.GetElementBounds("a")!.Value);
    }

    [Fact]
    public async Task Scale_GrowsTheBoxAboutItsCentre()
    {
        var (context, document) = await LoadAsync("scale: 2");

        // 20pt scaled by 2 about the centre covers -10..30; a box not scaled stops at 0..20.
        Assert.True(await IsRedAsync(context, document, -5, 10));
        Assert.True(await IsRedAsync(context, document, 25, 10));
        Assert.False(await IsRedAsync(context, document, -15, 10));
    }

    [Fact]
    public async Task Rotate_TurnsTheBoxAboutItsCentre()
    {
        var (context, document) = await LoadAsync("width: 40pt; height: 10pt; rotate: 90deg");

        // 40 x 10 turned a quarter about (20, 5) is 10 wide and 40 tall: it reaches up to y = -15 and no longer to x = 0.
        Assert.True(await IsRedAsync(context, document, 20, -10));
        Assert.False(await IsRedAsync(context, document, 2, 5));
    }

    [Fact]
    public async Task TheIndividualProperties_ComposeInFrontOfTransform_InTheSpecOrder()
    {
        var (individualContext, individual) = await LoadAsync("translate: 30pt 0; scale: 2");
        var (listContext, list) = await LoadAsync("transform: translate(30pt, 0) scale(2)");

        foreach (var (dx, dy) in new[] { (-5.0, 10.0), (45.0, 10.0), (60.0, 10.0), (25.0, 25.0), (10.0, -5.0) })
            Assert.Equal(await IsRedAsync(listContext, list, dx, dy), await IsRedAsync(individualContext, individual, dx, dy));

        // translate then scale then transform: the transform list's own translate comes last-applied-first.
        var (composedContext, composed) = await LoadAsync("translate: 10pt 0; transform: translate(20pt, 0)");
        Assert.True(await IsRedAsync(composedContext, composed, 35, 10));
        Assert.False(await IsRedAsync(composedContext, composed, 10, 10));
    }

    [Fact]
    public async Task ARotationAroundAnAxisKeyword_IsAcceptedAndPaints()
    {
        var (context, document) = await LoadAsync("width: 40pt; height: 10pt; rotate: z 90deg");

        Assert.True(await IsRedAsync(context, document, 20, -10));
    }

    [Fact]
    public async Task ABoxWithTheseNone_HasNoTransform_AndOnewithOneSetIsOne()
    {
        var (context, document) = await LoadAsync("translate: none; rotate: none; scale: none");
        Assert.True(await IsRedAsync(context, document, 10, 10));
        Assert.False(await IsRedAsync(context, document, -5, 10));
    }

    [Fact]
    public async Task Rotate_Animates_FromNone_ThroughItsAngle()
    {
        var css = "@keyframes turn { to { rotate: 90deg } } ";
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div id=a></div>", $"<style>{css} #a {{ width: 40pt; height: 10pt; margin: 40pt; background: rgb(255, 0, 0); animation: turn 1s linear forwards }}</style>"), Options());
        document.AdvanceAnimations(TimeSpan.Zero);
        document.AdvanceAnimations(TimeSpan.FromSeconds(3));

        // Finished: a quarter turn.
        Assert.True(await IsRedAsync(context, document, 20, -10));

        var (halfContext, half) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div id=a></div>", $"<style>{css} #a {{ width: 40pt; height: 10pt; margin: 40pt; background: rgb(255, 0, 0); animation: turn 1s linear forwards }}</style>"), Options());
        half.AdvanceAnimations(TimeSpan.Zero);
        half.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        // Half way: 45 degrees about the centre (20, 5), so the long axis runs down and to the right: (31, 16) is on it, and (38, 5), which the unrotated box covers, is not.
        Assert.True(await IsRedAsync(halfContext, half, 31, 16));
        Assert.False(await IsRedAsync(halfContext, half, 38, 5));
    }

    [Fact]
    public async Task Scale_AnimatesFromNoneToAFactor_AndTranslatePadsItsMissingParts()
    {
        var css = "@keyframes grow { from { scale: none; translate: 10pt } to { scale: 3; translate: 10pt 20pt } } ";
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div id=a></div>", $"<style>{css} #a {{ width: 20pt; height: 20pt; margin: 40pt; background: rgb(255, 0, 0); animation: grow 1s linear forwards }}</style>"), Options());
        document.AdvanceAnimations(TimeSpan.Zero);
        document.AdvanceAnimations(TimeSpan.FromSeconds(0.5));

        // Half way: scale 2 about the centre (reaching -10..30) and a translation of (10, 10): the box covers 0..40 in x and y.
        Assert.True(await IsRedAsync(context, document, 35, 35));
        Assert.False(await IsRedAsync(context, document, -5, 10));
    }

    [Fact]
    public async Task ATransform_IsNotJustTheTransformProperty_ForTheStackingContextItMakes()
    {
        // A scale of 1 is the identity, but a box with a scale that is not none places its descendants exactly as a transform that is not none does.
        async Task<(Rect A, Rect B)> BoundsAsync(string declaration)
        {
            var (_, document) = await TestDocument.LoadAsync(
                TestDocument.Wrap("<div id=a><div id=b></div></div>", $"<style>#a {{ {declaration}; width: 100pt; height: 100pt; margin: 40pt }} #b {{ position: absolute; left: 10pt; top: 10pt; width: 5pt; height: 5pt }}</style>"), Options());
            return (document.GetElementBounds("a")!.Value, document.GetElementBounds("b")!.Value);
        }

        var individual = await BoundsAsync("scale: 1");
        var list = await BoundsAsync("transform: scale(1)");

        Assert.Equal(list.B, individual.B);
    }
}

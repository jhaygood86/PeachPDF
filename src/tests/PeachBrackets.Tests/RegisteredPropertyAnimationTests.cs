using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>A registered custom property (<c>@property</c>) animates live, and what reads it through <c>var()</c> follows.</summary>
public class RegisteredPropertyAnimationTests
{
    private static HtmlDocumentOptions Options() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = true };

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, Canvas Measuring)> LoadAsync(string css, string body)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{css}</style>"), Options());
        return (context, document, context.CreateCanvas(1, 1));
    }

    [Fact]
    public async Task ALengthProperty_MovesTheLayoutThatReadsIt()
    {
        var css = "@property --w { syntax: '<length>'; inherits: false; initial-value: 0pt } @keyframes k { to { --w: 200pt } } #a { height: 10pt; width: var(--w); animation: k 1s linear forwards }";
        var (_, document, measuring) = await LoadAsync(css, "<div id=a></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        Assert.Equal(0, document.GetElementBounds("a")!.Value.Width, 1);

        var frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);
        Assert.True(frame.LayoutChanged);
        Assert.Equal(100, document.GetElementBounds("a")!.Value.Width, 1);

        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(3), measuring);
        Assert.Equal(200, document.GetElementBounds("a")!.Value.Width, 1);
    }

    [Fact]
    public async Task AColorProperty_ReachesTheBoxesThatInheritIt()
    {
        var css = "@property --c { syntax: '<color>'; inherits: true; initial-value: rgb(255, 0, 0) } @keyframes k { to { --c: rgb(0, 0, 255) } } #a { animation: k 1s linear forwards } #b { width: 50pt; height: 50pt; background: var(--c) }";
        var (context, document, measuring) = await LoadAsync(css, "<div id=a><div id=b></div></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var b = document.GetElementBounds("b")!.Value;
        using (var start = await TestDocument.PaintAsync(context, document))
            Assert.Equal(255, TestDocument.Pixel(start, (int)b.X + 10, (int)b.Y + 10).R);

        await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(3), measuring);
        using var end = await TestDocument.PaintAsync(context, document);
        var pixel = TestDocument.Pixel(end, (int)b.X + 10, (int)b.Y + 10);
        Assert.Equal(255, pixel.B);
        Assert.Equal(0, pixel.R);
    }

    [Fact]
    public async Task AnUnregisteredCustomProperty_StaysAStep_AndDoesNotRunTheClock()
    {
        var css = "@keyframes k { to { --w: 200pt } } #a { height: 10pt; width: var(--w, 50pt); animation: k 1s linear forwards }";
        var (_, document, measuring) = await LoadAsync(css, "<div id=a></div>");
        using var _ = measuring;

        await document.AdvanceAnimationsAsync(TimeSpan.Zero, measuring);
        var frame = await document.AdvanceAnimationsAsync(TimeSpan.FromSeconds(0.5), measuring);

        Assert.False(frame.LayoutChanged);
        Assert.Equal(50, document.GetElementBounds("a")!.Value.Width, 1);
    }
}

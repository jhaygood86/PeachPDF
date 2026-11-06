using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

/// <summary>An inherited property that only paints (<c>color</c>, <c>visibility</c>) reaches the boxes under the animated one.</summary>
public class InheritedPaintAnimationTests
{
    private static HtmlDocumentOptions Options() => new() { MediaType = MediaType.Screen, PageWidthPoints = 400, AnimationsEnabled = true };

    // A box of text that is all one colour: the glyphs' colour is the box's `color`.
    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string css, string body) =>
        await TestDocument.LoadAsync(TestDocument.Wrap(body, $"<style>{css}</style>"), Options());

    private static async Task<int> InkAsync(RasterRenderContext context, HtmlDocument document, Func<(byte R, byte G, byte B, byte A), bool> isInk)
    {
        var box = document.GetElementBounds("a")!.Value;
        using var canvas = await TestDocument.PaintAsync(context, document);
        var count = 0;
        for (var y = (int)box.Y; y < (int)box.Bottom; y++)
        {
            for (var x = (int)box.X; x < (int)Math.Min(box.Right, 380); x++)
            {
                if (isInk(TestDocument.Pixel(canvas, x, y))) count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task AnAnimatedColor_ChangesTheTextInsideTheBox()
    {
        var (context, document) = await LoadAsync(
            "@keyframes tint { from { color: rgb(255, 0, 0) } to { color: rgb(0, 0, 255) } } #a { font: 40pt sans-serif; animation: tint 1s linear forwards }", "<div id=a>HHHH</div>");

        var (staticContext, staticDocument) = await LoadAsync("#a { font: 40pt sans-serif; color: rgb(255, 0, 0) }", "<div id=a>HHHH</div>");
        Assert.True(await InkAsync(staticContext, staticDocument, p => p.R > 200 && p.B < 60 && p.G < 60) > 50, "a static red text paints red ink");

        document.AdvanceAnimations(TimeSpan.Zero);
        var red =await InkAsync(context, document, p => p.R > 200 && p.B < 60 && p.G < 60);
        document.AdvanceAnimations(TimeSpan.FromSeconds(3));
        var blue = await InkAsync(context, document, p => p.B > 200 && p.R < 60 && p.G < 60);
        var redAtEnd = await InkAsync(context, document, p => p.R > 200 && p.B < 60 && p.G < 60);

        Assert.True(red > 50, $"red text at the start: {red}");
        Assert.True(blue > 50, $"blue text at the end: {blue}");
        Assert.True(redAtEnd < 10, $"red text left at the end: {redAtEnd}");
    }
}

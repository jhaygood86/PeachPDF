using PeachBrackets.Updates;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class IncrementalUpdateTests
{
    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string body, bool updates = true)
    {
        var options = new HtmlDocumentOptions { RecordTextRuns = true, EnableIncrementalUpdates = updates };
        return await TestDocument.LoadAsync(TestDocument.Wrap(body, "<style>body { font-size: 30pt } p { margin: 0 }</style>"), options);
    }

    private static bool IsRed((byte R, byte G, byte B, byte A) pixel) => pixel.R > 200 && pixel.G < 60 && pixel.B < 60;

    private static bool IsBlue((byte R, byte G, byte B, byte A) pixel) => pixel.B > 200 && pixel.R < 60 && pixel.G < 60;

    private static bool IsBlack((byte R, byte G, byte B, byte A) pixel) => pixel.R < 60 && pixel.G < 60 && pixel.B < 60;

    private static bool Any(RasterCanvas canvas, PeachDrawing.Core.Rect area, Func<(byte R, byte G, byte B, byte A), bool> test)
    {
        for (var y = (int)area.Top; y < (int)area.Bottom; y++)
        {
            for (var x = (int)area.Left; x < (int)area.Right; x++)
            {
                if (test(TestDocument.Pixel(canvas, x, y)))
                    return true;
            }
        }

        return false;
    }

    [Fact]
    public async Task AColourChange_ReachesInheritingDescendants_AndNotOnesWithTheirOwn()
    {
        var (context, document) = await LoadAsync("<p id=a>Hello <strong>world</strong> <em style='color: rgb(0, 0, 255)'>blue</em></p>");
        var runs = document.TextRuns;
        var element = document.GetElement("a")!;

        var result = document.SetStyle(element, "color", "rgb(255, 0, 0)");
        Assert.True(result.Applied, result.Reason);

        using var canvas = await TestDocument.PaintAsync(context, document);
        var run = Assert.Single(runs);
        var line = new PeachDrawing.Core.Rect(0, run.Baseline - run.Ascent, 600, run.Ascent + run.Descent);
        var helloArea = new PeachDrawing.Core.Rect(run.CharacterOffsets[0], line.Top, run.CharacterOffsets[5] - run.CharacterOffsets[0], line.Height);
        var worldArea = new PeachDrawing.Core.Rect(run.CharacterOffsets[6], line.Top, run.CharacterOffsets[11] - run.CharacterOffsets[6], line.Height);
        var blueArea = new PeachDrawing.Core.Rect(run.CharacterOffsets[12], line.Top, run.CharacterOffsets[16] - run.CharacterOffsets[12], line.Height);

        Assert.True(Any(canvas, helloArea, IsRed), "the element's own text did not change colour");
        Assert.False(Any(canvas, helloArea, IsBlack), "old colour is still painted");
        Assert.True(Any(canvas, worldArea, IsRed), "an inheriting descendant did not follow");
        Assert.True(Any(canvas, blueArea, IsBlue), "a descendant with its own colour was overwritten");
        Assert.False(Any(canvas, blueArea, IsRed));
    }

    [Fact]
    public async Task APaintOnlyChange_ReportsWhereItShows_AndMovesNothing()
    {
        var (_, document) = await LoadAsync("<p>Before</p><p id=a>Hello world</p><p id=after>After</p>");
        var extent = document.ContentExtent;
        var after = document.GetElementBounds("after");
        var offsets = document.TextRuns[1].CharacterOffsets.ToArray();
        var run = document.TextRuns[1];

        var result = document.SetStyle(document.GetElement("a")!, "background-color", "rgb(255, 255, 0)");

        Assert.True(result.Applied);
        var region = Assert.Single(result.DirtyRegions);
        Assert.Equal(0, region.PageIndex);
        Assert.True(region.Bounds.Left <= run.Bounds.Left && region.Bounds.Right >= run.Bounds.Right);
        Assert.True(region.Bounds.Top <= run.Baseline - run.Ascent && region.Bounds.Bottom >= run.Baseline + run.Descent);
        Assert.False(region.Bounds.Contains(document.TextRuns[0].Bounds.Left + 1, document.TextRuns[0].Baseline), "the first paragraph is not affected");
        Assert.Equal(extent, document.ContentExtent);
        Assert.Equal(after, document.GetElementBounds("after"));
        Assert.Equal(offsets, document.TextRuns[1].CharacterOffsets);
    }

    [Fact]
    public async Task RepaintingJustTheRegion_GivesTheSamePageAsPaintingItAgain()
    {
        var (context, document) = await LoadAsync("<p>Before</p><p id=a>Hello <b>world</b></p><p>After</p>");

        using var incremental = await TestDocument.PaintAsync(context, document);
        var result = document.SetStyle(document.GetElement("a")!, "color", "rgb(0, 128, 0)");
        Assert.True(result.Applied);

        var white = incremental.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 255));
        foreach (var region in result.DirtyRegions)
        {
            incremental.DrawRectangle(white, region.Bounds.X, region.Bounds.Y, region.Bounds.Width, region.Bounds.Height);
            await document.PaintPageRegionAsync(region.PageIndex, incremental, region.Bounds);
        }

        using var fresh = await TestDocument.PaintAsync(context, document);
        Assert.True(incremental.ToPixelBuffer().PremultipliedRgba.Span.SequenceEqual(fresh.ToPixelBuffer().PremultipliedRgba.Span),
            "repainting only the dirty region differs from repainting the page");
    }

    [Fact]
    public async Task ABorderThatFollowsTheColour_FollowsItsChange()
    {
        var (context, document) = await LoadAsync("<p id=a style='border: 6pt solid; width: 100pt'>x</p>");

        using var before = await TestDocument.PaintAsync(context, document);
        Assert.True(IsBlack(TestDocument.Pixel(before, 22, 30)));

        document.SetStyle(document.GetElement("a")!, "color", "rgb(255, 0, 0)");
        using var after = await TestDocument.PaintAsync(context, document);
        Assert.True(IsRed(TestDocument.Pixel(after, 22, 30)), "the border kept its old colour");
    }

    [Fact]
    public async Task APropertyThatCouldChangeLayout_ACanvasBackground_OrABadValue_IsNotApplied()
    {
        var (context, document) = await LoadAsync("<p id=a>Hello</p>");
        var element = document.GetElement("a")!;
        var extent = document.ContentExtent;

        var font = document.SetStyle(element, "font-size", "60pt");
        Assert.False(font.Applied);
        Assert.Contains("font-size", font.Reason);
        Assert.Empty(font.DirtyRegions);

        Assert.False(document.SetStyle(element, "color", "not-a-colour").Applied);
        Assert.False(document.SetStyle(document.GetElement("a")!, "background-color", "").Applied);

        var body = document.GetElementAt(0, 30, 40);
        Assert.Equal("p", body?.TagName);
        Assert.Equal(extent, document.ContentExtent);

        using var canvas = await TestDocument.PaintAsync(context, document);
        Assert.True(TestDocument.HasInk(canvas, 20, 20, 150, 50));
    }

    [Fact]
    public async Task GetElementAt_FindsTheInnermostElement_AndNothingInTheMargin()
    {
        var (_, document) = await LoadAsync("<p>One <strong id=s>two</strong></p>");
        var run = document.TextRuns[0];
        var y = run.Baseline - (run.Ascent / 2);

        var strong = document.GetElementAt(0, (run.CharacterOffsets[4] + run.CharacterOffsets[7]) / 2, y);
        Assert.Equal("strong", strong?.TagName);
        Assert.Equal("s", strong?.Id);
        Assert.Equal(document.GetElement("s"), strong);

        Assert.Equal("p", document.GetElementAt(0, (run.CharacterOffsets[0] + run.CharacterOffsets[3]) / 2, y)?.TagName);
        Assert.Null(document.GetElementAt(0, 2, 2));
        Assert.Null(document.GetElement("nothing"));
    }

    [Fact]
    public async Task WithoutTheOption_SetStyleThrows()
    {
        var (_, document) = await LoadAsync("<p id=a>Hello</p>", updates: false);

        Assert.Throws<InvalidOperationException>(() => document.SetStyle(document.GetElement("a")!, "color", "red"));
    }
}

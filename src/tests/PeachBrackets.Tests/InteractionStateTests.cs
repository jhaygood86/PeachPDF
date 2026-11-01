using PeachBrackets.Text;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class InteractionStateTests
{
    private const string Styles = """
        <style>
        #a { height: 40px; background: rgb(0,0,255) }
        #a:hover { background: rgb(255,0,0) }
        #b { height: 20px; background: rgb(0,255,0) }
        </style>
        """;

    private const string Body = "<div id=a></div><div id=b></div>";

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, Canvas Measuring)> LoadAsync(
        string body, string head, HtmlDocumentOptions? options = null)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, head), options ?? new HtmlDocumentOptions { Media = "screen" });
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static (double X, double Y) Center(HtmlDocument document, string id)
    {
        var bounds = document.GetElementBounds(id)!.Value;
        return (bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    private static async Task<(byte R, byte G, byte B, byte A)> PixelAtAsync(RasterRenderContext context, HtmlDocument document, string id)
    {
        using var canvas = await TestDocument.PaintAsync(context, document);
        var (x, y) = Center(document, id);
        return TestDocument.Pixel(canvas, (int)x, (int)y);
    }

    [Fact]
    public async Task Hover_AppliesWhileThePointerIsOver_AndEndsWhenItLeaves()
    {
        var (context, document, measuring) = await LoadAsync(Body, Styles);
        var (x, y) = Center(document, "a");
        Assert.Equal(0, (await PixelAtAsync(context, document, "a")).R);

        var entered = await document.SetPointerStateAsync(0, x, y, pressed: false, measuring);

        Assert.True(entered.Applied);
        var region = Assert.Single(entered.DirtyRegions);
        Assert.True(region.Bounds.Contains(x, y));
        Assert.Equal(255, (await PixelAtAsync(context, document, "a")).R);

        var left = await document.ClearPointerStateAsync(measuring);

        Assert.True(left.Applied);
        Assert.Equal(0, (await PixelAtAsync(context, document, "a")).R);
    }

    [Fact]
    public async Task ASamePointerPosition_IsNotAChange()
    {
        var (_, document, measuring) = await LoadAsync(Body, Styles);
        var (x, y) = Center(document, "a");

        Assert.True((await document.SetPointerStateAsync(0, x, y, false, measuring)).Applied);
        var again = await document.SetPointerStateAsync(0, x + 1, y + 1, false, measuring);

        Assert.False(again.Applied);
        Assert.Contains("already", again.Reason);
    }

    [Fact]
    public async Task ARuleThatChangesLayout_MovesWhatFollows_AndNamesEveryChangedPage()
    {
        var (_, document, measuring) = await LoadAsync(Body, "<style>#a { height: 40px } #a:hover { height: 100px } #b { height: 20px }</style>");
        var before = document.GetElementBounds("b")!.Value;
        var (x, y) = Center(document, "a");

        var result = await document.SetPointerStateAsync(0, x, y, false, measuring);

        Assert.True(result.Applied);
        var after = document.GetElementBounds("b")!.Value;
        Assert.Equal(before.Y + 45, after.Y, 1); // 60px
        var region = Assert.Single(result.DirtyRegions);
        Assert.Equal(new Rect(0, 0, document.Pages[0].WidthPoints, document.Pages[0].HeightPoints), region.Bounds);
    }

    [Fact]
    public async Task APageWithNoStateRules_DoesNotReload()
    {
        var (_, document, measuring) = await LoadAsync(Body, "<style>#a { height: 40px }</style>");
        var (x, y) = Center(document, "a");

        var result = await document.SetPointerStateAsync(0, x, y, false, measuring);

        Assert.True(result.Applied);
        Assert.Empty(result.DirtyRegions);
    }

    [Fact]
    public async Task Active_NeedsTheButtonDown()
    {
        var (context, document, measuring) = await LoadAsync(Body, "<style>#a { height: 40px; background: rgb(0,0,255) } #a:active { background: rgb(255,0,0) }</style>");
        var (x, y) = Center(document, "a");

        await document.SetPointerStateAsync(0, x, y, pressed: false, measuring);
        Assert.Equal(0, (await PixelAtAsync(context, document, "a")).R);

        await document.SetPointerStateAsync(0, x, y, pressed: true, measuring);
        Assert.Equal(255, (await PixelAtAsync(context, document, "a")).R);

        await document.SetPointerStateAsync(0, x, y, pressed: false, measuring);
        Assert.Equal(0, (await PixelAtAsync(context, document, "a")).R);
    }

    [Fact]
    public async Task HoverReachesTheAncestorsOfTheElementUnderThePointer()
    {
        var (context, document, measuring) = await LoadAsync(
            "<div id=outer><div id=inner style='height:10px'></div></div>",
            "<style>#outer { height: 40px; background: rgb(0,0,255) } #outer:hover { background: rgb(255,0,0) }</style>");
        var (x, y) = Center(document, "inner");

        await document.SetPointerStateAsync(0, x, y, false, measuring);

        Assert.Equal(255, (await PixelAtAsync(context, document, "outer")).R);
    }

    [Fact]
    public async Task PointerEventsNone_DoesNotTakeTheHover()
    {
        var (context, document, measuring) = await LoadAsync(
            "<div id=a style='height:40px;background:rgb(0,0,255);pointer-events:none'></div>",
            "<style>#a:hover { background: rgb(255,0,0) }</style>");
        var (x, y) = Center(document, "a");

        await document.SetPointerStateAsync(0, x, y, false, measuring);

        Assert.Equal(0, (await PixelAtAsync(context, document, "a")).R);
    }

    [Fact]
    public async Task Focus_FocusVisible_AndFocusWithin()
    {
        var (context, document, measuring) = await LoadAsync(
            "<div id=wrap><input id=i></div>",
            "<style>#wrap { background: rgb(0,0,255) } #i { height: 20px; background: rgb(0,0,255) } #wrap:focus-within { background: rgb(255,0,0) } #i:focus-visible { background: rgb(0,255,0) }</style>");

        var focused = await document.SetFocusAsync(document.GetElement("i"), focusVisible: false, measuring);
        Assert.True(focused.Applied);
        Assert.Equal(255, (await PixelAtAsync(context, document, "wrap")).R);
        Assert.NotEqual(255, (await PixelAtAsync(context, document, "i")).G); // focus-visible needs keyboard focus

        await document.SetFocusAsync(document.GetElement("i"), focusVisible: true, measuring);
        Assert.Equal(255, (await PixelAtAsync(context, document, "i")).G);

        await document.SetFocusAsync(null, focusVisible: false, measuring);
        Assert.Equal(0, (await PixelAtAsync(context, document, "wrap")).R);
    }

    [Fact]
    public async Task ASelectionSurvivesWhenTheTextIsTheSame()
    {
        var (_, document, measuring) = await LoadAsync(
            "<p id=p>Select these words</p><div id=a style='height:40px'></div>",
            "<style>#a:hover { background: rgb(255,0,0) }</style>",
            new HtmlDocumentOptions { RecordTextRuns = true, EnableSelectionStyling = true, Media = "screen" });
        var run = document.TextRuns[0];
        var range = new TextRange(new TextPosition(0, 0), new TextPosition(0, 6));
        document.SetSelectedRanges([range]);
        var (x, y) = Center(document, "a");

        await document.SetPointerStateAsync(0, x, y, false, measuring);

        Assert.Equal(range, Assert.Single(document.SelectedRanges));
        Assert.Equal(run.Text, document.TextRuns[0].Text);
    }

    [Fact]
    public async Task AnEditedDocumentIsNotReloadedOverItsEdits()
    {
        var (_, document, measuring) = await LoadAsync(
            "<p id=p>Hello</p><div id=a style='height:40px'></div>",
            "<style>#a:hover { background: rgb(255,0,0) }</style>",
            new HtmlDocumentOptions { RecordTextRuns = true, EnableIncrementalUpdates = true, Media = "screen" });
        Assert.True((await document.InsertTextAsync(new TextPosition(0, 5), "!", measuring)).Applied);
        var (x, y) = Center(document, "a");

        var result = await document.SetPointerStateAsync(0, x, y, false, measuring);

        Assert.False(result.Applied);
        Assert.Contains("edited", result.Reason);
    }

    [Fact]
    public async Task ASelectionSurvivesSeveralRestylesInARow_AndStaysPainted()
    {
        var (context, document, measuring) = await LoadAsync(
            "<p id=p style='font-size:30px'>Select these words</p><div id=a style='height:40px'></div>",
            "<style>#a:hover { background: rgb(255,0,0) }</style>",
            new HtmlDocumentOptions { RecordTextRuns = true, EnableSelectionStyling = true, Media = "screen" });
        var run = document.TextRuns[0];
        Assert.True(document.Selection.SelectWordAt(0, run.Bounds.X + 5, run.Bounds.Y + (run.Bounds.Height / 2)));
        var selected = document.Selection.GetText();
        var (x, y) = Center(document, "a");

        // Each restyle clears the cached runs; the next one must still know what to carry over.
        await document.SetPointerStateAsync(0, x, y, false, measuring);
        await document.ClearPointerStateAsync(measuring);
        await document.SetPointerStateAsync(0, x, y, true, measuring);

        Assert.Equal(selected, document.Selection.GetText());
        using var canvas = await TestDocument.PaintAsync(context, document);
        var highlighted = false;
        for (var py = 0; py < 80 && !highlighted; py++)
        {
            for (var px = 0; px < 200 && !highlighted; px++)
            {
                var (r, g, b, a) = TestDocument.Pixel(canvas, px, py);
                highlighted = a > 0 && Math.Abs(r - 173) < 6 && Math.Abs(g - 214) < 6 && Math.Abs(b - 255) < 6;
            }
        }

        Assert.True(highlighted);
    }
}

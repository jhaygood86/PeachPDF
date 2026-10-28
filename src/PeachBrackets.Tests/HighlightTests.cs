using PeachBrackets.Text;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class HighlightTests
{
    private const string Rules =
        "<style>body { font-size: 40pt } ::highlight(found) { background: rgb(255, 0, 0) } ::highlight(note) { background: rgb(0, 200, 0); color: rgb(0, 0, 255) } "
        + "::highlight( spaced ) { background: rgb(0, 0, 255) } ::spelling-error { background: rgb(255, 255, 0) } ::selection { background: rgb(255, 0, 255) }</style>";

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(string body, string head = Rules, bool styling = true)
    {
        var options = new HtmlDocumentOptions { RecordTextRuns = true, EnableSelectionStyling = styling };
        return await TestDocument.LoadAsync(TestDocument.Wrap(body, head), options);
    }

    private static bool Is((byte R, byte G, byte B, byte A) pixel, int r, int g, int b) =>
        Math.Abs(pixel.R - r) < 12 && Math.Abs(pixel.G - g) < 12 && Math.Abs(pixel.B - b) < 12;

    // Below the baseline and above the bottom of the line: no glyph of the test text reaches there, so only a fill can show.
    private static int Row(TextRun run) => (int)(run.Baseline + (run.Descent * 0.5));

    private static int MiddleOf(TextRun run, int from, int to) => (int)((run.CharacterOffsets[from] + run.CharacterOffsets[to]) / 2);

    [Fact]
    public async Task ANamedHighlight_IsFilledWithTheRulesOfItsName_AndOnlyWhereItIs()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);

        document.SetHighlight("found", [document.FindText("Hello").Single()]);
        using var canvas = await TestDocument.PaintAsync(context, document);

        Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 0, 5), Row(run)), 255, 0, 0));
        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(canvas, MiddleOf(run, 6, 11), Row(run)));
    }

    [Fact]
    public async Task AHighlightWithNoRule_AndOneSetToNothing_PaintNothing()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);

        document.SetHighlight("nothing-styles-this", [document.FindText("Hello").Single()]);
        document.SetHighlight("found", [document.FindText("world").Single()]);
        document.SetHighlight("found", null);
        using var canvas = await TestDocument.PaintAsync(context, document);

        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(canvas, MiddleOf(run, 0, 5), Row(run)));
        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(canvas, MiddleOf(run, 6, 11), Row(run)));
        Assert.Equal(["nothing-styles-this"], document.HighlightNames);
    }

    [Fact]
    public async Task TheHighlightColour_ReplacesTheTextColourOnlyInsideIt()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);

        document.SetHighlight("note", [new TextRange(new TextPosition(0, 0), new TextPosition(0, 5))]);
        using var canvas = await TestDocument.PaintAsync(context, document);

        var inside = Count(canvas, (int)run.CharacterOffsets[0], (int)run.CharacterOffsets[5], run, p => p.B > 200 && p.R < 90 && p.G < 90);
        var outside = Count(canvas, (int)run.CharacterOffsets[6], (int)run.CharacterOffsets[11], run, p => p.B > 200 && p.R < 90 && p.G < 90);
        Assert.True(inside > 40, $"expected blue ink inside the highlight, got {inside}");
        Assert.Equal(0, outside);
    }

    private static int Count(RasterCanvas canvas, int left, int right, TextRun run, Func<(byte R, byte G, byte B, byte A), bool> test)
    {
        var count = 0;
        for (var y = (int)(run.Baseline - run.Ascent); y < (int)run.Baseline; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (test(TestDocument.Pixel(canvas, x, y))) count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task ThePriority_DecidesWhichHighlightIsOnTop_AndTheSelectionIsOverAll()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);
        var hello = document.FindText("Hello").Single();

        document.SetHighlight("found", [hello], priority: 5);
        document.SetHighlight("note", [hello], priority: 1);
        using (var canvas = await TestDocument.PaintAsync(context, document))
            Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 0, 5), Row(run)), 255, 0, 0), "the higher priority is not on top");

        document.SetHighlight("found", [hello], priority: 0);
        Assert.Equal(["found", "note"], document.HighlightNames);
        using (var canvas = await TestDocument.PaintAsync(context, document))
            Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 0, 5), Row(run)), 0, 200, 0), "the later one at an equal priority is not on top");

        document.SetSelectedRanges([hello]);
        using (var canvas = await TestDocument.PaintAsync(context, document))
            Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 0, 5), Row(run)), 255, 0, 255), "the selection is not over the highlights");
    }

    [Fact]
    public async Task TheBuiltInHighlightNames_UseTheirOwnPseudoElements_BelowTheCustomOnes()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);
        var hello = document.FindText("Hello").Single();

        document.SetHighlight("spelling-error", [hello]);
        using (var canvas = await TestDocument.PaintAsync(context, document))
            Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 0, 5), Row(run)), 255, 255, 0));

        document.SetHighlight("found", [hello], priority: -10);
        Assert.Equal(["spelling-error", "found"], document.HighlightNames);
        using (var canvas = await TestDocument.PaintAsync(context, document))
            Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 0, 5), Row(run)), 255, 0, 0));
    }

    [Fact]
    public async Task AHighlightNamedWithSpacesInTheSelector_StillMatches()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);

        document.SetHighlight("spaced", [document.FindText("world").Single()]);
        using var canvas = await TestDocument.PaintAsync(context, document);

        Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 6, 11), Row(run)), 0, 0, 255));
    }

    [Fact]
    public async Task AHighlightRuleOnAnElement_StylesOnlyTheTextInsideIt()
    {
        var (context, document) = await LoadAsync(
            "<p>Hello <em>world</em> again</p>", Rules + "<style>em::highlight(found) { background: rgb(0, 255, 255) }</style>");
        var run = Assert.Single(document.TextRuns);

        document.SetHighlight("found", [new TextRange(new TextPosition(0, 0), new TextPosition(0, run.Text.Length))]);
        using var canvas = await TestDocument.PaintAsync(context, document);

        Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 1, 4), Row(run)), 255, 0, 0));
        Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 7, 10), Row(run)), 0, 255, 255));
    }

    [Fact]
    public async Task InvalidNames_AndMissingOptions_AreRefused()
    {
        var (_, document) = await LoadAsync("<p>Hello world</p>");
        Assert.Throws<ArgumentException>(() => document.SetHighlight("", []));
        Assert.Throws<ArgumentException>(() => document.SetHighlight("two words", []));
        Assert.Throws<ArgumentException>(() => document.SetHighlight("1st", []));
        Assert.False(document.RemoveHighlight("never-set"));

        var (_, plain) = await LoadAsync("<p>Hello world</p>", styling: false);
        Assert.Throws<InvalidOperationException>(() => plain.SetHighlight("found", [new TextRange(new TextPosition(0, 0), new TextPosition(0, 2))]));
    }

    [Fact]
    public async Task ClearHighlights_RemovesAllOfThem_ButNotTheSelection()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);

        document.SetHighlight("found", [document.FindText("Hello").Single()]);
        document.SetSelectedRanges([document.FindText("world").Single()]);
        document.SetHighlight("note", [document.FindText("Hello").Single()]);
        Assert.True(document.RemoveHighlight("note"));
        document.ClearHighlights();
        using var canvas = await TestDocument.PaintAsync(context, document);

        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(canvas, MiddleOf(run, 0, 5), Row(run)));
        Assert.True(Is(TestDocument.Pixel(canvas, MiddleOf(run, 6, 11), Row(run)), 255, 0, 255));
    }
}

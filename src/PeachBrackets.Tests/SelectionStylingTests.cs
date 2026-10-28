using PeachBrackets.Text;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class SelectionStylingTests
{
    private const string Styled = "<style>body { font-size: 40pt } ::selection { background: rgb(255, 0, 0); color: rgb(0, 0, 255) }</style>";

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(
        string body, string head = Styled, bool styling = true)
    {
        var options = new HtmlDocumentOptions { RecordTextRuns = true, EnableSelectionStyling = styling };
        return await TestDocument.LoadAsync(TestDocument.Wrap(body, head), options);
    }

    private static bool IsRed((byte R, byte G, byte B, byte A) pixel) => pixel.R > 240 && pixel.G < 20 && pixel.B < 20;

    private static bool IsBlueInk((byte R, byte G, byte B, byte A) pixel) => pixel.B > 200 && pixel.R < 90 && pixel.G < 90;

    private static (int Left, int Right, int Row) BackgroundRow(TextRun run, double from, double to) =>
        // Below the baseline and above the bottom of the line: no glyph of "Hello world" reaches there, so only the fill can show.
        ((int)Math.Ceiling(from) + 1, (int)Math.Floor(to) - 1, (int)(run.Baseline + (run.Descent * 0.5)));

    [Fact]
    public async Task ASelectedWord_IsFilledWithTheSelectionBackground_AndNothingElseIs()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);

        var hello = document.FindText("Hello").Single();
        document.SetSelectedRanges([hello]);
        using var canvas = await TestDocument.PaintAsync(context, document);

        var (left, right, row) = BackgroundRow(run, run.CharacterOffsets[0], run.CharacterOffsets[5]);
        for (var x = left; x < right; x += 3)
            Assert.True(IsRed(TestDocument.Pixel(canvas, x, row)), $"no selection fill at x={x}");

        // "world" is not selected.
        var worldStart = (int)run.CharacterOffsets[6] + 2;
        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(canvas, worldStart, row));
    }

    [Fact]
    public async Task APartOfAWord_IsFilled_NotTheWholeWord()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);

        document.SetSelectedRanges([new TextRange(new TextPosition(0, 1), new TextPosition(0, 4))]);
        using var canvas = await TestDocument.PaintAsync(context, document);

        var row = (int)(run.Baseline + (run.Descent * 0.5));
        Assert.True(IsRed(TestDocument.Pixel(canvas, (int)((run.CharacterOffsets[1] + run.CharacterOffsets[4]) / 2), row)));
        Assert.False(IsRed(TestDocument.Pixel(canvas, (int)run.CharacterOffsets[1] - 3, row)));
        Assert.False(IsRed(TestDocument.Pixel(canvas, (int)run.CharacterOffsets[4] + 3, row)));
    }

    [Fact]
    public async Task TheSpaceBetweenSelectedWords_IsFilledToo()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);

        document.SetSelectedRanges([new TextRange(new TextPosition(0, 0), new TextPosition(0, run.Text.Length))]);
        using var canvas = await TestDocument.PaintAsync(context, document);

        var row = (int)(run.Baseline + (run.Descent * 0.5));
        var gapMiddle = (int)((run.CharacterOffsets[5] + run.CharacterOffsets[6]) / 2);
        Assert.True(IsRed(TestDocument.Pixel(canvas, gapMiddle, row)), "the gap between the words is not filled");
    }

    [Fact]
    public async Task TheSelectionColour_ReplacesTheTextColourInsideTheSelectionOnly()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>");
        var run = Assert.Single(document.TextRuns);

        document.SetSelectedRanges([document.FindText("Hello").Single()]);
        using var canvas = await TestDocument.PaintAsync(context, document);

        Assert.True(HasPixel(canvas, run, 0, 5, IsBlueInk), "no text in the selection colour inside the selection");
        Assert.False(HasPixel(canvas, run, 6, 11, IsBlueInk), "the selection colour leaked onto unselected text");
    }

    [Fact]
    public async Task WithoutARule_TheOptionsDefaultsPaint()
    {
        var options = new HtmlDocumentOptions
        {
            RecordTextRuns = true, EnableSelectionStyling = true,
            SelectionBackground = PaintColor.FromArgb(255, 0, 255, 0), SelectionForeground = PaintColor.FromArgb(255, 0, 0, 255),
        };
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>Hello world</p>", "<style>body { font-size: 40pt }</style>"), options);
        var run = Assert.Single(document.TextRuns);

        document.SetSelectedRanges([document.FindText("Hello").Single()]);
        using var canvas = await TestDocument.PaintAsync(context, document);

        var row = (int)(run.Baseline + (run.Descent * 0.5));
        Assert.Equal((0, 255, 0, 255), TestDocument.Pixel(canvas, (int)run.CharacterOffsets[2], row));
        Assert.True(HasPixel(canvas, run, 0, 5, IsBlueInk));
    }

    [Fact]
    public async Task ARuleOnAnElement_AppliesToItsText_AndNotItsSiblings()
    {
        var (context, document) = await LoadAsync(
            "<p>Hello</p><p class=a>Hello</p>",
            "<style>body { font-size: 40pt } .a::selection { background: rgb(255, 0, 0) }</style>");
        Assert.Equal(2, document.TextRuns.Count);

        document.SetSelectedRanges(document.FindText("Hello"));
        using var canvas = await TestDocument.PaintAsync(context, document);

        var first = document.TextRuns[0];
        var second = document.TextRuns[1];
        var firstRow = (int)(first.Baseline + (first.Descent * 0.5));
        var secondRow = (int)(second.Baseline + (second.Descent * 0.5));
        var x = (int)first.CharacterOffsets[2];

        Assert.False(IsRed(TestDocument.Pixel(canvas, x, firstRow)), "a rule for .a styled a different paragraph");
        Assert.True(IsRed(TestDocument.Pixel(canvas, x, secondRow)));
    }

    [Fact]
    public async Task WhenTheOptionIsOff_SelectionCannotBeSet_AndRulesPaintNothing()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p>", styling: false);

        Assert.Throws<InvalidOperationException>(() => document.SetSelectedRanges([document.FindText("Hello").Single()]));

        using var canvas = await TestDocument.PaintAsync(context, document);
        var run = Assert.Single(document.TextRuns);
        Assert.False(IsRed(TestDocument.Pixel(canvas, (int)run.CharacterOffsets[2], (int)(run.Baseline + (run.Descent * 0.5)))));
    }

    [Fact]
    public async Task SelectingDoesNotMoveAnything_AndClearingRestoresThePage()
    {
        var (context, document) = await LoadAsync("<p>Hello world</p><p id=after>More</p>");
        var before = document.GetElementBounds("after");
        var runBefore = document.TextRuns[0];

        document.SetSelectedRanges([document.FindText("Hello").Single()]);
        using var selected = await TestDocument.PaintAsync(context, document);
        Assert.Equal(before, document.GetElementBounds("after"));
        Assert.Equal(runBefore.CharacterOffsets, document.TextRuns[0].CharacterOffsets);
        Assert.Single(document.SelectedRanges);

        document.SetSelectedRanges(null);
        using var cleared = await TestDocument.PaintAsync(context, document);
        Assert.Empty(document.SelectedRanges);

        var row = (int)(runBefore.Baseline + (runBefore.Descent * 0.5));
        Assert.True(IsRed(TestDocument.Pixel(selected, (int)runBefore.CharacterOffsets[2], row)));
        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(cleared, (int)runBefore.CharacterOffsets[2], row));
    }

    private static bool HasPixel(RasterCanvas canvas, TextRun run, int fromChar, int toChar, Func<(byte R, byte G, byte B, byte A), bool> test)
    {
        var left = (int)Math.Min(run.CharacterOffsets[fromChar], run.CharacterOffsets[toChar]);
        var right = (int)Math.Max(run.CharacterOffsets[fromChar], run.CharacterOffsets[toChar]);
        var top = (int)(run.Baseline - run.Ascent);
        var bottom = (int)(run.Baseline + run.Descent);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (test(TestDocument.Pixel(canvas, x, y)))
                    return true;
            }
        }

        return false;
    }
}

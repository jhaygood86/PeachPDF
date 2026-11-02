using PeachDrawing;

namespace PeachBrackets.Tests;

public class TextAreaEditingTests
{
    private const string Head = "<style>@page { margin: 0 } textarea { position: absolute; margin: 0; left: 10pt; top: 10pt; width: 200pt; height: 100pt; font-size: 20pt; color: rgb(0,0,0); background: white }</style>";

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, RasterCanvas Measuring)> LoadAsync(string body)
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body, Head), new HtmlDocumentOptions { Media = "screen", PaintFormControls = true });
        var measuring = context.CreateCanvas(1, 1);
        await document.SetFocusAsync(document.GetElement("t"), focusVisible: true, measuring);
        return (context, document, measuring);
    }

    private static bool IsBlueish((byte R, byte G, byte B, byte A) p) => p.B > p.R + 40 && p.B > 200;

    private static int Count(RasterCanvas canvas, int x, int y, int width, int height, Func<(byte R, byte G, byte B, byte A), bool> match)
    {
        var count = 0;
        for (var j = y; j < y + height; j++)
        {
            for (var i = x; i < x + width; i++)
            {
                if (match(TestDocument.Pixel(canvas, i, j)))
                    count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task ATextAreaIsEditedLikeAFieldButKeepsItsLineBreaks()
    {
        var (_, document, _) = await LoadAsync("<textarea id=t>ab</textarea>");

        var start = document.GetEditState()!.Value;
        Assert.Equal(("ab", 2, 2, "textarea"), (start.Text, start.SelectionStart, start.SelectionEnd, start.Type));

        Assert.True(document.InsertText("c\r\nd\re").Applied);

        var state = document.GetEditState()!.Value;
        Assert.Equal("abc\nd\ne", state.Text);
        Assert.Equal((7, 7), (state.SelectionStart, state.SelectionEnd));
        Assert.Equal("abc\nd\ne", document.GetFormState(document.GetElement("t")!)!.Value.Value);
    }

    [Fact]
    public async Task TheCaretOfASecondLineSitsBelowTheFirst()
    {
        var (_, document, measuring) = await LoadAsync("<textarea id=t>first\nsecond</textarea>");

        var first = document.GetEditPlace(measuring, 2)!.Value;
        var second = document.GetEditPlace(measuring, 8)!.Value;

        Assert.Equal(first.PageIndex, second.PageIndex);
        Assert.True(second.Bounds.Y > first.Bounds.Y + (first.Bounds.Height / 2));
        Assert.True(second.Bounds.X > first.Bounds.X);
    }

    [Fact]
    public async Task APointPicksALineAndAPositionOnIt()
    {
        var (_, document, measuring) = await LoadAsync("<textarea id=t>first\nsecond</textarea>");
        var origin = document.GetEditPlace(measuring, 0)!.Value;
        var line = origin.Bounds.Height;

        Assert.Equal(0, document.GetEditIndexAt(measuring, origin.PageIndex, origin.Bounds.X, origin.Bounds.Y + 1));
        Assert.Equal(6, document.GetEditIndexAt(measuring, origin.PageIndex, origin.Bounds.X, origin.Bounds.Y + line + 1));
        Assert.Equal(12, document.GetEditIndexAt(measuring, origin.PageIndex, origin.Bounds.X + 190, origin.Bounds.Y + line + 1));
        Assert.Equal(0, document.GetEditIndexAt(measuring, origin.PageIndex, origin.Bounds.X));
    }

    [Fact]
    public async Task TheCaretMovesByLineKeepingItsColumnAndStopsAtTheEnds()
    {
        var (_, document, measuring) = await LoadAsync("<textarea id=t>abcd\nef\nabcdef</textarea>");

        Assert.Equal(7, document.GetEditIndexByLine(measuring, 3, 1));
        Assert.Equal(10, document.GetEditIndexByLine(measuring, 7, 1));
        Assert.Equal(4, document.GetEditIndexByLine(measuring, 14, -2));
        Assert.Equal(0, document.GetEditIndexByLine(measuring, 2, -1));
        Assert.Equal(14, document.GetEditIndexByLine(measuring, 14, 1));
        Assert.Equal((5, 7), document.GetEditLineRange(measuring, 6));
        Assert.Equal((8, 14), document.GetEditLineRange(measuring, 99));
    }

    [Fact]
    public async Task OnlyATextAreaMovesByLine()
    {
        var (_, document, measuring) = await LoadAsync("<textarea id=t>a</textarea><input id=i value=abc>");
        await document.SetFocusAsync(document.GetElement("i"), true, measuring);

        Assert.Null(document.GetEditIndexByLine(measuring, 1, 1));
        Assert.Null(document.GetEditLineRange(measuring, 1));
    }

    [Fact]
    public async Task ASelectionAcrossLinesIsHighlightedOnEachOfThem()
    {
        var (context, document, _) = await LoadAsync("<textarea id=t>HHHH\nHHHH\nHHHH</textarea>");
        document.SetEditSelection(2, 13);

        var canvas = await TestDocument.PaintAsync(context, document);

        // 20pt text, about 24pt a line, inside a box at 10pt: the three lines each carry the highlight.
        Assert.True(Count(canvas, 14, 14, 150, 24, IsBlueish) > 20);
        Assert.True(Count(canvas, 14, 40, 150, 24, IsBlueish) > 20);
        Assert.True(Count(canvas, 14, 64, 150, 24, IsBlueish) > 20);
    }

    [Fact]
    public async Task ACompositionIsUnderlinedAndTheCaretIsDrawnOnItsLine()
    {
        var (context, document, _) = await LoadAsync("<textarea id=t>HH\nHH</textarea>");
        document.SetEditSelection(4, 4);
        document.SetComposition(3, 5);
        var composing = await TestDocument.PaintAsync(context, document);

        document.SetComposition(-1, -1);
        document.SetEditSelection(4, 4);
        var plain = await TestDocument.PaintAsync(context, document);

        var dark = ((byte R, byte G, byte B, byte A) p) => p.R < 100 && p.G < 100 && p.B < 100;
        Assert.True(Count(composing, 14, 14, 190, 80, dark) > Count(plain, 14, 14, 190, 80, dark));

        // The caret of the second line: a dark column to the right of its text, on a line with no glyph there.
        document.SetEditSelection(5, 5);
        var caret = await TestDocument.PaintAsync(context, document);
        Assert.True(Count(caret, 14, 38, 100, 24, dark) > 0);
    }

    [Fact]
    public async Task AnUnfocusedTextAreaDrawsNoCaretOrHighlight()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<textarea id=t>HHHH</textarea>", Head), new HtmlDocumentOptions { Media = "screen", PaintFormControls = true });

        var canvas = await TestDocument.PaintAsync(context, document);

        Assert.Equal(0, Count(canvas, 14, 14, 190, 80, IsBlueish));
    }
}

using PeachBrackets.Text;
using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class InteractionStyleTests
{
    private static readonly HtmlDocumentOptions WithRuns = new() { RecordTextRuns = true };

    private static (double X, double Y) Center(HtmlDocument document, string id)
    {
        var bounds = document.GetElementBounds(id)!.Value;
        return (bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    [Fact]
    public async Task UserSelectNone_KeepsASelectionFromStartingOnTheText()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p>Selectable <span id=n style='user-select:none'>numbered</span> <span id=i>inside</span></p>"), WithRuns);

        var (x, y) = Center(document, "n");
        Assert.Null(document.HitTest(0, x, y));
        Assert.Null(document.GetCaretPosition(0, x, y));

        (x, y) = Center(document, "i");
        Assert.NotNull(document.HitTest(0, x, y));
    }

    [Fact]
    public async Task UserSelectNone_IsLeftOutOfTheTextThatIsRead_AndItIsInheritedThroughAuto()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p>keep <span style='user-select:none'>drop <b>also</b></span> end</p>"), WithRuns);

        var text = document.GetText(new TextPosition(0, 0), new TextPosition(document.TextRuns.Count - 1, document.TextRuns[^1].Text.Length));

        Assert.Contains("keep", text);
        Assert.Contains("end", text);
        Assert.DoesNotContain("drop", text);
        Assert.DoesNotContain("also", text);
    }

    [Fact]
    public async Task OverTextThatCannotBeSelected_AutoIsNotTheTextPointer()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p><span id=s>selectable</span> <span id=n style='user-select:none'>locked</span></p>"), WithRuns);

        var (x, y) = Center(document, "s");
        Assert.Equal(CursorShape.Text, document.GetCursorAt(0, x, y).Shape);

        (x, y) = Center(document, "n");
        Assert.Equal(CursorShape.Default, document.GetCursorAt(0, x, y).Shape);
    }

    [Fact]
    public async Task TheCaretIsTheTextColorUnlessCaretColorSaysOtherwise()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p style='color:rgb(0,0,255)'>blue <span style='caret-color:rgb(255,0,0)'>red</span> <span style='caret-color:currentcolor'>same</span></p>"), WithRuns);

        var text = document.TextRuns[0].Text;
        Assert.Equal(PaintColor.FromArgb(255, 0, 0, 255), document.GetCaretColor(new TextPosition(0, 1)));
        Assert.Equal(PaintColor.FromArgb(255, 255, 0, 0), document.GetCaretColor(new TextPosition(0, text.IndexOf("red", StringComparison.Ordinal) + 1)));
        Assert.Equal(PaintColor.FromArgb(255, 0, 0, 255), document.GetCaretColor(new TextPosition(0, text.IndexOf("same", StringComparison.Ordinal) + 1)));
    }

    private static bool HasRedInk(RasterCanvas canvas)
    {
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                var (r, g, b, a) = TestDocument.Pixel(canvas, x, y);
                if (a > 0 && r > 180 && g < 80 && b < 80)
                    return true;
            }
        }

        return false;
    }

    [Theory]
    [InlineData("checkbox", "checked", true)]
    [InlineData("radio", "checked", true)]
    [InlineData("checkbox", "checked", false)]
    public async Task AccentColor_TintsTheMarkOfACheckedControl(string type, string state, bool accent)
    {
        var style = accent ? "accent-color:rgb(255,0,0);" : "";
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap($"<input type={type} {state} style='margin:4px;width:20px;height:20px;{style}'>"), new HtmlDocumentOptions { CollectFormFields = true });

        using var canvas = await TestDocument.PaintAsync(context, document);

        Assert.Equal(accent, HasRedInk(canvas));
    }
}

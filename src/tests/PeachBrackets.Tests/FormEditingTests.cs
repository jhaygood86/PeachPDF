using PeachDrawing;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class FormEditingTests
{
    private const string Body = """
        <input id=name value='Ada' style='width:150pt;font-size:20pt'>
        <input id=short maxlength=4 value='ab' style='width:150pt'>
        <input id=locked readonly value='fixed'>
        <input id=secret type=password value='abc' style='width:150pt;font-size:20pt'>
        <input id=agree type=checkbox>
        <p id=text>plain</p>
        """;

    private static async Task<(RasterRenderContext Context, HtmlDocument Document, RasterCanvas Measuring)> LoadAsync(string body = Body, string head = "")
    {
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap(body, "<style>@page { margin: 0 } input { display: block; margin: 0 }</style>" + head), new HtmlDocumentOptions { MediaType = MediaType.Screen, PaintFormControls = true });
        return (context, document, context.CreateCanvas(1, 1));
    }

    private static ValueTask<PeachBrackets.Updates.UpdateResult> Focus(HtmlDocument document, RasterCanvas measuring, string id) =>
        document.SetFocusAsync(document.GetElement(id), focusVisible: true, measuring);

    [Fact]
    public async Task AnythingButATextFieldHasNothingToEdit()
    {
        var (_, document, measuring) = await LoadAsync();

        Assert.Null(document.GetEditState());
        Assert.False(document.InsertText("x").Applied);

        await Focus(document, measuring, "agree");
        Assert.Null(document.GetEditState());
        await Focus(document, measuring, "text");
        Assert.Null(document.GetEditState());
        Assert.Null(document.GetEditPlace(measuring));
        Assert.False(document.SetEditSelection(0, 1).Applied);
        Assert.False(document.SetComposition(0, 1).Applied);
        Assert.False(document.DeleteEditText(true).Applied);
        Assert.False(document.ReplaceEditText(0, 0, "x").Applied);
    }

    [Fact]
    public async Task AFocusedFieldStartsWithItsCaretAtTheEndOfItsText()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "name");

        var state = document.GetEditState()!.Value;
        Assert.Equal(("Ada", 3, 3, -1, -1, "text", -1, false), (state.Text, state.SelectionStart, state.SelectionEnd, state.CompositionStart, state.CompositionEnd, state.Type, state.MaxLength, state.ReadOnly));
    }

    [Fact]
    public async Task TypingReplacesTheSelectionAndMovesTheCaret()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "name");

        Assert.True(document.SetEditSelection(1, 3).Applied);
        var result = document.InsertText("X\r\nY");
        Assert.True(result.Applied);
        Assert.NotEmpty(result.DirtyRegions);

        var state = document.GetEditState()!.Value;
        Assert.Equal(("AXY", 3, 3), (state.Text, state.SelectionStart, state.SelectionEnd));
        Assert.Equal("AXY", document.GetFormState(document.GetElement("name")!)!.Value.Value);
    }

    [Fact]
    public async Task ASelectionGivenBackwardsOrPastTheEndIsClamped()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "name");

        document.SetEditSelection(99, 1);
        var state = document.GetEditState()!.Value;
        Assert.Equal((3, 1), (state.SelectionStart, state.SelectionEnd));

        // Typing replaces it from its start to its end however it was given.
        document.InsertText("Q");
        Assert.Equal("AQ", document.GetEditState()!.Value.Text);
    }

    [Fact]
    public async Task BackspaceAndDeleteTakeAWholeGraphemeOrTheSelection()
    {
        var (_, document, measuring) = await LoadAsync("<input id=f value='é😀z' style='width:150pt'>");
        await Focus(document, measuring, "f");

        document.SetEditSelection(4, 4);
        Assert.True(document.DeleteEditText(backward: true).Applied);
        Assert.Equal("éz", document.GetEditState()!.Value.Text);

        document.SetEditSelection(0, 0);
        Assert.True(document.DeleteEditText(backward: false).Applied);
        Assert.Equal("z", document.GetEditState()!.Value.Text);

        document.SetEditSelection(0, 1);
        document.DeleteEditText(backward: true);
        Assert.Equal(string.Empty, document.GetEditState()!.Value.Text);
        Assert.False(document.DeleteEditText(backward: true).Applied);
        Assert.False(document.DeleteEditText(backward: false).Applied);
    }

    [Fact]
    public async Task MaxLengthCutsWhatIsTypedWithoutSplittingASurrogatePair()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "short");

        document.InsertText("cdefg");
        Assert.Equal("abcd", document.GetEditState()!.Value.Text);
        Assert.Equal(4, document.GetEditState()!.Value.MaxLength);

        document.SetEditSelection(0, 4);
        document.InsertText("a😀😀");
        Assert.Equal("a😀", document.GetEditState()!.Value.Text);

        document.SetEditSelection(0, 4);
        document.InsertText("zz");
        document.ReplaceEditText(0, 0, "  ", caret: 1);
        Assert.Equal(("  zz", 1), (document.GetEditState()!.Value.Text, document.GetEditState()!.Value.SelectionEnd));
    }

    [Fact]
    public async Task AReadOnlyFieldCanBeSelectedButNotChanged()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "locked");

        Assert.True(document.GetEditState()!.Value.ReadOnly);
        Assert.True(document.SetEditSelection(0, 2).Applied);
        Assert.False(document.InsertText("x").Applied);
        Assert.Equal("fixed", document.GetEditState()!.Value.Text);
    }

    [Fact]
    public async Task TheTextAnInputMethodIsComposingIsReplacedByWhatItCommits()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "name");

        document.InsertText("ni");
        Assert.True(document.SetComposition(3, 5).Applied);
        var composing = document.GetEditState()!.Value;
        Assert.Equal((3, 5), (composing.CompositionStart, composing.CompositionEnd));

        // The commit takes the place of the composition, not of the caret.
        document.InsertText("你");
        var committed = document.GetEditState()!.Value;
        Assert.Equal(("Ada你", -1), (committed.Text, committed.CompositionStart));

        document.SetComposition(0, 1);
        Assert.True(document.SetComposition(-1, -1).Applied);
        Assert.Equal(-1, document.GetEditState()!.Value.CompositionStart);
    }

    [Fact]
    public async Task SettingTheValueFromOutsideEndsAComposition()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "name");
        document.SetComposition(0, 2);

        Assert.True(document.SetValue(document.GetElement("name")!, "Lin").Applied);

        var state = document.GetEditState()!.Value;
        Assert.Equal(("Lin", -1, 3), (state.Text, state.CompositionStart, state.SelectionEnd));
    }

    [Fact]
    public async Task FocusMovingToAnotherFieldStartsItsCaretAtItsEnd()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "name");
        document.SetEditSelection(0, 1);

        await Focus(document, measuring, "short");
        var state = document.GetEditState()!.Value;
        Assert.Equal(("ab", 2, 2), (state.Text, state.SelectionStart, state.SelectionEnd));
    }

    [Fact]
    public async Task TheCaretSitsAfterTheTextBeforeItAndMovesRightAsTheCaretDoes()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "name");

        var atEnd = document.GetEditPlace(measuring)!.Value;
        var atStart = document.GetEditPlace(measuring, 0)!.Value;
        var middle = document.GetEditPlace(measuring, 1)!.Value;

        Assert.Equal(0, atEnd.PageIndex);
        Assert.True(atStart.Bounds.X < middle.Bounds.X);
        Assert.True(middle.Bounds.X < atEnd.Bounds.X);
        Assert.Equal(atStart.Bounds.Y, atEnd.Bounds.Y);
        Assert.True(atEnd.Bounds.Height > 10);

        // Past the end is the end.
        Assert.Equal(atEnd.Bounds.X, document.GetEditPlace(measuring, 99)!.Value.Bounds.X);
    }

    [Fact]
    public async Task APasswordIsMeasuredAsTheBulletsItShows()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "secret");
        var password = document.GetEditPlace(measuring, 2)!.Value.Bounds.X;
        var start = document.GetEditPlace(measuring, 0)!.Value.Bounds.X;

        await Focus(document, measuring, "name");
        var plain = document.GetEditPlace(measuring, 2)!.Value.Bounds.X;

        Assert.NotEqual(plain - start, password - start);
    }

    private static int Dark(RasterCanvas canvas, int x, int y, int width, int height) =>
        Enumerable.Range(y, height).Sum(j => Enumerable.Range(x, width).Count(i => TestDocument.Pixel(canvas, i, j) is { R: < 120, G: < 120 }));

    [Fact]
    public async Task TheFocusedFieldDrawsACaretThatBlinksAndASelection()
    {
        var (context, document, measuring) = await LoadAsync("<input id=f value='' style='width:150pt;height:30pt;font-size:20pt;border:0;padding:0;color:rgb(0,0,0)'>");
        await Focus(document, measuring, "f");

        var withCaret = Dark(await TestDocument.PaintAsync(context, document), 0, 0, 40, 30);
        Assert.True(withCaret > 5, "the caret is drawn in an empty focused field");

        Assert.True(document.SetCaretVisible(false).Applied);
        Assert.False(document.SetCaretVisible(false).Applied);
        Assert.Equal(0, Dark(await TestDocument.PaintAsync(context, document), 0, 0, 40, 30));

        document.SetCaretVisible(true);
        document.InsertText("HHH");
        document.SetEditSelection(0, 3);
        var selection = await TestDocument.PaintAsync(context, document);
        var blue = Enumerable.Range(0, 30).Sum(j => Enumerable.Range(0, 60).Count(i => TestDocument.Pixel(selection, i, j) is { B: > 150, R: < 200 }));
        Assert.True(blue > 50, "the selection is highlighted");
    }

    [Fact]
    public async Task ACompositionIsUnderlinedAndAnUnfocusedFieldDrawsNeither()
    {
        var (context, document, measuring) = await LoadAsync("<input id=f value='HHH' style='width:150pt;height:30pt;font-size:20pt;border:0;padding:0;color:rgb(0,0,0)'><input id=g value=''>");
        var unfocused = Dark(await TestDocument.PaintAsync(context, document), 0, 0, 80, 30);

        await Focus(document, measuring, "f");
        document.SetCaretVisible(false);
        var plain = Dark(await TestDocument.PaintAsync(context, document), 0, 0, 80, 30);
        document.SetComposition(0, 3);
        var underlined = Dark(await TestDocument.PaintAsync(context, document), 0, 0, 80, 30);

        Assert.Equal(unfocused, plain);
        Assert.True(underlined > plain);
    }

    [Fact]
    public async Task WhatAControlHoldsSurvivesLoadingTheSourceAgainForAFocusRule()
    {
        // A :focus rule makes a change of focus load the source again, which would put every control back as the source has it.
        var (_, document, measuring) = await LoadAsync(Body, "<style>input:focus { outline: 2px solid red }</style>");
        await Focus(document, measuring, "name");
        document.SetEditSelection(0, 3);
        document.InsertText("Grace");
        document.SetChecked(document.GetElement("agree")!, true);

        await Focus(document, measuring, "agree");
        await Focus(document, measuring, "name");

        Assert.Equal("Grace", document.GetFormState(document.GetElement("name")!)!.Value.Value);
        Assert.True(document.GetFormState(document.GetElement("agree")!)!.Value.Checked);
    }

    [Fact]
    public async Task AClickIsRoundedToTheNearestEdgeBetweenCharacters()
    {
        var (_, document, measuring) = await LoadAsync();
        await Focus(document, measuring, "name");
        var start = document.GetEditPlace(measuring, 0)!.Value.Bounds.X;
        var one = document.GetEditPlace(measuring, 1)!.Value.Bounds.X;
        var two = document.GetEditPlace(measuring, 2)!.Value.Bounds.X;

        Assert.Equal(0, document.GetEditIndexAt(measuring, 0, start - 30));
        Assert.Equal(0, document.GetEditIndexAt(measuring, 0, start + ((one - start) * 0.2)));
        Assert.Equal(1, document.GetEditIndexAt(measuring, 0, start + ((one - start) * 0.8)));
        Assert.Equal(2, document.GetEditIndexAt(measuring, 0, two + 1));
        Assert.Equal(3, document.GetEditIndexAt(measuring, 0, 5000));
        Assert.Null(document.GetEditIndexAt(measuring, 1, start));

        await Focus(document, measuring, "agree");
        Assert.Null(document.GetEditIndexAt(measuring, 0, start));
    }
}

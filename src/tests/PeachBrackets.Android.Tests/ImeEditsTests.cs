using PeachBrackets.Android.Logic;

namespace PeachBrackets.Android.Tests;

public class ImeEditsTests
{
    [Fact]
    public void CommittedTextReplacesTheSelectionAndTheCaretGoesAfterIt()
    {
        var edit = ImeEdits.Commit("xy", 1, 10, 2, 5, -1, -1);

        Assert.Equal(new ImeEdit(2, 5, "xy", 4, -1, -1), edit);
    }

    [Fact]
    public void CommittedTextReplacesTheCompositionBeforeTheSelection()
    {
        var edit = ImeEdits.Commit("ab", 1, 10, 9, 9, 3, 6);

        Assert.Equal((3, 6, 5), (edit.Start, edit.End, edit.Caret));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 6)]
    [InlineData(0, 3)]
    [InlineData(-2, 1)]
    public void TheNewCursorIsCountedFromTheEndOfTheTextWhenPositiveAndFromItsStartWhenNot(int position, int expectedCaret) =>
        Assert.Equal(expectedCaret, ImeEdits.Commit("ab", position, 10, 3, 3, -1, -1).Caret);

    [Fact]
    public void TheCaretStaysInsideTheNewText()
    {
        Assert.Equal(0, ImeEdits.Commit("a", -50, 4, 2, 2, -1, -1).Caret);
        Assert.Equal(5, ImeEdits.Commit("a", 50, 4, 2, 2, -1, -1).Caret);
    }

    [Fact]
    public void ComposingTextIsTheCompositionUntilItIsCommitted()
    {
        var first = ImeEdits.Compose("n", 1, 3, 3, 3, -1, -1);
        Assert.Equal((3, 3, 4, 3, 4), (first.Start, first.End, first.Caret, first.CompositionStart, first.CompositionEnd));

        // The next keystroke replaces what is being composed, not what came before it.
        var second = ImeEdits.Compose("ni", 1, 4, 4, 4, 3, 4);
        Assert.Equal((3, 4, 5, 3, 5), (second.Start, second.End, second.Caret, second.CompositionStart, second.CompositionEnd));

        var commit = ImeEdits.Commit("你", 1, 5, 5, 5, 3, 5);
        Assert.Equal((3, 5, 4, -1, -1), (commit.Start, commit.End, commit.Caret, commit.CompositionStart, commit.CompositionEnd));
    }

    [Fact]
    public void ComposingNothingEndsTheComposition()
    {
        var edit = ImeEdits.Compose(string.Empty, 1, 5, 5, 5, 3, 5);

        Assert.Equal((3, 5, 3, -1, -1), (edit.Start, edit.End, edit.Caret, edit.CompositionStart, edit.CompositionEnd));
    }

    [Fact]
    public void DeletingAroundTheCaretRemovesTheTextOnBothSidesAndLeavesTheSelection()
    {
        var (after, before, caret) = ImeEdits.DeleteAround(10, 4, 6, 2, 3);

        Assert.Equal(((6, 9), (2, 4), 2), (after, before, caret));
    }

    [Fact]
    public void DeletingAroundStopsAtTheEndsOfTheText()
    {
        var (after, before, caret) = ImeEdits.DeleteAround(5, 1, 1, 9, 9);

        Assert.Equal(((1, 5), (0, 1), 0), (after, before, caret));
    }

    [Theory]
    [InlineData("text", 1, 6)]
    [InlineData("search", 1, 3)]
    [InlineData("url", 0x11, 2)]
    [InlineData("email", 0x21, 6)]
    [InlineData("password", 0x81, 6)]
    [InlineData("tel", 3, 6)]
    [InlineData("number", 0x3002, 6)]
    public void EachKindOfFieldAsksTheKeyboardForItsLayoutAndAction(string type, int inputType, int action) =>
        Assert.Equal((inputType, action), ImeEdits.InputTypeFor(type));
}

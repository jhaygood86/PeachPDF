using PeachBrackets.WinUI;

namespace PeachBrackets.WinUI.Tests;

public class HtmlViewEditingTests
{
    [Theory]
    [InlineData("hello world", 5, "Left", 4)]
    [InlineData("hello world", 5, "Right", 6)]
    [InlineData("hello world", 0, "Left", 0)]
    [InlineData("hello world", 11, "Right", 11)]
    [InlineData("hello world", 8, "Home", 0)]
    [InlineData("hello world", 2, "End", 11)]
    [InlineData("hello world", 11, "WordLeft", 6)]
    [InlineData("hello world", 6, "WordLeft", 0)]
    [InlineData("hello world", 0, "WordRight", 6)]
    [InlineData("hello world", 6, "WordRight", 11)]
    [InlineData("hello   world", 5, "WordLeft", 0)]
    [InlineData("a, b", 4, "WordLeft", 3)]
    [InlineData("hello", 99, "Left", 4)]
    public void TheCaretMovesByCharacterWordAndLine(string text, int from, string move, int expected) =>
        Assert.Equal(expected, HtmlViewEditing.MoveIndex(text, from, Enum.Parse<EditMove>(move)));

    [Fact]
    public void TheCaretStepsOverAWholeGraphemeCluster()
    {
        const string text = "é😀z";
        Assert.Equal(2, HtmlViewEditing.MoveIndex(text, 0, EditMove.Right));
        Assert.Equal(4, HtmlViewEditing.MoveIndex(text, 2, EditMove.Right));
        Assert.Equal(2, HtmlViewEditing.MoveIndex(text, 4, EditMove.Left));
        Assert.Equal(0, HtmlViewEditing.MoveIndex(text, 2, EditMove.Left));
    }

    [Fact]
    public void ShiftGrowsTheSelectionFromTheEndThatStays()
    {
        Assert.Equal((2, 3), HtmlViewEditing.Move("hello", 2, 2, EditMove.Right, extend: true));
        Assert.Equal((2, 1), HtmlViewEditing.Move("hello", 2, 2, EditMove.Left, extend: true));
        Assert.Equal((2, 5), HtmlViewEditing.Move("hello", 2, 3, EditMove.End, extend: true));
    }

    [Fact]
    public void WithoutShiftASelectionCollapsesToTheEdgeTheMoveHeadsFor()
    {
        Assert.Equal((1, 1), HtmlViewEditing.Move("hello", 1, 4, EditMove.Left, extend: false));
        Assert.Equal((4, 4), HtmlViewEditing.Move("hello", 4, 1, EditMove.Right, extend: false));
        Assert.Equal((0, 0), HtmlViewEditing.Move("hello", 1, 4, EditMove.Home, extend: false));
        Assert.Equal((3, 3), HtmlViewEditing.Move("hello", 2, 2, EditMove.Right, extend: false));
    }

    [Theory]
    [InlineData("a\r\nb", "a b")]
    [InlineData("a\nb\rc", "a b c")]
    [InlineData("plain", "plain")]
    public void APasteIntoOneLineKeepsItsWordsApart(string pasted, string expected) =>
        Assert.Equal(expected, HtmlViewEditing.ForSingleLine(pasted));

    [Theory]
    [InlineData('a', true)]
    [InlineData(' ', true)]
    [InlineData('你', true)]
    [InlineData('\b', false)]
    [InlineData('\t', false)]
    [InlineData('\r', false)]
    [InlineData('\u001b', false)]
    public void OnlyTextIsTypedIntoAField(char character, bool expected) =>
        Assert.Equal(expected, HtmlViewEditing.IsTypedText(character));
}

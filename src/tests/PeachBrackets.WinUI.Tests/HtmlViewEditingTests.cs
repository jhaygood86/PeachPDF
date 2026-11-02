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

    [Fact]
    public void ATextAreaKeepsLineBreaksAndEveryOtherFieldIsOneLine()
    {
        var lf = ((char)10).ToString();
        Assert.Equal("a" + lf + "b" + lf + "c", HtmlViewEditing.ForField("textarea", "a\r\nb\rc"));
        Assert.Equal("a b c", HtmlViewEditing.ForField("text", "a\r\nb\nc"));
    }

    [Fact]
    public void TheLineKeysAskForTheLineAboveBelowAndTheEndsOfTheLine()
    {
        Func<int, int?> byLine = lines => 100 + lines;
        Func<int, (int Start, int End)?> range = caret => (caret - 1, caret + 1);

        Assert.Equal(99, HtmlViewEditing.LineMove("Up", 5, byLine, range));
        Assert.Equal(101, HtmlViewEditing.LineMove("Down", 5, byLine, range));
        Assert.Equal(4, HtmlViewEditing.LineMove("Home", 5, byLine, range));
        Assert.Equal(6, HtmlViewEditing.LineMove("End", 5, byLine, range));
        Assert.Null(HtmlViewEditing.LineMove("Left", 5, byLine, range));
        Assert.Null(HtmlViewEditing.LineMove("Home", 5, byLine, _ => null));
    }

    [Theory]
    [InlineData("hello world", 2, 0, 5)]
    [InlineData("hello world", 5, 0, 5)]
    [InlineData("hello world", 6, 6, 11)]
    [InlineData("hello, world", 6, 6, 6)]
    [InlineData("", 0, 0, 0)]
    [InlineData("ab", 99, 0, 2)]
    public void ALongPressSelectsTheWordItIsOn(string text, int index, int start, int end) =>
        Assert.Equal((start, end), HtmlViewEditing.WordAt(text, index));
}

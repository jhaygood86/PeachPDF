using System;
using System.Globalization;

namespace PeachBrackets.WinUI;

/// <summary>A way the caret of a text field moves.</summary>
internal enum EditMove
{
    Left,
    Right,
    WordLeft,
    WordRight,
    Home,
    End,
}

/// <summary>The caret and selection arithmetic of a text field, kept apart from the window so it can be tested without one.</summary>
internal static class HtmlViewEditing
{
    /// <summary>Where the caret goes from <paramref name="index"/> when it moves: a whole grapheme cluster for Left and Right, a word for the word moves.</summary>
    public static int MoveIndex(string text, int index, EditMove move)
    {
        index = Math.Clamp(index, 0, text.Length);
        switch (move)
        {
            case EditMove.Home:
                return 0;
            case EditMove.End:
                return text.Length;
            case EditMove.Left:
                return PreviousCluster(text, index);
            case EditMove.Right:
                return index >= text.Length ? text.Length : index + StringInfo.GetNextTextElementLength(text, index);
            case EditMove.WordLeft:
                while (index > 0 && !IsWordCharacter(text[index - 1]))
                    index = PreviousCluster(text, index);
                while (index > 0 && IsWordCharacter(text[index - 1]))
                    index = PreviousCluster(text, index);
                return index;
            default:
                while (index < text.Length && IsWordCharacter(text[index]))
                    index += StringInfo.GetNextTextElementLength(text, index);
                while (index < text.Length && !IsWordCharacter(text[index]))
                    index += StringInfo.GetNextTextElementLength(text, index);
                return index;
        }
    }

    /// <summary>
    /// The selection after a move: the caret (<paramref name="end"/>) goes where the move says. With <paramref name="extend"/> the other end
    /// (<paramref name="start"/>) stays, so the selection grows; without it a selection collapses to the edge the move heads for, as in any text box.
    /// </summary>
    public static (int Start, int End) Move(string text, int start, int end, EditMove move, bool extend)
    {
        if (!extend && start != end && move is EditMove.Left or EditMove.Right)
        {
            var edge = move == EditMove.Left ? Math.Min(start, end) : Math.Max(start, end);
            return (edge, edge);
        }

        var caret = MoveIndex(text, end, move);
        return extend ? (start, caret) : (caret, caret);
    }

    /// <summary>The text a paste puts in a single-line field: line breaks become spaces, so a pasted paragraph is not run together.</summary>
    public static string ForSingleLine(string text) =>
        text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>Whether a character typed is text for a field, not a control character the keyboard also reports (Backspace, Tab, Enter, Escape).</summary>
    public static bool IsTypedText(char character) => !char.IsControl(character);

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';

    private static int PreviousCluster(string text, int index)
    {
        if (index <= 0)
            return 0;

        var previous = 0;
        foreach (var start in StringInfo.ParseCombiningCharacters(text))
        {
            if (start >= index)
                break;

            previous = start;
        }

        return previous;
    }
}

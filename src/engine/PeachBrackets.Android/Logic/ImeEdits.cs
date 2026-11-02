using System;

namespace PeachBrackets.Android.Logic;

/// <summary>
/// What an input method's edit does to the text of a field: the range it replaces, the text that goes in, where the caret ends up and which part is
/// still being composed. All positions are UTF-16 indexes; a composition of -1 means none.
/// </summary>
internal readonly record struct ImeEdit(int Start, int End, string Text, int Caret, int CompositionStart, int CompositionEnd);

/// <summary>
/// The arithmetic of Android's <c>InputConnection</c> edits (<c>commitText</c>, <c>setComposingText</c>, <c>deleteSurroundingText</c>), kept apart from
/// the view so it can be tested without a device. The rules are those of the platform: an edit replaces the composing region if there is one and the
/// selection if not, and the new cursor position is counted from the end of the new text when positive and from its start when not.
/// </summary>
internal static class ImeEdits
{
    /// <summary>The edit for <c>commitText</c>: the text replaces the composition (or the selection) and nothing is left composing.</summary>
    public static ImeEdit Commit(string text, int newCursorPosition, int length, int selectionStart, int selectionEnd, int compositionStart, int compositionEnd) =>
        Replace(text, newCursorPosition, length, selectionStart, selectionEnd, compositionStart, compositionEnd, composing: false);

    /// <summary>The edit for <c>setComposingText</c>: the text replaces the composition (or the selection) and is itself the composition now.</summary>
    public static ImeEdit Compose(string text, int newCursorPosition, int length, int selectionStart, int selectionEnd, int compositionStart, int compositionEnd) =>
        Replace(text, newCursorPosition, length, selectionStart, selectionEnd, compositionStart, compositionEnd, composing: true);

    /// <summary>
    /// The two ranges <c>deleteSurroundingText</c> removes, in the order to remove them (the one after the selection first, so the other keeps its
    /// place), and where the caret is afterwards. The selection itself stays.
    /// </summary>
    public static ((int Start, int End) After, (int Start, int End) Before, int Caret) DeleteAround(
        int length, int selectionStart, int selectionEnd, int before, int after)
    {
        var from = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, length);
        var to = Math.Clamp(Math.Max(selectionStart, selectionEnd), from, length);
        var afterEnd = Math.Min(to + Math.Max(after, 0), length);
        var beforeStart = Math.Max(from - Math.Max(before, 0), 0);
        return ((to, afterEnd), (beforeStart, from), beforeStart);
    }

    /// <summary>The Android <c>InputType</c> and <c>ImeOptions</c> action for an <c>&lt;input&gt;</c> of this <c>type</c>.</summary>
    public static (int InputType, int ImeAction) InputTypeFor(string type) => type switch
    {
        "password" => (TextClass | PasswordVariation, ActionDone),
        "email" => (TextClass | EmailVariation, ActionDone),
        "url" => (TextClass | UriVariation, ActionGo),
        "tel" => (PhoneClass, ActionDone),
        "number" => (NumberClass | NumberDecimal | NumberSigned, ActionDone),
        "search" => (TextClass, ActionSearch),
        _ => (TextClass, ActionDone),
    };

    private const int TextClass = 0x1;
    private const int NumberClass = 0x2;
    private const int PhoneClass = 0x3;
    private const int UriVariation = 0x10;
    private const int EmailVariation = 0x20;
    private const int PasswordVariation = 0x80;
    private const int NumberSigned = 0x1000;
    private const int NumberDecimal = 0x2000;
    private const int ActionGo = 2;
    private const int ActionSearch = 3;
    private const int ActionDone = 6;

    private static ImeEdit Replace(
        string text, int newCursorPosition, int length, int selectionStart, int selectionEnd, int compositionStart, int compositionEnd, bool composing)
    {
        int start, end;
        if (compositionStart >= 0)
        {
            start = Math.Clamp(compositionStart, 0, length);
            end = Math.Clamp(compositionEnd, start, length);
        }
        else
        {
            start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, length);
            end = Math.Clamp(Math.Max(selectionStart, selectionEnd), start, length);
        }

        var newLength = length - (end - start) + text.Length;
        var caret = newCursorPosition > 0 ? start + text.Length + (newCursorPosition - 1) : start + newCursorPosition;
        caret = Math.Clamp(caret, 0, newLength);

        return composing && text.Length > 0
            ? new ImeEdit(start, end, text, caret, start, start + text.Length)
            : new ImeEdit(start, end, text, caret, -1, -1);
    }
}

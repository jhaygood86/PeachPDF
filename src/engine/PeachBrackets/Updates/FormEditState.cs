using PeachDrawing.Core;

namespace PeachBrackets.Updates;

/// <summary>
/// What the text field that has the focus holds and where its caret is (<see cref="HtmlDocument.GetEditState"/>). Positions are UTF-16 indexes into
/// <see cref="Text"/>, from 0 to its length.
/// </summary>
/// <param name="Text">the field's text</param>
/// <param name="SelectionStart">where the selection starts; the same as <paramref name="SelectionEnd"/> when it is only a caret</param>
/// <param name="SelectionEnd">where the selection ends, which is where the caret is</param>
/// <param name="CompositionStart">where the text an input method is still composing starts, or -1 when none is</param>
/// <param name="CompositionEnd">where it ends, or -1</param>
/// <param name="Type">the field's <c>type</c> in lower case (<c>text</c>, <c>password</c>, <c>email</c>...)</param>
/// <param name="MaxLength">its <c>maxlength</c>, or -1 when it has none</param>
/// <param name="ReadOnly">whether the field is read-only or disabled, so the text cannot be changed</param>
public readonly record struct FormEditState(
    string Text, int SelectionStart, int SelectionEnd, int CompositionStart, int CompositionEnd, string Type, int MaxLength, bool ReadOnly);

/// <summary>A place on a page of the document, in points from the top left of the sheet (<see cref="HtmlDocument.GetEditPlace"/>).</summary>
/// <param name="PageIndex">the zero-based index of the page</param>
/// <param name="Bounds">the rectangle</param>
public readonly record struct TextEditPlace(int PageIndex, Rect Bounds);

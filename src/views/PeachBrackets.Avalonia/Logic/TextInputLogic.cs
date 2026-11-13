using Avalonia.Input;
using Avalonia.Input.TextInput;

namespace PeachBrackets.Avalonia.Logic;

/// <summary>What an input method's preedit text does to the text of a field: the range it replaces, the text that goes in, where the caret ends up and what is still being composed.</summary>
internal readonly record struct PreeditEdit(int Start, int End, string Text, int Caret, int CompositionStart, int CompositionEnd);

/// <summary>The window-free parts of editing a text field: input method arithmetic, key shortcuts, the geometry of the caret and the options an input method is told.</summary>
internal static class TextInputLogic
{
    /// <summary>
    /// The edit a preedit string makes: it replaces the text being composed (or, when none is, the selection), and is itself the composition until it is empty.
    /// Null when the string is empty and there is nothing composed, so there is nothing to take out (a selection must not be deleted by an empty preedit).
    /// </summary>
    public static PreeditEdit? Preedit(string? preedit, int? cursor, string text, int selectionStart, int selectionEnd, int compositionStart, int compositionEnd)
    {
        var length = text.Length;
        int start, end;
        if (compositionStart >= 0)
        {
            start = Math.Clamp(compositionStart, 0, length);
            end = Math.Clamp(compositionEnd, start, length);
        }
        else
        {
            if (string.IsNullOrEmpty(preedit))
                return null;

            start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, length);
            end = Math.Clamp(Math.Max(selectionStart, selectionEnd), start, length);
        }

        preedit ??= string.Empty;
        var caret = start + Math.Clamp(cursor ?? preedit.Length, 0, preedit.Length);
        return preedit.Length == 0
            ? new PreeditEdit(start, end, string.Empty, start, -1, -1)
            : new PreeditEdit(start, end, preedit, caret, start, start + preedit.Length);
    }

    /// <summary>The text a typed or committed string puts in a field: line breaks follow the field's kind, and control characters (Tab, Escape...) are dropped.</summary>
    public static string TypedText(string type, string text)
    {
        var shaped = PeachBrackets.HtmlViewCore.CaretMoves.ForField(type, text);
        if (shaped.All(c => !char.IsControl(c) || (c == '\n' && type == "textarea")))
            return shaped;

        return new string(shaped.Where(c => !char.IsControl(c) || (c == '\n' && type == "textarea")).ToArray());
    }

    /// <summary>The key that is the shortcut modifier: Command on a Mac, Control elsewhere.</summary>
    public static bool IsShortcut(KeyModifiers modifiers, bool mac) => (modifiers & (mac ? KeyModifiers.Meta : KeyModifiers.Control)) != 0;

    /// <summary>The modifier that moves by word: Option on a Mac, Control elsewhere.</summary>
    public static bool IsWordMove(KeyModifiers modifiers, bool mac) => (modifiers & (mac ? KeyModifiers.Alt : KeyModifiers.Control)) != 0;

    /// <summary>
    /// A rectangle of a page, given in points, as the rectangle in the control's device independent pixels.
    /// </summary>
    /// <param name="pageTop">the top of the page in device independent pixels at zoom 1</param>
    /// <param name="x">the left edge, in points</param>
    /// <param name="y">the top edge, in points</param>
    /// <param name="width">the width, in points</param>
    /// <param name="height">the height, in points</param>
    /// <param name="pixelsPerPoint">device independent pixels per point at zoom 1</param>
    /// <param name="scale">the device pixels per device independent pixel at zoom 1 times the zoom</param>
    /// <param name="scrollX">how far the view is scrolled right, in device pixels</param>
    /// <param name="scrollY">how far the view is scrolled down, in device pixels</param>
    /// <param name="dpi">device pixels per device independent pixel</param>
    public static global::Avalonia.Rect ToControl(
        double pageTop, double x, double y, double width, double height, double pixelsPerPoint, double scale, double scrollX, double scrollY, double dpi) =>
        new(
            ((x * pixelsPerPoint * scale) - scrollX) / dpi,
            (((pageTop + (y * pixelsPerPoint)) * scale) - scrollY) / dpi,
            Math.Max(width * pixelsPerPoint * scale / dpi, 1),
            height * pixelsPerPoint * scale / dpi);

    /// <summary>How far a view has to scroll, in device pixels, so a rectangle of it (in its device independent pixels) is in view; 0 when it already is.</summary>
    public static (double X, double Y) ScrollToReveal(global::Avalonia.Rect rect, double viewWidth, double viewHeight, double dpi)
    {
        double Axis(double from, double to, double size)
        {
            if (from < 0)
                return from * dpi;

            return to > size ? (to - size) * dpi : 0;
        }

        return (Axis(rect.Left, rect.Right, viewWidth), Axis(rect.Top, rect.Bottom, viewHeight));
    }

    /// <summary>The content type an input method is told for a field of this <c>type</c>.</summary>
    public static TextInputContentType ContentTypeFor(string type) => type switch
    {
        "password" => TextInputContentType.Password,
        "email" => TextInputContentType.Email,
        "url" => TextInputContentType.Url,
        "tel" => TextInputContentType.Digits,
        "number" => TextInputContentType.Number,
        "search" => TextInputContentType.Search,
        _ => TextInputContentType.Normal,
    };

    /// <summary>Whether a field of this <c>type</c> holds text an input method must not learn or suggest from.</summary>
    public static bool IsSensitive(string type) => type == "password";

    /// <summary>Whether an element's form control type is one whose text is edited in the page.</summary>
    public static bool IsTextField(string type) => type is "text" or "search" or "url" or "tel" or "email" or "password" or "number" or "textarea";
}

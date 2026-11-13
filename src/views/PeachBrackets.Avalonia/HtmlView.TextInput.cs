using Avalonia.Input;

namespace PeachBrackets.Avalonia;

// Editing the text of <input> and <textarea>: caret, selection inside the field, typing, IME composition and the cut/copy/paste menu.
public sealed partial class HtmlView
{
    // A press on a text field: true when it was one, in which case the press is a drag inside the field and not a click on the page.
    private bool PressInTextField(int page, double x, double y, int clickCount) => false;

    private void DragInTextField(int page, double x, double y)
    {
    }

    // A press of the secondary button or a long press: the cut/copy/paste menu.
    private void OnSecondaryPress(PointerPressedEventArgs e, global::Avalonia.Point position)
    {
    }

    // The keys a focused text field edits with: true when the key was used.
    private bool HandleEditKey(KeyEventArgs e) => false;
}

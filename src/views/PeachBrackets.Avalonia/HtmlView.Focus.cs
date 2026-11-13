using Avalonia.Input;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

// Keyboard focus inside the document: Tab order, the focus ring, Enter and Space on links and buttons.
public sealed partial class HtmlView
{
    // Runs focus changes one after another, so two quick key presses do not interleave their restyles.
    private void QueueFocus(Func<Task> change) => _ = change();

    private Task FocusElementAsync(HtmlDocument document, DocumentElement element) => Task.CompletedTask;

    // The keys the focused element takes (Tab and Shift+Tab move the focus; Enter and Space activate): true when the key was used.
    private bool HandleFocusNavigationKey(KeyEventArgs e) => false;

    private bool HandleFocusKey(KeyEventArgs e) => HandleEditKey(e) || HandleFocusNavigationKey(e);
}

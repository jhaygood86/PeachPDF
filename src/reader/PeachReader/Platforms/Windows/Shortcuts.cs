using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace PeachReader.WinUI;

/// <summary>
/// The keyboard shortcuts of the reader, registered on the window's root so they work wherever the focus is. (Copy and select all are left to the
/// control that has the focus: a text box must keep its own.)
/// </summary>
internal static class Shortcuts
{
    /// <summary>Registers the shortcuts on a window.</summary>
    /// <param name="window">the window, as MAUI's handler hands it over</param>
    /// <param name="run">runs a command of <see cref="MainPage.RunCommand"/></param>
    internal static void Install(object? window, Action<string> run)
    {
        if (window is not Microsoft.UI.Xaml.Window { Content: UIElement root }) return;

        void Add(VirtualKey key, VirtualKeyModifiers modifiers, string command, bool leaveTextBoxes = false)
        {
            var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, e) =>
            {
                // Copy and select all belong to a text box that has the focus (the find box): leave the key to it.
                if (leaveTextBoxes && FocusManager.GetFocusedElement(root.XamlRoot) is Microsoft.UI.Xaml.Controls.TextBox) return;
                e.Handled = true;
                run(command);
            };
            root.KeyboardAccelerators.Add(accelerator);
        }

        const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control, None = VirtualKeyModifiers.None;
        Add(VirtualKey.C, Ctrl, "copy", leaveTextBoxes: true);
        Add(VirtualKey.A, Ctrl, "select-all", leaveTextBoxes: true);
        Add(VirtualKey.O, Ctrl, "open");
        Add(VirtualKey.F, Ctrl, "find");
        Add(VirtualKey.D, Ctrl, "bookmark");
        Add(VirtualKey.F3, None, "find-next");
        Add(VirtualKey.F3, VirtualKeyModifiers.Shift, "find-previous");
        Add(VirtualKey.F9, None, "sidebar");
        Add(VirtualKey.Add, Ctrl, "zoom-in");
        Add((VirtualKey)187, Ctrl, "zoom-in");               // the = key, which is + without shift
        Add(VirtualKey.Subtract, Ctrl, "zoom-out");
        Add((VirtualKey)189, Ctrl, "zoom-out");
        Add(VirtualKey.Number0, Ctrl, "fit-width");
        Add(VirtualKey.Number9, Ctrl, "fit-page");
        Add(VirtualKey.Left, VirtualKeyModifiers.Menu, "back");
        Add(VirtualKey.Right, VirtualKeyModifiers.Menu, "forward");
    }
}

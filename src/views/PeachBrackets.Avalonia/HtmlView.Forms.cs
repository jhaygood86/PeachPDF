using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

// Form controls: checkboxes, radio buttons, buttons, ranges, details/summary, reset and submit. The click handling of HtmlView.Input.cs calls ActivateControlAt.
public sealed partial class HtmlView
{
    // A click on a form control, a details summary or a picker: true when it was one.
    private bool ActivateControlAt(int page, double x, double y) => false;
}

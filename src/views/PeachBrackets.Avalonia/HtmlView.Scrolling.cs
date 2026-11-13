using Avalonia.Input;

namespace PeachBrackets.Avalonia;

// Scroll containers (overflow: auto/scroll boxes, text areas), the scroll bars the document paints, and the wheel and keys that move them.
public sealed partial class HtmlView
{
    private bool BeginScrollbarDrag(int page, double x, double y) => false;

    private void EndScrollbarDrag()
    {
    }

    // The wheel over a scroll container: true when a container took it (otherwise the page scrolls). Distances are in points.
    private bool WheelScrollContainer(int page, double x, double y, double dxPoints, double dyPoints) => false;

    // An arrow or page key while a scroll container holds the focus: true when it scrolled.
    private bool ScrollFocusedContainer(double dx, double dy) => false;
}

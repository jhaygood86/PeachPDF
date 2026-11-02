using PeachBrackets.Accessibility;
using PeachBrackets.Updates;
using System;

namespace PeachBrackets.Android;

public sealed partial class HtmlView
{
    // A tap on a form control does what a tap does: toggles a check box, checks a radio button. Returns whether the point was over a control that
    // took it, so the tap is not also taken as a tap on a link around it.
    private bool ActivateControlAt(int page, double x, double y)
    {
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element || !IsControl(element))
            return false;

        var result = document.Activate(element);
        AfterControlChange(document, result);
        return result.Applied;
    }

    // Does what a tap on a control does, for an accessibility service or the keyboard.
    internal bool ActivateControl(AccessibilityNode node)
    {
        if (_document is not { } document || node.ElementId is not { } id || document.GetElement(id) is not { } element)
            return false;

        var result = document.Activate(element);
        AfterControlChange(document, result);
        return result.Applied;
    }

    private static bool IsControl(DocumentElement element) =>
        element.TagName.Equals("input", StringComparison.OrdinalIgnoreCase) || element.TagName.Equals("select", StringComparison.OrdinalIgnoreCase);

    private void AfterControlChange(HtmlDocument document, UpdateResult result)
    {
        if (!result.Applied)
            return;

        // The tree is built again after a change, so the nodes an accessibility service holds are replaced.
        RefreshAccessibility(document);
        _surface.Invalidate();
    }
}

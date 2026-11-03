using PeachBrackets;
using PeachBrackets.Accessibility;
using PeachBrackets.Updates;
using System;
using System.Threading.Tasks;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    // A click on a form control does what a click does: toggles a check box, checks a radio button. Returns whether the point was over a control
    // that took it, so the click is not also taken as a click on a link around it.
    private bool ActivateControlAt(int page, double x, double y)
    {
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element || !IsControl(element))
            return false;

        var result = document.Activate(element);
        AfterControlChange(document, result);
        return result.Applied;
    }

    // A click on a submit button raises FormSubmitting.
    internal bool SubmitFormAt(int page, double x, double y) =>
        _document is { } document && document.GetElementAt(page, x, y) is { } element && SubmitForm(element, page, x, y);

    // A click on a reset button puts the controls of its form back as the source has them.
    internal bool ResetFormAt(int page, double x, double y) =>
        _document is { } document && document.GetElementAt(page, x, y) is { } element && ResetForm(element);

    internal bool ResetForm(PeachBrackets.Updates.DocumentElement element)
    {
        if (_document is not { } document || !document.IsResetButton(element))
            return false;

        QueueFocus(() => ResetFormAsync(document, element));
        return true;
    }

    private async Task ResetFormAsync(PeachBrackets.HtmlDocument document, PeachBrackets.Updates.DocumentElement element)
    {
        if (_context is null || !ReferenceEquals(document, _document))
            return;

        using var measuring = new PeachDrawing.Win2D.Win2dComposer(_context, 1, 1);
        var result = await document.ResetFormAsync(element, measuring.Canvas);
        if (ReferenceEquals(document, _document))
            AfterControlChange(document, result);
    }

    // Gives a control the focus, as the first one that is not valid gets it.
    private async Task FocusElementAsync(PeachBrackets.HtmlDocument document, PeachBrackets.Updates.DocumentElement element)
    {
        if (_context is null || !ReferenceEquals(document, _document))
            return;

        using var measuring = new PeachDrawing.Win2D.Win2dComposer(_context, 1, 1);
        var result = await document.SetFocusAsync(element, true, measuring.Canvas);
        if (ReferenceEquals(document, _document))
            AfterFocusChange(document, result, document.FocusedNode?.Id);
    }

    /// <summary>Does what a click on a button does - resets its form or submits it - for a screen reader or the keyboard.</summary>
    internal bool InvokeButton(AccessibilityNode node) =>
        _document is { } document && node.ElementId is { } id && document.GetElement(id) is { } element && (ResetForm(element) || SubmitForm(element));

    /// <summary>Submits the form a button belongs to, for a screen reader or the keyboard.</summary>
    internal bool SubmitForm(AccessibilityNode node) =>
        _document is { } document && node.ElementId is { } id && document.GetElement(id) is { } element && SubmitForm(element);

    /// <summary>Does what a click on a control does, for a screen reader or the keyboard.</summary>
    internal bool ActivateControl(AccessibilityNode node)
    {
        if (_document is not { } document || node.ElementId is not { } id || document.GetElement(id) is not { } element)
            return false;

        var result = document.Activate(element);
        AfterControlChange(document, result);
        return result.Applied;
    }

    /// <summary>The node as the document describes it now: the tree is built again after a control changes, so a peer's own node can be an old one.</summary>
    internal AccessibilityNode CurrentNode(AccessibilityNode node) => _document?.GetAccessibilityTree().FindById(node.Id) ?? node;

    private static bool IsControl(DocumentElement element) =>
        element.TagName.Equals("input", StringComparison.OrdinalIgnoreCase) || element.TagName.Equals("select", StringComparison.OrdinalIgnoreCase);

    private void AfterControlChange(PeachBrackets.HtmlDocument document, UpdateResult result)
    {
        Repaint(result);
        if (result.Applied)
            RefreshAccessibility(document);

        // A rule that reads the control's state (input:checked + label) has to be matched again.
        if (document.NeedsRestyle)
            QueueFocus(() => RestyleFormAsync(document));
    }

    private async Task RestyleFormAsync(PeachBrackets.HtmlDocument document)
    {
        if (_context is null || !ReferenceEquals(document, _document))
            return;

        using var measuring = new PeachDrawing.Win2D.Win2dComposer(_context, 1, 1);
        var result = await document.RestyleFormAsync(measuring.Canvas);
        if (ReferenceEquals(document, _document))
            AfterControlChange(document, result);
    }

    // Space (or Enter) on a focused check box or radio button toggles it.
    private bool HandleControlKey(Windows.System.VirtualKey key)
    {
        if (key is not (Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter) || _document is not { } document)
            return false;

        var node = _focusTargetId is { } id ? document.GetAccessibilityTree().FindById(id) : document.FocusedNode;
        return node is not null && ((HtmlViewLogic.IsToggleControl(node.Role) && ActivateControl(node)) || (node.Role == AccessibilityRole.Button && InvokeButton(node)));
    }
}

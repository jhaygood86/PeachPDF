using Avalonia.Input;
using PeachBrackets.Accessibility;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

// Form controls: checkboxes, radio buttons, buttons, ranges, details/summary, reset and submit. The click handling of HtmlView.Input.cs calls ActivateControlAt.
public sealed partial class HtmlView
{
    // A click on a form control, a details summary or a picker: true when it was one, so the click is not also taken as a click on a link around it.
    private bool ActivateControlAt(int page, double x, double y)
    {
        if (_document is not { } document)
            return false;

        return ToggleDetailsAt(page, x, y)
            || OpenPickerAt(page, x, y)
            || ActivateCheckAt(document, page, x, y)
            || ResetFormAt(page, x, y)
            || SubmitFormAt(page, x, y);
    }

    // A click on a check box or a radio button toggles it; a click on a range sets it where the click is.
    private bool ActivateCheckAt(HtmlDocument document, int page, double x, double y)
    {
        if (document.GetElementAt(page, x, y) is not { } element || !IsControl(element))
            return false;

        if (document.GetFormState(element) is { Type: "range" })
        {
            _rangeElement = element;
            _rangePage = page;
            SetRange(x);
            _rangeElement = null;
            return true;
        }

        var result = document.Activate(element);
        AfterControlChange(document, result);
        return result.Applied;
    }

    // A click on a summary opens or closes its details, as a browser does; the document is laid out again, so what follows it moves.
    private bool ToggleDetailsAt(int page, double x, double y)
    {
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element)
            return false;

        return ToggleDetails(element);
    }

    private bool ToggleDetails(DocumentElement element)
    {
        if (_document is not { } document || document.GetDetailsToggledBy(element) is null)
            return false;

        QueueFocus(() => ToggleDetailsAsync(document, element));
        return true;
    }

    private async Task ToggleDetailsAsync(HtmlDocument document, DocumentElement element)
    {
        if (_context is not { } context || !ReferenceEquals(document, _document))
            return;

        UpdateResult result;
        using (var measuring = new MeasuringCanvas(context))
            result = await document.ToggleDetailsAsync(element, measuring.Canvas);

        if (ReferenceEquals(document, _document))
            AfterControlChange(document, result);
    }

    // A click on a submit button raises FormSubmitting.
    private bool SubmitFormAt(int page, double x, double y) =>
        _document is { } document && document.GetElementAt(page, x, y) is { } element && SubmitForm(element, page, x, y);

    // A click on a reset button puts the controls of its form back as the source has them.
    private bool ResetFormAt(int page, double x, double y) =>
        _document is { } document && document.GetElementAt(page, x, y) is { } element && ResetForm(element);

    private bool ResetForm(DocumentElement element)
    {
        if (_document is not { } document || !document.IsResetButton(element))
            return false;

        QueueFocus(() => ResetFormAsync(document, element));
        return true;
    }

    private async Task ResetFormAsync(HtmlDocument document, DocumentElement element)
    {
        if (_context is not { } context || !ReferenceEquals(document, _document))
            return;

        UpdateResult result;
        using (var measuring = new MeasuringCanvas(context))
            result = await document.ResetFormAsync(element, measuring.Canvas);

        if (ReferenceEquals(document, _document))
            AfterControlChange(document, result);
    }

    // Gives a control the focus, as the first one that is not valid gets it.
    private async Task FocusElementAsync(HtmlDocument document, DocumentElement element)
    {
        if (_context is not { } context || !ReferenceEquals(document, _document))
            return;

        UpdateResult result;
        using (var measuring = new MeasuringCanvas(context))
            result = await document.SetFocusAsync(element, true, measuring.Canvas);

        if (ReferenceEquals(document, _document))
        {
            _focusTargetId = document.FocusedNode?.Id;
            AfterFocusChange(document, result, _focusTargetId);
        }
    }

    // Does what a click on a button does - resets its form, submits it, or toggles its details - for the keyboard.
    private bool InvokeButton(AccessibilityNode node) =>
        _document is { } document && node.ElementId is { } id && document.GetElement(id) is { } element
        && (ResetForm(element) || SubmitForm(element) || ToggleDetails(element));

    // Does what a click on a control does, for the keyboard.
    private bool ActivateControl(AccessibilityNode node)
    {
        if (_document is not { } document || node.ElementId is not { } id || document.GetElement(id) is not { } element)
            return false;

        var result = document.Activate(element);
        AfterControlChange(document, result);
        return result.Applied;
    }

    private static bool IsControl(DocumentElement element) =>
        element.TagName.Equals("input", StringComparison.OrdinalIgnoreCase) || element.TagName.Equals("select", StringComparison.OrdinalIgnoreCase);

    // Shows what a change of a control did, and matches the rules that read the control's state again.
    private void AfterControlChange(HtmlDocument document, UpdateResult result)
    {
        if (result.Applied)
        {
            UpdateMetrics();
            RefreshAccessibility(document);
            if (result.DirtyRegions.Count > 0)
                InvalidateRegions(result.DirtyRegions);
            else
                InvalidateContent();
        }

        // A rule that reads the control's state (input:checked + label) has to be matched again.
        if (document.NeedsRestyle)
            QueueFocus(() => RestyleFormAsync(document));
    }

    private async Task RestyleFormAsync(HtmlDocument document)
    {
        if (_context is not { } context || !ReferenceEquals(document, _document))
            return;

        UpdateResult result;
        using (var measuring = new MeasuringCanvas(context))
            result = await document.RestyleFormAsync(measuring.Canvas);

        if (ReferenceEquals(document, _document))
            AfterControlChange(document, result);
    }

    // Space or Enter on a focused check box, radio button or button.
    private bool HandleControlKey(Key key)
    {
        if (_document is not { } document)
            return false;

        var node = _focusTargetId is { } id ? document.GetAccessibilityTree().FindById(id) : document.FocusedNode;
        if (node is null)
            return false;

        if (key is not (Key.Space or Key.Enter))
            return false;

        return AccessibilityRoles.IsToggle(node.Role) ? ActivateControl(node) : node.Role == AccessibilityRole.Button && InvokeButton(node);
    }
}

// What the keyboard can toggle.
internal static class AccessibilityRoles
{
    public static bool IsToggle(AccessibilityRole role) =>
        role is AccessibilityRole.CheckBox or AccessibilityRole.Radio or AccessibilityRole.Switch or AccessibilityRole.MenuItemCheckBox or AccessibilityRole.MenuItemRadio;
}

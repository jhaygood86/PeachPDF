using PeachBrackets.Accessibility;
using PeachBrackets.Updates;
using System;
using System.Threading.Tasks;

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

    /// <summary>
    /// Shows a change an application made to the document itself - through its text-editing and style-setting methods - which the view cannot see: it repaints, and tells an accessibility service what a live region (<c>aria-live</c>) now says and what
    /// else about the page changed.
    /// </summary>
    /// <param name="result">what the document returned for the change</param>
    public void ApplyUpdate(UpdateResult result)
    {
        if (_document is { } document)
            AfterControlChange(document, result);
    }

    private void AfterControlChange(HtmlDocument document, UpdateResult result)
    {
        if (!result.Applied)
            return;

        // The tree is built again after a change, so the nodes an accessibility service holds are replaced.
        RefreshAccessibility(document);
        _surface.Invalidate();

        // A rule that reads the control's state (input:checked + label) has to be matched again.
        if (document.NeedsRestyle)
            QueueFocus(() => RestyleFormAsync(document));
    }

    private async Task RestyleFormAsync(HtmlDocument document)
    {
        if (_context is not { } context || !ReferenceEquals(document, _document))
            return;

        UpdateResult result;
        using (var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1, 1)))
        using (var measuring = new PeachDrawing.Skia.SkiaComposer(context, surface, 1, 1))
            result = await document.RestyleFormAsync(measuring.Canvas);

        if (ReferenceEquals(document, _document))
        {
            UpdateMetrics();
            AfterControlChange(document, result);
        }
    }
}

using Android.Content;
using Android.Views;
using Android.Views.Accessibility;
using PeachBrackets.Accessibility;

namespace PeachBrackets.Android;

public sealed partial class HtmlView
{
    // The virtual view the finger is over while touch exploration is on, or -1 (the host's own id is -1 too, but a finger is never "over" it here).
    private int _hoverNode = -1;

    private bool IsTouchExplorationEnabled() =>
        Context?.GetSystemService(Context.AccessibilityService) is AccessibilityManager { IsEnabled: true, IsTouchExplorationEnabled: true };

    /// <inheritdoc />
    public override bool OnHoverEvent(MotionEvent? e)
    {
        if (e is null || !IsTouchExplorationEnabled())
            return base.OnHoverEvent(e);

        switch (e.Action)
        {
            case MotionEventActions.HoverEnter or MotionEventActions.HoverMove:
                HoverTo(NodeIdAt(e.GetX(), e.GetY()));
                return true;
            case MotionEventActions.HoverExit:
                HoverTo(-1);
                return true;
            default:
                return base.OnHoverEvent(e);
        }
    }

    // The virtual view id of the part of the page under a point in the view's pixels: the innermost node the tree reads, or -1.
    private int NodeIdAt(float px, float py)
    {
        if (_document is not { } document || !TryLocate(px, py, out var page, out var x, out var y))
            return -1;

        for (var node = document.GetAccessibilityNodeAt(page, x, y); node is not null; node = node.Parent)
        {
            if (_a11yIds.TryGetValue(node, out var id))
                return id;

            // The tree may have been built again since the table was; the same part has the same id.
            for (var i = 0; i < _a11yNodes.Length; i++)
            {
                if (_a11yNodes[i].Id == node.Id)
                    return i + 1;
            }
        }

        return -1;
    }

    /// <summary>Sets the number of a slider, as an accessibility service's set-progress action does. The document keeps it in the slider's range.</summary>
    internal bool SetRangeValue(AccessibilityNode node, float value)
    {
        if (_document is not { } document || node.ElementId is not { } id || document.GetElement(id) is not { } element)
            return false;

        var result = document.SetValue(element, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AfterControlChange(document, result);
        return result.Applied;
    }

    // Tells the accessibility service the finger left one part and entered another, which is what makes it announce what is under the finger.
    private void HoverTo(int virtualViewId)
    {
        if (virtualViewId == _hoverNode)
            return;

        if (_hoverNode > 0)
            SendNodeEvent(_hoverNode, EventTypes.ViewHoverExit);

        _hoverNode = virtualViewId;
        if (virtualViewId > 0)
            SendNodeEvent(virtualViewId, EventTypes.ViewHoverEnter);
    }
}

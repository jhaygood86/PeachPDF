using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PeachBrackets.Accessibility;
using System;
using System.Threading.Tasks;
using Windows.System;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    // Where the focus is going, by the node's id: Tab decides at once, the document catches up a moment later, and a second Tab in between
    // must step on from where the first is going.
    private string? _focusTargetId;
    private Task _focusWork = Task.CompletedTask;

    /// <summary>The part of the page that has the keyboard focus (a link, a button, a form control, anything with a <c>tabindex</c>), or <see langword="null"/> if none has.</summary>
    public AccessibilityNode? FocusedNode => _document?.FocusedNode;

    private void WireFocus()
    {
        GotFocus += OnGotFocus;
        LostFocus += OnLostFocus;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // Tab and Shift+Tab step through the tab stops of the page; past the last one (or before the first) the focus is left to XAML, which moves it to
    // the next control. Returns whether the key was used.
    private bool HandleFocusKey(KeyRoutedEventArgs e)
    {
        if (_document is not { } document || IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu))
            return false;

        switch (e.Key)
        {
            case VirtualKey.Tab:
                return StepFocus(!IsDown(VirtualKey.Shift));

            case VirtualKey.Space or VirtualKey.Enter when HandleControlKey(e.Key):
                return true;

            case VirtualKey.Enter when (_focusTargetId is { } targetId ? document.GetAccessibilityTree().FindById(targetId) : document.FocusedNode) is { Role: AccessibilityRole.Link } link:
                ActivateLink(link);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// What Tab (or Shift+Tab) does: moves the page's focus to the next (or previous) tab stop and returns true, or, when there is none in that
    /// direction, gives up the page's focus and returns false so the key moves the XAML focus on.
    /// </summary>
    internal bool StepFocus(bool forward)
    {
        if (_document is not { } document)
            return false;

        var tree = document.GetAccessibilityTree();
        var current = _focusTargetId is { } id ? tree.FindById(id) : document.FocusedNode;
        var next = tree.NextFocus(current, forward);
        if (next is null)
        {
            if (current is not null)
                QueueFocus(async () => await ClearDocumentFocusAsync());

            return false;
        }

        _focusTargetId = next.Id;
        QueueFocus(() => FocusNodeAsync(next, true));
        return true;
    }

    /// <summary>The focus changes that have been asked for and not yet made.</summary>
    internal Task FocusIdle => _focusWork;

    // One change at a time, in order: each restyles the document and lays it out again.
    private void QueueFocus(Func<Task> work)
    {
        _focusWork = RunAfterAsync(_focusWork, work);
    }

    private static async Task RunAfterAsync(Task before, Func<Task> work)
    {
        try
        {
            await before;
        }
        catch (Exception)
        {
            // The earlier change failed; this one does not depend on it.
        }

        await work();
    }

    private async Task FocusNodeAsync(AccessibilityNode node, bool focusVisible)
    {
        if (_document is not { } document || _context is null)
            return;

        using var measuring = new PeachDrawing.Win2D.Win2dComposer(_context, 1, 1);
        var result = await document.FocusAsync(node, focusVisible, measuring.Canvas);
        if (!ReferenceEquals(document, _document))
            return;

        AfterFocusChange(document, result, node.Id);
    }

    private async Task ClearDocumentFocusAsync()
    {
        _focusTargetId = null;
        if (_document is not { } document || _context is null)
            return;

        using var measuring = new PeachDrawing.Win2D.Win2dComposer(_context, 1, 1);
        var result = await document.SetFocusAsync(null, false, measuring.Canvas);
        if (ReferenceEquals(document, _document))
            AfterFocusChange(document, result, null);
    }

    // The document was restyled, which can have moved things: repaint, take the tree again (the document builds a new one, so nodes are compared by
    // id from here on), bring the focused part into view and tell UI Automation.
    private void AfterFocusChange(HtmlDocument document, PeachBrackets.Updates.UpdateResult result, string? focusedId)
    {
        Repaint(result);
        if (result.Applied && result.DirtyRegions.Count > 0)
            RefreshAccessibility(document);

        UpdateEditing();
        if (focusedId is not null && _accessibilityTree?.FindById(focusedId) is { } focused)
        {
            BringIntoView(focused);
            RaiseFocusChanged(focused);
        }
    }

    private void Repaint(PeachBrackets.Updates.UpdateResult result)
    {
        if (!result.Applied || result.DirtyRegions.Count == 0)
            return;

        UpdateMetrics();
        foreach (var region in result.DirtyRegions)
            InvalidateRegion(region.PageIndex, region.Bounds);
    }

    // When the view takes the focus from the keyboard it goes to the first tab stop (the last, coming from behind with Shift+Tab).
    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        if (_document is not { } document || document.FocusedNode is not null || _focusTargetId is not null)
            return;

        if ((e.OriginalSource as Control)?.FocusState != FocusState.Keyboard)
            return;

        var forward = !IsDown(VirtualKey.Shift);
        var next = document.GetAccessibilityTree().NextFocus(null, forward);
        if (next is not null)
        {
            _focusTargetId = next.Id;
            QueueFocus(() => FocusNodeAsync(next, true));
        }
    }

    // When the focus goes to something outside the view, the page keeps none.
    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        // The menu of a text field is a popup: while it is open the field keeps its focus.
        if (_fieldMenuOpen)
            return;

        if (XamlRoot is { } root && FocusManager.GetFocusedElement(root) is DependencyObject focused && IsInside(focused))
            return;

        if (_focusTargetId is not null || _document?.FocusedNode is not null)
            QueueFocus(async () => await ClearDocumentFocusAsync());
    }

    private bool IsInside(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, this))
                return true;
        }

        return false;
    }

    // Brings the view to the node a screen reader asked to focus, and moves the page's focus there.
    internal void FocusFromAutomation(AccessibilityNode node)
    {
        Focus(FocusState.Programmatic);
        _focusTargetId = node.Id;
        QueueFocus(() => FocusNodeAsync(node, false));
    }

    // Tells UI Automation the focus moved, so a screen reader reads the new place.
    private void RaiseFocusChanged(AccessibilityNode node)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.AutomationFocusChanged))
            return;

        if (FrameworkElementAutomationPeer.FromElement(_host) is HtmlViewAutomationPeer peer && peer.FindPeer(node) is { } nodePeer)
            nodePeer.RaiseAutomationEvent(AutomationEvents.AutomationFocusChanged);
    }
}

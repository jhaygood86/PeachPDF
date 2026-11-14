using Avalonia.Controls;
using Avalonia.Input;
using PeachBrackets.Accessibility;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

// Keyboard focus inside the document: Tab order, the focus ring, Enter and Space on links and buttons.
public sealed partial class HtmlView
{
    // Where the focus is going, by the node's id: Tab decides at once, the document catches up a moment later, and a second Tab in between
    // must step on from where the first is going.
    private string? _focusTargetId;
    private Task _focusWork = Task.CompletedTask;

    /// <summary>Raised when the part of the page that has the keyboard focus changes (see <see cref="FocusedNode"/>).</summary>
    public event EventHandler? FocusChanged;

    /// <summary>The part of the page that has the keyboard focus (a link, a button, a form control, anything with a <c>tabindex</c>), or <see langword="null"/> if none has.</summary>
    public AccessibilityNode? FocusedNode => _document?.FocusedNode;

    internal double ScrollOffsetY => _scrollY;

    /// <summary>The focus changes that have been asked for and not yet made.</summary>
    internal Task FocusIdle => _focusWork;

    // Runs focus changes one after another, so two quick key presses do not interleave their restyles.
    internal void QueueFocus(Func<Task> change) => _focusWork = RunAfterAsync(_focusWork, change);

    private async Task RunAfterAsync(Task before, Func<Task> work)
    {
        try
        {
            await before;
        }
        catch (Exception)
        {
            // The earlier change failed and has been reported; this one does not depend on it.
        }

        // A change that was waiting on a document a reload has since replaced fails quietly: the document it was for is gone.
        var epoch = _documentEpoch;
        try
        {
            await work();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && epoch == _documentEpoch)
        {
            RaiseDocumentFailed(exception);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Stale.
        }
    }

    // The keys the focused element takes (Tab and Shift+Tab move the focus; Enter and Space activate): true when the key was used.
    private bool HandleFocusNavigationKey(KeyEventArgs e)
    {
        if (_document is not { } document || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
            return false;

        switch (e.Key)
        {
            case Key.Tab:
                return StepFocus((e.KeyModifiers & KeyModifiers.Shift) == 0);

            case Key.Enter or Key.Space:
                if (HandleControlKey(e.Key))
                    return true;

                if (e.Key == Key.Enter && CurrentFocus(document) is { Role: AccessibilityRole.Link } link)
                {
                    FollowLinkNode(link);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private AccessibilityNode? CurrentFocus(HtmlDocument document) =>
        _focusTargetId is { } id ? document.GetAccessibilityTree().FindById(id) : document.FocusedNode;

    /// <summary>
    /// What Tab (or Shift+Tab) does: moves the page's focus to the next (or previous) tab stop and returns true, or, when there is none in that
    /// direction, gives up the page's focus and returns false so the key moves Avalonia's focus on.
    /// </summary>
    internal bool StepFocus(bool forward)
    {
        if (_document is not { } document)
            return false;

        var tree = document.GetAccessibilityTree();
        var current = CurrentFocus(document);
        var next = tree.NextFocus(current, forward);
        if (next is null)
        {
            if (current is not null)
                QueueFocus(ClearDocumentFocusAsync);

            return false;
        }

        _focusTargetId = next.Id;
        QueueFocus(() => FocusNodeAsync(next, true));
        return true;
    }

    // Follows the link a focused node is, as a click on it does.
    private void FollowLinkNode(AccessibilityNode node)
    {
        if (node.Uri is not { } uri)
            return;

        var isAnchor = uri.StartsWith('#');
        var args = new HtmlLinkActivatedEventArgs(isAnchor ? null : uri, isAnchor ? uri[1..] : null);
        RaiseLinkActivated(args);
        if (args.Cancel)
            return;

        if (isAnchor)
            ScrollToElement(uri[1..]);
        else if (PageLayout.IsLaunchable(uri) && Uri.TryCreate(uri, UriKind.Absolute, out var target) && TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            Forget(launcher.LaunchUriAsync(target));
    }

    private async Task FocusNodeAsync(AccessibilityNode node, bool focusVisible)
    {
        if (_document is not { } document || _context is not { } context)
            return;

        UpdateResult result;
        using (var measuring = RentMeasuring(context))
            result = await document.FocusAsync(node, focusVisible, measuring.Canvas);

        if (ReferenceEquals(document, _document))
            AfterFocusChange(document, result, node.Id);
    }

    private async Task ClearDocumentFocusAsync()
    {
        _focusTargetId = null;
        if (_document is not { } document || _context is not { } context)
            return;

        UpdateResult result;
        using (var measuring = RentMeasuring(context))
            result = await document.SetFocusAsync(null, false, measuring.Canvas);

        if (ReferenceEquals(document, _document))
            AfterFocusChange(document, result, null);
    }

    // The document was restyled, which can have moved things: repaint, take the tree again, bring the focused part into view and say so.
    private void AfterFocusChange(HtmlDocument document, UpdateResult result, string? focusedId)
    {
        if (result.Applied && result.DirtyRegions.Count > 0)
        {
            UpdateMetrics();
            RefreshAccessibility(document);
            InvalidateRegions(result.DirtyRegions);
        }

        if (focusedId is not null && document.GetAccessibilityTree().FindById(focusedId) is { } focused)
            RevealNode(focused);

        // A field that took the focus offers its input method client.
        UpdateTextInputClient();
        FocusChanged?.Invoke(this, EventArgs.Empty);
    }

    // Scrolls so that a node the keyboard moved to is on screen.
    private void RevealNode(AccessibilityNode node)
    {
        if (IsAutoHeight || node.Bounds.Count == 0 || (uint)node.Bounds[0].PageIndex >= (uint)_pageTops.Length)
            return;

        var bounds = node.Bounds[0];
        var top = (_pageTops[bounds.PageIndex] + (bounds.Bounds.Y * PixelsPerPoint)) * Scale;
        var bottom = (_pageTops[bounds.PageIndex] + (bounds.Bounds.Bottom * PixelsPerPoint)) * Scale;
        var margin = ViewportHeightPx / 8;
        if (top < _scrollY)
            ScrollTo(_scrollX, top - margin);
        else if (bottom > _scrollY + ViewportHeightPx)
            ScrollTo(_scrollX, bottom - ViewportHeightPx + margin);
    }

    /// <inheritdoc />
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (_document is not { } document || document.FocusedNode is not null || _focusTargetId is not null)
            return;

        // Coming in from the keyboard (Tab or Shift+Tab from another control) lands on the first tab stop, or the last.
        if (e.NavigationMethod != NavigationMethod.Tab)
            return;

        var forward = (e.KeyModifiers & KeyModifiers.Shift) == 0;
        if (document.GetAccessibilityTree().NextFocus(null, forward) is { } first)
        {
            _focusTargetId = first.Id;
            QueueFocus(() => FocusNodeAsync(first, true));
        }
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);

        // The focus went into a flyout of the view's own (its field menu or a picker): the page keeps its focus, as the flyout works on it.
        if (HasOpenFlyout)
            return;

        if (_focusTargetId is not null || _document?.FocusedNode is not null)
            QueueFocus(ClearDocumentFocusAsync);
    }

    private bool HandleFocusKey(KeyEventArgs e) => HandleEditKey(e) || HandleRangeKey(e) || HandleFocusNavigationKey(e);
}

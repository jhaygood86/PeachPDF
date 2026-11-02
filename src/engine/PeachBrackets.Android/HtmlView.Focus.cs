using Android.Graphics;
using Android.Views;
using Android.Views.Accessibility;
using PeachBrackets.Accessibility;
using PeachBrackets.Android.Logic;
using Rect = Android.Graphics.Rect;

namespace PeachBrackets.Android;

public sealed partial class HtmlView
{
    // Where the focus is going, by the node's id: Tab decides at once, the document catches up a moment later, and a second Tab in between must
    // step on from where the first is going.
    private string? _focusTargetId;
    private Task _focusWork = Task.CompletedTask;

    /// <summary>Raised when the part of the page that has the keyboard focus changes (see <see cref="FocusedNode"/>).</summary>
    public event EventHandler? FocusChanged;

    /// <summary>The part of the page that has the keyboard focus (a link, a button, a form control, anything with a <c>tabindex</c>), or <see langword="null"/> if none has.</summary>
    public AccessibilityNode? FocusedNode => _document?.FocusedNode;

    // Tab and Shift+Tab step through the tab stops of the page; past the last one (or before the first) the key is left to Android, which moves the
    // focus to the next view. Enter follows the link that has the focus. Returns whether the key was used.
    private bool HandleFocusKey(Keycode keyCode, KeyEvent? e)
    {
        if (e is { IsCtrlPressed: true } or { IsAltPressed: true })
            return false;

        switch (keyCode)
        {
            case Keycode.Tab:
                return StepFocus(e is not { IsShiftPressed: true });

            case Keycode.Enter or Keycode.NumpadEnter or Keycode.DpadCenter or Keycode.Space:
                if (_document is { } document
                    && (_focusTargetId is { } id ? document.GetAccessibilityTree().FindById(id) : document.FocusedNode) is { } focused)
                {
                    if (focused.Role == AccessibilityRole.Link && keyCode != Keycode.Space)
                    {
                        FollowLinkNode(focused);
                        return true;
                    }

                    if (AccessibilityMapping.IsCheckable(focused.Role))
                        return ActivateControl(focused);
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// What Tab (or Shift+Tab) does: moves the page's focus to the next (or previous) tab stop and returns true, or, when there is none in that
    /// direction, gives up the page's focus and returns false so the key moves Android's focus on.
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
                QueueFocus(ClearDocumentFocusAsync);

            return false;
        }

        _focusTargetId = next.Id;
        QueueFocus(() => FocusNodeAsync(next, true));
        return true;
    }

    // One change at a time, in order: each restyles the document and lays it out again.
    private void QueueFocus(Func<Task> work) => _focusWork = RunAfterAsync(_focusWork, work);

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
        if (_document is not { } document || _context is not { } context)
            return;

        PeachBrackets.Updates.UpdateResult result;
        using (var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1, 1)))
        using (var measuring = new PeachDrawing.Skia.SkiaComposer(context, surface, 1, 1))
            result = await document.FocusAsync(node, focusVisible, measuring.Canvas);

        if (!ReferenceEquals(document, _document))
            return;

        AfterFocusChange(document, result, node.Id);
    }

    private async Task ClearDocumentFocusAsync()
    {
        _focusTargetId = null;
        if (_document is not { } document || _context is not { } context)
            return;

        PeachBrackets.Updates.UpdateResult result;
        using (var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(1, 1)))
        using (var measuring = new PeachDrawing.Skia.SkiaComposer(context, surface, 1, 1))
            result = await document.SetFocusAsync(null, false, measuring.Canvas);

        if (ReferenceEquals(document, _document))
            AfterFocusChange(document, result, null);
    }

    // The document was restyled, which can have moved things: repaint, take the tree again, bring the focused part into view and say so.
    private void AfterFocusChange(HtmlDocument document, PeachBrackets.Updates.UpdateResult result, string? focusedId)
    {
        if (result.Applied && result.DirtyRegions.Count > 0)
        {
            UpdateMetrics();
            RefreshAccessibility(document);
            _surface.Invalidate();
        }

        UpdateEditing();
        if (focusedId is not null && document.GetAccessibilityTree().FindById(focusedId) is { } focused)
        {
            RevealNode(focused);
            if (_a11yIds.TryGetValue(focused, out var virtualViewId))
                SendNodeEvent(virtualViewId, EventTypes.ViewFocused);
        }

        FocusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void OnFocusChanged(bool gainFocus, FocusSearchDirection direction, Rect? previouslyFocusedRect)
    {
        base.OnFocusChanged(gainFocus, direction, previouslyFocusedRect);
        if (_document is not { } document)
            return;

        if (gainFocus)
        {
            // Coming in from the keyboard (Tab or Shift+Tab from another view) lands on the first tab stop, or the last.
            if (direction is FocusSearchDirection.Forward or FocusSearchDirection.Backward && document.FocusedNode is null && _focusTargetId is null
                && document.GetAccessibilityTree().NextFocus(null, direction == FocusSearchDirection.Forward) is { } first)
            {
                _focusTargetId = first.Id;
                QueueFocus(() => FocusNodeAsync(first, true));
            }
        }
        else if (_focusTargetId is not null || document.FocusedNode is not null)
        {
            QueueFocus(ClearDocumentFocusAsync);
        }
    }

    // The focus an accessibility service asked for, as a screen reader's "set focus".
    internal void FocusFromAccessibility(AccessibilityNode node)
    {
        RequestFocus();
        _focusTargetId = node.Id;
        QueueFocus(() => FocusNodeAsync(node, false));
    }
}

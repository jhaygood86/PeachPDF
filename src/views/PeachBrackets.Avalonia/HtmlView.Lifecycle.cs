namespace PeachBrackets.Avalonia;

// What the view lets go of when its document goes away (a reload, a cleared source, leaving the tree): the timers, flyouts and half-made gestures that refer to the old one.
public sealed partial class HtmlView
{
    /// <summary>
    /// Ends everything in progress that belongs to the document being shown: a fling, a scroll bar track held down or a thumb being dragged, a pinch, a press,
    /// the caret's blinking, the image upgrade that was waiting, the pickers that are open, and the focus the keyboard was heading for. What these hold
    /// (a scroll container, a form control) belongs to the old document, and a timer tick would use it on the new one. The pump of the pointer state is left
    /// to end by itself: it picks up the document that is current each time round.
    /// </summary>
    private void ResetInteractionState()
    {
        EndFling(settle: false);
        EndTrackRepeat();
        _scrollbarDrag = null;
        _touchStart = null;
        _touchContainer = null;
        _touchScrolling = false;
        _touchPressValid = false;
        _pinching = false;

        _pressed = false;
        _dragged = false;
        _selecting = false;
        _pressedInField = false;
        _pendingPointer = null;
        _clearPending = false;

        _rangeElement = null;
        _fieldAnchor = null;
        _editFieldKey = null;
        _focusTargetId = null;
        _caretShown = true;
        _blink?.Stop();
        _upgradeTimer?.Stop();
        _pendingImageCursor = null;
        CloseAllFlyouts();
    }

    // Hides every flyout the view opened, and forgets them: what they were going to do is for a document that is gone.
    private void CloseAllFlyouts()
    {
        foreach (var flyout in _ownFlyouts.ToArray())
        {
            flyout.Closed -= OnOwnFlyoutClosed;
            flyout.Hide();
        }

        _ownFlyouts.Clear();
        _openFlyout = null;
    }

    // Whether a document is still the one the view shows: work that waited for it checks before it goes on.
    private bool IsCurrent(HtmlDocument document) => ReferenceEquals(document, _document);

    /// <summary>
    /// Whether the view is done with what its input set going: no pointer state being applied, no focus or form change queued, no content-visibility update or image
    /// upgrade waiting, and no load or reflow under way. For tests, which wait for it instead of for a fixed time.
    /// </summary>
    internal bool IsIdle =>
        !_pumping && _pendingPointer is null && !_clearPending && !_updatingContentVisibility && _focusWork.IsCompleted
        && _upgradeTimer is not { IsEnabled: true } && !IsReloading;

    /// <summary>Whether a load is running or a reflow is waiting to start one. For tests.</summary>
    internal bool IsReloading => _loadsUsing.Count > 0 || _reflowTimer is { IsEnabled: true };
}

using CoreAnimation;
using CoreGraphics;
using Foundation;
using PeachBrackets.Accessibility;
using PeachBrackets.Apple.Logic;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using PeachDrawing.CoreGraphics;

namespace PeachBrackets.Apple;

// Form controls, keyboard focus and text editing: what a click or a tap on a control does, Tab through the page, and the caret and text of the field with the focus.
internal sealed partial class HtmlViewEngine
{
    private static readonly string[] TextTypes = ["text", "search", "url", "tel", "email", "password", "number", "textarea"];

    private Task _work = Task.CompletedTask;
    private string? _focusTargetId;
    private bool _editing;
    private string? _editFieldKey;
    private NSTimer? _blink;
    private bool _caretShown = true;

    /// <summary>What a press that moves on does, decided where it began.</summary>
    public enum DragKind
    {
        /// <summary>Nothing of the engine's: the press scrolls or selects page text.</summary>
        None,

        /// <summary>It began on a range control: moving sets the value.</summary>
        Range,

        /// <summary>It began in a text field: moving extends the selection from where it began.</summary>
        Field,
    }

    private DragKind _dragKind;
    private DocumentElement? _dragElement;
    private int? _fieldAnchor;

    /// <summary>Called when a press begins: whether it began on a range control or in a text field, which then take the moves that follow instead of the page.</summary>
    public DragKind BeginDrag(int page, double x, double y)
    {
        _dragKind = DragKind.None;
        _dragElement = null;
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element || document.GetFormState(element) is not { } state || state.Disabled)
            return DragKind.None;

        if (state.Type == "range")
        {
            _dragKind = DragKind.Range;
            _dragElement = element;
        }
        else if (Array.IndexOf(TextTypes, state.Type) >= 0)
        {
            _dragKind = DragKind.Field;
            _dragElement = element;
            _fieldAnchor = null;

            // A field that already has the focus anchors at the press itself; one that does not takes the focus (and so the anchor) when the press is taken as a tap.
            if (_editing && _context is not null)
            {
                using var measuring = new CoreGraphicsComposer(_context, 1, 1, 1, 1);
                _fieldAnchor = document.GetEditIndexAt(measuring.Canvas, page, x, y);
            }
        }

        return _dragKind;
    }

    /// <summary>The press moved to a point of a page.</summary>
    public void Drag(int page, double x, double y)
    {
        if (_document is not { } document || _context is null || _dragElement is not { } element)
            return;

        if (_dragKind == DragKind.Range)
        {
            AfterControlChange(document, document.SetRangeAtPoint(element, page, x));
        }
        else if (_dragKind == DragKind.Field && _fieldAnchor is { } anchor)
        {
            using var measuring = new CoreGraphicsComposer(_context, 1, 1, 1, 1);
            if (document.GetEditIndexAt(measuring.Canvas, page, x, y) is { } at)
                AfterEdit(document.SetEditSelection(anchor, at), CurrentFieldText());
        }
    }

    /// <summary>The press ended.</summary>
    public void EndDrag()
    {
        _dragKind = DragKind.None;
        _dragElement = null;
    }

    /// <summary>Whether a text field has the focus, so key input goes to it.</summary>
    public bool IsEditing => _editing;

    /// <summary>The part of the page that has the keyboard focus, or null.</summary>
    public AccessibilityNode? FocusedNode => _document?.FocusedNode;

    // One change at a time, in order: each restyles the document and lays it out again.
    private void Queue(Func<Task> work)
    {
        // Counted from now, not from when it runs: a change waiting its turn holds the document it will use.
        BeginWork();
        var held = _context;
        _work = RunAfterAsync(_work, async () =>
        {
            try
            {
                await work();
            }
            finally
            {
                EndWork(held);
            }
        });
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

    private static bool IsControl(DocumentElement element) =>
        element.TagName.Equals("input", StringComparison.OrdinalIgnoreCase) || element.TagName.Equals("select", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a click or a tap at a point does when it is over a form control: a text field takes the focus (and the caret goes where the point was), a check box
    /// toggles, a radio button checks, a drop-down offers its options, a reset button resets its form, a submit button submits it. False when the point is over none.
    /// </summary>
    public bool TapForm(int page, double x, double y, CGRect anchor)
    {
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element)
            return false;

        var state = document.GetFormState(element);
        if (state is { } text && Array.IndexOf(TextTypes, text.Type) >= 0)
        {
            Queue(() => FocusFieldAtAsync(document, element, page, x, y));
            return true;
        }

        if (state is { Type: "select" or "select-one" } && document.GetSelectOptions(element) is { Count: > 0 } options)
        {
            host.PickOption(options, anchor, index =>
            {
                if (ReferenceEquals(document, _document) && index >= 0 && index < options.Count)
                    AfterControlChange(document, document.SetValue(element, options[index].Value));
            });
            return true;
        }

        if (state is { } control && control.Type == "range")
        {
            AfterControlChange(document, document.SetRangeAtPoint(element, page, x));
            return true;
        }

        if (state is { } picked && !picked.Disabled && PickerValues.KindOf(picked.Type) is { } kind && kind != "select")
        {
            if (kind == "file")
            {
                var info = document.GetFileInputInfo(element) ?? new FileInputInfo(false, string.Empty);
                var (mimeTypes, extensions) = PickerValues.AcceptTokens(info.Accept);
                host.PickFiles(info.Multiple, mimeTypes, extensions, files => { if (ReferenceEquals(document, _document)) AfterControlChange(document, document.SetFiles(element, files)); });
            }
            else
            {
                host.PickValue(kind, picked.Value ?? string.Empty, anchor, value => { if (ReferenceEquals(document, _document)) AfterControlChange(document, document.SetValue(element, value)); });
            }

            return true;
        }

        if (IsControl(element))
        {
            var result = document.Activate(element);
            AfterControlChange(document, result);
            if (result.Applied)
                return true;
        }

        if (document.IsResetButton(element))
        {
            Queue(() => ResetFormAsync(document, element));
            return true;
        }

        return SubmitForm(element, page, x, y) || ToggleDetails(document, element);
    }

    /// <summary>Does what a click does on a control, for an assistive technology or the keyboard.</summary>
    public bool ActivateControl(AccessibilityNode node)
    {
        if (_document is not { } document || node.ElementId is not { } id || document.GetElement(id) is not { } element)
            return false;

        if (document.IsResetButton(element))
        {
            Queue(() => ResetFormAsync(document, element));
            return true;
        }

        if (!IsControl(element) && SubmitForm(element))
            return true;

        if (ToggleDetails(document, element))
            return true;

        var result = document.Activate(element);
        AfterControlChange(document, result);
        return result.Applied;
    }

    private bool SubmitForm(DocumentElement submitter, int page = -1, double x = 0, double y = 0) =>
        _document is { } document && Submit(document, submitter, page >= 0 ? document.CreateFormSubmission(submitter, page, x, y) : document.CreateFormSubmission(submitter));

    private bool SubmitFromField(DocumentElement field) =>
        _document is { } document && Submit(document, field, document.CreateImplicitSubmission(field));

    // Raises FormSubmitting - or, when a value is not valid, gives the first such control the focus and raises FormInvalid.
    private bool Submit(HtmlDocument document, DocumentElement trigger, FormSubmission? submission)
    {
        if (submission is null)
            return false;

        if (document.GetValidationProblems(trigger) is { Count: > 0 } problems)
        {
            Queue(() => FocusElementAsync(document, problems[0].Element));
            host.FormInvalid(problems);
            return true;
        }

        host.FormSubmitting(submission);
        return true;
    }

    private async Task<T> WithMeasuringAsync<T>(Func<Canvas, ValueTask<T>> work)
    {
        using var measuring = new CoreGraphicsComposer(_context!, 1, 1, 1, 1);
        return await work(measuring.Canvas);
    }

    // A click on a summary opens or closes its details, as a browser does; the document is laid out again, so what follows it moves.
    private bool ToggleDetails(HtmlDocument document, DocumentElement element)
    {
        if (document.GetDetailsToggledBy(element) is null)
            return false;

        Queue(() => ToggleDetailsAsync(document, element));
        return true;
    }

    private async Task ToggleDetailsAsync(HtmlDocument document, DocumentElement element)
    {
        if (_context is null || !ReferenceEquals(document, _document))
            return;

        var result = await WithMeasuringAsync(canvas => document.ToggleDetailsAsync(element, canvas));
        if (ReferenceEquals(document, _document))
            AfterControlChange(document, result);
    }

    private async Task ResetFormAsync(HtmlDocument document, DocumentElement element)
    {
        if (_context is null || !ReferenceEquals(document, _document))
            return;

        var result = await WithMeasuringAsync(canvas => document.ResetFormAsync(element, canvas));
        if (ReferenceEquals(document, _document))
            AfterControlChange(document, result);
    }

    private async Task FocusElementAsync(HtmlDocument document, DocumentElement element)
    {
        if (_context is null || !ReferenceEquals(document, _document))
            return;

        var result = await WithMeasuringAsync(canvas => document.SetFocusAsync(element, true, canvas));
        if (ReferenceEquals(document, _document))
            AfterFocusChange(document, result, document.FocusedNode?.Id);
    }

    private void AfterControlChange(HtmlDocument document, UpdateResult result)
    {
        if (!result.Applied)
            return;

        // The tree is built again after a change, so the nodes an assistive technology holds are replaced.
        RefreshAccessibility();
        Invalidate(result.DirtyRegions);

        // A rule that reads the control's state (input:checked + label) has to be matched again.
        if (document.NeedsRestyle)
            Queue(() => RestyleFormAsync(document));
    }

    private async Task RestyleFormAsync(HtmlDocument document)
    {
        if (_context is null || !ReferenceEquals(document, _document))
            return;

        var result = await WithMeasuringAsync(canvas => document.RestyleFormAsync(canvas));
        if (ReferenceEquals(document, _document))
        {
            UpdateMetrics();
            AfterControlChange(document, result);
        }
    }

    // The state of the focus, the caret and a press that belonged to the document that is going: nothing of it applies to the one that replaces it.
    private void ResetInteractionState()
    {
        StopBlink();
        _focusTargetId = null;
        _fieldAnchor = null;
        _dragKind = DragKind.None;
        _dragElement = null;
        _pendingPointer = null;
        _clearPending = false;
        _editFieldKey = null;
        if (_editing)
        {
            _editing = false;
            host.EditingChanged(false);
        }
    }

    // ---- keyboard focus ----

    /// <summary>Tab (or Shift+Tab): moves the page's focus to the next (or previous) tab stop and returns true, or, past the last one, gives up the page's focus and returns false.</summary>
    public bool StepFocus(bool forward)
    {
        if (_document is not { } document)
            return false;

        var tree = document.GetAccessibilityTree();
        var current = _focusTargetId is { } id ? tree.FindById(id) : document.FocusedNode;
        var next = tree.NextFocus(current, forward);
        if (next is null)
        {
            if (current is not null)
                Queue(ClearDocumentFocusAsync);

            return false;
        }

        _focusTargetId = next.Id;
        Queue(() => FocusNodeAsync(next, true));
        return true;
    }

    /// <summary>Gives the page's focus up (the view lost it).</summary>
    public void LoseFocus()
    {
        if (_document is { } document && (_focusTargetId is not null || document.FocusedNode is not null))
            Queue(ClearDocumentFocusAsync);
    }

    /// <summary>The focus an assistive technology asked for.</summary>
    public void FocusFromAccessibility(AccessibilityNode node)
    {
        _focusTargetId = node.Id;
        Queue(() => FocusNodeAsync(node, false));
    }

    /// <summary>Enter or Space on the part of the page with the focus: a link is followed (Enter), a control is activated. Returns the link node to follow, if it was a link; null otherwise.</summary>
    public (bool Handled, AccessibilityNode? Link) ActivateFocused(bool space)
    {
        if (_document is not { } document
            || (_focusTargetId is { } id ? document.GetAccessibilityTree().FindById(id) : document.FocusedNode) is not { } focused)
        {
            return (false, null);
        }

        if (focused.Role == AccessibilityRole.Link && !space)
            return (true, focused);

        return (AppleAccessibility.IsActivatable(focused.Role) && ActivateControl(focused), null);
    }

    private async Task FocusNodeAsync(AccessibilityNode node, bool focusVisible)
    {
        if (_document is not { } document || _context is null)
            return;

        var result = await WithMeasuringAsync(canvas => document.FocusAsync(node, focusVisible, canvas));
        if (ReferenceEquals(document, _document))
            AfterFocusChange(document, result, node.Id);
    }

    private async Task ClearDocumentFocusAsync()
    {
        _focusTargetId = null;
        if (_document is not { } document || _context is null)
            return;

        var result = await WithMeasuringAsync(canvas => document.SetFocusAsync(null, false, canvas));
        if (ReferenceEquals(document, _document))
            AfterFocusChange(document, result, null);
    }

    // The document was restyled, which can have moved things: repaint, take the tree again, bring the focused part into view and say so.
    private void AfterFocusChange(HtmlDocument document, UpdateResult result, string? focusedId)
    {
        if (result.Applied && result.DirtyRegions.Count > 0)
        {
            UpdateMetrics();
            Invalidate(result.DirtyRegions);
            RefreshAccessibility();
        }

        UpdateEditing();
        if (focusedId is not null && document.GetAccessibilityTree().FindById(focusedId) is { } focused && FrameOf(focused) is { } frame)
            host.Reveal(frame);

        host.FocusChanged();
    }

    // ---- editing ----

    // Called when the page's focus has moved: starts or ends editing and shows or hides the keyboard.
    private void UpdateEditing()
    {
        var state = _document?.GetEditState();
        var key = state is null ? null : _document!.FocusedNode?.Id;
        if (state is null)
        {
            if (_editing)
            {
                _editing = false;
                _editFieldKey = null;
                StopBlink();
                host.EditingChanged(false);
            }

            return;
        }

        if (_editing && key == _editFieldKey)
            return;

        _editing = true;
        _editFieldKey = key;

        // Also when the focus moved from one field to another: the keyboard to show depends on the field (an address, a number, a password).
        host.EditingChanged(true);

        StartBlink();
    }

    private void StartBlink()
    {
        _blink?.Invalidate();
        _caretShown = true;
        _blink = NSTimer.CreateRepeatingScheduledTimer(0.53, timer =>
        {
            if (_document is not { } document || !_editing)
            {
                timer.Invalidate();
                return;
            }

            _caretShown = !_caretShown;
            if (document.SetCaretVisible(_caretShown) is { Applied: true } blink)
                Invalidate(blink.DirtyRegions);
        });
    }

    private void StopBlink()
    {
        _blink?.Invalidate();
        _blink = null;
    }

    // The caret is solid while the user types, then blinks again.
    private void ShowCaret()
    {
        if (!_editing || _document is not { } document)
            return;

        if (document.SetCaretVisible(true) is { Applied: true } shown)
            Invalidate(shown.DirtyRegions);

        StartBlink();
    }

    private async Task FocusFieldAtAsync(HtmlDocument document, DocumentElement element, int page, double x, double y)
    {
        if (_context is null)
            return;

        using var measuring = new CoreGraphicsComposer(_context, 1, 1, 1, 1);
        var result = await document.SetFocusAsync(element, false, measuring.Canvas);
        if (!ReferenceEquals(document, _document))
            return;

        AfterFocusChange(document, result, document.FocusedNode?.Id);
        if (document.GetEditIndexAt(measuring.Canvas, page, x, y) is { } at)
        {
            _fieldAnchor = at;
            AfterEdit(document.SetEditSelection(at, at), CurrentFieldText());
        }
    }

    /// <summary>The text of the field with the focus.</summary>
    public string CurrentFieldText() => _document?.GetEditState()?.Text ?? string.Empty;

    /// <summary>The rectangle of the part of the page with the keyboard focus, in the content (CSS pixels), or null.</summary>
    public CGRect? FocusedFrame => _document?.FocusedNode is { } node ? FrameOf(node) : null;

    /// <summary>The state of the field with the focus: its text, selection and composition; null when none has the focus.</summary>
    public FormEditState? EditState => _document?.GetEditState();

    private void AfterEdit(UpdateResult result, string textBefore)
    {
        if (_document is not { } document)
            return;

        if (document.GetEditState() is { } now && now.Text != textBefore)
            RefreshAccessibility();

        if (result.Applied)
            Invalidate(result.DirtyRegions);

        ShowCaret();
    }

    /// <summary>
    /// Types text at the caret (replacing the selection), or, when <paramref name="replaceStart"/> is given, in place of the <paramref name="replaceLength"/> characters
    /// from there (as an input method that replaces the character before the caret, like a press-and-hold accent, asks).
    /// </summary>
    public bool InsertText(string text, int? replaceStart = null, int replaceLength = 0)
    {
        if (_document is not { } document || document.GetEditState() is not { } state)
            return false;

        var value = state.Type == "textarea" ? text : CaretMoves.ForSingleLine(text);
        if (replaceStart is { } start && start >= 0 && start <= state.Text.Length)
        {
            var end = Math.Min(state.Text.Length, start + Math.Max(replaceLength, 0));
            AfterEdit(document.ReplaceEditText(start, end, value, start + value.Length), state.Text);
            return true;
        }

        AfterEdit(document.InsertText(value), state.Text);
        return true;
    }

    /// <summary>
    /// Sets text that is still being composed (an input method's marked text), in place of the previous composition, or of the selection when there is none, and puts the
    /// caret <paramref name="caretOffset"/> characters into it.
    /// </summary>
    public bool SetComposition(string text, int caretOffset)
    {
        if (_document is not { } document || document.GetEditState() is not { } state)
            return false;

        var value = state.Type == "textarea" ? text : CaretMoves.ForSingleLine(text);
        var start = state.CompositionStart >= 0 ? state.CompositionStart : Math.Min(state.SelectionStart, state.SelectionEnd);
        var end = state.CompositionStart >= 0 ? state.CompositionEnd : Math.Max(state.SelectionStart, state.SelectionEnd);
        start = Math.Clamp(start, 0, state.Text.Length);
        end = Math.Clamp(end, start, state.Text.Length);
        var caret = start + Math.Clamp(caretOffset, 0, value.Length);
        var result = document.ReplaceEditText(start, end, value, caret);
        if (value.Length > 0)
            document.SetComposition(start, start + value.Length);

        AfterEdit(result, state.Text);
        return true;
    }

    /// <summary>Ends composition, keeping the text.</summary>
    public void EndComposition()
    {
        if (_document is { } document && document.GetEditState() is { } state)
            AfterEdit(document.SetComposition(-1, -1), state.Text);
    }

    /// <summary>Deletes the selection, or one character (or word) before or after the caret.</summary>
    public bool Delete(bool backward, bool word)
    {
        if (_document is not { } document || document.GetEditState() is not { } state)
            return false;

        if (word && state.SelectionStart == state.SelectionEnd)
        {
            var caret = state.SelectionEnd;
            var other = CaretMoves.MoveIndex(state.Text, caret, backward ? EditMove.WordLeft : EditMove.WordRight);
            AfterEdit(document.ReplaceEditText(Math.Min(caret, other), Math.Max(caret, other), string.Empty), state.Text);
        }
        else
        {
            AfterEdit(document.DeleteEditText(backward), state.Text);
        }

        return true;
    }

    /// <summary>Moves the caret (or, with <paramref name="extend"/>, the end of the selection).</summary>
    public bool MoveCaret(EditMove move, bool extend)
    {
        if (_document is not { } document || document.GetEditState() is not { } state)
            return false;

        var (start, end) = CaretMoves.Move(state.Text, state.SelectionStart, state.SelectionEnd, move, extend);
        AfterEdit(document.SetEditSelection(start, end), state.Text);
        return true;
    }

    /// <summary>Moves the caret up or down a line of a text area (or to the start or end of the line); false in a one-line field.</summary>
    public bool MoveLine(string name, bool extend)
    {
        if (_document is not { } document || _context is null || document.GetEditState() is not { Type: "textarea" } state)
            return false;

        using var measuring = new CoreGraphicsComposer(_context, 1, 1, 1, 1);
        var canvas = measuring.Canvas;
        var target = CaretMoves.LineMove(name, state.SelectionEnd, lines => document.GetEditIndexByLine(canvas, state.SelectionEnd, lines),
            index => document.GetEditLineRange(canvas, index));
        if (target is not { } to)
            return false;

        AfterEdit(document.SetEditSelection(extend ? state.SelectionStart : to, to), state.Text);
        return true;
    }

    /// <summary>Selects all the text of the field.</summary>
    public bool SelectAllInField()
    {
        if (_document is not { } document || document.GetEditState() is not { } state)
            return false;

        AfterEdit(document.SetEditSelection(0, state.Text.Length), state.Text);
        return true;
    }

    /// <summary>The selected text of the field, for Copy and Cut.</summary>
    public string FieldSelection()
    {
        if (_document?.GetEditState() is not { } state || state.SelectionStart == state.SelectionEnd)
            return string.Empty;

        var start = Math.Min(state.SelectionStart, state.SelectionEnd);
        return state.Text.Substring(start, Math.Abs(state.SelectionEnd - state.SelectionStart));
    }

    /// <summary>Enter in a one-line field: submits its form, when a browser would. In a text area it types a line break.</summary>
    public bool EnterInField()
    {
        if (_document is not { } document || document.GetEditState() is not { } state)
            return false;

        if (state.Type == "textarea")
        {
            AfterEdit(document.InsertText("\n"), state.Text);
            return true;
        }

        return document.FocusedNode?.ElementId is { } id && document.GetElement(id) is { } field && SubmitFromField(field);
    }

    /// <summary>Sets a field's whole value (an assistive technology's "set text").</summary>
    public bool SetFieldText(AccessibilityNode node, string value)
    {
        if (_document is not { } document || node.ElementId is not { } id || document.GetElement(id) is not { } element)
            return false;

        var result = document.SetValue(element, value);
        AfterControlChange(document, result);
        return result.Applied;
    }
}

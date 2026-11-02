using Android.Content;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Java.Lang;
using PeachBrackets.Android.Logic;
using PeachBrackets.Updates;
using PeachDrawing.Skia;
using SkiaSharp;
using System.Threading.Tasks;
using Math = System.Math;

namespace PeachBrackets.Android;

/// <summary>
/// Typing into a text field of the page. The engine draws the field and keeps its text, selection and caret (<see cref="HtmlDocument.GetEditState"/>);
/// this part carries Android's text input to it: an <see cref="IInputConnection"/> for the soft keyboard and its composing text, the keys of a
/// hardware keyboard, the clipboard, a blinking caret, and a tap that puts the caret where it landed.
/// </summary>
public sealed partial class HtmlView
{
    private const int CaretBlinkMilliseconds = 500;

    private bool _editing;
    private string? _editFieldKey;
    private bool _caretShown = true;
    private Runnable? _blinkTick;

    /// <summary>Whether the page's focus is on a text field, which is when the view is a text editor to the system.</summary>
    internal bool IsEditing => _editing;

    /// <inheritdoc />
    public override bool OnCheckIsTextEditor() => _editing;

    /// <inheritdoc />
    public override IInputConnection? OnCreateInputConnection(EditorInfo? outAttrs)
    {
        if (outAttrs is null || _document?.GetEditState() is not { } state)
            return null;

        var (inputType, action) = ImeEdits.InputTypeFor(state.Type);
        outAttrs.InputType = (InputTypes)inputType;
        outAttrs.ImeOptions = (ImeFlags)action | ImeFlags.NoExtractUi;
        outAttrs.InitialSelStart = state.SelectionStart;
        outAttrs.InitialSelEnd = state.SelectionEnd;
        return new FieldInputConnection(this);
    }

    // ---- what the input method asks for ------------------------------------------------------------------------------------------------

    // Applies an edit the input method asked for: the replacement, then the composition it leaves.
    private bool ApplyImeEdit(ImeEdit edit)
    {
        if (_document is not { } document || document.GetEditState() is not { } state)
            return false;

        var result = document.ReplaceEditText(edit.Start, edit.End, edit.Text, edit.Caret);
        if (!result.Applied && edit.Text.Length > 0)
            return false;

        if (edit.CompositionStart >= 0)
            document.SetComposition(edit.CompositionStart, edit.CompositionEnd);

        AfterEdit(result, state.Text);
        return true;
    }

    internal bool ImeCommit(string text, int newCursor)
    {
        if (_document?.GetEditState() is not { } s)
            return false;

        return ApplyImeEdit(ImeEdits.Commit(text, newCursor, s.Text.Length, s.SelectionStart, s.SelectionEnd, s.CompositionStart, s.CompositionEnd));
    }

    internal bool ImeCompose(string text, int newCursor)
    {
        if (_document?.GetEditState() is not { } s)
            return false;

        return ApplyImeEdit(ImeEdits.Compose(text, newCursor, s.Text.Length, s.SelectionStart, s.SelectionEnd, s.CompositionStart, s.CompositionEnd));
    }

    internal bool ImeFinishComposing()
    {
        if (_document is not { } document || document.GetEditState() is null)
            return false;

        AfterEdit(document.SetComposition(-1, -1), CurrentFieldText());
        return true;
    }

    internal bool ImeSetComposingRegion(int start, int end)
    {
        if (_document is not { } document || document.GetEditState() is null)
            return false;

        AfterEdit(document.SetComposition(Math.Min(start, end), Math.Max(start, end)), CurrentFieldText());
        return true;
    }

    internal bool ImeSetSelection(int start, int end)
    {
        if (_document is not { } document || document.GetEditState() is null)
            return false;

        AfterEdit(document.SetEditSelection(start, end), CurrentFieldText());
        return true;
    }

    internal bool ImeDeleteAround(int before, int after)
    {
        if (_document is not { } document || document.GetEditState() is not { } s)
            return false;

        var (afterRange, beforeRange, caret) = ImeEdits.DeleteAround(s.Text.Length, s.SelectionStart, s.SelectionEnd, before, after);
        if (afterRange.End > afterRange.Start)
            document.ReplaceEditText(afterRange.Start, afterRange.End, string.Empty, Math.Min(s.SelectionEnd, s.Text.Length));

        if (beforeRange.End > beforeRange.Start)
        {
            var span = s.SelectionEnd - s.SelectionStart;
            document.ReplaceEditText(beforeRange.Start, beforeRange.End, string.Empty, caret + Math.Max(span, 0));
            if (span != 0)
                document.SetEditSelection(caret, caret + Math.Abs(span));
        }

        AfterEdit(new UpdateResult(true, null, []), s.Text);
        return true;
    }

    internal string CurrentFieldText() => _document?.GetEditState()?.Text ?? string.Empty;

    // The field changed: repaint, tell the input method where the caret is, update what a screen reader sees, and keep the caret on while typing.
    private void AfterEdit(UpdateResult result, string textBefore)
    {
        if (_document is not { } document)
            return;

        var state = document.GetEditState();
        if (state is { } now && now.Text != textBefore)
            RefreshAccessibility(document);

        if (result.Applied)
            _surface.Invalidate();

        if (state is { } s && InputMethod() is { } manager)
            manager.UpdateSelection(this, s.SelectionStart, s.SelectionEnd, s.CompositionStart, s.CompositionEnd);

        ShowCaret();
    }

    private InputMethodManager? InputMethod() => Context?.GetSystemService(Context.InputMethodService) as InputMethodManager;

    // ---- the keyboard and the clipboard ------------------------------------------------------------------------------------------------

    // The keys of a hardware keyboard (and of an input method that sends key events) in a focused text field. Returns whether the key was used.
    private bool HandleEditKey(Keycode keyCode, KeyEvent? e)
    {
        if (_document is not { } document || document.GetEditState() is not { } state || e is { IsAltPressed: true })
            return false;

        var control = e is { IsCtrlPressed: true };
        var shift = e is { IsShiftPressed: true };
        var move = keyCode switch
        {
            Keycode.DpadLeft => control ? EditMove.WordLeft : EditMove.Left,
            Keycode.DpadRight => control ? EditMove.WordRight : EditMove.Right,
            Keycode.MoveHome => EditMove.Home,
            Keycode.MoveEnd => EditMove.End,
            _ => (EditMove?)null,
        };

        if (move is { } m)
        {
            var (start, end) = CaretMoves.Move(state.Text, state.SelectionStart, state.SelectionEnd, m, shift);
            AfterEdit(document.SetEditSelection(start, end), state.Text);
            return true;
        }

        switch (keyCode)
        {
            case Keycode.Del or Keycode.ForwardDel:
                var backward = keyCode == Keycode.Del;
                if (control && state.SelectionStart == state.SelectionEnd)
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

            case Keycode.A when control:
                AfterEdit(document.SetEditSelection(0, state.Text.Length), state.Text);
                return true;

            case Keycode.C when control:
                CopyFieldSelection(state);
                return true;

            case Keycode.X when control:
                CopyFieldSelection(state);
                AfterEdit(document.DeleteEditText(true), state.Text);
                return true;

            case Keycode.V when control:
                PasteIntoField();
                return true;

            default:
                // A printable key: the character it makes, unless it is a shortcut.
                if (!control && e is not null && e.UnicodeChar > 0 && e.UnicodeChar != '\n' && e.UnicodeChar != '\t')
                {
                    var text = char.ConvertFromUtf32(e.UnicodeChar);
                    AfterEdit(document.InsertText(text), state.Text);
                    return true;
                }

                return false;
        }
    }

    private void CopyFieldSelection(FormEditState state)
    {
        var from = Math.Min(state.SelectionStart, state.SelectionEnd);
        var to = Math.Max(state.SelectionStart, state.SelectionEnd);
        if (to <= from || state.Type == "password" || Context?.GetSystemService(Context.ClipboardService) is not global::Android.Content.ClipboardManager clipboard)
            return;

        clipboard.PrimaryClip = ClipData.NewPlainText("text", state.Text[from..to]);
    }

    internal void PasteIntoField()
    {
        if (_document is not { } document || document.GetEditState() is not { } state
            || Context?.GetSystemService(Context.ClipboardService) is not global::Android.Content.ClipboardManager { PrimaryClip: { ItemCount: > 0 } clip })
        {
            return;
        }

        var text = clip.GetItemAt(0)?.CoerceToText(Context)?.ToString() ?? string.Empty;
        text = text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
        AfterEdit(document.InsertText(text), state.Text);
    }

    // ---- focus, the caret and the pointer ----------------------------------------------------------------------------------------------

    // Called when the page's focus has moved: starts or ends editing, shows or hides the soft keyboard, and moves the input method between fields.
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
                InputMethod()?.HideSoftInputFromWindow(WindowToken, HideSoftInputFlags.None);
            }

            return;
        }

        if (_editing && key == _editFieldKey)
            return;

        var wasEditing = _editing;
        _editing = true;
        _editFieldKey = key;
        if (wasEditing)
            InputMethod()?.RestartInput(this);
        else
            InputMethod()?.ShowSoftInput(this, ShowFlags.Implicit);

        StartBlink();
    }

    private void StartBlink()
    {
        _caretShown = true;
        _blinkTick ??= new Runnable(() =>
        {
            if (_document is { } document && _editing)
            {
                _caretShown = !_caretShown;
                if (document.SetCaretVisible(_caretShown).Applied)
                    _surface.Invalidate();

                _handler.PostDelayed(_blinkTick!, CaretBlinkMilliseconds);
            }
        });
        _handler.RemoveCallbacks(_blinkTick);
        _handler.PostDelayed(_blinkTick, CaretBlinkMilliseconds);
    }

    private void StopBlink()
    {
        if (_blinkTick is not null)
            _handler.RemoveCallbacks(_blinkTick);
    }

    // The caret is solid while the user types, then blinks again.
    private void ShowCaret()
    {
        if (!_editing || _document is not { } document)
            return;

        _caretShown = true;
        if (document.SetCaretVisible(true).Applied)
            _surface.Invalidate();

        StartBlink();
    }

    // A tap on a text field gives it the focus and puts the caret where the tap was. Returns whether the point was over a text field.
    private bool FocusTextFieldAt(int page, double x, double y)
    {
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element
            || document.GetFormState(element) is not { Type: "text" or "search" or "url" or "tel" or "email" or "password" or "number" })
        {
            return false;
        }

        RequestFocus();
        _ = FocusFieldAtAsync(document, element, page, x);
        return true;
    }

    private async Task FocusFieldAtAsync(HtmlDocument document, DocumentElement element, int page, double x)
    {
        if (_context is not { } context)
            return;

        UpdateResult result;
        int? index;
        using (var surface = SKSurface.Create(new SKImageInfo(1, 1)))
        using (var measuring = new SkiaComposer(context, surface, 1, 1))
        {
            result = await document.SetFocusAsync(element, false, measuring.Canvas);
            if (!ReferenceEquals(document, _document))
                return;

            AfterFocusChange(document, result, document.FocusedNode?.Id);
            index = document.GetEditIndexAt(measuring.Canvas, page, x);
        }

        if (index is { } at)
            AfterEdit(document.SetEditSelection(at, at), CurrentFieldText());
    }

    // ---- an accessibility service editing a field --------------------------------------------------------------------------------------

    /// <summary>Sets the text of a field, as an accessibility service's <c>ACTION_SET_TEXT</c> does.</summary>
    internal bool SetFieldText(PeachBrackets.Accessibility.AccessibilityNode node, string value)
    {
        if (_document is not { } document || node.ElementId is not { } id || document.GetElement(id) is not { } element)
            return false;

        var result = document.SetValue(element, value);
        AfterControlChange(document, result);
        return result.Applied;
    }

    /// <summary>Selects text in a field, as an accessibility service's <c>ACTION_SET_SELECTION</c> does.</summary>
    internal bool SetFieldSelection(PeachBrackets.Accessibility.AccessibilityNode node, int start, int end)
    {
        if (_document is not { } document || document.GetEditState() is null || document.FocusedNode?.Id != node.Id)
            return false;

        AfterEdit(document.SetEditSelection(start, end), CurrentFieldText());
        return true;
    }

    // ---- the connection ----------------------------------------------------------------------------------------------------------------

    // What the input method sees of the field: its text, selection and composition, and the edits it can make.
    private sealed class FieldInputConnection(HtmlView view) : BaseInputConnection(view, true)
    {
        public override bool CommitText(ICharSequence? text, int newCursorPosition) => view.ImeCommit(text?.ToString() ?? string.Empty, newCursorPosition);

        public override bool SetComposingText(ICharSequence? text, int newCursorPosition) => view.ImeCompose(text?.ToString() ?? string.Empty, newCursorPosition);

        public override bool FinishComposingText() => view.ImeFinishComposing();

        public override bool SetComposingRegion(int start, int end) => view.ImeSetComposingRegion(start, end);

        public override bool SetSelection(int start, int end) => view.ImeSetSelection(start, end);

        public override bool DeleteSurroundingText(int beforeLength, int afterLength) => view.ImeDeleteAround(beforeLength, afterLength);

        // The text around the caret is read from a copy of the field, which the base class answers its questions from.
        public override IEditable? Editable
        {
            get
            {
                if (view._document?.GetEditState() is not { } s)
                    return null;

                var mirror = new SpannableStringBuilder(s.Type == "password" ? new string('•', s.Text.Length) : s.Text);
                global::Android.Text.Selection.SetSelection(mirror, Math.Clamp(s.SelectionStart, 0, mirror.Length()), Math.Clamp(s.SelectionEnd, 0, mirror.Length()));
                return mirror;
            }
        }

        public override bool PerformEditorAction(ImeAction actionCode)
        {
            // The action key of a one-line field leaves it, as Enter on a form field does elsewhere.
            view.StepFocus(true);
            return true;
        }

        public override bool PerformContextMenuAction(int id)
        {
            if (id == global::Android.Resource.Id.SelectAll)
                return view.ImeSetSelection(0, view.CurrentFieldText().Length);

            if (id == global::Android.Resource.Id.Paste)
            {
                view.PasteIntoField();
                return true;
            }

            return false;
        }

        public override bool BeginBatchEdit() => true;

        public override bool EndBatchEdit() => true;
    }
}

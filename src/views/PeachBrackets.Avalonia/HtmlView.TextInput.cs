using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using PeachBrackets.Avalonia.Logic;
using PeachBrackets.HtmlViewCore;
using PeachBrackets.Updates;

namespace PeachBrackets.Avalonia;

// Editing the text of <input> and <textarea>: caret, selection inside the field, typing, IME composition and the cut/copy/paste menu.
// The engine draws the field and keeps its text, selection and caret (HtmlDocument.GetEditState); this part carries Avalonia's input to it.
public sealed partial class HtmlView
{
    // The blink time of the caret; Avalonia has no system setting for it, so it is the usual one.
    private const int CaretBlinkMilliseconds = 530;

    private static readonly bool TextInputWired = WireTextInput();

    private FieldInputClient? _inputClient;
    private DispatcherTimer? _blink;
    private bool _caretShown = true;
    private string? _editFieldKey;
    private int? _fieldAnchor;
    private Task _placement = Task.CompletedTask;

    // The input method asks the control for its client when it takes the keyboard focus, and the caret blinks only while the control has it.
    private static bool WireTextInput()
    {
        TextInputMethodClientRequestedEvent.AddClassHandler<HtmlView>((view, e) =>
        {
            if (view._document is not null)
                e.Client = view._inputClient ??= new FieldInputClient(view);
        });
        GotFocusEvent.AddClassHandler<HtmlView>((view, _) => view.UpdateTextInputClient());
        LostFocusEvent.AddClassHandler<HtmlView>((view, _) => view.StopBlink());
        return true;
    }

    /// <summary>Whether the page's focus is on a text field, which is when typed text goes to the page and not to its scrolling.</summary>
    internal bool IsEditingField => _document?.GetEditState() is not null;

    /// <summary>The work of the last press that put the caret in a field, for a test to wait on.</summary>
    internal Task PlacementTask => _placement;

    // ---- the pointer ----------------------------------------------------------------------------------------------------------------

    // A press on a text field: true when it was one, in which case the press is a drag inside the field and not a click on the page.
    internal bool PressInTextField(int page, double x, double y, int clickCount)
    {
        if (!TryGetTextFieldAt(page, x, y, out var document, out var element))
            return false;

        _fieldAnchor = null;
        _placement = PlaceCaretAsync(document, element, page, x, y, clickCount);
        return true;
    }

    private bool TryGetTextFieldAt(int page, double x, double y, out HtmlDocument document, out DocumentElement element)
    {
        document = null!;
        element = null!;
        if (_document is not { } current || _context is null || current.GetElementAt(page, x, y) is not { } found
            || current.GetFormState(found) is not { } state || !TextInputLogic.IsTextField(state.Type))
        {
            return false;
        }

        document = current;
        element = found;
        return true;
    }

    // Gives the field the focus (when it does not have it), puts the caret where the press was and, for a double or triple click, selects the word or line.
    private async Task PlaceCaretAsync(HtmlDocument document, DocumentElement element, int page, double x, double y, int clickCount)
    {
        if (_context is not { } context)
            return;

        using var measuring = new MeasuringCanvas(context);
        if (!element.Equals(FocusedFieldElement()))
        {
            var result = await document.SetFocusAsync(element, false, measuring.Canvas);
            if (!ReferenceEquals(document, _document))
                return;

            ApplyUpdate(result);
        }

        if (document.GetEditIndexAt(measuring.Canvas, page, x, y) is not { } index || document.GetEditState() is not { } state)
        {
            UpdateTextInputClient();
            return;
        }

        var (start, end) = clickCount switch
        {
            2 => CaretMoves.WordAt(state.Text, index),
            >= 3 => state.Type == "textarea" ? document.GetEditLineRange(measuring.Canvas, index) ?? (0, state.Text.Length) : (0, state.Text.Length),
            _ => (index, index),
        };

        _fieldAnchor = start;
        AfterEdit(document.SetEditSelection(start, end), state.Text);
        UpdateTextInputClient();
    }

    // A drag from inside a field selects between where it began and the pointer.
    private void DragInTextField(int page, double x, double y)
    {
        if (_document is not { } document || _context is not { } context || _fieldAnchor is not { } anchor || document.GetEditState() is null)
            return;

        using var measuring = new MeasuringCanvas(context);
        if (document.GetEditIndexAt(measuring.Canvas, page, x, y) is { } index)
            AfterEdit(document.SetEditSelection(anchor, index), CurrentFieldText());
    }

    // ---- the menu of a text field ---------------------------------------------------------------------------------------------------

    // A press of the secondary button or a long press: the cut/copy/paste menu of the text field under it, when it is over one.
    private void OnSecondaryPress(PointerPressedEventArgs e, global::Avalonia.Point position)
    {
        if (!TryLocate(position, out var page, out var x, out var y) || !TryGetTextFieldAt(page, x, y, out var document, out var element))
            return;

        e.Handled = true;
        _placement = OpenFieldMenuAsync(document, element, page, x, y);
    }

    private async Task OpenFieldMenuAsync(HtmlDocument document, DocumentElement element, int page, double x, double y)
    {
        // A right click inside a selection keeps it, as in a browser; elsewhere it puts the caret there first.
        var keep = element.Equals(FocusedFieldElement()) && document.GetEditState() is { } state && state.SelectionStart != state.SelectionEnd;
        if (!keep)
            await PlaceCaretAsync(document, element, page, x, y, 1);
        else
            UpdateTextInputClient();

        if (ReferenceEquals(document, _document) && IsEditingField)
            BuildFieldMenu().ShowAt(this, true);
    }

    /// <summary>The cut, copy, paste and select all menu of the focused text field.</summary>
    internal MenuFlyout BuildFieldMenu()
    {
        var state = _document?.GetEditState();
        var readOnly = state is not { ReadOnly: false };
        var hasSelection = state is { } s && s.SelectionStart != s.SelectionEnd;
        var copyable = hasSelection && state!.Value.Type != "password";
        var flyout = new MenuFlyout();
        void Add(string header, bool enabled, Func<Task> action)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => _ = action();
            flyout.Items.Add(item);
        }

        Add("Cut", !readOnly && copyable, CutFieldAsync);
        Add("Copy", copyable, CopyFieldAsync);
        Add("Paste", !readOnly, PasteIntoFieldAsync);
        Add("Select all", state is not null, () =>
        {
            SelectAllInField();
            return Task.CompletedTask;
        });
        return flyout;
    }

    // ---- the clipboard --------------------------------------------------------------------------------------------------------------

    internal void SelectAllInField()
    {
        if (_document is { } document && document.GetEditState() is { } state)
            AfterEdit(document.SetEditSelection(0, state.Text.Length), state.Text);
    }

    internal async Task CopyFieldAsync()
    {
        if (_document?.GetEditState() is not { } state)
            return;

        var from = Math.Min(state.SelectionStart, state.SelectionEnd);
        var to = Math.Max(state.SelectionStart, state.SelectionEnd);
        if (to <= from || state.Type == "password" || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(state.Text[from..to]);
    }

    internal async Task CutFieldAsync()
    {
        if (_document is not { } document || document.GetEditState() is not { ReadOnly: false } state || state.Type == "password")
            return;

        await CopyFieldAsync();
        if (ReferenceEquals(document, _document))
            AfterEdit(document.DeleteEditText(true), state.Text);
    }

    internal async Task PasteIntoFieldAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || _document is not { } document || document.GetEditState() is not { ReadOnly: false })
            return;

        var pasted = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(pasted) || !ReferenceEquals(document, _document) || document.GetEditState() is not { } state)
            return;

        var text = TextInputLogic.TypedText(state.Type, pasted);
        if (text.Length > 0)
            InsertTyped(document, state, text);
    }

    // ---- the keyboard ---------------------------------------------------------------------------------------------------------------

    // The element of the text field that has the focus.
    private DocumentElement? FocusedFieldElement() =>
        _document is { } document && document.GetEditState() is not null && document.FocusedNode?.ElementId is { } id ? document.GetElement(id) : null;

    internal string CurrentFieldText() => _document?.GetEditState()?.Text ?? string.Empty;

    // The keys a focused text field edits with: true when the key was used. Tab is left to the focus navigation.
    internal bool HandleEditKey(KeyEventArgs e)
    {
        if (_document is not { } document || document.GetEditState() is not { } state)
            return false;

        var mac = OperatingSystem.IsMacOS();
        var shortcut = TextInputLogic.IsShortcut(e.KeyModifiers, mac);
        var word = TextInputLogic.IsWordMove(e.KeyModifiers, mac);
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (!mac && (e.KeyModifiers & KeyModifiers.Alt) != 0 && (e.KeyModifiers & KeyModifiers.Control) == 0)
            return false;

        if (e.Key is Key.Tab or Key.Escape)
            return false;

        if (state.Type == "textarea" && HandleTextAreaKey(document, state, e.Key, shortcut, shift))
            return true;

        if (e.Key == Key.Enter && !shortcut)
            return FocusedFieldElement() is { } field && SubmitFromField(field);

        var move = e.Key switch
        {
            Key.Left => word ? EditMove.WordLeft : EditMove.Left,
            Key.Right => word ? EditMove.WordRight : EditMove.Right,
            Key.Home => EditMove.Home,
            Key.End => EditMove.End,
            _ => (EditMove?)null,
        };

        if (move is { } m)
        {
            var (start, end) = CaretMoves.Move(state.Text, state.SelectionStart, state.SelectionEnd, m, shift);
            AfterEdit(document.SetEditSelection(start, end), state.Text);
            return true;
        }

        switch (e.Key)
        {
            case Key.Back or Key.Delete:
                var backward = e.Key == Key.Back;
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

            case Key.A when shortcut:
                SelectAllInField();
                return true;

            case Key.C when shortcut:
                _ = CopyFieldAsync();
                return true;

            case Key.X when shortcut:
                _ = CutFieldAsync();
                return true;

            case Key.V when shortcut:
                _ = PasteIntoFieldAsync();
                return true;

            case Key.Insert when shortcut:
                _ = CopyFieldAsync();
                return true;

            default:
                return false;
        }
    }

    // The keys that work on the lines of a text area: Up and Down, Home and End (to the ends of the line; with the shortcut key, of the text), and Enter.
    private bool HandleTextAreaKey(HtmlDocument document, FormEditState state, Key key, bool shortcut, bool shift)
    {
        if (key == Key.Enter && !shortcut)
        {
            AfterEdit(document.InsertText("\n"), state.Text);
            return true;
        }

        var name = key switch
        {
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Home when !shortcut => "Home",
            Key.End when !shortcut => "End",
            _ => null,
        };

        if (name is null || _context is not { } context)
            return false;

        using var measuring = new MeasuringCanvas(context);
        var canvas = measuring.Canvas;
        var target = CaretMoves.LineMove(name, state.SelectionEnd,
            lines => document.GetEditIndexByLine(canvas, state.SelectionEnd, lines), index => document.GetEditLineRange(canvas, index));
        if (target is not { } to)
            return false;

        AfterEdit(document.SetEditSelection(shift ? state.SelectionStart : to, to), state.Text);
        return true;
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!e.Handled && TypeText(e.Text))
            e.Handled = true;
    }

    // Typed or committed text: it goes in at the caret (replacing the selection), or over the text an input method was composing.
    internal bool TypeText(string? typed)
    {
        if (string.IsNullOrEmpty(typed) || _document is not { } document || document.GetEditState() is not { } state)
            return false;

        if (state.ReadOnly)
            return true;

        var text = TextInputLogic.TypedText(state.Type, typed);
        if (text.Length == 0)
            return false;

        InsertTyped(document, state, text);
        return true;
    }

    private void InsertTyped(HtmlDocument document, FormEditState state, string text)
    {
        if (state.CompositionStart >= 0)
        {
            var result = document.ReplaceEditText(state.CompositionStart, state.CompositionEnd, text, state.CompositionStart + text.Length);
            document.SetComposition(-1, -1);
            AfterEdit(result, state.Text);
        }
        else
        {
            AfterEdit(document.InsertText(text), state.Text);
        }
    }

    // ---- the input method -----------------------------------------------------------------------------------------------------------

    // The input method's preedit string: the text being composed is part of the field, drawn underlined, until it is committed through TextInput.
    internal void SetPreedit(string? preedit, int? cursor)
    {
        if (_document is not { } document || document.GetEditState() is not { ReadOnly: false } state)
            return;

        var shaped = preedit is null ? null : TextInputLogic.TypedText(state.Type, preedit);
        if (TextInputLogic.Preedit(shaped, cursor, state.Text, state.SelectionStart, state.SelectionEnd, state.CompositionStart, state.CompositionEnd) is not { } edit)
            return;

        var result = document.ReplaceEditText(edit.Start, edit.End, edit.Text, edit.Caret);
        document.SetComposition(edit.CompositionStart, edit.CompositionEnd);
        AfterEdit(result, state.Text);
    }

    /// <summary>
    /// Tells the input method which field has the focus (its kind, so the right keyboard is offered), where the caret is and what the text is, and starts or stops the
    /// blinking of the caret. Called whenever the page's focus may have moved to or from a text field.
    /// </summary>
    internal void UpdateTextInputClient()
    {
        var state = _document?.GetEditState();
        if (state is not { } field)
        {
            _editFieldKey = null;
            StopBlink();
            _inputClient?.Notify();
            return;
        }

        TextInputOptions.SetContentType(this, TextInputLogic.ContentTypeFor(field.Type));
        TextInputOptions.SetIsSensitive(this, TextInputLogic.IsSensitive(field.Type));
        TextInputOptions.SetMultiline(this, field.Type == "textarea");
        TextInputOptions.SetReturnKeyType(this, field.Type == "textarea" ? TextInputReturnKeyType.Return : field.Type == "search" ? TextInputReturnKeyType.Search : TextInputReturnKeyType.Done);

        var key = _document!.FocusedNode?.Id;
        if (key != _editFieldKey)
        {
            _editFieldKey = key;
            _inputClient?.Reset();
        }

        _inputClient?.Notify();
        ShowCaret();
        ScrollCaretIntoView();
    }

    // The caret rectangle in the control's device independent pixels, or null when the focus is not on a text field.
    internal global::Avalonia.Rect? CaretRectangle()
    {
        if (_document is not { } document || _context is not { } context)
            return null;

        using var measuring = new MeasuringCanvas(context);
        if (document.GetEditPlace(measuring.Canvas) is not { } place || (uint)place.PageIndex >= (uint)_pageTops.Length)
            return null;

        var b = place.Bounds;
        return TextInputLogic.ToControl(_pageTops[place.PageIndex], b.X, b.Y, b.Width, b.Height, PixelsPerPoint, Scale, _scrollX, _scrollY, _dpi);
    }

    // Scrolls the page so the caret is in view.
    private void ScrollCaretIntoView()
    {
        if (CaretRectangle() is not { } rect)
            return;

        if (IsAutoHeight)
        {
            RaiseEvent(new RequestBringIntoViewEventArgs { RoutedEvent = RequestBringIntoViewEvent, TargetObject = this, TargetRect = rect });
            return;
        }

        var (dx, dy) = TextInputLogic.ScrollToReveal(rect, Bounds.Width, Bounds.Height, _dpi);
        if (dx != 0 || dy != 0)
            ScrollTo(_scrollX + dx, _scrollY + dy);
    }

    // The field changed: repaint it, tell the input method and the screen reader, keep the caret on while typing and in view.
    private void AfterEdit(UpdateResult result, string textBefore)
    {
        if (_document is not { } document)
            return;

        ApplyUpdate(result);
        if (document.GetEditState() is { } now && now.Text != textBefore)
            RefreshAccessibility(document);

        _inputClient?.Notify();
        ShowCaret();
        ScrollCaretIntoView();
    }

    // ---- the caret ------------------------------------------------------------------------------------------------------------------

    private void StartBlink()
    {
        _blink ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CaretBlinkMilliseconds) };
        _blink.Tick -= OnBlinkTick;
        _blink.Tick += OnBlinkTick;
        _blink.Stop();
        _blink.Start();
    }

    private void StopBlink()
    {
        _blink?.Stop();
        if (_document is { } document && !_caretShown)
        {
            _caretShown = true;
            ApplyUpdate(document.SetCaretVisible(true));
        }
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        if (_document is not { } document || !IsEditingField)
        {
            _blink?.Stop();
            return;
        }

        _caretShown = !_caretShown;
        ApplyUpdate(document.SetCaretVisible(_caretShown));
    }

    // The caret is solid while the user types, then blinks again.
    private void ShowCaret()
    {
        if (_document is not { } document || !IsEditingField)
            return;

        _caretShown = true;
        ApplyUpdate(document.SetCaretVisible(true));
        if (IsFocused)
            StartBlink();
    }

    // What an input method sees of the field: its text, selection, composition and caret.
    private sealed class FieldInputClient(HtmlView view) : TextInputMethodClient
    {
        public override global::Avalonia.Visual TextViewVisual => view;

        public override bool SupportsPreedit => true;

        public override bool SupportsSurroundingText => true;

        public override string SurroundingText => view._document?.GetEditState() is { Type: not "password" } state ? state.Text : string.Empty;

        public override global::Avalonia.Rect CursorRectangle => view.CaretRectangle() ?? default;

        public override TextSelection Selection
        {
            get => view._document?.GetEditState() is { Type: not "password" } state ? new TextSelection(state.SelectionStart, state.SelectionEnd) : default;
            set
            {
                if (view._document is { } document && document.GetEditState() is { } state)
                    view.AfterEdit(document.SetEditSelection(value.Start, value.End), state.Text);
            }
        }

        public override void SetPreeditText(string? preeditText) => view.SetPreedit(preeditText, null);

        public override void SetPreeditText(string? preeditText, int? cursorPos) => view.SetPreedit(preeditText, cursorPos);

        // Tells the input method what changed.
        public void Notify()
        {
            RaiseSurroundingTextChanged();
            RaiseSelectionChanged();
            RaiseCursorRectangleChanged();
        }

        // The field the input method is typing into is another one: it drops what it was composing.
        public void Reset() => RequestReset();
    }
}

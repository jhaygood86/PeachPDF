
using Microsoft.UI.Xaml.Input;
using PeachBrackets.Updates;
using PeachDrawing.Win2D;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Text.Core;

namespace PeachBrackets.WinUI;

/// <summary>
/// Typing into a text field of the page. The engine draws the field and keeps its text, selection and caret (<see cref="HtmlDocument.GetEditState"/>);
/// this part carries the system's text input to it: the keys that move the caret and delete, characters as they are typed, the clipboard, and, where
/// the system offers it, Text Services Framework through <see cref="CoreTextEditContext"/>, which is what lets an input method, handwriting, dictation
/// and the emoji panel put text in a field. Without it, typed characters arrive as the view's own character events and everything but composition works.
/// </summary>
public sealed partial class HtmlView
{
    private CoreTextEditContext? _editContext;
    private bool _textServicesTried;
    private bool _editing;
    private string? _editFieldKey;
    private bool _composing;
    private bool _inTextServices;
    private string _servicesText = string.Empty;
    private char _highSurrogate;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _blink;
    private bool _caretShown = true;

    private void WireTextInput() => CharacterReceived += OnCharacterReceived;

    // ---- the keyboard ------------------------------------------------------------------------------------------------------------------

    // The keys of a focused text field. Returns whether the key was used.
    internal bool HandleEditKey(VirtualKey key)
    {
        if (_document is not { } document || document.GetEditState() is not { } state || IsDown(VirtualKey.Menu))
            return false;

        var control = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        var move = key switch
        {
            VirtualKey.Left => control ? EditMove.WordLeft : EditMove.Left,
            VirtualKey.Right => control ? EditMove.WordRight : EditMove.Right,
            VirtualKey.Home => EditMove.Home,
            VirtualKey.End => EditMove.End,
            _ => (EditMove?)null,
        };

        if (move is { } m)
        {
            var (start, end) = HtmlViewEditing.Move(state.Text, state.SelectionStart, state.SelectionEnd, m, shift);
            AfterEdit(document.SetEditSelection(start, end), state.Text);
            return true;
        }

        switch (key)
        {
            case VirtualKey.Back or VirtualKey.Delete:
                var backward = key == VirtualKey.Back;
                if (control && state.SelectionStart == state.SelectionEnd)
                {
                    var caret = state.SelectionEnd;
                    var other = HtmlViewEditing.MoveIndex(state.Text, caret, backward ? EditMove.WordLeft : EditMove.WordRight);
                    AfterEdit(document.ReplaceEditText(Math.Min(caret, other), Math.Max(caret, other), string.Empty), state.Text);
                }
                else
                {
                    AfterEdit(document.DeleteEditText(backward), state.Text);
                }

                return true;

            case VirtualKey.A when control:
                AfterEdit(document.SetEditSelection(0, state.Text.Length), state.Text);
                return true;

            case VirtualKey.C when control:
                CopyFieldSelection(state);
                return true;

            case VirtualKey.X when control:
                CopyFieldSelection(state);
                AfterEdit(document.DeleteEditText(true), state.Text);
                return true;

            case VirtualKey.V when control:
                _ = PasteIntoFieldAsync();
                return true;

            default:
                return false;
        }
    }

    private void CopyFieldSelection(FormEditState state)
    {
        var from = Math.Min(state.SelectionStart, state.SelectionEnd);
        var to = Math.Max(state.SelectionStart, state.SelectionEnd);
        if (to <= from || state.Type == "password")
            return;

        var package = new DataPackage();
        package.SetText(state.Text[from..to]);
        Clipboard.SetContent(package);
    }

    private async Task PasteIntoFieldAsync()
    {
        var content = Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Text) || _document is not { } document || document.GetEditState() is not { } state)
            return;

        var text = HtmlViewEditing.ForSingleLine(await content.GetTextAsync());
        if (ReferenceEquals(document, _document))
            AfterEdit(document.InsertText(text), state.Text);
    }

    // A character typed. When text services are in use they deliver typed text themselves, so this would put it in twice.
    private void OnCharacterReceived(Microsoft.UI.Xaml.UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (_editContext is not null && _editing || IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Menu))
            return;

        if (TypeCharacter((char)e.Character))
            e.Handled = true;
    }

    // One character of typed text (half of a surrogate pair waits for the other half). Returns whether it was taken.
    internal bool TypeCharacter(char character)
    {
        if (_document is not { } document || document.GetEditState() is not { } state)
            return false;

        string text;
        if (char.IsHighSurrogate(character))
        {
            _highSurrogate = character;
            return true;
        }

        if (char.IsLowSurrogate(character) && _highSurrogate != '\0')
        {
            text = new string([_highSurrogate, (char)character]);
            _highSurrogate = '\0';
        }
        else if (HtmlViewEditing.IsTypedText(character))
        {
            text = character.ToString();
        }
        else
        {
            return false;
        }

        AfterEdit(document.InsertText(text), state.Text);
        return true;
    }

    // The field changed: repaint it, tell the screen reader and the text services, and show the caret (it stays on while the user is typing).
    private void AfterEdit(UpdateResult result, string textBefore)
    {
        if (_document is not { } document)
            return;

        Repaint(result);
        var state = document.GetEditState();
        if (state is { } now && now.Text != textBefore)
            RefreshAccessibility(document);

        TellTextServices(state, textBefore);
        ShowCaret();
    }

    // ---- text services -----------------------------------------------------------------------------------------------------------------

    private void EnsureTextServices()
    {
        if (_textServicesTried)
            return;

        _textServicesTried = true;
        try
        {
            var context = CoreTextServicesManager.GetForCurrentView().CreateEditContext();
            context.InputPaneDisplayPolicy = CoreTextInputPaneDisplayPolicy.Automatic;
            context.TextRequested += (_, args) =>
            {
                var text = CurrentFieldText();
                var range = args.Request.Range;
                var start = Math.Clamp(range.StartCaretPosition, 0, text.Length);
                var end = Math.Clamp(range.EndCaretPosition, start, text.Length);
                args.Request.Text = text[start..end];
            };
            context.SelectionRequested += (_, args) =>
            {
                var state = _document?.GetEditState();
                args.Request.Selection = new CoreTextRange
                {
                    StartCaretPosition = state?.SelectionStart ?? 0,
                    EndCaretPosition = state?.SelectionEnd ?? 0,
                };
            };
            context.LayoutRequested += OnTextLayoutRequested;
            context.TextUpdating += OnTextUpdating;
            context.SelectionUpdating += (_, args) =>
            {
                args.Result = CoreTextSelectionUpdatingResult.Succeeded;
                if (_document is { } document)
                    InTextServices(() => AfterEdit(document.SetEditSelection(args.Selection.StartCaretPosition, args.Selection.EndCaretPosition), CurrentFieldText()));
            };
            context.FormatUpdating += (_, args) => args.Result = CoreTextFormatUpdatingResult.Succeeded;
            context.CompositionStarted += (_, _) => _composing = true;
            context.CompositionCompleted += (_, _) =>
            {
                _composing = false;
                if (_document is { } document)
                    InTextServices(() => Repaint(document.SetComposition(-1, -1)));
            };
            _editContext = context;
        }
        catch (Exception)
        {
            // This kind of window does not offer text services; typed characters arrive as character events instead (OnCharacterReceived).
            _editContext = null;
        }
    }

    private void OnTextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs args)
    {
        if (_document is not { } document || document.GetEditState() is null)
        {
            args.Result = CoreTextTextUpdatingResult.Failed;
            return;
        }

        var before = CurrentFieldText();
        var start = Math.Min(args.Range.StartCaretPosition, args.Range.EndCaretPosition);
        var end = Math.Max(args.Range.StartCaretPosition, args.Range.EndCaretPosition);
        var composing = _composing;
        InTextServices(() =>
        {
            var result = document.ReplaceEditText(start, end, HtmlViewEditing.ForSingleLine(args.Text), args.NewSelection.EndCaretPosition);
            args.Result = result.Applied ? CoreTextTextUpdatingResult.Succeeded : CoreTextTextUpdatingResult.Failed;
            if (result.Applied && composing)
                document.SetComposition(start, start + args.Text.Length);

            AfterEdit(result, before);
        });
    }

    // Tells the system's input method where the field and its caret are on the screen, so it can put its candidate window beside them.
    private void OnTextLayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs args)
    {
        if (_document is not { } document || _context is null)
            return;

        using var measuring = new Win2dComposer(_context, 1, 1);
        if (document.GetEditPlace(measuring.Canvas) is not { } caret)
            return;

        var bounds = ToScreen(caret.PageIndex, caret.Bounds);
        args.Request.LayoutBounds.TextBounds = bounds;
        args.Request.LayoutBounds.ControlBounds = bounds;
    }

    // A rectangle of a page in points, as the rectangle of the screen in physical pixels the text services ask for.
    private Rect ToScreen(int page, PeachDrawing.Core.Rect bounds)
    {
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var top = (uint)page < (uint)_pageTops.Length ? _pageTops[page] : 0;
        var local = new Rect(bounds.X * PixelsPerPoint, top + (bounds.Y * PixelsPerPoint), Math.Max(bounds.Width * PixelsPerPoint, 1), bounds.Height * PixelsPerPoint);
        var origin = _canvas.TransformToVisual(null).TransformPoint(new Point(local.X, local.Y));
        double windowX = 0, windowY = 0;
        if (XamlRoot?.ContentIslandEnvironment?.AppWindowId is { } id && Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id) is { } window)
        {
            var frame = (window.Size.Width - window.ClientSize.Width) / 2.0;
            windowX = window.Position.X + frame;
            windowY = window.Position.Y + (window.Size.Height - window.ClientSize.Height) - frame;
        }

        return new Rect(windowX + (origin.X * scale), windowY + (origin.Y * scale), local.Width * scale, local.Height * scale);
    }

    private string CurrentFieldText() => _document?.GetEditState()?.Text ?? string.Empty;

    // Changes the document makes on the text services' behalf must not be reported back to them as if they were ours.
    private void InTextServices(Action action)
    {
        var was = _inTextServices;
        _inTextServices = true;
        try
        {
            action();
        }
        finally
        {
            _inTextServices = was;
        }
    }

    // The field's text or selection changed behind the text services' back (a key, the clipboard, a screen reader): tell them.
    private void TellTextServices(FormEditState? state, string textBefore)
    {
        if (_editContext is not { } context || _inTextServices || state is not { } now)
            return;

        var selection = new CoreTextRange { StartCaretPosition = now.SelectionStart, EndCaretPosition = now.SelectionEnd };
        try
        {
            if (now.Text != textBefore)
            {
                context.NotifyTextChanged(new CoreTextRange { StartCaretPosition = 0, EndCaretPosition = textBefore.Length }, now.Text.Length, selection);
            }
            else
            {
                context.NotifySelectionChanged(selection);
            }

            context.NotifyLayoutChanged();
        }
        catch (Exception)
        {
            // The system dropped the context; the next focus makes a new notification.
        }
    }

    // ---- focus, the caret and the pointer ----------------------------------------------------------------------------------------------

    // Called when the page's focus has moved: starts or ends editing, and moves the text services to the field that now has the focus.
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
                _composing = false;
                try
                {
                    _editContext?.NotifyFocusLeave();
                }
                catch (Exception)
                {
                    // Nothing to leave.
                }

                StopBlink();
            }

            return;
        }

        EnsureTextServices();
        if (_editing && key == _editFieldKey)
            return;

        if (_editing)
        {
            try
            {
                _editContext?.NotifyFocusLeave();
            }
            catch (Exception)
            {
                // Nothing to leave.
            }
        }

        _editing = true;
        _editFieldKey = key;
        _servicesText = state.Value.Text;
        try
        {
            if (_editContext is { } context)
            {
                context.InputScope = state.Value.Type switch
                {
                    "password" => CoreTextInputScope.Password,
                    "email" => CoreTextInputScope.EmailAddress,
                    "url" => CoreTextInputScope.Url,
                    "tel" => CoreTextInputScope.TelephoneNumber,
                    "number" => CoreTextInputScope.Number,
                    "search" => CoreTextInputScope.Search,
                    _ => CoreTextInputScope.Default,
                };
                context.NotifyFocusEnter();
            }
        }
        catch (Exception)
        {
            // Typed characters still arrive as character events.
        }

        StartBlink();
    }

    private void StartBlink()
    {
        _caretShown = true;
        _blink ??= CreateBlinkTimer();
        _blink.Start();
    }

    private void StopBlink() => _blink?.Stop();

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateBlinkTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        var interval = GetCaretBlinkTime();
        timer.Interval = TimeSpan.FromMilliseconds(interval is 0 or uint.MaxValue ? 530 : interval);
        timer.Tick += (_, _) =>
        {
            if (_document is { } document && _editing)
            {
                _caretShown = !_caretShown;
                Repaint(document.SetCaretVisible(_caretShown));
            }
        };
        return timer;
    }

    // The caret is solid while the user types, then blinks again.
    private void ShowCaret()
    {
        if (!_editing || _document is not { } document)
            return;

        _caretShown = true;
        Repaint(document.SetCaretVisible(true));
        _blink?.Stop();
        _blink?.Start();
    }

    // A click on a text field gives it the focus and puts the caret where the click was. Returns whether the point was over a text field.
    private bool FocusTextFieldAt(int page, double x, double y)
    {
        if (_document is not { } document || document.GetElementAt(page, x, y) is not { } element
            || document.GetFormState(element) is not { Type: "text" or "search" or "url" or "tel" or "email" or "password" or "number" })
        {
            return false;
        }

        Focus(Microsoft.UI.Xaml.FocusState.Pointer);
        _ = FocusFieldAtAsync(document, element, page, x);
        return true;
    }

    private async Task FocusFieldAtAsync(HtmlDocument document, DocumentElement element, int page, double x)
    {
        if (_context is null)
            return;

        using var measuring = new Win2dComposer(_context, 1, 1);
        var result = await document.SetFocusAsync(element, false, measuring.Canvas);
        if (!ReferenceEquals(document, _document))
            return;

        AfterFocusChange(document, result, document.FocusedNode?.Id);
        if (document.GetEditIndexAt(measuring.Canvas, page, x) is { } index)
            AfterEdit(document.SetEditSelection(index, index), CurrentFieldText());
    }

    /// <summary>The text a field holds, for a screen reader's Value pattern.</summary>
    internal string? FieldValue(PeachBrackets.Accessibility.AccessibilityNode node) =>
        _document is { } document && node.ElementId is { } id && document.GetElement(id) is { } element ? document.GetFormState(element)?.Value : null;

    /// <summary>Sets the text of a field, as a screen reader's Value pattern does.</summary>
    internal bool SetFieldValue(PeachBrackets.Accessibility.AccessibilityNode node, string value)
    {
        if (_document is not { } document || node.ElementId is not { } id || document.GetElement(id) is not { } element)
            return false;

        var result = document.SetValue(element, value);
        AfterControlChange(document, result);
        TellTextServices(document.GetEditState(), _servicesText);
        return result.Applied;
    }

    [DllImport("user32.dll")]
    private static extern uint GetCaretBlinkTime();
}

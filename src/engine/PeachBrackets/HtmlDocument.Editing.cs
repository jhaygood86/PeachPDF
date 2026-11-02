using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Paint.Content;
using PeachBrackets.Html.Updates;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachBrackets;

public sealed partial class HtmlDocument
{
    /// <summary>
    /// What the text field that has the focus holds, where its selection or caret is, and what an input method is composing in it. Null when the
    /// focus is not on a text-like <c>&lt;input&gt;</c> (text, search, URL, telephone, email, password or number).
    /// </summary>
    /// <returns>the field's editing state, or null</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public FormEditState? GetEditState()
    {
        _ = Pages;
        if (EditableField() is not { } field)
            return null;

        var text = field.GetAttribute("value", string.Empty);
        var session = SessionOf(field, text);
        return new FormEditState(text, session.Start, session.End, session.CompositionStart, session.CompositionEnd, TypeOf(field), MaxLengthOf(field), IsLocked(field));
    }

    /// <summary>Selects a range of the focused text field, or puts its caret there when the two positions are the same.</summary>
    /// <param name="start">where the selection starts, from 0 to the length of the text; clamped</param>
    /// <param name="end">where it ends, which is where the caret is</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult SetEditSelection(int start, int end)
    {
        _ = Pages;
        if (EditableField() is not { } field)
            return UpdateResult.NotApplied("The focus is not on a text field.");

        var text = field.GetAttribute("value", string.Empty);
        var session = SessionOf(field, text);
        session.Start = Math.Clamp(start, 0, text.Length);
        session.End = Math.Clamp(end, 0, text.Length);
        return Changed([field]);
    }

    /// <summary>
    /// Replaces a range of the focused text field with new text and puts the caret after it (or where <paramref name="caret"/> says), as typing,
    /// pasting and an input method's commits do. The text is cut to the field's <c>maxlength</c>, and line breaks are dropped. Not applied to a
    /// field that is read-only or disabled.
    /// </summary>
    /// <param name="start">where the range starts, clamped to the text</param>
    /// <param name="end">where it ends</param>
    /// <param name="text">the text to put there; may be empty to delete the range</param>
    /// <param name="caret">where the caret goes afterwards, or -1 for the end of the new text</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult ReplaceEditText(int start, int end, string text, int caret = -1)
    {
        ArgumentNullException.ThrowIfNull(text);
        _ = Pages;
        if (EditableField() is not { } field)
            return UpdateResult.NotApplied("The focus is not on a text field.");
        if (IsLocked(field))
            return UpdateResult.NotApplied("The field is read-only or disabled.");

        var current = field.GetAttribute("value", string.Empty);
        var session = SessionOf(field, current);
        start = Math.Clamp(start, 0, current.Length);
        end = Math.Clamp(end, start, current.Length);

        var insert = text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
        if (MaxLengthOf(field) is >= 0 and var max)
            insert = Truncate(insert, Math.Max(max - (current.Length - (end - start)), 0));

        var value = string.Concat(current.AsSpan(0, start), insert, current.AsSpan(end));
        SetControlAttribute(field, "value", value);

        var at = caret >= 0 ? Math.Clamp(caret, 0, value.Length) : start + insert.Length;
        session.Start = session.End = at;
        session.CompositionStart = session.CompositionEnd = -1;
        return Changed([field]);
    }

    /// <summary>
    /// Types text into the focused text field: it takes the place of the text an input method is composing, or else of the selection. See
    /// <see cref="ReplaceEditText"/>.
    /// </summary>
    /// <param name="text">the text to insert</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _ = Pages;
        if (EditableField() is not { } field)
            return UpdateResult.NotApplied("The focus is not on a text field.");

        var session = SessionOf(field, field.GetAttribute("value", string.Empty));
        return session.CompositionStart >= 0
            ? ReplaceEditText(session.CompositionStart, session.CompositionEnd, text)
            : ReplaceEditText(Math.Min(session.Start, session.End), Math.Max(session.Start, session.End), text);
    }

    /// <summary>
    /// Deletes the selection of the focused text field, or when it is only a caret the character before it (Backspace) or after it (Delete) - a
    /// whole grapheme cluster, so an accented letter or an emoji goes at once.
    /// </summary>
    /// <param name="backward"><see langword="true"/> for Backspace, <see langword="false"/> for Delete</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult DeleteEditText(bool backward)
    {
        _ = Pages;
        if (EditableField() is not { } field)
            return UpdateResult.NotApplied("The focus is not on a text field.");

        var text = field.GetAttribute("value", string.Empty);
        var session = SessionOf(field, text);
        var from = Math.Min(session.Start, session.End);
        var to = Math.Max(session.Start, session.End);
        if (from == to)
        {
            if (backward)
                from = PreviousBoundary(text, from);
            else
                to = NextBoundary(text, to);
        }

        return from == to ? UpdateResult.NotApplied("There is nothing to delete.") : ReplaceEditText(from, to, string.Empty);
    }

    /// <summary>
    /// Marks the text an input method is composing in the focused text field, which is drawn underlined until it is committed, or clears the mark
    /// with -1.
    /// </summary>
    /// <param name="start">where the composition starts, or -1 for none</param>
    /// <param name="end">where it ends</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult SetComposition(int start, int end)
    {
        _ = Pages;
        if (EditableField() is not { } field)
            return UpdateResult.NotApplied("The focus is not on a text field.");

        var text = field.GetAttribute("value", string.Empty);
        var session = SessionOf(field, text);
        if (start < 0)
        {
            session.CompositionStart = session.CompositionEnd = -1;
        }
        else
        {
            session.CompositionStart = Math.Clamp(start, 0, text.Length);
            session.CompositionEnd = Math.Clamp(end, session.CompositionStart, text.Length);
        }

        return Changed([field]);
    }

    /// <summary>Shows or hides the caret of the focused text field, which a host does on a timer to make it blink. The caret is shown by default.</summary>
    /// <param name="visible">whether the caret is drawn</param>
    /// <returns>whether it was applied and the regions to repaint</returns>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public UpdateResult SetCaretVisible(bool visible)
    {
        _ = Pages;
        var session = _container.FormEdit;
        if (session.CaretVisible == visible)
            return UpdateResult.NotApplied("The caret is already so.");

        session.CaretVisible = visible;
        return EditableField() is { } field ? Changed([field]) : new UpdateResult(true, null, []);
    }

    /// <summary>
    /// Where a position of the focused text field is on the page: a thin rectangle as tall as a line of its text, at the caret. A host gives it to the
    /// system's text services, so an input method can put its candidate window next to what is being typed.
    /// </summary>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <param name="index">the position in the text, or -1 for the caret</param>
    /// <returns>the page and rectangle, or null when the focus is not on a text field or it is not on a page</returns>
    /// <exception cref="ArgumentNullException"><paramref name="measuringCanvas"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public TextEditPlace? GetEditPlace(Canvas measuringCanvas, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        _ = Pages;
        if (EditableField() is not { } field)
            return null;

        var regions = StyleUpdater.DirtyRegionsOf(_container, field, _container.FragmentTree?.Fragmentainers ?? []);
        if (regions.Count == 0)
            return null;

        var text = field.GetAttribute("value", string.Empty);
        var session = SessionOf(field, text);
        var at = Math.Clamp(index >= 0 ? index : session.End, 0, text.Length);
        var units = _unitsPerPoint;
        var region = regions[0];
        var font = field.ActualFont;
        var left = (field.ActualBorderLeftWidth + field.ActualPaddingLeft) / units;
        var top = (field.ActualBorderTopWidth + field.ActualPaddingTop) / units;
        var contentHeight = Math.Max(region.Bounds.Height - top - ((field.ActualBorderBottomWidth + field.ActualPaddingBottom) / units), 0);
        var offset = ScreenFormControlPainter.OffsetOf(measuringCanvas, field, ScreenFormControlPainter.DisplayText(field, text), at) / units;
        var lineHeight = font.Height / units;
        return new TextEditPlace(region.PageIndex, new Rect(
            region.Bounds.X + left + offset, region.Bounds.Y + top + Math.Max((contentHeight - lineHeight) / 2, 0), 1, lineHeight));
    }

    /// <summary>
    /// The position in the focused text field's text that a point on its page is nearest to - where a click puts the caret. Clicks left of the text
    /// give 0, right of it the length, and a point is rounded to the nearest edge between two grapheme clusters.
    /// </summary>
    /// <param name="measuringCanvas">a canvas to measure text on</param>
    /// <param name="pageIndex">the zero-based index of the page the point is on</param>
    /// <param name="x">the distance from the left edge of the sheet, in points</param>
    /// <returns>the position, or null when the focus is not on a text field or it is not on that page</returns>
    /// <exception cref="ArgumentNullException"><paramref name="measuringCanvas"/> is null</exception>
    /// <exception cref="InvalidOperationException">the document has not been laid out</exception>
    public int? GetEditIndexAt(Canvas measuringCanvas, int pageIndex, double x)
    {
        ArgumentNullException.ThrowIfNull(measuringCanvas);
        _ = Pages;
        if (EditableField() is not { } field || GetEditPlace(measuringCanvas, 0) is not { } origin || origin.PageIndex != pageIndex)
            return null;

        var text = field.GetAttribute("value", string.Empty);
        var display = ScreenFormControlPainter.DisplayText(field, text);
        var target = x - origin.Bounds.X;
        var best = 0;
        var bestDistance = double.MaxValue;
        foreach (var index in BoundariesOf(text))
        {
            var distance = Math.Abs((ScreenFormControlPainter.OffsetOf(measuringCanvas, field, display, index) / _unitsPerPoint) - target);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    // Every place between two grapheme clusters, the two ends included.
    private static IEnumerable<int> BoundariesOf(string text)
    {
        foreach (var start in StringInfo.ParseCombiningCharacters(text))
            yield return start;

        yield return text.Length;
    }

    // The focused element, when it is a field whose text can be edited.
    private CssBox? EditableField()
    {
        if (_focused < 0 || FindByOrdinal(_container.Root, _focused) is not CssBoxFormField { HtmlTag: not null } field)
            return null;

        return field.HtmlTag!.Name.Equals("input", StringComparison.OrdinalIgnoreCase)
            && TypeOf(field) is "text" or "search" or "url" or "tel" or "email" or "password" or "number"
            ? field
            : null;
    }

    private static bool IsLocked(CssBox field) => field.HtmlTag!.HasAttribute("disabled") || field.HtmlTag.HasAttribute("readonly");

    private static int MaxLengthOf(CssBox field) =>
        int.TryParse(field.GetAttribute("maxlength", string.Empty), NumberStyles.None, CultureInfo.InvariantCulture, out var max) ? max : -1;

    // The session of the field, started at the end of its text when the focus has just come to it.
    private Html.FormEditSession SessionOf(CssBox field, string text)
    {
        var session = _container.FormEdit;
        var ordinal = field.HtmlTag!.SourceOrdinal;
        if (session.Ordinal != ordinal)
        {
            session.Ordinal = ordinal;
            session.Start = session.End = text.Length;
            session.CompositionStart = session.CompositionEnd = -1;
        }

        session.Start = Math.Clamp(session.Start, 0, text.Length);
        session.End = Math.Clamp(session.End, 0, text.Length);
        if (session.CompositionStart >= 0)
        {
            session.CompositionStart = Math.Clamp(session.CompositionStart, 0, text.Length);
            session.CompositionEnd = Math.Clamp(session.CompositionEnd, session.CompositionStart, text.Length);
        }

        return session;
    }

    // A value was set from outside: a composition over the old text means nothing now.
    private void ClearComposition(CssBox control)
    {
        var session = _container.FormEdit;
        if (session.Ordinal == control.HtmlTag!.SourceOrdinal)
        {
            session.CompositionStart = session.CompositionEnd = -1;
            session.Ordinal = -1;
        }
    }

    private static string Truncate(string text, int length)
    {
        if (text.Length <= length)
            return text;

        // Never in the middle of a surrogate pair.
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;

        return text[..length];
    }

    private static int PreviousBoundary(string text, int index)
    {
        var previous = 0;
        foreach (var start in StringInfo.ParseCombiningCharacters(text))
        {
            if (start >= index)
                break;

            previous = start;
        }

        return index == 0 ? 0 : previous;
    }

    private static int NextBoundary(string text, int index) =>
        index >= text.Length ? text.Length : index + StringInfo.GetNextTextElementLength(text, index);
}

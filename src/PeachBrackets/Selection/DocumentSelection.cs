using PeachBrackets.Html.TextGeometry;
using PeachBrackets.Text;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Selection;

/// <summary>How far <see cref="DocumentSelection.Modify"/> moves.</summary>
public enum SelectionGranularity
{
    /// <summary>One character (a grapheme cluster).</summary>
    Character,

    /// <summary>To the end of the next word forward, or the start of the previous one backward.</summary>
    Word,

    /// <summary>One line down (forward) or up (backward), keeping the horizontal position.</summary>
    Line,

    /// <summary>To the end (forward) or start (backward) of the current line: the End and Home keys.</summary>
    LineBoundary,

    /// <summary>To the end (forward) or start (backward) of the current paragraph or other block, or of the next or previous one if already there.</summary>
    Block,

    /// <summary>To the end (forward) or start (backward) of the document.</summary>
    Document,
}

/// <summary>Which way <see cref="DocumentSelection.Modify"/> moves.</summary>
public enum SelectionDirection
{
    /// <summary>Towards the start of the document (up, for <see cref="SelectionGranularity.Line"/>), in logical order.</summary>
    Backward,

    /// <summary>Towards the end of the document (down, for <see cref="SelectionGranularity.Line"/>), in logical order.</summary>
    Forward,
}

/// <summary>What <see cref="DocumentSelection.SelectionChanged"/> reports.</summary>
public sealed class SelectionChangedEventArgs : EventArgs
{
    internal SelectionChangedEventArgs(TextPosition? anchor, TextPosition? focus, IReadOnlyList<DirtyRegion> dirtyRegions)
    {
        Anchor = anchor;
        Focus = focus;
        DirtyRegions = dirtyRegions;
    }

    /// <summary>The fixed end of the selection after the change, or null if there is none.</summary>
    public TextPosition? Anchor { get; }

    /// <summary>The moving end of the selection (the caret) after the change, or null if there is none.</summary>
    public TextPosition? Focus { get; }

    /// <summary>
    /// What looks different now - where the selection (or caret) was and where it is - to repaint, as
    /// <see cref="HtmlDocument.PaintPageRegionAsync"/> takes them.
    /// </summary>
    public IReadOnlyList<DirtyRegion> DirtyRegions { get; }
}

/// <summary>
/// The selection of a laid-out document, in the manner of the DOM's <c>Selection</c>: an anchor, a focus (the caret end), movement by character,
/// word, line and block, extending, and the text, rectangles and caret position of what is selected. It is a model a consumer drives from its
/// pointer and keyboard events; it draws nothing itself. With <see cref="HtmlDocumentOptions.EnableSelectionStyling"/> every change is also
/// handed to <see cref="HtmlDocument.SetSelectedRanges"/>, so the next paint shows it in the document's <c>::selection</c> style, and
/// <see cref="SelectionChanged"/> reports just the regions to repaint. Get it from <see cref="HtmlDocument.Selection"/>.
/// </summary>
/// <remarks>Positions are those of <see cref="HtmlDocument.TextRuns"/>, so the document needs <see cref="HtmlDocumentOptions.RecordTextRuns"/>. Moving is in logical order: in right-to-left text, the arrow key that moves towards the left is a backward move.</remarks>
public sealed class DocumentSelection
{
    private readonly HtmlDocument _document;
    private TextPosition? _anchor;
    private TextPosition? _focus;
    private double? _goalX;

    internal DocumentSelection(HtmlDocument document) => _document = document;

    /// <summary>Raised after a change that moved the anchor or the focus.</summary>
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    /// <summary>The fixed end of the selection, or null if nothing is selected and there is no caret.</summary>
    public TextPosition? Anchor => _anchor;

    /// <summary>The moving end of the selection - where the caret is - or null if nothing is selected and there is no caret.</summary>
    public TextPosition? Focus => _focus;

    /// <summary>Whether there is a selection or a caret at all.</summary>
    public bool HasSelection => _anchor is not null;

    /// <summary>Whether the anchor and the focus are the same place: a caret, selecting no text. True too when there is nothing at all.</summary>
    public bool IsCollapsed => _anchor == _focus;

    /// <summary>Whether the focus comes before the anchor.</summary>
    public bool IsBackward => _anchor is { } anchor && _focus is { } focus && focus.CompareTo(anchor) < 0;

    /// <summary>1 when there is a selection or a caret, otherwise 0, as in the DOM.</summary>
    public int RangeCount => HasSelection ? 1 : 0;

    /// <summary>The selected text as a range with its start first, or null if there is no selection. An empty range for a caret.</summary>
    public TextRange? GetRange() =>
        _anchor is { } anchor && _focus is { } focus
            ? anchor.CompareTo(focus) <= 0 ? new TextRange(anchor, focus) : new TextRange(focus, anchor)
            : null;

    /// <summary>The selected text, as <see cref="HtmlDocument.GetText"/> reads it; empty for a caret or no selection.</summary>
    public string GetText() => GetRange() is { } range ? _document.GetText(range.Start, range.End) : string.Empty;

    /// <inheritdoc/>
    public override string ToString() => GetText();

    /// <summary>The rectangles, one per line and in points, that cover the selected text, each with its page.</summary>
    public IReadOnlyList<DirtyRegion> GetRegions() => Regions(_anchor, _focus, inflate: 0);

    /// <summary>
    /// Where a caret at the focus is drawn: a thin rectangle as tall as the line's font, in points on its page, or null if there is no
    /// selection.
    /// </summary>
    /// <param name="width">the caret's width in points</param>
    public DirtyRegion? GetCaretBounds(double width = 1) =>
        _focus is { } focus ? CaretRegion(focus, width) : null;

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="focus"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">a position is not in the document's text</exception>
    public void SetBaseAndExtent(TextPosition anchor, TextPosition focus)
    {
        Validate(anchor);
        Validate(focus);
        Set(anchor, focus, goalX: null);
    }

    /// <summary>Puts the caret at <paramref name="position"/>, selecting nothing.</summary>
    /// <exception cref="ArgumentOutOfRangeException">the position is not in the document's text</exception>
    public void Collapse(TextPosition position) => SetBaseAndExtent(position, position);

    /// <summary>Collapses the selection to its start.</summary>
    public void CollapseToStart()
    {
        if (GetRange() is { } range)
            Set(range.Start, range.Start, goalX: null);
    }

    /// <summary>Collapses the selection to its end.</summary>
    public void CollapseToEnd()
    {
        if (GetRange() is { } range)
            Set(range.End, range.End, goalX: null);
    }

    /// <summary>Moves the focus to <paramref name="position"/>, keeping the anchor (there is one already, or the focus becomes the anchor too).</summary>
    /// <exception cref="ArgumentOutOfRangeException">the position is not in the document's text</exception>
    public void Extend(TextPosition position)
    {
        Validate(position);
        Set(_anchor ?? position, position, goalX: null);
    }

    /// <summary>Selects all of the document's text.</summary>
    public void SelectAll()
    {
        var runs = _document.TextRuns;
        if (runs.Count == 0)
        {
            RemoveAllRanges();
            return;
        }

        Set(new TextPosition(0, 0), new TextPosition(runs.Count - 1, runs[^1].Text.Length), goalX: null);
    }

    /// <summary>Clears the selection and the caret.</summary>
    public void RemoveAllRanges() => Set(null, null, goalX: null);

    /// <summary>Puts the caret at the character nearest a point of a page, as a click does.</summary>
    /// <returns>whether there was text on the page to put it in</returns>
    public bool SelectAt(int pageIndex, double x, double y)
    {
        if (_document.GetCaretPosition(pageIndex, x, y) is not { } position)
            return false;

        Set(position, position, goalX: null);
        return true;
    }

    /// <summary>Moves the focus to the character nearest a point of a page, as a drag or a shift-click does.</summary>
    /// <returns>whether there was text on the page to move it to</returns>
    public bool ExtendTo(int pageIndex, double x, double y)
    {
        if (_document.GetCaretPosition(pageIndex, x, y) is not { } position)
            return false;

        Set(_anchor ?? position, position, goalX: null);
        return true;
    }

    /// <summary>Selects the word at a point of a page, as a double click does.</summary>
    /// <returns>whether there was a word there</returns>
    public bool SelectWordAt(int pageIndex, double x, double y) =>
        _document.HitTest(pageIndex, x, y) is { } position && _document.GetWordAt(position) is { } word && TrySelect(word);

    /// <summary>Selects the block (paragraph, list item, <c>pre</c>) at a point of a page, as a triple click does.</summary>
    /// <returns>whether there was a block there</returns>
    public bool SelectBlockAt(int pageIndex, double x, double y) =>
        _document.HitTest(pageIndex, x, y) is { } position && _document.GetBlockAt(position) is { } block && TrySelect(block);

    /// <summary>Selects a range, anchor at its start.</summary>
    public void Select(TextRange range)
    {
        Validate(range.Start);
        Validate(range.End);
        Set(range.Start, range.End, goalX: null);
    }

    private bool TrySelect(TextRange? range)
    {
        if (range is not { } found)
            return false;

        Set(found.Start, found.End, goalX: null);
        return true;
    }

    /// <summary>
    /// Moves the caret, or extends the selection, by a unit - what the arrow keys do, with the modifier keys picking the granularity. A move
    /// that does not extend, over a selection, first collapses it: to its end for a forward move and its start for a backward one, by
    /// a character.
    /// </summary>
    /// <param name="extend">true to move only the focus, keeping the anchor (shift held); false to move the caret</param>
    /// <param name="direction">which way</param>
    /// <param name="granularity">how far</param>
    /// <returns>whether anything changed; false at the edge of the document, or with no selection to move</returns>
    public bool Modify(bool extend, SelectionDirection direction, SelectionGranularity granularity)
    {
        var runs = _document.TextRuns;
        if (_focus is not { } focus || _anchor is not { } anchor)
            return false;

        if (!extend && !IsCollapsed && granularity == SelectionGranularity.Character)
        {
            var range = GetRange()!.Value;
            var edge = direction == SelectionDirection.Forward ? range.End : range.Start;
            Set(edge, edge, goalX: null);
            return true;
        }

        var goal = _goalX;
        if (SelectionMovement.Move(runs, focus, direction, granularity, ref goal) is not { } moved)
            return false;

        Set(extend ? anchor : moved, moved, goal);
        return true;
    }

    private void Validate(TextPosition position)
    {
        var runs = _document.TextRuns;
        if ((uint)position.RunIndex >= (uint)runs.Count)
            throw new ArgumentOutOfRangeException(nameof(position), position, $"The document has {runs.Count} text run(s).");

        if ((uint)position.CharIndex > (uint)runs[position.RunIndex].Text.Length)
            throw new ArgumentOutOfRangeException(nameof(position), position, "The character index is outside the text of its run.");
    }

    private void Set(TextPosition? anchor, TextPosition? focus, double? goalX)
    {
        var changed = _anchor != anchor || _focus != focus;
        var before = changed ? Regions(_anchor, _focus, inflate: 1) : [];

        _anchor = anchor;
        _focus = focus;
        _goalX = goalX;
        if (!changed)
            return;

        // The document paints whatever it was last handed, and holds nothing when the selection is a caret.
        if (_document.SelectionStylingEnabled)
            _document.SetSelectedRanges(GetRange() is { } range && !IsCollapsed ? [range] : null);

        var dirty = new List<DirtyRegion>(before);
        dirty.AddRange(Regions(anchor, focus, inflate: 1));
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(anchor, focus, dirty));
    }

    private List<DirtyRegion> Regions(TextPosition? anchor, TextPosition? focus, double inflate)
    {
        var regions = new List<DirtyRegion>();
        if (anchor is not { } a || focus is not { } f)
            return regions;

        var (from, to) = a.CompareTo(f) <= 0 ? (a, f) : (f, a);
        foreach (var (page, rect) in TextRunQueries.SelectionRegions(_document.TextRuns, from, to))
            regions.Add(new DirtyRegion(page, Inflate(rect, inflate)));

        if (a == f && CaretRegion(f, 1) is { } caret)
            regions.Add(new DirtyRegion(caret.PageIndex, Inflate(caret.Bounds, inflate + 1)));

        return regions;
    }

    private DirtyRegion? CaretRegion(TextPosition focus, double width)
    {
        var runs = _document.TextRuns;
        if ((uint)focus.RunIndex >= (uint)runs.Count)
            return null;

        var run = runs[focus.RunIndex];
        var x = run.CharacterOffsets[Math.Min(focus.CharIndex, run.CharacterOffsets.Count - 1)];
        return new DirtyRegion(run.PageIndex, new Rect(x - (width / 2), run.Baseline - run.Ascent, width, run.Ascent + run.Descent));
    }

    private static Rect Inflate(Rect rect, double by) => new(rect.X - by, rect.Y - by, rect.Width + (by * 2), rect.Height + (by * 2));
}

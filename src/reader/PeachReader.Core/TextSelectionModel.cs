using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF;

namespace PeachReader.Core;

/// <summary>A caret position in a document: between two characters of a page's text.</summary>
/// <param name="Page">the zero-based page number</param>
/// <param name="Index">the caret position in the page's text, from 0 to its length</param>
public readonly record struct TextPosition(int Page, int Index) : IComparable<TextPosition>
{
    /// <inheritdoc />
    public int CompareTo(TextPosition other) => Page != other.Page ? Page.CompareTo(other.Page) : Index.CompareTo(other.Index);

    /// <summary>Whether this position is before another.</summary>
    /// <param name="a">the first position</param>
    /// <param name="b">the second position</param>
    /// <returns><see langword="true"/> when <paramref name="a"/> comes first</returns>
    public static bool operator <(TextPosition a, TextPosition b) => a.CompareTo(b) < 0;

    /// <summary>Whether this position is after another.</summary>
    /// <param name="a">the first position</param>
    /// <param name="b">the second position</param>
    /// <returns><see langword="true"/> when <paramref name="a"/> comes last</returns>
    public static bool operator >(TextPosition a, TextPosition b) => a.CompareTo(b) > 0;
}

/// <summary>
/// The text a reader has selected, which may run across pages: a drag from an anchor to a focus, a double-click on a word, a triple-click
/// on a line, or everything. It answers where to paint the highlight on a page and what to copy. The selection is one immutable value that is
/// replaced whole, so a thread that paints it while another changes it sees either the old selection or the new one, never half of each.
/// </summary>
public sealed class TextSelectionModel
{
    private sealed record Span(TextPosition Anchor, TextPosition Focus);

    private readonly Func<int, PdfPageText> _textOf;
    private readonly int _pageCount;
    private volatile Span? _span;

    /// <summary>Makes an empty selection over a document's pages.</summary>
    /// <param name="textOf">gives the text of a page, such as <see cref="ReaderDocument.GetText"/></param>
    /// <param name="pageCount">the number of pages</param>
    public TextSelectionModel(Func<int, PdfPageText> textOf, int pageCount)
    {
        ArgumentNullException.ThrowIfNull(textOf);
        _textOf = textOf;
        _pageCount = pageCount;
    }

    /// <summary>Raised when the selection changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether any text is selected.</summary>
    public bool HasSelection => Range is { } r && r.Start != r.End;

    /// <summary>The selection from its first position to its last, or <see langword="null"/> when there is none.</summary>
    public (TextPosition Start, TextPosition End)? Range => _span is { } s ? (s.Anchor < s.Focus ? (s.Anchor, s.Focus) : (s.Focus, s.Anchor)) : null;

    private void Set(Span? span)
    {
        _span = span;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts a selection at a caret position, as on pressing the pointer.</summary>
    /// <param name="page">the page</param>
    /// <param name="caret">the caret position in the page's text</param>
    public void Begin(int page, int caret)
    {
        var position = new TextPosition(page, caret);
        Set(new Span(position, position));
    }

    /// <summary>Moves the free end of the selection, as on dragging or shift-clicking; starts one at the point when there is none.</summary>
    /// <param name="page">the page</param>
    /// <param name="caret">the caret position in the page's text</param>
    public void Extend(int page, int caret)
    {
        var position = new TextPosition(page, caret);
        Set(new Span(_span?.Anchor ?? position, position));
    }

    /// <summary>Selects the word at a caret position.</summary>
    /// <param name="page">the page</param>
    /// <param name="caret">the caret position in the page's text; a position on the right half of a letter belongs to that letter</param>
    public void SelectWord(int page, int caret) => Select(page, caret, text => text.WordAt);

    /// <summary>Selects the whole line at a caret position.</summary>
    /// <param name="page">the page</param>
    /// <param name="caret">the caret position in the page's text</param>
    public void SelectLine(int page, int caret) => Select(page, caret, text => text.LineAt);

    private void Select(int page, int caret, Func<PdfPageText, Func<int, PdfTextRange>> expand)
    {
        var text = _textOf(page);
        // A caret at the very end belongs to the last character.
        var range = expand(text)(Math.Max(0, caret - (caret > 0 && caret == text.Length ? 1 : 0)));
        if (range.Length == 0) return;
        Set(new Span(new TextPosition(page, range.Start), new TextPosition(page, range.End)));
    }

    /// <summary>Selects all the text of the document.</summary>
    public void SelectAll()
    {
        if (_pageCount == 0) return;
        Set(new Span(new TextPosition(0, 0), new TextPosition(_pageCount - 1, _textOf(_pageCount - 1).Length)));
    }

    /// <summary>Selects nothing.</summary>
    public void Clear()
    {
        if (_span is null) return;
        Set(null);
    }

    /// <summary>The range of a page's text that is selected.</summary>
    /// <param name="page">the page</param>
    /// <returns>the range, empty when none of the page is selected</returns>
    public PdfTextRange SelectedOnPage(int page)
    {
        if (Range is not { } r || page < r.Start.Page || page > r.End.Page) return new PdfTextRange(0, 0);
        int length = _textOf(page).Length;
        int start = page == r.Start.Page ? Math.Min(r.Start.Index, length) : 0;
        int end = page == r.End.Page ? Math.Min(r.End.Index, length) : length;
        return end > start ? new PdfTextRange(start, end - start) : new PdfTextRange(0, 0);
    }

    /// <summary>The rectangles to highlight on a page, in points from its top-left.</summary>
    /// <param name="page">the page</param>
    /// <returns>one rectangle per selected line of the page, none when it has no selected text</returns>
    public IReadOnlyList<Rect> RectsOnPage(int page)
    {
        var range = SelectedOnPage(page);
        return range.Length == 0 ? [] : _textOf(page).GetBounds(range.Start, range.Length);
    }

    /// <summary>The selected text, for copying; pages are separated by a blank line.</summary>
    /// <returns>the text, empty when nothing is selected</returns>
    public string GetSelectedText()
    {
        if (Range is not { } r) return "";
        var sb = new StringBuilder();
        for (int page = r.Start.Page; page <= r.End.Page && page < _pageCount; page++)
        {
            var range = SelectedOnPage(page);
            if (range.Length == 0) continue;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(_textOf(page).GetText(range.Start, range.Length));
        }

        return sb.ToString();
    }
}

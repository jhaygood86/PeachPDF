using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF;

/// <summary>A run of characters of a <see cref="PdfPageText"/>: where it starts and how many characters it covers.</summary>
/// <param name="Start">the index of the first character in <see cref="PdfPageText.Text"/></param>
/// <param name="Length">the number of characters</param>
public readonly record struct PdfTextRange(int Start, int Length)
{
    /// <summary>The index just after the last character.</summary>
    public int End => Start + Length;
}

/// <summary>How <see cref="PdfPageText.Find"/> compares text.</summary>
[Flags]
public enum PdfTextSearchOptions
{
    /// <summary>Ignore case; "é" and "e" are different letters.</summary>
    None = 0,
    /// <summary>Upper and lower case letters are different.</summary>
    MatchCase = 1,
    /// <summary>A match must not touch a letter or digit on either side.</summary>
    WholeWord = 2,
    /// <summary>Accents and other combining marks are ignored, so "resume" finds "résumé".</summary>
    IgnoreDiacritics = 4,
}

/// <summary>
/// The text of one page, in reading order, with where each character is on the page: what a viewer needs to select, copy and search it. The
/// positions are in the same terms as <see cref="PdfPageSource.GetLinks"/> and <see cref="PdfPageSource.Paint(int, Canvas)"/>: points from the
/// top-left of the page as it is shown, with y growing downwards and the page's rotation applied.
/// </summary>
/// <remarks>
/// Text is read in the order the file draws it, which is reading order for most producers; lines are made of the glyphs that follow one
/// another along a baseline (so rotated text is a line too), a space is added where the gap between two glyphs is wider than a space would be,
/// and a new line starts a new line of <see cref="Text"/>. Glyphs with no Unicode mapping in the file are left out. Text drawn invisibly (the
/// text layer of a scanned page) is included.
/// </remarks>
public sealed class PdfPageText
{
    // The box of a glyph relative to its baseline, as a share of the font size.
    private const float Ascent = 0.8f, Descent = 0.2f;

    private readonly string _text;
    private readonly Rect[] _rects;
    private readonly int[] _lineOf;
    private readonly List<PdfTextRange> _lines;

    private PdfPageText(string text, Rect[] rects, int[] lineOf, List<PdfTextRange> lines)
    {
        _text = text;
        _rects = rects;
        _lineOf = lineOf;
        _lines = lines;
    }

    /// <summary>A page with no text.</summary>
    internal static PdfPageText Empty { get; } = new("", [], [], []);

    /// <summary>All the text of the page, lines separated by <c>\n</c>.</summary>
    public string Text => _text;

    /// <summary>The number of characters in <see cref="Text"/>.</summary>
    public int Length => _text.Length;

    /// <summary>The number of lines.</summary>
    public int LineCount => _lines.Count;

    /// <summary>Returns the characters from <paramref name="start"/> for <paramref name="length"/> characters, clamped to the text.</summary>
    /// <param name="start">the index of the first character</param>
    /// <param name="length">how many characters</param>
    /// <returns>the text, which may be shorter than asked for at the end of the page</returns>
    public string GetText(int start, int length)
    {
        (start, length) = Clamp(start, length);
        return _text.Substring(start, length);
    }

    /// <summary>The rectangles that cover a range of characters: one per line the range touches, in line order.</summary>
    /// <param name="start">the index of the first character</param>
    /// <param name="length">how many characters</param>
    /// <returns>the rectangles, in points from the top-left of the page; empty for an empty range</returns>
    public IReadOnlyList<Rect> GetBounds(int start, int length)
    {
        (start, length) = Clamp(start, length);
        var result = new List<Rect>();
        int i = start, end = start + length;
        while (i < end)
        {
            int line = _lineOf[i];
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            for (; i < end && _lineOf[i] == line; i++)
            {
                if (_text[i] == '\n') continue;
                var r = _rects[i];
                left = Math.Min(left, r.X); top = Math.Min(top, r.Y);
                right = Math.Max(right, r.X + r.Width); bottom = Math.Max(bottom, r.Y + r.Height);
            }
            if (left <= right) result.Add(new Rect(left, top, right - left, bottom - top));
        }
        return result;
    }

    /// <summary>
    /// The caret position nearest a point: the index of the character the point is on if it is in the left half of it, the next index if in the
    /// right half.
    /// </summary>
    /// <param name="point">a point in points from the top-left of the page</param>
    /// <param name="nearest">
    /// <see langword="false"/> to answer only for a point that is on a character; <see langword="true"/> to answer with the closest character
    /// of the closest line wherever the point is, which is how a drag that leaves the text keeps selecting
    /// </param>
    /// <returns>a value from 0 to <see cref="Length"/>, or -1 when <paramref name="nearest"/> is <see langword="false"/> and the point is on no character</returns>
    public int HitTest(PaintPoint point, bool nearest = false)
    {
        if (_text.Length == 0) return -1;
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < _rects.Length; i++)
        {
            if (_text[i] == '\n') continue;
            var r = _rects[i];
            double dx = point.X < r.X ? r.X - point.X : point.X > r.X + r.Width ? point.X - (r.X + r.Width) : 0;
            double dy = point.Y < r.Y ? r.Y - point.Y : point.Y > r.Y + r.Height ? point.Y - (r.Y + r.Height) : 0;
            if (dx == 0 && dy == 0) return point.X < r.X + r.Width / 2 ? i : i + 1;
            if (!nearest) continue;
            // The line decides first (a drag below a line picks that line), then the horizontal distance within it.
            double distance = dy * 1000 + dx;
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        if (best < 0) return -1;
        var b = _rects[best];
        return point.X < b.X + b.Width / 2 ? best : best + 1;
    }

    /// <summary>The word around a character: letters and digits that run together, or the single character when it is not part of a word.</summary>
    /// <param name="index">a character index</param>
    /// <returns>the range of the word, empty when <paramref name="index"/> is outside the text</returns>
    public PdfTextRange WordAt(int index)
    {
        if ((uint)index >= (uint)_text.Length) return new PdfTextRange(Math.Clamp(index, 0, _text.Length), 0);
        if (!IsWordChar(_text[index])) return new PdfTextRange(index, 1);
        int start = index, end = index + 1;
        while (start > 0 && IsWordChar(_text[start - 1])) start--;
        while (end < _text.Length && IsWordChar(_text[end])) end++;
        return new PdfTextRange(start, end - start);
    }

    /// <summary>The line a character is on, without its line break.</summary>
    /// <param name="index">a character index</param>
    /// <returns>the range of the line, empty when <paramref name="index"/> is outside the text</returns>
    public PdfTextRange LineAt(int index)
    {
        if ((uint)index >= (uint)_text.Length) return new PdfTextRange(Math.Clamp(index, 0, _text.Length), 0);
        return _lines[_lineOf[index]];
    }

    /// <summary>Finds every occurrence of <paramref name="query"/>. A line break in the text matches a space in the query, so a phrase that wraps is found.</summary>
    /// <param name="query">the text to look for; an empty query finds nothing</param>
    /// <param name="options">how to compare</param>
    /// <returns>the occurrences, in order and not overlapping</returns>
    public IReadOnlyList<PdfTextRange> Find(string query, PdfTextSearchOptions options = PdfTextSearchOptions.None)
    {
        ArgumentNullException.ThrowIfNull(query);
        var found = new List<PdfTextRange>();
        if (query.Length == 0 || _text.Length == 0) return found;
        string haystack = Fold(_text, options), needle = Fold(query, options);
        for (int at = 0; (at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0; at += needle.Length)
        {
            if ((options & PdfTextSearchOptions.WholeWord) != 0
                && ((at > 0 && IsWordChar(haystack[at - 1])) || (at + needle.Length < haystack.Length && IsWordChar(haystack[at + needle.Length]))))
            {
                at -= needle.Length - 1;      // the next candidate starts one character on
                continue;
            }
            found.Add(new PdfTextRange(at, needle.Length));
        }
        return found;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>One character out for each in, so indexes into the result are indexes into the text.</summary>
    private static string Fold(string s, PdfTextSearchOptions options)
    {
        var chars = new char[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c is '\n' or '\r' or '\t' or ' ') c = ' ';
            if ((options & PdfTextSearchOptions.IgnoreDiacritics) != 0 && c > 0x7F)
            {
                string d = c.ToString().Normalize(NormalizationForm.FormD);
                if (d.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(d[0]) != UnicodeCategory.NonSpacingMark) c = d[0];
            }
            if ((options & PdfTextSearchOptions.MatchCase) == 0) c = char.ToLowerInvariant(c);
            chars[i] = c;
        }
        return new string(chars);
    }

    private (int Start, int Length) Clamp(int start, int length)
    {
        start = Math.Clamp(start, 0, _text.Length);
        return (start, Math.Clamp(length, 0, _text.Length - start));
    }

    /// <summary>Builds the text of a page from its glyphs in the order the file drew them.</summary>
    internal static PdfPageText Build(IReadOnlyList<PdfTextGlyph> glyphs)
    {
        var text = new StringBuilder();
        var rects = new List<Rect>();
        var lineOf = new List<int>();
        var lines = new List<PdfTextRange>();
        int lineStart = 0;
        bool open = false;
        Vector2 lastEnd = default, dir = default;
        float lastSize = 0;

        void Add(char c, Rect r)
        {
            text.Append(c); rects.Add(r); lineOf.Add(lines.Count);
        }

        foreach (var g in glyphs)
        {
            if (string.IsNullOrEmpty(g.Text)) continue;
            float size = g.Up.Length();
            if (size < 1e-3f) continue;
            var glyphDir = new Vector2(-g.Up.Y, g.Up.X) / size;
            Vector2 delta = g.Start - lastEnd;
            float along = open ? Vector2.Dot(delta, dir) : 0;
            float across = open ? Math.Abs(delta.X * -dir.Y + delta.Y * dir.X) : 0;
            float reference = Math.Max(size, lastSize);
            bool sameLine = open && across <= 0.6f * reference && along >= -0.6f * reference && Vector2.Dot(glyphDir, dir) > 0.7f;

            if (open && !sameLine)
            {
                // The line break is a character with no width, at the end of the line it ends.
                Add('\n', new Rect(lastEnd.X, lastEnd.Y, 0, 0));
                lines.Add(new PdfTextRange(lineStart, text.Length - 1 - lineStart));
                lineStart = text.Length;
                open = false;
            }
            else if (sameLine && along > 0.15f * reference && text[^1] != ' ' && !char.IsWhiteSpace(g.Text[0]))
            {
                var gapRect = Bounds(lastEnd, g.Start, g.Up / size * lastSize);
                Add(' ', gapRect);
            }

            var advance = g.End - g.Start;
            for (int k = 0; k < g.Text.Length; k++)
            {
                var p0 = g.Start + advance * (k / (float)g.Text.Length);
                var p1 = g.Start + advance * ((k + 1) / (float)g.Text.Length);
                Add(g.Text[k], Bounds(p0, p1, g.Up));
            }
            if (!open) dir = glyphDir;
            open = true;
            lastEnd = g.End;
            lastSize = size;
        }

        if (open) lines.Add(new PdfTextRange(lineStart, text.Length - lineStart));
        return new PdfPageText(text.ToString(), [.. rects], [.. lineOf], lines);
    }

    /// <summary>The axis-aligned box of a glyph from its baseline start and end and its up vector.</summary>
    private static Rect Bounds(Vector2 start, Vector2 end, Vector2 up)
    {
        Span<Vector2> corners = [start + up * Ascent, end + up * Ascent, start - up * Descent, end - up * Descent];
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        foreach (var c in corners)
        {
            left = Math.Min(left, c.X); top = Math.Min(top, c.Y);
            right = Math.Max(right, c.X); bottom = Math.Max(bottom, c.Y);
        }
        return new Rect(left, top, right - left, bottom - top);
    }
}

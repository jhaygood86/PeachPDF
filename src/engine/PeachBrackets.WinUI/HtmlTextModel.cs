using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using PeachBrackets.Accessibility;

namespace PeachBrackets.WinUI;

/// <summary>The sizes a text range is measured and moved in.</summary>
internal enum TextUnitKind
{
    Character,
    Word,
    Line,
    Paragraph,
    Document,
}

/// <summary>One block of the document's text and the node it came from.</summary>
internal readonly record struct TextSegment(int Start, int Length, AccessibilityNode? Node)
{
    public int End => Start + Length;
}

/// <summary>
/// The text a reader walks through a document, as one string with the node each stretch of it came from, and the arithmetic on ranges of it that
/// UI Automation's Text pattern asks for: expanding a range to a character, word, line or paragraph, moving it, and moving one end of it. It touches
/// no window, so it is tested on its own. Each block of text (a paragraph, a heading, a list item, a field's value) ends in a line break; a line is
/// taken to be a block, since the engine lays text out into lines only for painting.
/// </summary>
internal sealed class HtmlTextModel
{
    private readonly List<TextSegment> _segments = [];
    private readonly Dictionary<TextUnitKind, int[]> _boundaries = [];

    private HtmlTextModel(string text)
    {
        Text = text;
    }

    /// <summary>All the text, blocks separated by a line break.</summary>
    public string Text { get; }

    public int Length => Text.Length;

    public IReadOnlyList<TextSegment> Segments => _segments;

    /// <summary>Reads the text of a document, or of a part of it, in reading order.</summary>
    public static HtmlTextModel Build(AccessibilityNode root, Func<AccessibilityNode, string?>? valueOf = null)
    {
        var blocks = new List<(string Text, AccessibilityNode? Node)>();

        void Visit(AccessibilityNode node)
        {
            var own = valueOf?.Invoke(node);
            var hasChildren = false;
            foreach (var child in node.ReadingChildren)
            {
                hasChildren = true;
                break;
            }

            if (own is null && hasChildren && node.Role != AccessibilityRole.Link)
            {
                foreach (var child in node.ReadingChildren)
                    Visit(child);

                return;
            }

            var reading = own ?? node.ReadingText;
            if (reading.Length == 0)
                return;

            blocks.Add((reading, node));
        }

        Visit(root);
        return FromBlocks(blocks);
    }

    /// <summary>A text made of the given blocks, each ending in a line break.</summary>
    public static HtmlTextModel FromBlocks(IEnumerable<(string Text, AccessibilityNode? Node)> blocks)
    {
        var text = new StringBuilder();
        var segments = new List<TextSegment>();
        foreach (var (block, node) in blocks)
        {
            segments.Add(new TextSegment(text.Length, block.Length, node));
            text.Append(block).Append('\n');
        }

        var model = new HtmlTextModel(text.ToString());
        model._segments.AddRange(segments);
        return model;
    }

    /// <summary>The text of a stretch of it, clipped to what there is; a negative maximum means all of it.</summary>
    public string Slice(int start, int end, int maxLength = -1)
    {
        start = Math.Clamp(start, 0, Length);
        end = Math.Clamp(end, start, Length);
        if (maxLength >= 0)
            end = Math.Min(end, start + maxLength);

        return Text[start..end];
    }

    /// <summary>The block a position falls in, or null for none (the document has no text).</summary>
    public TextSegment? SegmentAt(int position)
    {
        foreach (var segment in _segments)
        {
            if (position < segment.End + 1)
                return segment;
        }

        return _segments.Count > 0 ? _segments[^1] : null;
    }

    /// <summary>The blocks a stretch of text touches.</summary>
    public IEnumerable<TextSegment> SegmentsIn(int start, int end)
    {
        foreach (var segment in _segments)
        {
            if (segment.End > start && segment.Start < Math.Max(end, start + 1))
                yield return segment;
        }
    }

    /// <summary>Where the next stretch of the given size starts after each one, as sorted offsets from 0 to the end of the text.</summary>
    public IReadOnlyList<int> Boundaries(TextUnitKind unit)
    {
        if (_boundaries.TryGetValue(unit, out var cached))
            return cached;

        var set = new SortedSet<int> { 0, Length };
        switch (unit)
        {
            case TextUnitKind.Character:
                foreach (var index in StringInfo.ParseCombiningCharacters(Text))
                    set.Add(index);
                break;

            case TextUnitKind.Word:
                for (var i = 0; i < Text.Length; i++)
                {
                    var startsWord = !char.IsWhiteSpace(Text[i]) && (i == 0 || char.IsWhiteSpace(Text[i - 1]) || Class(Text[i - 1]) != Class(Text[i]));
                    if (startsWord || (i > 0 && Text[i - 1] == '\n'))
                        set.Add(i);
                }
                break;

            case TextUnitKind.Line:
            case TextUnitKind.Paragraph:
                foreach (var segment in _segments)
                    set.Add(segment.Start);
                break;
        }

        return _boundaries[unit] = [.. set];

        static int Class(char c) => char.IsLetterOrDigit(c) ? 0 : 1;
    }

    /// <summary>The range, widened to whole units: it starts where the unit it starts in starts, and ends where the unit it ends in ends.</summary>
    public (int Start, int End) Expand(int start, int end, TextUnitKind unit)
    {
        var bounds = Boundaries(unit);
        start = Math.Clamp(start, 0, Length);
        end = Math.Clamp(end, start, Length);
        var from = AtOrBefore(bounds, start);
        var to = start == end ? After(bounds, start) : AtOrAfter(bounds, end);
        return (from, Math.Max(to, from));
    }

    /// <summary>The range moved by whole units, after widening it to whole units; says how many it moved (fewer than asked at either end of the text).</summary>
    public (int Start, int End, int Moved) Move(int start, int end, TextUnitKind unit, int count)
    {
        var bounds = Boundaries(unit);
        (start, end) = Expand(start, end, unit);
        if (count == 0 || bounds.Count < 2)
            return (start, end, 0);

        // The places a unit can start are every boundary but the end of the text.
        var starts = bounds.Count > 1 ? bounds.Take(bounds.Count - 1).ToList() : [0];
        // A range that starts at the end of the text is one past the last unit.
        var index = starts.IndexOf(start);
        if (index < 0)
            index = starts.Count;

        if (index >= starts.Count && count > 0)
            return (start, end, 0);

        var target = Math.Clamp(index + count, 0, starts.Count - 1);
        var moved = target - index;
        if (moved == 0)
            return (start, end, 0);

        var newEnd = target + 1 < bounds.Count ? bounds[target + 1] : Length;
        return (starts[target], newEnd, moved);
    }

    /// <summary>One end of the range moved by whole units; if it crosses the other end, the other end follows. Says how many units it moved.</summary>
    public (int Start, int End, int Moved) MoveEndpoint(int start, int end, bool moveStart, TextUnitKind unit, int count)
    {
        var bounds = Boundaries(unit);
        var edge = moveStart ? start : end;
        var index = Array.BinarySearch(bounds is int[] array ? array : [.. bounds], edge);
        if (index < 0)
        {
            // Not on a boundary: moving forward lands on the next one, backward on the one before.
            index = count > 0 ? ~index - 1 : ~index;
        }

        var target = Math.Clamp(index + count, 0, bounds.Count - 1);
        var moved = target - index;
        var place = bounds[target];
        if (moveStart)
        {
            start = place;
            if (start > end)
                end = start;
        }
        else
        {
            end = place;
            if (end < start)
                start = end;
        }

        return (start, end, moved);
    }

    /// <summary>Finds a phrase in a stretch of text; the offset of its first character, or -1.</summary>
    public int Find(string phrase, int start, int end, bool backward, bool ignoreCase)
    {
        if (phrase.Length == 0)
            return -1;

        var span = Slice(start, end);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var at = backward ? span.LastIndexOf(phrase, comparison) : span.IndexOf(phrase, comparison);
        return at < 0 ? -1 : start + at;
    }

    private static int AtOrBefore(IReadOnlyList<int> bounds, int position)
    {
        var best = bounds[0];
        foreach (var bound in bounds)
        {
            if (bound > position)
                break;

            best = bound;
        }

        return best;
    }

    private static int AtOrAfter(IReadOnlyList<int> bounds, int position)
    {
        foreach (var bound in bounds)
        {
            if (bound >= position)
                return bound;
        }

        return bounds[^1];
    }

    private static int After(IReadOnlyList<int> bounds, int position)
    {
        foreach (var bound in bounds)
        {
            if (bound > position)
                return bound;
        }

        return bounds[^1];
    }
}

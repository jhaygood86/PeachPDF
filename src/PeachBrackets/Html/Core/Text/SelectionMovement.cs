using PeachBrackets.Selection;
using PeachBrackets.Text;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.TextGeometry
{
    /// <summary>
    /// Where a caret goes when it moves by a character, word, line, block or the whole document: the arithmetic behind
    /// <see cref="DocumentSelection.Modify"/>, over the text runs alone.
    /// </summary>
    internal static class SelectionMovement
    {
        private const double SameLineTolerance = 0.5;

        /// <summary>The position <paramref name="from"/> moves to, or null when it cannot move that way (it is already at the edge).</summary>
        /// <param name="runs">the document's text runs</param>
        /// <param name="from">where the caret is</param>
        /// <param name="direction">backward is towards the start of the document, forward towards its end (down, for a line)</param>
        /// <param name="granularity">how far to move</param>
        /// <param name="goalX">for a line move, the x to keep across lines; null to use the caret's own</param>
        public static TextPosition? Move(IReadOnlyList<TextRun> runs, TextPosition from, SelectionDirection direction, SelectionGranularity granularity, ref double? goalX)
        {
            if (runs.Count == 0)
                return null;

            var forward = direction == SelectionDirection.Forward;
            TextPosition? target = granularity switch
            {
                SelectionGranularity.Character => forward ? NextCharacter(runs, from) : PreviousCharacter(runs, from),
                SelectionGranularity.Word => forward ? NextWord(runs, from) : PreviousWord(runs, from),
                SelectionGranularity.LineBoundary => LineBoundary(runs, from, forward),
                SelectionGranularity.Block => Block(runs, from, forward),
                SelectionGranularity.Document => forward ? new TextPosition(runs.Count - 1, runs[^1].Text.Length) : new TextPosition(0, 0),
                SelectionGranularity.Line => Line(runs, from, forward, ref goalX),
                _ => throw new ArgumentOutOfRangeException(nameof(granularity)),
            };

            if (granularity != SelectionGranularity.Line)
                goalX = null;

            return target is { } moved && moved != from ? moved : null;
        }

        private static TextPosition? NextCharacter(IReadOnlyList<TextRun> runs, TextPosition from)
        {
            var run = runs[from.RunIndex];
            foreach (var boundary in run.CharacterBoundaries)
            {
                if (boundary > from.CharIndex)
                    return new TextPosition(from.RunIndex, boundary);
            }

            // Past the end of a run: into the next one. Within a line (a run split at a link) the end of one and the start of the next are the same
            // place, so the step is one character into it.
            for (var i = from.RunIndex + 1; i < runs.Count; i++)
            {
                var next = runs[i];
                if (next.LineId != run.LineId)
                    return new TextPosition(i, 0);

                foreach (var boundary in next.CharacterBoundaries)
                {
                    if (boundary > 0)
                        return new TextPosition(i, boundary);
                }
            }

            return null;
        }

        private static TextPosition? PreviousCharacter(IReadOnlyList<TextRun> runs, TextPosition from)
        {
            var run = runs[from.RunIndex];
            for (var k = run.CharacterBoundaries.Count - 1; k >= 0; k--)
            {
                if (run.CharacterBoundaries[k] < from.CharIndex)
                    return new TextPosition(from.RunIndex, run.CharacterBoundaries[k]);
            }

            for (var i = from.RunIndex - 1; i >= 0; i--)
            {
                var previous = runs[i];
                if (previous.LineId != run.LineId)
                    return new TextPosition(i, previous.Text.Length);

                for (var k = previous.CharacterBoundaries.Count - 1; k >= 0; k--)
                {
                    if (previous.CharacterBoundaries[k] < previous.Text.Length)
                        return new TextPosition(i, previous.CharacterBoundaries[k]);
                }
            }

            return null;
        }

        // The end of the next word: the first word piece that ends after the caret, spaces skipped.
        private static TextPosition? NextWord(IReadOnlyList<TextRun> runs, TextPosition from)
        {
            for (var i = from.RunIndex; i < runs.Count; i++)
            {
                var text = runs[i].Text;
                var start = i == from.RunIndex ? from.CharIndex : 0;
                var pieces = Segmenter.FindWordBoundaries(text);
                for (var k = 0; k + 1 < pieces.Length; k++)
                {
                    if (pieces[k + 1] > start && !IsSpace(text, pieces[k], pieces[k + 1]))
                        return new TextPosition(i, pieces[k + 1]);
                }
            }

            return new TextPosition(runs.Count - 1, runs[^1].Text.Length);
        }

        // The start of the previous word: the last word piece that starts before the caret, spaces skipped.
        private static TextPosition? PreviousWord(IReadOnlyList<TextRun> runs, TextPosition from)
        {
            for (var i = from.RunIndex; i >= 0; i--)
            {
                var text = runs[i].Text;
                var end = i == from.RunIndex ? from.CharIndex : text.Length;
                var pieces = Segmenter.FindWordBoundaries(text);
                for (var k = pieces.Length - 2; k >= 0; k--)
                {
                    if (pieces[k] < end && !IsSpace(text, pieces[k], pieces[k + 1]))
                        return new TextPosition(i, pieces[k]);
                }
            }

            return new TextPosition(0, 0);
        }

        private static bool IsSpace(string text, int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                if (!char.IsWhiteSpace(text[i]))
                    return false;
            }

            return true;
        }

        private static TextPosition LineBoundary(IReadOnlyList<TextRun> runs, TextPosition from, bool forward)
        {
            var lineId = runs[from.RunIndex].LineId;
            var first = from.RunIndex;
            var last = from.RunIndex;
            while (first > 0 && runs[first - 1].LineId == lineId)
                first--;

            while (last + 1 < runs.Count && runs[last + 1].LineId == lineId)
                last++;

            return forward ? new TextPosition(last, runs[last].Text.Length) : new TextPosition(first, 0);
        }

        // The end (or start) of the block the caret is in; when it is already there, of the next (or previous) one.
        private static TextPosition? Block(IReadOnlyList<TextRun> runs, TextPosition from, bool forward)
        {
            var range = TextRunQueries.BlockAt(runs, from);
            if (range is not { } block)
                return null;

            if (forward)
            {
                if (from != block.End)
                    return block.End;

                var next = block.End.RunIndex + 1;
                return next < runs.Count && TextRunQueries.BlockAt(runs, new TextPosition(next, 0)) is { } following ? following.End : null;
            }

            if (from != block.Start)
                return block.Start;

            var previous = block.Start.RunIndex - 1;
            return previous >= 0 && TextRunQueries.BlockAt(runs, new TextPosition(previous, 0)) is { } preceding ? preceding.Start : null;
        }

        // The caret one line up or down, as near the same x as it started at as the line allows; at the first or last line, to the line's edge.
        private static TextPosition? Line(IReadOnlyList<TextRun> runs, TextPosition from, bool down, ref double? goalX)
        {
            var current = runs[from.RunIndex];
            goalX ??= current.CharacterOffsets[Math.Min(from.CharIndex, current.CharacterOffsets.Count - 1)];

            TextRun? target = null;
            foreach (var run in runs)
            {
                var isAfter = run.PageIndex > current.PageIndex
                    || (run.PageIndex == current.PageIndex && run.Baseline > current.Baseline + SameLineTolerance);
                if (down ? !isAfter : !IsBefore(run, current))
                    continue;

                if (target is null || IsCloser(run, target, down))
                    target = run;
            }

            if (target is null)
                return LineBoundary(runs, from, down);

            var y = target.Baseline - (target.Ascent / 2);
            return TextRunQueries.HitTest(runs, target.PageIndex, goalX.Value, y, caret: true);
        }

        // On an earlier page, or above on this one.
        private static bool IsBefore(TextRun run, TextRun current) =>
            run.PageIndex < current.PageIndex || (run.PageIndex == current.PageIndex && run.Baseline < current.Baseline - SameLineTolerance);

        // Whether a is nearer to the current line than b in the direction of travel: the first line after it (or the last one before it).
        private static bool IsCloser(TextRun a, TextRun b, bool down)
        {
            if (a.PageIndex != b.PageIndex)
                return down ? a.PageIndex < b.PageIndex : a.PageIndex > b.PageIndex;

            return down ? a.Baseline < b.Baseline - SameLineTolerance : a.Baseline > b.Baseline + SameLineTolerance;
        }
    }
}

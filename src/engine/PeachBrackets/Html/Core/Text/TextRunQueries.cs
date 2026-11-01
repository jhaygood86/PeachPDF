using PeachBrackets.Text;
using PeachDrawing.Core;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachBrackets.Html.TextGeometry
{
    /// <summary>
    /// The questions a consumer asks of the text runs of a laid-out document: which character is at a point, what rectangles cover a selection,
    /// what text a selection holds, where a string occurs. They work on the runs alone, so they know nothing of the box tree.
    /// </summary>
    internal static class TextRunQueries
    {
        /// <summary>
        /// The position at a point on a page. With <paramref name="caret"/> false it is the start of the character the point is over; with it
        /// true, the boundary between characters nearest the point, which is where a text cursor goes. A point in a gap or margin is taken to the
        /// nearest line, and beyond a line's end to its nearest character.
        /// </summary>
        public static TextPosition? HitTest(IReadOnlyList<TextRun> runs, int pageIndex, double x, double y, bool caret)
        {
            var best = -1;
            var bestDy = double.MaxValue;
            var bestDx = double.MaxValue;

            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                if (run.PageIndex != pageIndex)
                    continue;

                var top = run.Baseline - run.Ascent;
                var bottom = run.Baseline + run.Descent;
                var dy = y < top ? top - y : y > bottom ? y - bottom : 0;
                var dx = x < run.Bounds.Left ? run.Bounds.Left - x : x > run.Bounds.Right ? x - run.Bounds.Right : 0;

                if (dy < bestDy || (dy == bestDy && dx < bestDx))
                {
                    best = i;
                    bestDy = dy;
                    bestDx = dx;
                }
            }

            if (best < 0)
                return null;

            var found = runs[best];
            var offsets = found.CharacterOffsets;
            var boundaries = found.CharacterBoundaries;
            var chosen = 0;
            var chosenDistance = double.MaxValue;

            if (caret)
            {
                foreach (var boundary in boundaries)
                {
                    var distance = Math.Abs(offsets[boundary] - x);
                    if (distance < chosenDistance)
                    {
                        chosen = boundary;
                        chosenDistance = distance;
                    }
                }
            }
            else
            {
                for (var k = 0; k + 1 < boundaries.Count; k++)
                {
                    var a = offsets[boundaries[k]];
                    var b = offsets[boundaries[k + 1]];
                    var lo = Math.Min(a, b);
                    var hi = Math.Max(a, b);

                    // Half open, so a point on the edge between two characters is over the one that starts there.
                    var distance = x < lo ? lo - x : x >= hi ? x - hi + 1e-9 : 0;
                    if (distance < chosenDistance)
                    {
                        chosen = boundaries[k];
                        chosenDistance = distance;
                    }
                }
            }

            return new TextPosition(best, chosen);
        }

        /// <summary>The rectangles that cover the text from <paramref name="from"/> to <paramref name="to"/>, one per line, in points.</summary>
        public static IReadOnlyList<Rect> SelectionRects(IReadOnlyList<TextRun> runs, TextPosition from, TextPosition to)
        {
            var rects = new List<Rect>();
            foreach (var (_, rect) in SelectionRegions(runs, from, to))
                rects.Add(rect);

            return rects;
        }

        /// <summary><see cref="SelectionRects"/> with the page each rectangle is on.</summary>
        public static List<(int PageIndex, Rect Rect)> SelectionRegions(IReadOnlyList<TextRun> runs, TextPosition from, TextPosition to)
        {
            var rects = new List<(int, Rect)>();
            if (!Normalize(runs, ref from, ref to))
                return rects;

            var lineId = -1;
            var page = 0;
            var left = 0d;
            var right = 0d;
            var top = 0d;
            var bottom = 0d;

            void Flush()
            {
                if (lineId >= 0)
                    rects.Add((page, new Rect(left, top, right - left, bottom - top)));
            }

            for (var i = from.RunIndex; i <= to.RunIndex; i++)
            {
                var run = runs[i];
                var start = i == from.RunIndex ? from.CharIndex : 0;
                var end = i == to.RunIndex ? to.CharIndex : run.Text.Length;
                if (end <= start)
                    continue;

                var a = run.CharacterOffsets[start];
                var b = run.CharacterOffsets[end];
                var runLeft = Math.Min(a, b);
                var runRight = Math.Max(a, b);
                var runTop = run.Baseline - run.Ascent;
                var runBottom = run.Baseline + run.Descent;

                // The pieces a line was split into (at a link, say) are one rectangle.
                if (lineId == run.LineId && i > 0 && runs[i - 1].PageIndex == run.PageIndex)
                {
                    left = Math.Min(left, runLeft);
                    right = Math.Max(right, runRight);
                    top = Math.Min(top, runTop);
                    bottom = Math.Max(bottom, runBottom);
                    continue;
                }

                Flush();
                lineId = run.LineId;
                page = run.PageIndex;
                left = runLeft;
                right = runRight;
                top = runTop;
                bottom = runBottom;
            }

            Flush();
            return rects;
        }

        /// <summary>The plain text from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static string GetText(IReadOnlyList<TextRun> runs, TextPosition from, TextPosition to, Func<TextWordSlice, bool>? omit = null)
        {
            if (!Normalize(runs, ref from, ref to))
                return string.Empty;

            var text = new StringBuilder();
            var previous = (TextRun?)null;
            for (var i = from.RunIndex; i <= to.RunIndex; i++)
            {
                var run = runs[i];
                var start = i == from.RunIndex ? from.CharIndex : 0;
                var end = i == to.RunIndex ? to.CharIndex : run.Text.Length;
                if (end <= start)
                    continue;

                var piece = omit is null ? run.Text.AsSpan(start, end - start).ToString() : WithoutOmitted(run, start, end, omit);
                if (piece.Length == 0)
                    continue;

                if (previous is not null)
                    text.Append(Separator(previous, run, piece[0]));

                text.Append(piece);
                previous = run;
            }

            return text.ToString();
        }

        // The text of run[start..end] without the words the caller leaves out (text the reader may not select).
        private static string WithoutOmitted(TextRun run, int start, int end, Func<TextWordSlice, bool> omit)
        {
            var kept = new StringBuilder();
            for (var i = start; i < end; i++)
            {
                var omitted = false;
                foreach (var slice in run.Slices)
                {
                    if (i >= slice.TextStart && i < slice.TextStart + slice.Length && omit(slice))
                    {
                        omitted = true;
                        break;
                    }
                }

                if (!omitted)
                    kept.Append(run.Text[i]);
            }

            return kept.ToString();
        }

        /// <summary>Every place <paramref name="query"/> occurs in the text of the document, in order.</summary>
        public static IReadOnlyList<TextRange> Find(IReadOnlyList<TextRun> runs, string query, bool ignoreCase)
        {
            var found = new List<TextRange>();
            if (query.Length == 0 || runs.Count == 0)
                return found;

            // The whole text as GetText gives it, with where each run's text begins in it.
            var text = new StringBuilder();
            var starts = new List<int>(runs.Count);
            TextRun? previous = null;
            for (var i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                if (previous is not null && run.Text.Length > 0)
                    text.Append(Separator(previous, run, run.Text[0]));

                starts.Add(text.Length);
                text.Append(run.Text);
                if (run.Text.Length > 0)
                    previous = run;
            }

            var whole = text.ToString();
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var at = 0;
            while ((at = whole.IndexOf(query, at, comparison)) >= 0)
            {
                var first = PositionAt(runs, starts, at, atEnd: false);
                var last = PositionAt(runs, starts, at + query.Length, atEnd: true);
                if (first is { } start && last is { } end)
                    found.Add(new TextRange(start, end));

                at += query.Length;
            }

            return found;
        }

        /// <summary>The word at a position, as a double click selects it.</summary>
        public static TextRange? WordAt(IReadOnlyList<TextRun> runs, TextPosition position)
        {
            if ((uint)position.RunIndex >= (uint)runs.Count)
                return null;

            var run = runs[position.RunIndex];
            if (run.Text.Length == 0)
                return null;

            var boundaries = Segmenter.FindWordBoundaries(run.Text);
            var at = Math.Min(position.CharIndex, run.Text.Length - 1);
            for (var k = 0; k + 1 < boundaries.Length; k++)
            {
                if (at >= boundaries[k] && at < boundaries[k + 1])
                {
                    return new TextRange(new TextPosition(position.RunIndex, boundaries[k]), new TextPosition(position.RunIndex, boundaries[k + 1]));
                }
            }

            return null;
        }

        /// <summary>The whole block at a position - every run of it - as a triple click selects it.</summary>
        public static TextRange? BlockAt(IReadOnlyList<TextRun> runs, TextPosition position)
        {
            if ((uint)position.RunIndex >= (uint)runs.Count)
                return null;

            var block = runs[position.RunIndex].BlockId;
            var first = position.RunIndex;
            var last = position.RunIndex;
            while (first > 0 && runs[first - 1].BlockId == block)
                first--;

            while (last + 1 < runs.Count && runs[last + 1].BlockId == block)
                last++;

            return new TextRange(new TextPosition(first, 0), new TextPosition(last, runs[last].Text.Length));
        }

        /// <summary>What copied text puts between two runs: nothing within a line, a space where a line wrapped, a line break between blocks and at a forced one.</summary>
        private static string Separator(TextRun previous, TextRun next, char nextFirst)
        {
            if (previous.BlockId != next.BlockId)
                return "\n";

            if (previous.LineId == next.LineId)
                return string.Empty;

            if (next.FollowsForcedBreak)
                return "\n";

            return char.IsWhiteSpace(previous.Text[^1]) || char.IsWhiteSpace(nextFirst) ? string.Empty : " ";
        }

        private static TextPosition? PositionAt(IReadOnlyList<TextRun> runs, List<int> starts, int index, bool atEnd)
        {
            // The last run that begins at or before the index (for an end, before it: the end of a match is the end of its last character).
            var target = atEnd ? index - 1 : index;
            var run = -1;
            for (var i = 0; i < starts.Count; i++)
            {
                if (starts[i] <= target && runs[i].Text.Length > 0)
                    run = i;
                else if (starts[i] > target)
                    break;
            }

            if (run < 0)
                return null;

            var within = target - starts[run];
            if (within >= runs[run].Text.Length)
            {
                // The index fell in the separator after the run.
                return atEnd ? new TextPosition(run, runs[run].Text.Length) : null;
            }

            return new TextPosition(run, atEnd ? within + 1 : within);
        }

        private static bool Normalize(IReadOnlyList<TextRun> runs, ref TextPosition from, ref TextPosition to)
        {
            if (from.CompareTo(to) > 0)
                (from, to) = (to, from);

            if (runs.Count == 0 || from.RunIndex < 0 || to.RunIndex >= runs.Count || from.CharIndex < 0)
                return false;

            return to.CharIndex <= runs[to.RunIndex].Text.Length;
        }
    }
}

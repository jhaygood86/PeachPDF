using PeachBrackets.Html.Dom;
using PeachBrackets.Text;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.TextGeometry
{
    /// <summary>
    /// The selected ranges of a document, resolved to the words the painter draws: for each selected word, which of its characters are
    /// selected and where they sit, so the painter needs nothing but the word's rectangle to highlight them.
    /// </summary>
    internal sealed class SelectionModel
    {
        /// <summary>The selected part of one word.</summary>
        /// <param name="Start">the first selected UTF-16 index of the word's text</param>
        /// <param name="End">the index after the last selected one (equal to <paramref name="Start"/> when only the space before the word is selected)</param>
        /// <param name="Lo">the left edge of the selected characters, in points from the word's left edge</param>
        /// <param name="Hi">their right edge</param>
        /// <param name="GapSelected">whether the separator space before the word is selected too</param>
        /// <param name="GapLo">the left edge of that space, in points from the word's left edge</param>
        /// <param name="GapHi">its right edge</param>
        internal readonly record struct Part(int Start, int End, double Lo, double Hi, bool GapSelected, double GapLo, double GapHi)
        {
            public bool HasCharacters => End > Start;
        }

        private readonly Dictionary<CssRect, Part> _parts = new(ReferenceEqualityComparer.Instance);

        public bool TryGet(CssRect word, out Part part) => _parts.TryGetValue(word, out part);

        public bool IsEmpty => _parts.Count == 0;

        /// <summary>Resolves <paramref name="ranges"/> of <paramref name="runs"/> to the words they cover.</summary>
        public static SelectionModel Build(IReadOnlyList<TextRun> runs, IEnumerable<TextRange> ranges)
        {
            var model = new SelectionModel();
            foreach (var range in ranges)
            {
                var from = range.Start;
                var to = range.End;
                if (from.CompareTo(to) > 0)
                    (from, to) = (to, from);

                if (from.RunIndex < 0 || to.RunIndex >= runs.Count)
                    continue;

                for (var i = from.RunIndex; i <= to.RunIndex; i++)
                {
                    var run = runs[i];
                    var selectionStart = i == from.RunIndex ? from.CharIndex : 0;
                    var selectionEnd = i == to.RunIndex ? to.CharIndex : run.Text.Length;
                    if (selectionEnd < selectionStart)
                        continue;

                    foreach (var slice in run.Slices)
                        model.Add(slice, selectionStart, selectionEnd);
                }
            }

            return model;
        }

        private void Add(TextWordSlice slice, int selectionStart, int selectionEnd)
        {
            var first = Math.Max(selectionStart, slice.TextStart);
            var last = Math.Min(selectionEnd, slice.TextStart + slice.Length);
            var gapSelected = slice.HasGap && selectionStart <= slice.TextStart - 1 && slice.TextStart - 1 < selectionEnd;
            var hasCharacters = last > first;
            if (!hasCharacters && !gapSelected)
                return;

            var start = 0;
            var end = 0;
            double lo = 0, hi = 0;
            if (hasCharacters)
            {
                start = first - slice.TextStart;
                end = last - slice.TextStart;
                var a = slice.Edges[start];
                var b = slice.Edges[end];
                lo = Math.Min(a, b);
                hi = Math.Max(a, b);
            }

            var part = new Part(start, end, lo, hi, gapSelected, slice.GapStart, slice.GapEnd);
            if (_parts.TryGetValue(slice.Word, out var existing))
                part = Merge(existing, part);

            _parts[slice.Word] = part;
        }

        // Two ranges that reach the same word (a selection drawn as several ranges): the union of what they cover.
        private static Part Merge(Part a, Part b)
        {
            if (!a.HasCharacters) return b with { GapSelected = a.GapSelected || b.GapSelected };
            if (!b.HasCharacters) return a with { GapSelected = a.GapSelected || b.GapSelected };

            return new Part(
                Math.Min(a.Start, b.Start), Math.Max(a.End, b.End), Math.Min(a.Lo, b.Lo), Math.Max(a.Hi, b.Hi),
                a.GapSelected || b.GapSelected, a.GapLo, a.GapHi);
        }
    }
}

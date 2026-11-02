using System;
using System.Collections.Generic;
using System.Globalization;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Paint.Content
{
    /// <summary>One line of a text area's text: the stretch <c>[Start, End)</c> of its value shown on it. A hard line break ends the line without being part of it.</summary>
    internal readonly record struct TextAreaLine(int Start, int End);

    /// <summary>
    /// How a text area's value is broken into lines for its width, and the arithmetic between a position in the value and a place on those lines. The
    /// screen look, the PDF appearance and the editing model all use it, so a caret, a click and a drawn line agree. Lines break at a line break in the
    /// value and otherwise after a space (<c>white-space: pre-wrap</c>), or inside a word too long for a line.
    /// </summary>
    internal static class TextAreaLayout
    {
        /// <summary>Breaks a value into lines no wider than the given width.</summary>
        public static IReadOnlyList<TextAreaLine> Wrap(Canvas g, Font font, string text, double width)
        {
            var lines = new List<TextAreaLine>();
            var paragraphStart = 0;
            while (true)
            {
                var paragraphEnd = text.IndexOf('\n', paragraphStart);
                var end = paragraphEnd < 0 ? text.Length : paragraphEnd;
                WrapParagraph(g, font, text, paragraphStart, end, width, lines);
                if (paragraphEnd < 0)
                    break;

                paragraphStart = paragraphEnd + 1;
            }

            return lines;
        }

        private static void WrapParagraph(Canvas g, Font font, string text, int start, int end, double width, List<TextAreaLine> lines)
        {
            if (start == end)
            {
                lines.Add(new TextAreaLine(start, start));
                return;
            }

            var lineStart = start;
            var index = start;
            while (index < end)
            {
                // A word and the spaces after it.
                var wordEnd = index;
                while (wordEnd < end && text[wordEnd] != ' ')
                    wordEnd++;
                var tokenEnd = wordEnd;
                while (tokenEnd < end && text[tokenEnd] == ' ')
                    tokenEnd++;

                if (Width(g, font, text, lineStart, wordEnd) <= width || index == lineStart && wordEnd == index)
                {
                    index = tokenEnd;
                    continue;
                }

                if (index > lineStart)
                {
                    // The word does not fit after what is on the line: it starts the next one.
                    lines.Add(new TextAreaLine(lineStart, index));
                    lineStart = index;
                    continue;
                }

                // A word wider than the line: break it where it stops fitting, keeping at least one cluster.
                var cut = index;
                foreach (var cluster in StringInfo.ParseCombiningCharacters(text.Substring(index, wordEnd - index)))
                {
                    var next = index + cluster;
                    if (next > cut + 0 && Width(g, font, text, lineStart, next) > width && cut > lineStart)
                        break;

                    cut = next;
                }

                if (cut <= lineStart)
                    cut = Math.Min(lineStart + 1, end);

                lines.Add(new TextAreaLine(lineStart, cut));
                lineStart = index = cut;
            }

            lines.Add(new TextAreaLine(lineStart, end));
        }

        private static double Width(Canvas g, Font font, string text, int start, int end) =>
            end <= start ? 0 : g.MeasureString(text.Substring(start, end - start).TrimEnd(' '), font).Width;

        /// <summary>The line a position is on; a position at the break between two soft-wrapped lines belongs to the later one.</summary>
        public static int LineOf(IReadOnlyList<TextAreaLine> lines, int index)
        {
            for (var i = lines.Count - 1; i > 0; i--)
            {
                if (lines[i].Start <= index)
                    return i;
            }

            return 0;
        }

        /// <summary>How far from the left of the content a position is, on its line.</summary>
        public static double OffsetOf(Canvas g, Font font, string text, TextAreaLine line, int index)
        {
            var upTo = Math.Clamp(index, line.Start, line.End);
            return upTo <= line.Start ? 0 : g.MeasureString(text.Substring(line.Start, upTo - line.Start), font).Width;
        }

        /// <summary>The position nearest to a point, given in the content's own space (from its top left).</summary>
        public static int IndexAt(Canvas g, Font font, string text, IReadOnlyList<TextAreaLine> lines, double x, double y)
        {
            if (lines.Count == 0)
                return 0;

            var lineNumber = Math.Clamp((int)Math.Floor(y / Math.Max(font.Height, 1)), 0, lines.Count - 1);
            var line = lines[lineNumber];
            var best = line.Start;
            var bestDistance = double.MaxValue;
            var boundaries = new List<int>(StringInfo.ParseCombiningCharacters(text.Substring(line.Start, line.End - line.Start)));
            for (var i = 0; i < boundaries.Count; i++)
                boundaries[i] += line.Start;
            boundaries.Add(line.End);

            foreach (var index in boundaries)
            {
                var distance = Math.Abs(OffsetOf(g, font, text, line, index) - x);
                if (distance < bestDistance)
                {
                    best = index;
                    bestDistance = distance;
                }
            }

            // At the end of a soft-wrapped line, the caret belongs after the space the line broke on, not before the next line's first character.
            return best;
        }
    }
}

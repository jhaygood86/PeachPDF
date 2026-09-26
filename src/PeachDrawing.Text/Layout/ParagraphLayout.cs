using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace PeachDrawing.Text.Layout
{
    /// <summary>
    /// A stretch of one line that is drawn as one: text of one style, direction and script, shaped in one face.
    /// </summary>
    /// <remarks>
    /// The glyphs of <see cref="Glyphs"/> are in the order they are drawn, left to right, and each starts where the previous one's
    /// advance ends. The face of a glyph's advance and the layout size give a distance in layout units as
    /// <c>(Typeface.GetAdvance(glyph) + glyph.XAdvanceDelta) * Size / Typeface.Metrics.UnitsPerEm</c>, and the offsets of a glyph scale the same way.
    /// </remarks>
    public sealed class PlacedRun
    {
        private readonly double[] _boundaryX;

        internal PlacedRun(TextRange range, RunStyle style, GlyphRun glyphs, byte level, double x, double baseline, double width, double[] boundaryX)
        {
            Range = range;
            Style = style;
            Glyphs = glyphs;
            Level = level;
            X = x;
            Baseline = baseline;
            Width = width;
            _boundaryX = boundaryX;
        }

        /// <summary>The text this run stands for.</summary>
        public TextRange Range { get; }

        /// <summary>The face and size the run is set in.</summary>
        public RunStyle Style { get; }

        /// <summary>The shaped glyphs, in the order they are drawn.</summary>
        public GlyphRun Glyphs { get; }

        /// <summary>The bidirectional embedding level of the run; odd for right to left.</summary>
        public byte Level { get; }

        /// <summary>Whether the run's text reads right to left.</summary>
        public bool IsRightToLeft => (Level & 1) == 1;

        /// <summary>The distance from the left edge of the layout to the left edge of the run, in layout units.</summary>
        public double X { get; }

        /// <summary>The distance from the top of the layout to the baseline the run sits on, in layout units.</summary>
        public double Baseline { get; }

        /// <summary>How wide the run is, in layout units.</summary>
        public double Width { get; }

        /// <summary>
        /// Where a caret at a boundary of the run's text is, measured from the left edge of the layout.
        /// </summary>
        /// <param name="index">A UTF-16 offset in the text of the paragraph, from <see cref="Range"/>'s start to its end.</param>
        /// <returns>The distance, in layout units.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the run.</exception>
        public double GetCaretX(int index)
        {
            if (index < Range.Start || index > Range.End)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "The offset is outside the run.");
            }

            return X + _boundaryX[index - Range.Start];
        }
    }

    /// <summary>One line of a laid-out paragraph.</summary>
    public sealed class LineBox
    {
        internal LineBox(TextRange range, int contentEnd, IReadOnlyList<PlacedRun> runs, double left, double top, double width, double ascent, double descent, double height, LineEnd end)
        {
            Range = range;
            ContentEnd = contentEnd;
            Runs = runs;
            Left = left;
            Top = top;
            Width = width;
            Ascent = ascent;
            Descent = descent;
            Height = height;
            End = end;
        }

        /// <summary>The text of the line, including the spaces that hang at its end and the character that forces a break.</summary>
        public TextRange Range { get; }

        /// <summary>The offset where the line's drawn text ends; what follows up to the end of <see cref="Range"/> hangs.</summary>
        public int ContentEnd { get; }

        /// <summary>The runs of the line, in the order they are drawn from left to right.</summary>
        public IReadOnlyList<PlacedRun> Runs { get; }

        /// <summary>The distance from the left edge of the layout to the left edge of the line's text, after alignment.</summary>
        public double Left { get; }

        /// <summary>The distance from the top of the layout to the top of the line box.</summary>
        public double Top { get; }

        /// <summary>The width of the line's drawn text, not counting hanging spaces.</summary>
        public double Width { get; }

        /// <summary>The distance from the top of the line box to its baseline.</summary>
        public double Ascent { get; }

        /// <summary>The distance from the baseline to the bottom of the line box.</summary>
        public double Descent { get; }

        /// <summary>The height of the line box.</summary>
        public double Height { get; }

        /// <summary>The distance from the top of the layout to the baseline.</summary>
        public double Baseline => Top + Ascent;

        /// <summary>Why the line ends where it does.</summary>
        public LineEnd End { get; }

        /// <summary>The rectangle the line's text is in.</summary>
        public RectangleF Bounds => new((float)Left, (float)Top, (float)Width, (float)Height);
    }

    /// <summary>
    /// A paragraph laid out at one width: an immutable snapshot of the lines, with the queries an editor or a hit test needs.
    /// </summary>
    public sealed class ParagraphLayout
    {
        private readonly Paragraph _paragraph;
        private readonly LineBox[] _lines;

        internal ParagraphLayout(Paragraph paragraph, LineBox[] lines, double width, double contentWidth, double height)
        {
            _paragraph = paragraph;
            _lines = lines;
            Width = width;
            ContentWidth = contentWidth;
            Height = height;
        }

        /// <summary>The lines from top to bottom.</summary>
        public IReadOnlyList<LineBox> Lines => _lines;

        /// <summary>The width the paragraph was laid out at, or the width of the widest line when it was laid out without a limit.</summary>
        public double Width { get; }

        /// <summary>The width of the widest line.</summary>
        public double ContentWidth { get; }

        /// <summary>The height of all the lines together.</summary>
        public double Height { get; }

        /// <summary>The paragraph this is a layout of.</summary>
        public Paragraph Paragraph => _paragraph;

        /// <summary>
        /// Finds the text position nearest a point.
        /// </summary>
        /// <param name="point">A point in the layout's coordinates; one outside the text gives the nearest edge.</param>
        /// <exception cref="ArgumentOutOfRangeException">A coordinate of the point is not a number.</exception>
        /// <returns>The position, at a boundary between two characters that a reader sees as one unit.</returns>
        public TextPosition PositionAt(PointF point)
        {
            if (_lines.Length == 0)
            {
                return new TextPosition(0);
            }

            if (double.IsNaN(point.X) || double.IsNaN(point.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(point), point, "The point is not a number.");
            }

            var line = _lines[LineIndexAtY(point.Y)];
            if (line.Runs.Count == 0)
            {
                return new TextPosition(line.Range.Start);
            }

            TextPosition At(int index) => new(index, index == line.Range.End && line.End is LineEnd.Soft or LineEnd.Emergency ? TextAffinity.Upstream : TextAffinity.Downstream);

            // Left or right of everything: the boundary at that visual edge.
            var first = line.Runs[0];
            if (point.X <= first.X)
            {
                return At(first.IsRightToLeft ? first.Range.End : first.Range.Start);
            }

            var last = line.Runs[^1];
            if (point.X >= last.X + last.Width)
            {
                return At(last.IsRightToLeft ? last.Range.Start : last.Range.End);
            }

            foreach (var run in line.Runs)
            {
                if (point.X > run.X + run.Width)
                {
                    continue;
                }

                int best = run.Range.Start;
                double bestDistance = double.MaxValue;
                for (int offset = run.Range.Start; offset <= run.Range.End; offset++)
                {
                    if (!_paragraph.IsGraphemeBoundary(offset))
                    {
                        continue;
                    }

                    double distance = Math.Abs(run.GetCaretX(offset) - point.X);
                    if (distance < bestDistance)
                    {
                        best = offset;
                        bestDistance = distance;
                    }
                }

                return At(best);
            }

            return At(line.ContentEnd);
        }

        private int LineIndexAtY(double y)
        {
            for (int i = 0; i < _lines.Length; i++)
            {
                if (y < _lines[i].Top + _lines[i].Height)
                {
                    return i;
                }
            }

            return _lines.Length - 1;
        }

        /// <summary>
        /// Finds where a caret at a position is drawn.
        /// </summary>
        /// <param name="position">A position in the text.</param>
        /// <returns>A rectangle of no width the height of the line, at the caret's place.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The position is outside the text.</exception>
        public RectangleF CaretRect(TextPosition position)
        {
            if (position.Index < 0 || position.Index > _paragraph.Text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, "The position is outside the text.");
            }

            if (_lines.Length == 0)
            {
                return RectangleF.Empty;
            }

            var line = LineOf(position);
            double x = line.Left;
            var run = RunAt(line, position);
            if (run is not null)
            {
                x = run.GetCaretX(Math.Clamp(position.Index, run.Range.Start, run.Range.End));
            }
            else if (line.Runs.Count > 0)
            {
                // A position in hanging spaces: at the end of what is drawn.
                x = _paragraph.IsRightToLeft ? line.Runs[0].X : line.Runs[^1].X + line.Runs[^1].Width;
            }

            return new RectangleF((float)x, (float)line.Top, 0, (float)line.Height);
        }

        private LineBox LineOf(TextPosition position)
        {
            int index = position.Index;
            for (int i = 0; i < _lines.Length; i++)
            {
                var range = _lines[i].Range;
                bool isLast = i == _lines.Length - 1;
                if (index > range.Start && index < range.End)
                {
                    return _lines[i];
                }

                if (index == range.Start && !(position.Affinity == TextAffinity.Upstream && i > 0 && _lines[i - 1].End is LineEnd.Soft or LineEnd.Emergency))
                {
                    return _lines[i];
                }

                // The end of a line is the start of the next one, except where a soft break lets the position choose.
                if (index == range.End && (isLast || (position.Affinity == TextAffinity.Upstream && _lines[i].End is LineEnd.Soft or LineEnd.Emergency)))
                {
                    return _lines[i];
                }
            }

            return _lines[^1];
        }

        private static PlacedRun? RunAt(LineBox line, TextPosition position)
        {
            PlacedRun? boundary = null;
            foreach (var run in line.Runs)
            {
                if (position.Index > run.Range.Start && position.Index < run.Range.End)
                {
                    return run;
                }

                if (position.Index == run.Range.Start || position.Index == run.Range.End)
                {
                    // Where two runs meet, the one the caret enters when moving on in the paragraph's direction.
                    if (boundary is null || position.Index == run.Range.Start)
                    {
                        boundary = run;
                    }
                }
            }

            return boundary;
        }

        /// <summary>
        /// Finds the rectangles that cover a stretch of text, as a selection would be drawn.
        /// </summary>
        /// <param name="range">The text; a range whose end is before its start is taken the other way round. Hanging spaces and line-ending characters are not drawn, and so have no box.</param>
        /// <returns>One rectangle for each stretch of a line that is selected and drawn without a gap; a range that spans right-to-left text can give several on one line.</returns>
        public IReadOnlyList<RectangleF> SelectionBoxes(TextRange range)
        {
            var boxes = new List<RectangleF>();
            if (range.Start > range.End)
            {
                range = new TextRange(range.End, range.Start);
            }

            foreach (var line in _lines)
            {
                if (range.End <= line.Range.Start || range.Start >= line.Range.End)
                {
                    continue;
                }

                float? left = null;
                float right = 0;
                foreach (var run in line.Runs)
                {
                    int start = Math.Max(range.Start, run.Range.Start);
                    int end = Math.Min(range.End, run.Range.End);
                    if (end <= start)
                    {
                        if (left is not null)
                        {
                            boxes.Add(new RectangleF(left.Value, (float)line.Top, right - left.Value, (float)line.Height));
                            left = null;
                        }

                        continue;
                    }

                    float a = (float)run.GetCaretX(start);
                    float b = (float)run.GetCaretX(end);
                    float from = Math.Min(a, b);
                    float to = Math.Max(a, b);
                    if (left is null)
                    {
                        left = from;
                        right = to;
                    }
                    else
                    {
                        right = Math.Max(right, to);
                    }
                }

                if (left is not null)
                {
                    boxes.Add(new RectangleF(left.Value, (float)line.Top, right - left.Value, (float)line.Height));
                }
            }

            return boxes;
        }

        /// <summary>The word the character at <paramref name="index"/> is in, as a reader's double-click would select it (UAX #29).</summary>
        /// <param name="index">A UTF-16 offset in the text.</param>
        /// <returns>The range of the word, or of the space or punctuation between words.</returns>
        public TextRange WordRangeAt(int index) => RangeAt(index, Segmenter.FindWordBoundaries(_paragraph.Text));

        /// <summary>The user-perceived character the character at <paramref name="index"/> is part of (UAX #29).</summary>
        /// <param name="index">A UTF-16 offset in the text.</param>
        /// <returns>The range of the grapheme cluster.</returns>
        public TextRange GraphemeRangeAt(int index) => RangeAt(index, Segmenter.FindGraphemeBoundaries(_paragraph.Text));

        private TextRange RangeAt(int index, int[] boundaries)
        {
            int length = _paragraph.Text.Length;
            if (index < 0 || index > length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "The offset is outside the text.");
            }

            if (length == 0)
            {
                return new TextRange(0, 0);
            }

            index = Math.Min(index, length - 1);
            int start = 0;
            int end = length;
            foreach (var boundary in boundaries)
            {
                if (boundary <= index)
                {
                    start = boundary;
                }
                else
                {
                    end = boundary;
                    break;
                }
            }

            return new TextRange(start, end);
        }
    }
}

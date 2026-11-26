using PeachDrawing.Text;
using PeachDrawing.Text.Layout;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachDrawing.Core
{
    /// <summary>One run of a laid-out line: text that reads one way, in one font, and where it is.</summary>
    /// <param name="Start">the index of the run's first UTF-16 code unit in the text</param>
    /// <param name="End">the index one past its last</param>
    /// <param name="X">where the run starts, from the left edge of the line</param>
    /// <param name="Width">how wide the run is</param>
    /// <param name="IsRightToLeft">whether the run reads right to left</param>
    public readonly record struct TextRunSegment(int Start, int End, double X, double Width, bool IsRightToLeft);

    /// <summary>
    /// A line of text laid out for hit testing and selection: the bidirectional order of its runs, the font each character is drawn in (a character the font lacks in another that has it),
    /// where each boundary between characters falls, and the box the ink of any stretch of it covers. A canvas's <c>measureText</c> answers its range, caret and selection questions
    /// from this.
    /// </summary>
    public sealed class TextRunLayout
    {
        private readonly ParagraphLayout _layout;
        private readonly LineBox? _line;
        private readonly int[] _graphemes;

        private TextRunLayout(string text, ParagraphLayout layout, double fontAscent, double fontDescent)
        {
            Text = text;
            _layout = layout;
            _line = layout.Lines.Count > 0 ? layout.Lines[0] : null;
            _graphemes = Segmenter.FindGraphemeBoundaries(text.AsSpan());
            FontAscent = fontAscent;
            FontDescent = fontDescent;
            Width = _line?.Width ?? 0;
        }

        /// <summary>The text.</summary>
        public string Text { get; }

        /// <summary>The distance the line takes up along its length.</summary>
        public double Width { get; }

        /// <summary>How far the first font rises above the baseline, as its own metrics say.</summary>
        public double FontAscent { get; }

        /// <summary>How far the first font falls below the baseline.</summary>
        public double FontDescent { get; }

        /// <summary>
        /// Lays <paramref name="text"/> out as one line.
        /// </summary>
        /// <param name="font">the font</param>
        /// <param name="text">the text</param>
        /// <param name="rightToLeft">whether the line's base direction is right to left</param>
        /// <param name="letterSpacing">added after every character</param>
        /// <param name="wordSpacing">added after every space, besides the letter spacing</param>
        /// <param name="features">how the text is shaped, or <see langword="null"/> for the defaults</param>
        /// <param name="fallback">gives the font to draw a character with that <paramref name="font"/> lacks, or <see langword="null"/> (or a function that returns <see langword="null"/>) for none</param>
        /// <exception cref="ArgumentNullException"><paramref name="font"/> or <paramref name="text"/> is <see langword="null"/></exception>
        public static TextRunLayout Create(Font font, string text, bool rightToLeft = false, double letterSpacing = 0, double wordSpacing = 0, ShapeSettings? features = null, Func<Rune, Font?>? fallback = null)
        {
            ArgumentNullException.ThrowIfNull(font);
            ArgumentNullException.ThrowIfNull(text);

            var typeface = font.Typeface ?? throw new InvalidOperationException("The font has no typeface to lay text out with.");
            var scale = font.Size / typeface.Metrics.UnitsPerEm;
            Func<Rune, Typeface>? typefaceFallback = fallback is null ? null : rune => fallback(rune)?.Typeface!;
            var style = new RunStyle(typeface, font.Size, features ?? ShapeSettings.Default, typefaceFallback, letterSpacing, wordSpacing);

            var builder = new ParagraphBuilder(style);
            builder.SetStyle(new ParagraphStyle { Direction = rightToLeft ? BaseDirection.Rtl : BaseDirection.Ltr, NoWrap = true });
            builder.AddText(text);
            var layout = builder.Build().Layout(1e9);
            return new TextRunLayout(text, layout, typeface.Metrics.NormalLineAscent * scale, typeface.Metrics.NormalLineDescent * scale);
        }

        /// <summary>The runs of the line, left to right.</summary>
        public IReadOnlyList<TextRunSegment> Runs =>
            _line is null ? [] : [.. _line.Runs.Where(r => !r.IsGenerated && r.Range.Length > 0).Select(r => new TextRunSegment(r.Range.Start, r.Range.End, r.X - _line.Left, r.Width, r.IsRightToLeft))];

        /// <summary>The boundaries between characters (as a person sees them: a letter with its marks, an emoji with its modifiers), from 0 to the text's length.</summary>
        public IReadOnlyList<int> Boundaries => _graphemes;

        /// <summary>The area of the line that stretches of text from <paramref name="start"/> to <paramref name="end"/> take, as the boxes a selection would be drawn with: one for each stretch without a gap, left to right.</summary>
        /// <param name="start">the index of the first UTF-16 code unit</param>
        /// <param name="end">the index one past the last</param>
        public IReadOnlyList<(double X, double Width)> SelectionBoxes(int start, int end)
        {
            if (_line is null)
                return [];

            return [.. _layout.SelectionBoxes(new TextRange(Math.Clamp(start, 0, Text.Length), Math.Clamp(end, 0, Text.Length))).Select(b => ((double)b.X - _line.Left, (double)b.Width))];
        }

        /// <summary>Where along the line a caret between two characters is.</summary>
        /// <param name="index">the index of a UTF-16 code unit boundary</param>
        public double CaretX(int index)
        {
            if (_line is null)
                return 0;

            return (double)_layout.CaretRect(new TextPosition(Math.Clamp(index, 0, Text.Length))).X - _line.Left;
        }

        /// <summary>The boundary between characters nearest to a distance along the line (a boundary half way between two goes to the one before).</summary>
        /// <param name="x">the distance from the line's left edge</param>
        /// <returns>an index into the text</returns>
        public int IndexFromOffset(double x)
        {
            if (_line is null)
                return 0;

            // The layout finds the character boundary nearest to a point, in its own reading of the line's directions; a point half way between two goes to the one to its left.
            var position = _layout.PositionAt(new System.Drawing.PointF((float)(x + _line.Left - 1e-4), (float)(_line.Top + _line.Height / 2)));
            return Math.Clamp(position.Index, 0, Text.Length);
        }

        /// <summary>
        /// The box the ink of the characters in a range covers: from the line's left edge, and from the baseline with up positive. Characters with no ink (spaces) are not in it.
        /// </summary>
        /// <param name="start">the index of the first UTF-16 code unit</param>
        /// <param name="end">the index one past the last</param>
        /// <param name="left">the box's left edge</param>
        /// <param name="right">the box's right edge</param>
        /// <param name="top">how far the box rises above the baseline</param>
        /// <param name="bottom">how far the box falls below it (negative when it is all above)</param>
        /// <returns>false when no character of the range has ink</returns>
        public bool TryGetInk(int start, int end, out double left, out double right, out double top, out double bottom)
        {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, minY = double.PositiveInfinity;
            left = right = top = bottom = 0;
            if (_line is null)
                return false;

            foreach (var run in _line.Runs)
            {
                var typeface = run.Style.Typeface;
                var scale = run.Style.Size / typeface.Metrics.UnitsPerEm;
                double pen = 0;
                for (var i = 0; i < run.Glyphs.Glyphs.Count; i++)
                {
                    var glyph = run.Glyphs.Glyphs[i];
                    var advance = run.GetGlyphAdvance(i);

                    // The glyph's characters are named from the start of the run's text.
                    var index = run.Range.Start + glyph.ClusterStart;
                    var inRange = index >= start && index < end;
                    if (inRange && !glyph.IsHiddenIgnorable && typeface.TryGetOutline((ushort)glyph.GlyphIndex, out var outline) && outline is not null && !outline.IsEmpty)
                    {
                        var originX = run.X - _line.Left + pen + glyph.XOffset * scale;
                        foreach (var contour in outline.Contours)
                        {
                            var (px, py) = (contour.Start.X, contour.Start.Y);
                            Include(px, py);
                            foreach (var segment in contour.Segments)
                            {
                                if (segment.IsCubic)
                                {
                                    for (var step = 1; step <= 8; step++)
                                    {
                                        var t = step / 8.0;
                                        var u = 1 - t;
                                        Include(u * u * u * px + 3 * u * u * t * segment.Control1.X + 3 * u * t * t * segment.Control2.X + t * t * t * segment.End.X,
                                            u * u * u * py + 3 * u * u * t * segment.Control1.Y + 3 * u * t * t * segment.Control2.Y + t * t * t * segment.End.Y);
                                    }
                                }
                                else
                                {
                                    Include(segment.End.X, segment.End.Y);
                                }

                                (px, py) = (segment.End.X, segment.End.Y);
                            }
                        }

                        void Include(double x, double y)
                        {
                            var gx = originX + x * scale;
                            var gy = (glyph.YOffset + y) * scale;
                            minX = Math.Min(minX, gx);
                            maxX = Math.Max(maxX, gx);
                            maxY = Math.Max(maxY, gy);
                            minY = Math.Min(minY, gy);
                        }
                    }

                    pen += advance;
                }
            }

            if (minX > maxX)
                return false;

            (left, right, top, bottom) = (minX, maxX, maxY, -minY);
            return true;
        }
    }
}

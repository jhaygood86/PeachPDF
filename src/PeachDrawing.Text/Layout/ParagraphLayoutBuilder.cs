using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Layout
{
    /// <summary>Breaks a paragraph into lines at a width and places the runs of each line.</summary>
    internal static class ParagraphLayoutBuilder
    {
        private readonly record struct LineSpec(int Start, int End, LineEnd Kind);

        private readonly record struct Piece(Paragraph.Atom Atom, int From, int To, GlyphRun Glyphs, double Width);

        internal static ParagraphLayout Build(Paragraph paragraph, double availableWidth)
        {
            var specs = BreakLines(paragraph, availableWidth);
            var built = new List<(LineSpec Spec, List<Piece> Pieces, double Width, double Ascent, double Descent, double Height)>();
            double contentWidth = 0;
            foreach (var spec in specs)
            {
                var (pieces, width) = Assemble(paragraph, spec);
                var (ascent, descent, height) = VerticalExtent(paragraph, spec, pieces);
                built.Add((spec, pieces, width, ascent, descent, height));
                contentWidth = Math.Max(contentWidth, width);
            }

            double extent = double.IsInfinity(availableWidth) ? contentWidth : availableWidth;
            var lines = new LineBox[built.Count];
            double top = 0;
            for (int i = 0; i < built.Count; i++)
            {
                var (spec, pieces, width, ascent, descent, height) = built[i];
                bool endsParagraphOrForced = spec.Kind is LineEnd.Last or LineEnd.Forced;
                var align = ResolveAlign(paragraph, endsParagraphOrForced);

                // Justification widens the spaces of a line that is not the last, so that it fills the width.
                double spaceExtra = 0;
                if (align == TextAlign.Justify && !double.IsInfinity(extent) && extent > width)
                {
                    int spaces = 0;
                    foreach (var piece in pieces)
                    {
                        spaces += paragraph.CountSpaces(piece.Glyphs, piece.From);
                    }

                    if (spaces > 0)
                    {
                        spaceExtra = (extent - width) / spaces;
                        width = extent;
                    }
                }

                double left = AlignedLeft(paragraph, align, extent, width);
                double baseline = top + ascent;
                var runs = new List<PlacedRun>(pieces.Count);
                double x = left;
                foreach (var piece in pieces)
                {
                    var style = piece.Atom.Style;
                    var (boundaries, advances, pieceWidth) = PlaceRun(paragraph, piece, style, spaceExtra);
                    runs.Add(new PlacedRun(new TextRange(piece.From, piece.To), style, piece.Glyphs, piece.Atom.Level, x, baseline, pieceWidth, boundaries, advances));
                    x += pieceWidth;
                }

                lines[i] = new LineBox(new TextRange(spec.Start, spec.End), paragraph.ContentEnd(spec.Start, spec.End), runs, left, top, width, ascent, descent, height, spec.Kind);
                top += height;
            }

            return new ParagraphLayout(paragraph, lines, extent, contentWidth, top);
        }

        /// <summary>How a line is aligned: the paragraph's alignment, or for the last line and one that ends in a forced break, the last-line alignment.</summary>
        private static TextAlign ResolveAlign(Paragraph paragraph, bool lastOrForced)
        {
            var align = paragraph.Style.Align;
            if (lastOrForced)
            {
                align = paragraph.Style.AlignLast ?? (align == TextAlign.Justify ? TextAlign.Start : align);
            }

            return align;
        }

        private static double AlignedLeft(Paragraph paragraph, TextAlign align, double extent, double lineWidth)
        {
            bool rtl = paragraph.IsRightToLeft;
            if (align == TextAlign.Start)
            {
                align = rtl ? TextAlign.Right : TextAlign.Left;
            }
            else if (align == TextAlign.End)
            {
                align = rtl ? TextAlign.Left : TextAlign.Right;
            }

            return align switch
            {
                TextAlign.Right => extent - lineWidth,
                TextAlign.Center => (extent - lineWidth) / 2,
                TextAlign.Justify => rtl ? extent - lineWidth : 0,
                _ => 0,
            };
        }

        // ---- breaking --------------------------------------------------------------------------------------------------------------

        private static List<LineSpec> BreakLines(Paragraph p, double maxWidth)
        {
            var lines = new List<LineSpec>();
            int length = p.Text.Length;
            if (length == 0)
            {
                lines.Add(new LineSpec(0, 0, LineEnd.Last));
                return lines;
            }

            bool wrap = !p.Style.NoWrap && !double.IsInfinity(maxWidth);
            var opportunities = p.Opportunities;
            int lineStart = 0;
            int segmentStart = 0;
            double lineWidth = 0;
            for (int i = 1; i <= length; i++)
            {
                if (opportunities[i] == LineBreakOpportunity.Prohibited)
                {
                    continue;
                }

                int contentEnd = p.ContentEnd(segmentStart, i);
                double contentWidth = p.Measure(segmentStart, contentEnd);
                if (wrap && lineStart < segmentStart && lineWidth + contentWidth > maxWidth)
                {
                    lines.Add(new LineSpec(lineStart, segmentStart, LineEnd.Soft));
                    lineStart = segmentStart;
                    lineWidth = 0;
                }

                if (wrap && p.Style.OverflowWrap != OverflowWrap.Normal && contentWidth > maxWidth)
                {
                    // The line is empty and the word is still wider than it: cut the word between characters.
                    int position = segmentStart;
                    while (p.Measure(position, contentEnd) > maxWidth)
                    {
                        int cut = LargestFit(p, position, contentEnd, maxWidth);
                        lines.Add(new LineSpec(lineStart, cut, LineEnd.Emergency));
                        lineStart = cut;
                        position = cut;
                    }

                    lineWidth = p.Measure(position, i);
                }
                else
                {
                    lineWidth += p.Measure(segmentStart, i);
                }

                if (opportunities[i] == LineBreakOpportunity.Mandatory)
                {
                    lines.Add(new LineSpec(lineStart, i, i == length ? LineEnd.Last : LineEnd.Forced));
                    lineStart = i;
                    lineWidth = 0;
                }

                segmentStart = i;
            }

            // Text that ends in a line terminator leaves an empty line to put a caret on.
            if (Paragraph.IsLineTerminator(p.Text[^1]))
            {
                var last = lines[^1];
                lines[^1] = last with { Kind = LineEnd.Forced };
                lines.Add(new LineSpec(length, length, LineEnd.Last));
            }

            return lines;
        }

        /// <summary>The last grapheme boundary after <paramref name="start"/> that the text up to fits in <paramref name="width"/>, and at least the first one.</summary>
        private static int LargestFit(Paragraph p, int start, int end, double width)
        {
            var boundaries = new List<int>();
            for (int b = start + 1; b < end; b++)
            {
                if (p.IsGraphemeBoundary(b))
                {
                    boundaries.Add(b);
                }
            }

            if (boundaries.Count == 0)
            {
                return end;
            }

            int low = 0;
            int high = boundaries.Count - 1;
            int best = 0;
            while (low <= high)
            {
                int middle = (low + high) >>> 1;
                if (p.Measure(start, boundaries[middle]) <= width)
                {
                    best = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return boundaries[best];
        }

        // ---- assembling a line -----------------------------------------------------------------------------------------------------

        private static (List<Piece> Pieces, double Width) Assemble(Paragraph p, LineSpec spec)
        {
            var pieces = new List<Piece>();
            int contentEnd = p.ContentEnd(spec.Start, spec.End);
            if (contentEnd <= spec.Start)
            {
                return (pieces, 0);
            }

            double width = 0;
            var atoms = p.Atoms;
            foreach (var run in Bidi.ReorderLine(p.Levels, spec.Start, contentEnd - spec.Start))
            {
                var inRun = new List<Piece>();
                int runEnd = run.Start + run.Length;
                for (int a = p.FirstAtomAfter(run.Start); a < atoms.Length; a++)
                {
                    var atom = atoms[a];
                    if (atom.Start >= runEnd)
                    {
                        break;
                    }

                    int from = Math.Max(atom.Start, run.Start);
                    int to = Math.Min(atom.End, runEnd);
                    if (to <= from)
                    {
                        continue;
                    }

                    var glyphs = p.ShapePiece(atom, from, to);
                    inRun.Add(new Piece(atom, from, to, glyphs, p.WidthOf(glyphs, atom.Style, from)));
                }

                if (run.IsRtl)
                {
                    inRun.Reverse();
                }

                foreach (var piece in inRun)
                {
                    pieces.Add(piece);
                    width += piece.Width;
                }
            }

            return (pieces, width);
        }

        private static (double Ascent, double Descent, double Height) VerticalExtent(Paragraph p, LineSpec spec, List<Piece> pieces)
        {
            double ascent = 0, descent = 0, gap = 0, size = 0;
            void Include(RunStyle style)
            {
                var metrics = style.Typeface.Metrics;
                double scale = style.Size / metrics.UnitsPerEm;
                ascent = Math.Max(ascent, metrics.NormalLineAscent * scale);
                descent = Math.Max(descent, metrics.NormalLineDescent * scale);
                gap = Math.Max(gap, metrics.NormalLineGap * scale);
                size = Math.Max(size, style.Size);
            }

            if (pieces.Count == 0)
            {
                Include(p.StyleAt(Math.Min(spec.Start, Math.Max(0, p.Text.Length - 1))));
            }
            else
            {
                foreach (var piece in pieces)
                {
                    Include(piece.Atom.Style);
                }
            }

            if (p.Style.LineHeight is { } multiple)
            {
                double height = multiple * size;
                double leading = (height - (ascent + descent)) / 2;
                return (ascent + leading, descent + leading, height);
            }

            return (ascent + gap / 2, descent + gap / 2, ascent + descent + gap);
        }

        // ---- caret positions inside a run ------------------------------------------------------------------------------------------

        /// <summary>
        /// The distance from the left edge of a run to the caret at each boundary of its text, for every offset from the run's start to
        /// its end. A cluster that stands for several characters (a ligature) is shared out equally between its grapheme clusters.
        /// </summary>
        private static (double[] Boundaries, double[] Advances, double Width) PlaceRun(Paragraph p, Piece piece, RunStyle style, double spaceExtra)
        {
            int length = piece.To - piece.From;
            var x = new double[length + 1];
            var known = new bool[length + 1];
            bool rtl = (piece.Atom.Level & 1) == 1;
            double scale = style.Size / piece.Glyphs.Typeface.Metrics.UnitsPerEm;
            // A cluster can be several glyphs (a base and its marks), so its extent is what they cover together.
            var clusters = new Dictionary<(int Start, int End), (double Left, double Right)>();
            double pen = 0;
            var advances = new double[piece.Glyphs.Glyphs.Count];
            int glyphNumber = 0;
            foreach (var glyph in piece.Glyphs.Glyphs)
            {
                double advance = (piece.Glyphs.Typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale;
                if (p.EndsCluster(piece.Glyphs, piece.From, glyphNumber))
                {
                    advance += style.LetterSpacing;
                    if (p.IsWordSeparatorAt(piece.From + glyph.ClusterStart))
                    {
                        advance += style.WordSpacing + spaceExtra;
                    }
                }

                advances[glyphNumber++] = advance;
                double glyphLeft = pen;
                double glyphRight = pen + advance;
                pen = glyphRight;

                int clusterStart = glyph.ClusterStart;
                int clusterEnd = glyph.ClusterStart + glyph.ClusterLength;
                if (glyph.ClusterLength <= 0 || clusterStart < 0 || clusterEnd > length)
                {
                    continue;
                }

                var key = (clusterStart, clusterEnd);
                clusters[key] = clusters.TryGetValue(key, out var existing)
                    ? (Math.Min(existing.Left, glyphLeft), Math.Max(existing.Right, glyphRight))
                    : (glyphLeft, glyphRight);
            }

            foreach (var (span, extent) in clusters)
            {
                double left = extent.Left;
                double right = extent.Right;
                int start = span.Start;
                int end = span.End;

                var parts = new List<int> { start };
                for (int o = start + 1; o < end; o++)
                {
                    if (p.IsGraphemeBoundary(piece.From + o))
                    {
                        parts.Add(o);
                    }
                }

                parts.Add(end);
                int count = parts.Count - 1;
                double width = right - left;
                for (int k = 0; k < count; k++)
                {
                    // In a right-to-left run the first characters are at the right.
                    double partLeft = rtl ? left + width * (count - 1 - k) / count : left + width * k / count;
                    double partRight = partLeft + width / count;
                    int a = parts[k];
                    int b = parts[k + 1];
                    x[a] = rtl ? partRight : partLeft;
                    known[a] = true;
                    if (!known[b])
                    {
                        x[b] = rtl ? partLeft : partRight;
                        known[b] = true;
                    }
                }
            }

            // Offsets no glyph covers (a hidden character) take the position of the boundary before them.
            double carry = 0;
            bool seen = false;
            for (int i = 0; i <= length; i++)
            {
                if (known[i])
                {
                    carry = x[i];
                    seen = true;
                }
                else if (seen)
                {
                    x[i] = carry;
                }
            }

            if (seen)
            {
                for (int i = 0; i <= length && !known[i]; i++)
                {
                    x[i] = x[Array.FindIndex(known, k => k)];
                }
            }

            return (x, advances, pen);
        }
    }
}

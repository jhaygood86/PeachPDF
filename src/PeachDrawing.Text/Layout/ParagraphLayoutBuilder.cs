using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Layout
{
    /// <summary>Breaks a paragraph into lines at a width and places the runs of each line.</summary>
    internal static class ParagraphLayoutBuilder
    {
        internal readonly record struct LineSpec(int Start, int End, LineEnd Kind);

        private readonly record struct Piece(Paragraph.Atom Atom, int From, int To, GlyphRun Glyphs, double Width, bool IsTab = false);

        internal static ParagraphLayout Build(Paragraph paragraph, double availableWidth)
        {
            var built = new List<(LineSpec Spec, double Indent, List<Piece> Pieces, double Width, double Ascent, double Descent, double Height)>();
            double contentWidth = 0;
            int start = 0;
            while (true)
            {
                double indent = paragraph.IndentAt(start);
                var spec = FitLine(paragraph, start, RoomFor(availableWidth, indent), indent);
                var (pieces, width) = Assemble(paragraph, spec, indent);
                var (ascent, descent, height) = VerticalExtent(paragraph, spec, pieces);
                built.Add((spec, indent, pieces, width, ascent, descent, height));
                contentWidth = Math.Max(contentWidth, width + indent);
                if (spec.Kind == LineEnd.Last)
                {
                    break;
                }

                start = spec.End;
            }

            double extent = double.IsInfinity(availableWidth) ? contentWidth : availableWidth;
            var lines = new LineBox[built.Count];
            double top = 0;
            for (int i = 0; i < built.Count; i++)
            {
                var (spec, indent, pieces, width, ascent, descent, height) = built[i];
                bool endsParagraphOrForced = spec.Kind is LineEnd.Last or LineEnd.Forced;
                var align = ResolveAlign(paragraph, endsParagraphOrForced);

                // The indent is taken from the start side; what is left is where the line is aligned.
                double areaLeft = paragraph.IsRightToLeft ? 0 : indent;
                double areaWidth = extent - indent;

                // Justification shares the room a line that is not the last has left between its opportunities, so that it fills the width.
                double extra = 0;
                bool[][]? expandAfter = null;
                if (align == TextAlign.Justify && !double.IsInfinity(areaWidth) && areaWidth > width)
                {
                    int opportunities;
                    (expandAfter, opportunities) = FindJustificationOpportunities(paragraph, pieces);
                    if (opportunities > 0)
                    {
                        extra = (areaWidth - width) / opportunities;
                        width = areaWidth;
                    }
                    else
                    {
                        expandAfter = null;
                    }
                }

                double left = areaLeft + AlignedLeft(paragraph, align, areaWidth, width);
                double baseline = top + ascent;
                var runs = new List<PlacedRun>(pieces.Count);
                double x = left;
                for (int n = 0; n < pieces.Count; n++)
                {
                    var piece = pieces[n];
                    var style = piece.Atom.Style;
                    var (boundaries, advances, pieceWidth) = PlaceRun(paragraph, piece, style, extra, expandAfter?[n]);
                    runs.Add(new PlacedRun(new TextRange(piece.From, piece.To), style, piece.Glyphs, piece.Atom.Level, x, baseline, pieceWidth, boundaries, advances));
                    x += pieceWidth;
                }

                lines[i] = new LineBox(new TextRange(spec.Start, spec.End), paragraph.ContentEnd(spec.Start, spec.End), runs, left, top, width, ascent, descent, height, spec.Kind);
                top += height;
            }

            return new ParagraphLayout(paragraph, lines, extent, contentWidth, top);
        }

        /// <summary>The width a line of text can fill once its indent is taken from <paramref name="available"/>; it is never less than nothing.</summary>
        private static double RoomFor(double available, double indent) => double.IsInfinity(available) ? available : Math.Max(0, available - indent);

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

        /// <summary>Where a line of <paramref name="lineWidth"/> starts within an area <paramref name="areaWidth"/> wide, measured from the area's left edge.</summary>
        private static double AlignedLeft(Paragraph paragraph, TextAlign align, double areaWidth, double lineWidth)
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
                TextAlign.Right => areaWidth - lineWidth,
                TextAlign.Center => (areaWidth - lineWidth) / 2,
                TextAlign.Justify => rtl ? areaWidth - lineWidth : 0,
                _ => 0,
            };
        }

        // ---- justification ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Finds where a line may be widened: for each piece, a flag for every glyph that ends a cluster the room goes after, and how many there are. A
        /// space is one, unless the paragraph is justified between characters only; the boundaries between two characters are the others, of the kind
        /// <see cref="ParagraphStyle.TextJustify"/> asks for. Nothing goes at the end of the line, and a tab is a wall: nothing is added next to it.
        /// </summary>
        private static (bool[][]? Expand, int Count) FindJustificationOpportunities(Paragraph p, List<Piece> pieces)
        {
            var mode = p.Style.TextJustify;
            if (mode == TextJustify.None)
            {
                return (null, 0);
            }

            // The clusters of the line in drawing order, as (piece, glyph, offset of the cluster's text); a tab separates the ones before it from those after.
            var clusters = new List<(int Piece, int Glyph, int Offset)>();
            var expand = new bool[pieces.Count][];
            for (int n = 0; n < pieces.Count; n++)
            {
                var piece = pieces[n];
                expand[n] = new bool[piece.Glyphs.Glyphs.Count];
                if (piece.IsTab)
                {
                    clusters.Add((n, -1, -1));
                    continue;
                }

                for (int g = 0; g < piece.Glyphs.Glyphs.Count; g++)
                {
                    if (p.EndsCluster(piece.Glyphs, piece.From, g))
                    {
                        clusters.Add((n, g, piece.From + piece.Glyphs.Glyphs[g].ClusterStart));
                    }
                }
            }

            int count = 0;
            for (int k = 0; k < clusters.Count; k++)
            {
                var (pieceIndex, glyph, offset) = clusters[k];
                if (glyph < 0)
                {
                    continue;
                }

                bool opportunity = p.IsWordSeparatorAt(offset);
                if (!opportunity && mode != TextJustify.InterWord && k + 1 < clusters.Count && clusters[k + 1].Glyph >= 0)
                {
                    int next = clusters[k + 1].Offset;
                    opportunity = mode == TextJustify.InterCharacter
                        ? !p.JoinsNext(Math.Min(offset, next))
                        : p.IsBlockScriptAt(offset) || p.IsBlockScriptAt(next);
                }

                if (opportunity)
                {
                    expand[pieceIndex][glyph] = true;
                    count++;
                }
            }

            return (expand, count);
        }

        // ---- breaking --------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Finds where the line that starts at <paramref name="start"/> ends, given what it may fill. It looks at nothing but the paragraph and its arguments, so
        /// the same arguments always give the same line, whatever was laid out before.
        /// </summary>
        /// <param name="p">The paragraph.</param>
        /// <param name="start">The offset of the line's first character.</param>
        /// <param name="room">The width the line's text may fill, or <see cref="double.PositiveInfinity"/> for a line that breaks only where it is forced to.</param>
        /// <param name="pen">The distance from the paragraph's start edge to where the line's text starts, which tab stops are measured from.</param>
        internal static LineSpec FitLine(Paragraph p, int start, double room, double pen)
        {
            int length = p.Text.Length;
            if (start >= length)
            {
                return new LineSpec(length, length, LineEnd.Last);
            }

            bool wrap = !p.Style.NoWrap && !double.IsInfinity(room);
            bool mayCut = p.Style.OverflowWrap != OverflowWrap.Normal;
            var opportunities = p.Opportunities;
            int segmentStart = start;
            double lineWidth = 0;
            for (int i = start + 1; i <= length; i++)
            {
                if (opportunities[i] == LineBreakOpportunity.Prohibited)
                {
                    continue;
                }

                if (wrap)
                {
                    int contentEnd = p.ContentEnd(segmentStart, i);
                    double at = pen + lineWidth;
                    if (mayCut && segmentStart == start)
                    {
                        // The line is empty: a word wider than it is cut between characters.
                        int cut = LargestFit(p, start, contentEnd, room, pen);
                        if (cut < contentEnd)
                        {
                            return new LineSpec(start, cut, LineEnd.Emergency);
                        }
                    }

                    double contentWidth = p.Measure(segmentStart, contentEnd, at);
                    if (start < segmentStart && lineWidth + contentWidth > room)
                    {
                        return new LineSpec(start, segmentStart, LineEnd.Soft);
                    }

                    lineWidth += p.Measure(segmentStart, i, at);
                }

                if (opportunities[i] == LineBreakOpportunity.Mandatory)
                {
                    return new LineSpec(start, i, i == length && !Paragraph.IsLineTerminator(p.Text[length - 1]) ? LineEnd.Last : LineEnd.Forced);
                }

                segmentStart = i;
            }

            return new LineSpec(start, length, LineEnd.Last);
        }

        /// <summary>
        /// The last grapheme boundary after <paramref name="start"/> that the text up to fits in <paramref name="width"/> when the pen starts at
        /// <paramref name="pen"/>, and at least the first one; <paramref name="end"/> itself when all of the text fits or it has no boundary inside.
        /// It probes further and further out, so what it costs follows the length of the line it finds and not of the word it looks into.
        /// </summary>
        private static int LargestFit(Paragraph p, int start, int end, double width, double pen)
        {
            int first = NextBoundary(p, start + 1, end);
            if (first < 0)
            {
                return end;
            }

            if (p.Measure(start, first, pen) > width)
            {
                return first;
            }

            int best = first;
            int high;
            int step = 16;
            while (true)
            {
                int probe = NextBoundary(p, best + step, end);
                if (probe < 0)
                {
                    if (p.Measure(start, end, pen) <= width)
                    {
                        return end;
                    }

                    high = end;
                    break;
                }

                if (p.Measure(start, probe, pen) <= width)
                {
                    best = probe;
                    step = Math.Min(step * 2, 1 << 20);
                }
                else
                {
                    high = probe;
                    break;
                }
            }

            var boundaries = new List<int>();
            for (int b = best + 1; b < high; b++)
            {
                if (p.IsGraphemeBoundary(b))
                {
                    boundaries.Add(b);
                }
            }

            int low = 0;
            int top = boundaries.Count - 1;
            int found = -1;
            while (low <= top)
            {
                int middle = (low + top) >>> 1;
                if (p.Measure(start, boundaries[middle], pen) <= width)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    top = middle - 1;
                }
            }

            return found < 0 ? best : boundaries[found];
        }

        /// <summary>The first grapheme boundary at or after <paramref name="from"/> and before <paramref name="end"/>, or -1.</summary>
        private static int NextBoundary(Paragraph p, int from, int end)
        {
            for (int b = from; b < end; b++)
            {
                if (p.IsGraphemeBoundary(b))
                {
                    return b;
                }
            }

            return -1;
        }

        // ---- assembling a line -----------------------------------------------------------------------------------------------------

        private static (List<Piece> Pieces, double Width) Assemble(Paragraph p, LineSpec spec, double indent)
        {
            var pieces = new List<Piece>();
            int contentEnd = p.ContentEnd(spec.Start, spec.End);
            if (contentEnd <= spec.Start)
            {
                return (pieces, 0);
            }

            var tabWidths = TabWidths(p, spec.Start, contentEnd, indent);
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

                    if (tabWidths is not null && tabWidths.TryGetValue(from, out double tabWidth))
                    {
                        // A tab draws nothing: it is room, in a run of its own with no glyphs.
                        inRun.Add(new Piece(atom, from, to, new GlyphRun(atom.Style.Typeface, []), tabWidth, IsTab: true));
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

        /// <summary>The width of each tab of the line, by its offset, worked out along the text in the order it is written; null when the line has none.</summary>
        private static Dictionary<int, double>? TabWidths(Paragraph p, int start, int contentEnd, double indent)
        {
            if (p.Text.AsSpan(start, contentEnd - start).IndexOf('\t') < 0)
            {
                return null;
            }

            var widths = new Dictionary<int, double>();
            var atoms = p.Atoms;
            double pen = indent;
            for (int a = p.FirstAtomAfter(start); a < atoms.Length; a++)
            {
                var atom = atoms[a];
                if (atom.Start >= contentEnd)
                {
                    break;
                }

                int from = Math.Max(atom.Start, start);
                int to = Math.Min(atom.End, contentEnd);
                if (to <= from)
                {
                    continue;
                }

                if (p.IsTab(atom))
                {
                    double advance = p.TabAdvance(atom.Style, pen);
                    widths[from] = advance;
                    pen += advance;
                }
                else
                {
                    pen += p.WidthOf(p.ShapePiece(atom, from, to), atom.Style, from);
                }
            }

            return widths;
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
        private static (double[] Boundaries, double[] Advances, double Width) PlaceRun(Paragraph p, Piece piece, RunStyle style, double extra, bool[]? expandAfter)
        {
            int length = piece.To - piece.From;
            var x = new double[length + 1];
            if (piece.IsTab)
            {
                // The caret before a tab is at the edge it is entered from, the one after it at the other.
                bool tabRtl = (piece.Atom.Level & 1) == 1;
                x[0] = tabRtl ? piece.Width : 0;
                x[length] = tabRtl ? 0 : piece.Width;
                return (x, [], piece.Width);
            }

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
                        advance += style.WordSpacing;
                    }

                    if (expandAfter is not null && expandAfter[glyphNumber])
                    {
                        advance += extra;
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

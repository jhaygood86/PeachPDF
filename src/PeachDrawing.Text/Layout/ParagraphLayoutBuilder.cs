using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Layout
{
    /// <summary>Breaks a paragraph into lines at a width and places the runs of each line.</summary>
    internal static class ParagraphLayoutBuilder
    {
        internal readonly record struct LineSpec(int Start, int End, LineEnd Kind, int CutAt = -1);

        private readonly record struct Piece(Paragraph.Atom Atom, int From, int To, GlyphRun Glyphs, double Width, bool IsTab = false, bool IsGenerated = false);

        private readonly record struct Break(int Position, double Width, bool Hyphen);

        internal static ParagraphLayout Build(Paragraph paragraph, double availableWidth)
        {
            var built = new List<(LineSpec Spec, double Indent, List<Piece> Pieces, double Width, double Ascent, double Descent, double Height)>();
            double contentWidth = 0;
            int start = 0;
            int hyphenRun = 0;
            int maxLines = paragraph.Style.MaxLines ?? int.MaxValue;
            bool truncated = false;
            while (true)
            {
                double indent = paragraph.IndentAt(start);
                double room = RoomFor(availableWidth, indent);
                var spec = FitLine(paragraph, start, room, indent, hyphenRun, RoomFor(availableWidth, paragraph.IndentOf(false, false)));
                hyphenRun = spec.Kind == LineEnd.Hyphenated ? hyphenRun + 1 : 0;

                // The last line the paragraph may have holds what does not fit, cut short, and ends with the ellipsis.
                bool lastAllowed = built.Count + 1 >= maxLines;
                bool hidesText = lastAllowed && spec.End < paragraph.Text.Length;
                truncated |= hidesText;
                spec = Ellipsize(paragraph, spec, room, indent, hidesText);

                var (pieces, width) = Assemble(paragraph, spec, indent);
                var (ascent, descent, height) = VerticalExtent(paragraph, spec, pieces);
                built.Add((spec, indent, pieces, width, ascent, descent, height));
                contentWidth = Math.Max(contentWidth, width + indent);
                if (spec.Kind == LineEnd.Last || lastAllowed)
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
                    runs.Add(new PlacedRun(new TextRange(piece.From, piece.To), style, piece.Glyphs, piece.Atom.Level, x, baseline, pieceWidth, boundaries, advances, piece.IsGenerated));
                    x += pieceWidth;
                }

                lines[i] = new LineBox(new TextRange(spec.Start, spec.End), ContentEndOf(paragraph, spec), runs, left, top, width, ascent, descent, height, spec.Kind, spec.CutAt >= 0);
                top += height;
            }

            return new ParagraphLayout(paragraph, lines, extent, contentWidth, top, truncated);
        }

        /// <summary>Where the drawn text of a line ends: where it was cut, or else before the spaces that hang at its end.</summary>
        private static int ContentEndOf(Paragraph p, LineSpec spec) => spec.CutAt >= 0 ? spec.CutAt : p.ContentEnd(spec.Start, spec.End);

        /// <summary>
        /// Cuts a line short, at a boundary between characters, so that it and the ellipsis fit: a line that holds the text the paragraph has no room for
        /// (<paramref name="hidesText"/>), which then ends the paragraph, or one wider than its room when <see cref="ParagraphStyle.TextOverflow"/> asks for that.
        /// </summary>
        private static LineSpec Ellipsize(Paragraph p, LineSpec spec, double room, double pen, bool hidesText)
        {
            bool overflowing = p.Style.TextOverflow == TextOverflow.Ellipsis && !double.IsInfinity(room);
            if (!hidesText && !overflowing)
            {
                return spec;
            }

            int natural = p.ContentEnd(spec.Start, spec.End);
            double contentWidth = p.Measure(spec.Start, natural, pen);
            if (!hidesText && contentWidth <= room)
            {
                return spec;
            }

            int cut = natural;
            if (!double.IsInfinity(room))
            {
                double ellipsisWidth = p.EllipsisAt(natural, spec.Start).Width;
                if (contentWidth + ellipsisWidth > room)
                {
                    cut = FitWithin(p, spec.Start, natural, room - ellipsisWidth, pen);
                }
            }

            // Spaces do not stand before an ellipsis.
            cut = p.ContentEnd(spec.Start, cut);

            // The ellipsis takes the style of the last character drawn, which the cut may have moved into a run of another size or face: measure the one that is drawn.
            while (cut > spec.Start && !double.IsInfinity(room))
            {
                double drawn = p.EllipsisAt(cut, spec.Start).Width;
                if (p.Measure(spec.Start, cut, pen) + drawn <= room)
                {
                    break;
                }

                cut = p.ContentEnd(spec.Start, FitWithin(p, spec.Start, cut, room - drawn, pen));
            }

            return hidesText ? new LineSpec(spec.Start, p.Text.Length, LineEnd.Last, cut) : spec with { CutAt = cut };
        }

        /// <summary>The last grapheme boundary from <paramref name="start"/> to <paramref name="end"/>, or <paramref name="start"/> itself, that the text up to fits in <paramref name="width"/>.</summary>
        private static int FitWithin(Paragraph p, int start, int end, double width, double pen)
        {
            if (width <= 0)
            {
                return start;
            }

            int first = NextBoundary(p, start + 1, end);
            if (first < 0)
            {
                return p.Measure(start, end, pen) <= width ? end : start;
            }

            return p.Measure(start, first, pen) > width ? start : LargestFit(p, start, end, width, pen);
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
                if (piece.IsTab || piece.IsGenerated)
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
        /// <param name="nextRoom">The width a line after this one may fill, which the limit on hyphenating the last full line tests the rest of the text against.</param>
        /// <param name="hyphenRun">How many lines in a row before this one ended with a hyphenation (<see cref="ParagraphStyle.HyphenateLimitLines"/> counts them).</param>
        internal static LineSpec FitLine(Paragraph p, int start, double room, double pen, int hyphenRun, double nextRoom)
        {
            int length = p.Text.Length;
            if (start >= length)
            {
                return new LineSpec(length, length, LineEnd.Last);
            }

            var style = p.Style;
            bool wrap = !style.NoWrap && !double.IsInfinity(room);
            bool mayCut = style.OverflowWrap != OverflowWrap.Normal;
            bool hyphens = style.Hyphens != Hyphens.None;
            bool hyphenAllowed = hyphens && (style.HyphenateLimitLines is not { } limit || hyphenRun < limit);
            bool automatic = hyphenAllowed && style.Hyphens == Hyphens.Auto;
            var opportunities = p.Opportunities;
            int segmentStart = start;
            double lineWidth = 0;
            List<Break>? breaks = null;
            bool trackBreaks = wrap && p.HasSoftHyphens;
            for (int i = start + 1; i <= length; i++)
            {
                // A long word is jumped over, not walked: cutting one into many lines must not cost its length for each of them.
                i = p.NextOpportunityAtOrAfter(i);
                if (i > length)
                {
                    break;
                }

                if (wrap)
                {
                    int contentEnd = p.ContentEnd(segmentStart, i);
                    double at = pen + lineWidth;
                    if (segmentStart == start && (mayCut || automatic))
                    {
                        // The line is empty: a word wider than it is hyphenated, or else cut between characters.
                        int fit = LargestFit(p, start, contentEnd, room, pen);
                        if (fit < contentEnd)
                        {
                            if (automatic && HyphenationBreak(p, start, start, contentEnd, room, nextRoom, pen, 0) is > 0 and var hyphenated)
                            {
                                return new LineSpec(start, hyphenated, LineEnd.Hyphenated);
                            }

                            if (mayCut)
                            {
                                return new LineSpec(start, fit, LineEnd.Emergency);
                            }
                        }
                    }

                    double contentWidth = p.Measure(segmentStart, contentEnd, at);
                    if (start < segmentStart && lineWidth + contentWidth > room)
                    {
                        if (automatic && HyphenationBreak(p, start, segmentStart, contentEnd, room, nextRoom, pen, lineWidth) is > 0 and var hyphenated)
                        {
                            return new LineSpec(start, hyphenated, LineEnd.Hyphenated);
                        }

                        return BreakBefore(p, start, segmentStart, room, breaks, hyphens, hyphenAllowed);
                    }

                    lineWidth += p.Measure(segmentStart, i, at);
                }

                if (opportunities[i] == LineBreakOpportunity.Mandatory)
                {
                    return new LineSpec(start, i, i == length && !Paragraph.IsLineTerminator(p.Text[length - 1]) ? LineEnd.Last : LineEnd.Forced);
                }

                if (trackBreaks)
                {
                    (breaks ??= []).Add(new Break(i, lineWidth, hyphens && p.Text[i - 1] == '\u00AD'));
                }

                segmentStart = i;
            }

            return new LineSpec(start, length, LineEnd.Last);
        }

        /// <summary>
        /// Ends the line at <paramref name="position"/>, where the next word does not fit. After a soft hyphen the line must also have room for the hyphen it
        /// ends with, and when it has none the line ends at the last earlier place that does.
        /// </summary>
        private static LineSpec BreakBefore(Paragraph p, int start, int position, double room, List<Break>? breaks, bool hyphens, bool hyphenAllowed)
        {
            if (!hyphens || p.Text[position - 1] != '\u00AD')
            {
                return new LineSpec(start, position, LineEnd.Soft);
            }

            if (breaks is not null)
            {
                for (int k = breaks.Count - 1; k >= 0; k--)
                {
                    var candidate = breaks[k];
                    if (candidate.Position > position)
                    {
                        continue;
                    }

                    if (!candidate.Hyphen)
                    {
                        return new LineSpec(start, candidate.Position, LineEnd.Soft);
                    }

                    if (hyphenAllowed && candidate.Width + p.HyphenAt(candidate.Position).Width <= room)
                    {
                        return new LineSpec(start, candidate.Position, LineEnd.Hyphenated);
                    }
                }
            }

            // Nowhere fits it (or the limit on hyphenated lines has been reached and no other break is left): the hyphen overflows.
            return new LineSpec(start, position, LineEnd.Hyphenated);
        }

        /// <summary>
        /// The place to hyphenate the word in <c>[wordStart, contentEnd)</c>, which does not fit after <paramref name="lineWidth"/> of the line: the last place
        /// the patterns of its language allow that leaves room for the hyphen, or -1. The limits on how much room a line may leave, and on hyphenating the
        /// last full line, are applied here; the ones on the size of the pieces are in the points themselves.
        /// </summary>
        private static int HyphenationBreak(Paragraph p, int start, int wordStart, int contentEnd, double room, double nextRoom, double pen, double lineWidth)
        {
            var points = p.HyphenationPoints(wordStart, contentEnd);
            if (points.Length == 0)
            {
                return -1;
            }

            double zone = p.Style.HyphenateLimitZone;
            if (zone > 0 && wordStart > start && room - p.Measure(start, p.ContentEnd(start, wordStart), pen) < zone)
            {
                return -1;
            }

            double at = pen + lineWidth;
            int low = 0;
            int high = points.Length - 1;
            int best = -1;
            while (low <= high)
            {
                int middle = (low + high) >>> 1;
                if (lineWidth + p.Measure(wordStart, points[middle], at) + p.HyphenAt(points[middle]).Width <= room)
                {
                    found = middle;
                    low = middle + 1;
                }
                else
                {
                    top = middle - 1;
                }
            }

            for (; best >= 0; best--)
            {
                if (p.Style.HyphenateLimitLast != HyphenateLimitLast.Always || !FitsOnALineOfItsOwn(p, points[best], nextRoom))
                {
                    return points[best];
                }
            }

            return -1;
        }

        /// <summary>
        /// Whether the text from <paramref name="from"/> to the next forced break, or the end of the paragraph, fits a line of the paragraph without a break. It gives up
        /// as soon as the words are wider than the line, so it costs no more than one line's worth of text.
        /// </summary>
        private static bool FitsOnALineOfItsOwn(Paragraph p, int from, double room)
        {
            var opportunities = p.Opportunities;
            double pen = p.IndentOf(false, false);
            double width = 0;
            int segmentStart = from;
            for (int i = from + 1; i <= p.Text.Length; i++)
            {
                if (opportunities[i] == LineBreakOpportunity.Prohibited)
                {
                    continue;
                }

                double at = pen + width;
                if (width + p.Measure(segmentStart, p.ContentEnd(segmentStart, i), at) > room)
                {
                    return false;
                }

                if (opportunities[i] == LineBreakOpportunity.Mandatory)
                {
                    return true;
                }

                width += p.Measure(segmentStart, i, at);
                segmentStart = i;
            }

            return true;
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
            int contentEnd = ContentEndOf(p, spec);
            var tabWidths = contentEnd > spec.Start ? TabWidths(p, spec.Start, contentEnd, indent) : null;
            double width = 0;
            var atoms = p.Atoms;
            foreach (var run in contentEnd > spec.Start ? Bidi.ReorderLine(p.Levels, spec.Start, contentEnd - spec.Start) : [])
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

            bool cut = spec.CutAt >= 0;
            if (cut || spec.Kind == LineEnd.Hyphenated)
            {
                var (glyphs, generatedStyle, generatedWidth) = cut ? p.EllipsisAt(contentEnd, spec.Start) : p.HyphenAt(contentEnd);
                if (glyphs.Glyphs.Count > 0)
                {
                    var atom = new Paragraph.Atom(contentEnd, contentEnd, 0, p.ParagraphLevel, "Latn", generatedStyle);
                    var generated = new Piece(atom, contentEnd, contentEnd, glyphs, generatedWidth, IsGenerated: true);

                    // A hyphen or an ellipsis ends the text, so it is at the end of the line in the paragraph's direction.
                    if (p.IsRightToLeft)
                    {
                        pieces.Insert(0, generated);
                    }
                    else
                    {
                        pieces.Add(generated);
                    }

                    width += generatedWidth;
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

            if (piece.IsGenerated)
            {
                // Generated text stands for none of the paragraph's: its one boundary is where it is entered from, in the paragraph's direction.
                bool generatedRtl = (piece.Atom.Level & 1) == 1;
                x[0] = generatedRtl ? piece.Width : 0;
                var glyphs = piece.Glyphs.Glyphs;
                double generatedScale = style.Size / piece.Glyphs.Typeface.Metrics.UnitsPerEm;
                var generatedAdvances = new double[glyphs.Count];
                for (int g = 0; g < glyphs.Count; g++)
                {
                    generatedAdvances[g] = (piece.Glyphs.Typeface.GetAdvance((ushort)glyphs[g].GlyphIndex) + glyphs[g].XAdvanceDelta) * generatedScale;
                    if (glyphs[g].ClusterLength > 0 && (g == glyphs.Count - 1 || glyphs[g + 1].ClusterStart != glyphs[g].ClusterStart))
                    {
                        generatedAdvances[g] += style.LetterSpacing;
                    }
                }

                return (x, generatedAdvances, piece.Width);
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

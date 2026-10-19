using PeachDrawing.Core;
using PeachDrawing.Text.Unicode;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachPDF.Svg
{
    internal static partial class SvgRenderer
    {
        /// <summary>
        /// Lays out one <c>&lt;text&gt;</c>'s characters: auto-wrapped into line boxes when it has an <c>inline-size</c> or a <c>shape-inside</c>
        /// (<see cref="LayoutWrappedText"/>), otherwise along text chunks (<see cref="LayoutGlyphs"/>), then reordered for bidi.
        /// </summary>
        private static void LayoutTextBlock(Canvas g, SvgTextElement text, List<GlyphInfo> glyphs, List<EmbeddingSpan> overrides, bool isVertical)
        {
            if (!isVertical && text.IsAutoWrapped)
            {
                LayoutWrappedText(g, text, glyphs, overrides);
                return;
            }

            LayoutGlyphs(g, glyphs, isVertical);
            ApplyBidiReordering(text, glyphs, overrides, isVertical);
        }

        /// <summary>One place a wrapped line can break, with the width of the line if it breaks there.</summary>
        private readonly record struct WrapCandidate(int Index, double Width, bool Hyphenated, double HyphenWidth);

        /// <summary>The result of filling one stretch of a line box: where it ends and how wide its content is.</summary>
        private readonly record struct WrapFill(int End, double Content, bool Forced, bool Hyphenated, bool Fit);

        /// <summary>A laid-out stretch of a line box (a line has several when a shape or <c>shape-subtract</c> splits it), before alignment.</summary>
        private readonly record struct WrappedLine(int Start, int Length, double Available, double Content);

        /// <summary>
        /// Auto-wrapped text (SVG 2 §11.7): breaks the flattened characters into line boxes of the <c>inline-size</c> (or the stretches of
        /// <c>shape-inside</c>, minus <c>shape-subtract</c>) - at the opportunities of the Unicode line breaking algorithm the HTML layout uses, at
        /// hyphenation points when <c>hyphens</c> asks - then places the lines (<c>line-height</c>, the first line's <c>text-indent</c>, <c>text-align</c>
        /// including <c>justify</c>, bidi reordering per line). Characters that do not fit in the shape are omitted. The <c>x</c>/<c>y</c> of the
        /// <c>&lt;text&gt;</c> is the start edge of the box and the first baseline; per-character <c>x</c>/<c>y</c>/<c>dx</c>/<c>dy</c> and
        /// <c>textLength</c> have no effect on wrapped text, while <c>rotate</c> still turns the glyph it names.
        /// </summary>
        private static void LayoutWrappedText(Canvas g, SvgTextElement text, List<GlyphInfo> glyphs, List<EmbeddingSpan> overrides)
        {
            // Measured along one unbroken line first: every glyph's Advance (and Size) is what the breaking works with.
            LayoutGlyphs(g, glyphs, isVertical: false, measureOnly: true);

            var count = glyphs.Count;
            var rtl = text.Direction == "rtl";

            var paragraph = new StringBuilder();
            var starts = new int[count + 1];
            for (var i = 0; i < count; i++)
            {
                starts[i] = paragraph.Length;
                paragraph.Append(glyphs[i].Glyph);
            }

            starts[count] = paragraph.Length;

            var paragraphText = paragraph.ToString();
            var opportunities = UnicodeLineBreaks.Find(paragraphText, PeachPDF.CSS.WordBreak.Normal, 0, PeachPDF.CSS.LineBreak.Auto, text.ShapingFeatures.Language);
            var hyphenAfter = FindHyphenationPoints(glyphs, paragraphText, starts);

            var hyphenWidths = new Dictionary<GlyphInfo, double>();
            double HyphenWidth(GlyphInfo gi)
            {
                if (!hyphenWidths.TryGetValue(gi, out var width))
                    hyphenWidths[gi] = width = g.MeasureString("-", gi.Font, gi.Run.ShapingFeatures).Width + gi.Run.LetterSpacing;
                return width;
            }

            // A soft hyphen and a line feed take no room of their own; a soft hyphen that ends a line becomes the visible hyphen below.
            for (var i = 0; i < count; i++)
            {
                if (IsLineFeed(glyphs[i]) || glyphs[i].Glyph == "­")
                    glyphs[i].Advance = 0;
            }

            bool CanBreakBefore(int index) =>
                opportunities[starts[index]] != LineBreakOpportunity.Prohibited && glyphs[index - 1].Run.WhiteSpace is not ("nowrap" or "pre");

            // A space at the end of a line hangs outside it (and collapses away under white-space: normal), except when white-space keeps it in place.
            static bool IsHangingSpace(GlyphInfo gi) => gi.Glyph == " " && gi.Run.WhiteSpace is not ("pre" or "break-spaces");

            var lines = new List<WrappedLine>();
            var omittedFrom = count;
            var sequence = 0;
            var indentPending = true;
            var previousBaseline = 0.0;
            var previousBelow = 0.0;
            var isFirstLine = true;

            var polygonBounds = text.ShapeInside is { Length: > 0 } polygon
                ? (Left: polygon.Min(p => p.X), Right: polygon.Max(p => p.X), Top: polygon.Min(p => p.Y), Bottom: polygon.Max(p => p.Y))
                : default((double Left, double Right, double Top, double Bottom)?);

            var next = 0;
            while (next < count)
            {
                var lead = glyphs[next];
                var leadHeight = LineHeightOf(lead);

                // The first line's baseline is the text's y; each later one is as far below it as the line above reaches down plus this one reaches up.
                var candidateBaseline = isFirstLine ? text.Y : previousBaseline + previousBelow + LineAbove(lead);
                var top = candidateBaseline - LineAbove(lead);
                var spans = LineSpans(text, rtl, top, top + leadHeight);

                if (spans.Count == 0)
                {
                    // No room at this height inside the shape: try the next line down, until the shape is below.
                    if (polygonBounds is { } bounds && top > bounds.Bottom)
                    {
                        omittedFrom = next;
                        break;
                    }

                    previousBaseline = isFirstLine ? candidateBaseline : Math.Max(candidateBaseline, previousBaseline + 1);
                    previousBelow = LineBelow(lead);
                    isFirstLine = false;
                    continue;
                }

                var lineStart = next;
                var forcedBreak = false;
                var placed = new List<(int Start, int End, double Left, double Available, double Content, bool Justifiable)>();
                var segmentStart = next;

                foreach (var (spanLeft, spanRight) in spans)
                {
                    if (segmentStart >= count || forcedBreak)
                        break;

                    var indent = indentPending ? text.TextIndent : 0;
                    var available = spanRight - spanLeft - indent;
                    var left = rtl ? spanLeft : spanLeft + indent;
                    var canOverflow = text.ShapeInside is null && text.ShapeSubtract.Count == 0
                        || spanRight - spanLeft >= (polygonBounds is { } wide ? wide.Right - wide.Left : text.InlineSize ?? 0) - 1e-6;

                    var fill = Fill(segmentStart, available, canOverflow);
                    if (!fill.Fit)
                        continue;

                    indentPending = false;
                    placed.Add((segmentStart, fill.End, left, available, fill.Content, !fill.Forced && fill.End < count));
                    forcedBreak = fill.Forced;
                    segmentStart = fill.End;
                }

                if (placed.Count == 0)
                {
                    // Nothing fits in any stretch at this height (a word wider than the shape here): look further down.
                    if (polygonBounds is { } b && top > b.Bottom)
                    {
                        omittedFrom = next;
                        break;
                    }

                    previousBaseline = isFirstLine ? candidateBaseline : Math.Max(candidateBaseline, previousBaseline + 1);
                    previousBelow = LineBelow(lead);
                    isFirstLine = false;
                    continue;
                }

                // The line's own box may reach further than the estimate the band was read with (a taller run on it); the baseline follows the real one.
                double above = double.MinValue, below = double.MinValue;
                for (var k = lineStart; k < segmentStart; k++)
                {
                    // A collapsed space at the end of the line is gone and does not count towards the line's height.
                    if (glyphs[k].Glyph.Length == 0 && k > lineStart)
                        continue;

                    above = Math.Max(above, LineAbove(glyphs[k]));
                    below = Math.Max(below, LineBelow(glyphs[k]));
                }

                var baseline = isFirstLine ? text.Y : previousBaseline + previousBelow + above;

                foreach (var segment in placed)
                {
                    var x = segment.Left;
                    var gapGlyphs = new List<int>();
                    for (var k = segment.Start; k < segment.End; k++)
                    {
                        if (glyphs[k].Glyph == " " && glyphs[k].Advance > 0 && k < segment.End - 1)
                            gapGlyphs.Add(k);
                    }

                    var justify = ResolveTextAlign(text, rtl) == "justify" && segment.Justifiable && gapGlyphs.Count > 0 && segment.Content < segment.Available;
                    var extra = justify ? (segment.Available - segment.Content) / gapGlyphs.Count : 0;
                    foreach (var k in gapGlyphs)
                    {
                        if (justify)
                        {
                            glyphs[k].Advance += extra;
                            glyphs[k].SpacingAdjusted = true;
                        }
                    }

                    for (var k = segment.Start; k < segment.End; k++)
                    {
                        var gi = glyphs[k];
                        gi.Px = x;
                        gi.Py = baseline + BaselineOffset(gi.Run);
                        gi.LineIndex = sequence;
                        x += gi.Advance;
                    }

                    sequence++;
                    lines.Add(new WrappedLine(segment.Start, segment.End - segment.Start, segment.Available, justify ? segment.Available : segment.Content));
                }

                isFirstLine = false;
                previousBaseline = baseline;
                previousBelow = below;
                next = segmentStart;
                if (next == lineStart)
                    break;
            }

            // The characters after the last line that fit are not painted.
            omittedFrom = Math.Min(omittedFrom, next);
            for (var i = omittedFrom; i < count; i++)
                glyphs[i].Omitted = true;

            // A soft hyphen or line feed that did not become a hyphen is invisible; a break that did gets its hyphen painted by the glyph before it.
            for (var i = 0; i < count; i++)
            {
                var gi = glyphs[i];
                if (IsLineFeed(gi) || gi.Glyph == "­")
                    gi.Glyph = "";

                // Consumed: the wrapped layout owns every position, and a leftover value would split a paint batch in PaintGlyphs.
                gi.X = null;
                gi.Y = null;
                gi.Dx = null;
                gi.Dy = null;
            }

            var lineRanges = lines.Select(l => (l.Start, l.Length)).ToList();
            ApplyBidiReordering(text, glyphs, overrides, isVertical: false, lineRanges);

            // Alignment shifts each stretch as a whole, over its own content: the glyphs already sit in visual order within it.
            foreach (var line in lines)
            {
                var align = ResolveTextAlign(text, rtl);

                // The last line of justified text (and one that ends in a forced break) starts at its start edge.
                if (align == "justify")
                    align = rtl ? "right" : "left";

                var delta = AlignmentOffset(align, rtl, line.Available - line.Content);
                if (delta == 0)
                    continue;

                for (var k = line.Start; k < line.Start + line.Length; k++)
                    glyphs[k].Px += delta;
            }

            glyphs.RemoveAll(gi => gi.Omitted);

            // Fills one stretch of a line box starting at glyph `start`, reporting where it ends.
            WrapFill Fill(int start, double available, bool canOverflow)
            {
                var candidates = new List<WrapCandidate>();
                var width = 0.0;
                var hang = 0.0;
                var overflowed = false;

                var j = start;
                for (; j < count; j++)
                {
                    var gi = glyphs[j];

                    if (IsLineFeed(gi))
                        return Finish(j + 1, width - hang, forced: true, hyphenated: false);

                    if (j > start)
                    {
                        if (CanBreakBefore(j))
                        {
                            // Once a word has overflowed with no break to use, the next place the line may end is where it does.
                            if (overflowed)
                                return Finish(j, width - hang, forced: false, hyphenated: false);

                            candidates.Add(new WrapCandidate(j, width - hang, false, 0));
                        }
                        else if (hyphenAfter[j - 1] && !overflowed)
                        {
                            candidates.Add(new WrapCandidate(j, width, true, HyphenWidth(glyphs[j - 1])));
                        }
                    }

                    var advance = gi.Advance;
                    var hangs = IsHangingSpace(gi);
                    if (!hangs && !overflowed && width + advance > available + 1e-6)
                    {
                        // The glyph does not fit: end the line at the last break that does (a hyphenated one needs room for its hyphen).
                        for (var c = candidates.Count - 1; c >= 0; c--)
                        {
                            if (candidates[c].Width + candidates[c].HyphenWidth <= available + 1e-6)
                                return Finish(candidates[c].Index, candidates[c].Width + candidates[c].HyphenWidth, forced: false, hyphenated: candidates[c].Hyphenated);
                        }

                        // No break fits: in a shape the line is looked for further down, where it may be wider; otherwise the word overflows.
                        if (!canOverflow)
                            return new WrapFill(start, 0, false, false, false);

                        overflowed = true;
                    }

                    width += advance;
                    hang = hangs ? hang + advance : 0;
                }

                return Finish(count, width - hang, forced: false, hyphenated: false);

                WrapFill Finish(int end, double content, bool forced, bool hyphenated)
                {
                    // Spaces at the end of the line collapse away (or hang): they take no part in the line's width or alignment.
                    for (var k = end - 1; k >= start && (IsHangingSpace(glyphs[k]) || IsLineFeed(glyphs[k])); k--)
                    {
                        glyphs[k].Advance = 0;

                        // Collapsible spaces are gone from the line altogether; preserved ones stay (invisible) in the text.
                        if (glyphs[k].Glyph == " " && glyphs[k].Run.WhiteSpace is not ("pre-wrap" or "pre" or "break-spaces"))
                            glyphs[k].Glyph = "";
                    }

                    if (hyphenated)
                    {
                        var last = glyphs[end - 1];
                        last.TrailingHyphen = true;
                        last.Advance += HyphenWidth(last);
                    }

                    return new WrapFill(end, content, forced, hyphenated, true);
                }
            }
        }

        private static bool IsLineFeed(GlyphInfo gi) => gi.Glyph == "\n";

        private static double LineHeightOf(GlyphInfo gi) => gi.Run.LineHeight ?? gi.Font.Height;

        /// <summary>How far a glyph's line box reaches above its baseline: the font's ascent plus half the leading its <c>line-height</c> adds.</summary>
        private static double LineAbove(GlyphInfo gi) => gi.Font.Ascent + (LineHeightOf(gi) - gi.Font.Height) / 2;

        /// <summary>How far a glyph's line box reaches below its baseline (the rest of its <c>line-height</c>).</summary>
        private static double LineBelow(GlyphInfo gi) => LineHeightOf(gi) - LineAbove(gi);

        /// <summary>
        /// The stretches, from left to right, of the line box between <paramref name="top"/> and <paramref name="bottom"/> that text can use: the
        /// <c>inline-size</c> box (it starts at the text's x and runs towards the end of the line, so leftwards for right-to-left text) or the inside of
        /// the <c>shape-inside</c>, less whatever <c>shape-subtract</c> covers.
        /// </summary>
        private static List<(double Left, double Right)> LineSpans(SvgTextElement text, bool rtl, double top, double bottom)
        {
            List<(double Left, double Right)> spans;
            if (text.ShapeInside is { } shape)
            {
                spans = SvgTextShapes.InteriorSpans(shape, top, bottom);
            }
            else
            {
                var size = text.InlineSize ?? 0;
                spans = [rtl ? (text.X - size, text.X) : (text.X, text.X + size)];
            }

            foreach (var cut in text.ShapeSubtract)
                spans = SvgTextShapes.Subtract(spans, SvgTextShapes.CoveredSpans(cut, top, bottom));

            spans.RemoveAll(s => s.Right - s.Left <= 0 && text.InlineSize is not 0);
            return spans;
        }

        /// <summary>
        /// The <c>text-align</c> the lines use: the one specified, else what <c>text-anchor</c> asks (<c>start</c>, <c>middle</c> as <c>center</c>,
        /// <c>end</c>), resolved to <c>left</c>, <c>right</c>, <c>center</c> or <c>justify</c> for the text's direction.
        /// </summary>
        private static string ResolveTextAlign(SvgTextElement text, bool rtl)
        {
            var align = text.TextAlign ?? text.TextAnchor switch
            {
                SvgTextAnchor.Middle => "center",
                SvgTextAnchor.End => "end",
                _ => "start",
            };

            return align switch
            {
                "start" or "match-parent" => rtl ? "right" : "left",
                "end" => rtl ? "left" : "right",
                _ => align,
            };
        }

        /// <summary>How far a line's content moves from the start of its box: <paramref name="free"/> is the room left over (negative when it overflows).</summary>
        private static double AlignmentOffset(string align, bool rtl, double free) => align switch
        {
            "right" => free,
            // Content that overflows is anchored at its start edge rather than split about the centre.
            "center" => free >= 0 ? free / 2 : rtl ? free : 0,
            _ => 0,
        };

        /// <summary>
        /// Marks the glyphs a line may end after by adding a hyphen: a soft hyphen when <c>hyphens</c> is <c>manual</c> or <c>auto</c>, and the
        /// points the hyphenation patterns find inside each word when it is <c>auto</c> and the text has a language (the HTML layout's own engine).
        /// </summary>
        private static bool[] FindHyphenationPoints(List<GlyphInfo> glyphs, string paragraphText, int[] starts)
        {
            var points = new bool[glyphs.Count];

            for (var i = 0; i < glyphs.Count; i++)
            {
                if (glyphs[i].Glyph == "­" && glyphs[i].Run.Hyphens != "none")
                    points[i] = true;
            }

            var i0 = 0;
            while (i0 < glyphs.Count)
            {
                if (glyphs[i0].Run.Hyphens != "auto" || string.IsNullOrEmpty(glyphs[i0].Run.ShapingFeatures.Language) || !IsWordGlyph(glyphs[i0]))
                {
                    i0++;
                    continue;
                }

                var end = i0;
                while (end < glyphs.Count && glyphs[end].Run.Hyphens == "auto" && IsWordGlyph(glyphs[end]))
                    end++;

                var word = paragraphText.Substring(starts[i0], starts[end] - starts[i0]);
                foreach (var point in Hyphenator.FindBreakPoints(word, glyphs[i0].Run.ShapingFeatures.Language!))
                {
                    // A point is the number of UTF-16 units before the break; the glyph that ends there is the one the hyphen follows.
                    for (var k = i0 + 1; k < end; k++)
                    {
                        if (starts[k] - starts[i0] == point)
                        {
                            points[k - 1] = true;
                            break;
                        }
                    }
                }

                i0 = end;
            }

            return points;
        }

        private static bool IsWordGlyph(GlyphInfo gi) =>
            gi.Glyph.Length > 0 && System.Text.Rune.DecodeFromUtf16(gi.Glyph, out var rune, out _) == System.Buffers.OperationStatus.Done && System.Text.Rune.IsLetter(rune);
    }
}

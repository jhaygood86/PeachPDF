using PeachBrackets.Css;
using PeachBrackets.Html.Annotations;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Text;
using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachBrackets.Html.TextGeometry
{
    /// <summary>
    /// Works out where the laid-out text of a document is, from its fragment tree: one <see cref="TextRun"/> per line of a block, positioned on
    /// its page in points, with the x position of every character. It reads the same words, rectangles and fonts the painter draws from and
    /// draws nothing, so what it reports is where the text lands and costs nothing unless it is asked for.
    /// </summary>
    internal static class TextRunCollector
    {
        /// <summary>
        /// Collects the text runs of a document that has been laid out, in reading order: page by page, in document order within a page.
        /// </summary>
        /// <param name="container">the laid-out document</param>
        /// <param name="fragmentainers">the pages, in order</param>
        public static IReadOnlyList<TextRun> Collect(HtmlContainerInt container, IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            var ppp = container.RenderContext.LayoutUnitsPerPoint;
            var runs = new List<TextRun>();
            var blockIds = new Dictionary<CssBox, int>(ReferenceEqualityComparer.Instance);
            var linkCache = new Dictionary<CssBox, string?>(ReferenceEqualityComparer.Instance);
            var lineIds = 0;

            for (var pageIndex = 0; pageIndex < fragmentainers.Count; pageIndex++)
            {
                var geometry = fragmentainers[pageIndex].Geometry;
                var frame = new Frame(geometry.ContentLeftPt, geometry.ContentTopPt, container.MarginLeft, container.MarginTop, ppp);

                var groups = new List<LineGroup>();
                var groupOf = new Dictionary<object, LineGroup>(ReferenceEqualityComparer.Instance);
                Walk(fragmentainers[pageIndex].Root, groups, groupOf);

                foreach (var group in groups)
                    BuildRuns(container, group, pageIndex, frame, blockIds, linkCache, ref lineIds, runs);
            }

            return runs;
        }

        private readonly record struct Frame(double ContentLeftPt, double ContentTopPt, double MarginLeft, double MarginTop, double Ppp)
        {
            public double X(double x) => ContentLeftPt + (x - MarginLeft) / Ppp;

            public double Y(double y) => ContentTopPt + (y - MarginTop) / Ppp;
        }

        private sealed class LineGroup(CssLineBox? line)
        {
            public CssLineBox? Line { get; } = line;

            public Dictionary<CssRect, (Rect Rect, CssBox Box)> Entries { get; } = new(ReferenceEqualityComparer.Instance);

            public List<CssRect> Order { get; } = [];
        }

        private static void Walk(BoxFragment fragment, List<LineGroup> groups, Dictionary<object, LineGroup> groupOf)
        {
            foreach (var textFragment in fragment.Words)
            {
                var word = textFragment.Word;
                if (!IsText(word, fragment.Box))
                    continue;

                object key = (object?)word.Line ?? word;
                if (!groupOf.TryGetValue(key, out var group))
                {
                    group = new LineGroup(word.Line);
                    groupOf[key] = group;
                    groups.Add(group);
                }

                if (group.Entries.TryAdd(word, (textFragment.Rect, fragment.Box)))
                    group.Order.Add(word);
            }

            foreach (var child in fragment.Children)
                Walk(child, groups, groupOf);
        }

        /// <summary>Whether <paramref name="word"/> is text a reader of the page sees: not a break, an image, a leader, or text that is not drawn.</summary>
        private static bool IsText(CssRect word, CssBox box)
        {
            if (word.IsLineBreak || word.IsImage || word is CssRectLeader)
                return false;

            if (string.IsNullOrEmpty(word.FirstLineText ?? word.Text))
                return false;

            // Vertical text has its own geometry, which the horizontal model here does not describe.
            if (box.WritingMode.Value is WritingMode.VerticalRl or WritingMode.VerticalLr)
                return false;

            return word.OwnerBox.Visibility.Value == Visibility.Visible;
        }

        private readonly record struct WordGeometry(
            string Text, double[] Starts, double End, int[] Boundaries, Rect Bounds, double Baseline, double Ascent, double Descent, bool Rtl, CssRect Word);

        private sealed class RunBuilder
        {
            public readonly StringBuilder Text = new();
            public readonly List<double> Offsets = [];
            public readonly List<int> Boundaries = [];
            public readonly List<TextWordSlice> Slices = [];
            private double _gapStart, _gapEnd;
            public double End;
            public double Left = double.MaxValue, Top = double.MaxValue, Right = double.MinValue, Bottom = double.MinValue;
            public double Baseline, Ascent, Descent;
            public bool Rtl, Any;
            public string? Link;
            public CssBox? Block;

            public bool EndsWithWhitespace => Text.Length > 0 && char.IsWhiteSpace(Text[Text.Length - 1]);

            public void Add(WordGeometry word)
            {
                var start = Text.Length;
                Text.Append(word.Text);

                var edges = new double[word.Starts.Length + 1];
                for (var i = 0; i < word.Starts.Length; i++)
                    edges[i] = word.Starts[i] - word.Bounds.Left;
                edges[^1] = word.End - word.Bounds.Left;

                // A separator space was just appended: it ends where this word's text begins.
                var hasGap = _gapEnd > _gapStart;
                Slices.Add(new TextWordSlice(word.Word, start, word.Text.Length, edges,
                    hasGap ? _gapStart - word.Bounds.Left : 0, hasGap ? _gapEnd - word.Bounds.Left : 0));
                _gapStart = _gapEnd = 0;
                Offsets.AddRange(word.Starts);
                foreach (var boundary in word.Boundaries)
                {
                    if (boundary < word.Text.Length)
                        Boundaries.Add(start + boundary);
                }

                End = word.End;
                Left = Math.Min(Left, word.Bounds.Left);
                Top = Math.Min(Top, word.Bounds.Top);
                Right = Math.Max(Right, word.Bounds.Right);
                Bottom = Math.Max(Bottom, word.Bounds.Bottom);
                if (!Any)
                {
                    Baseline = word.Baseline;
                    Any = true;
                }

                Ascent = Math.Max(Ascent, word.Ascent);
                Descent = Math.Max(Descent, word.Descent);
            }

            /// <summary>Appends the space that separates two words; it spans the gap between the end of the text so far and <paramref name="nextStart"/>.</summary>
            public void AddSeparator(double nextStart)
            {
                Boundaries.Add(Text.Length);
                Offsets.Add(End);
                Text.Append(' ');

                // The run covers the gap the space stands for.
                Left = Math.Min(Left, Math.Min(End, nextStart));
                Right = Math.Max(Right, Math.Max(End, nextStart));
                _gapStart = Math.Min(End, nextStart);
                _gapEnd = Math.Max(End, nextStart);
                End = nextStart;
            }
        }

        private static void BuildRuns(
            HtmlContainerInt container, LineGroup group, int pageIndex, Frame frame,
            Dictionary<CssBox, int> blockIds, Dictionary<CssBox, string?> linkCache, ref int lineIds, List<TextRun> runs)
        {
            // Logical order is the line's own; a word without a line is on its own.
            IEnumerable<CssRect> ordered = group.Line is { } line ? OrderedWords(line, group) : group.Order;

            var lineId = lineIds++;
            var followsForcedBreak = group.Line?.FollowsForcedBreak ?? false;
            var lineRtl = (group.Line?.OwnerBox ?? group.Entries[group.Order[0]].Box.ContainingBlock).Direction.Value == DirectionMode.Rtl;
            var current = new RunBuilder { Rtl = lineRtl };
            var firstOfLine = true;

            void Flush()
            {
                if (!current.Any)
                    return;

                var block = current.Block!;
                if (!blockIds.TryGetValue(block, out var blockId))
                {
                    blockId = blockIds.Count;
                    blockIds[block] = blockId;
                }

                current.Boundaries.Add(current.Text.Length);
                current.Offsets.Add(current.End);

                var bounds = new Rect(current.Left, current.Top, current.Right - current.Left, current.Bottom - current.Top);
                runs.Add(new TextRun(
                    pageIndex, current.Text.ToString(), bounds, current.Baseline, current.Ascent, current.Descent,
                    current.Offsets.ToArray(), current.Boundaries.ToArray(), current.Rtl, blockId,
                    block.HtmlTag?.TryGetAttribute("id") is { Length: > 0 } id ? id : null, current.Link,
                    lineId, followsForcedBreak && firstOfLine, current.Slices.ToArray()));
                firstOfLine = false;
                current = new RunBuilder { Rtl = lineRtl };
            }

            foreach (var word in ordered)
            {
                var (rect, box) = group.Entries[word];
                var geometry = Measure(word, box, rect, frame);
                var link = LinkOf(container, word.OwnerBox, linkCache);
                var wantsSeparator = word.PrecededByWordSeparator && current.Any;

                if (current.Any && !string.Equals(link, current.Link, StringComparison.Ordinal))
                {
                    // The space between a link and what follows it ends the run it follows.
                    if (word.PrecededByWordSeparator && !current.EndsWithWhitespace && !char.IsWhiteSpace(geometry.Text[0]))
                        current.AddSeparator(geometry.Starts[0]);

                    Flush();
                    wantsSeparator = false;
                }

                if (!current.Any)
                {
                    current.Link = link;
                    current.Block = word.OwnerBox.ContainingBlock;
                }

                if (wantsSeparator && !current.EndsWithWhitespace && !char.IsWhiteSpace(geometry.Text[0]))
                    current.AddSeparator(geometry.Starts[0]);

                current.Add(geometry);
            }

            Flush();
        }

        private static IEnumerable<CssRect> OrderedWords(CssLineBox line, LineGroup group)
        {
            foreach (var word in line.Words)
            {
                if (group.Entries.ContainsKey(word))
                    yield return word;
            }
        }

        private static string? LinkOf(HtmlContainerInt container, CssBox? box, Dictionary<CssBox, string?> cache)
        {
            for (var candidate = box; candidate is not null; candidate = candidate.ParentBox)
            {
                if (cache.TryGetValue(candidate, out var cached))
                    return cached;

                if (candidate.IsClickable && candidate.HtmlTag?.TryGetAttribute("href") is { Length: > 0 } href)
                {
                    var resolved = DocumentAnnotationCollector.ResolveHref(container, href);
                    cache[candidate] = resolved;
                    return resolved;
                }
            }

            return null;
        }

        /// <summary>
        /// Lays one word's characters across its rectangle: where each begins, in logical order, from how the font advances the glyphs the
        /// word was drawn with.
        /// </summary>
        private static WordGeometry Measure(CssRect word, CssBox box, Rect rect, Frame frame)
        {
            var styleSource = word.FirstLineStyle ?? box;
            var visual = word.FirstLineText ?? word.Text!;
            var rtl = (word.BidiLevel & 1) == 1;

            // A right-to-left word is stored in visual order, with paired punctuation mirrored; the source it came from is the logical text.
            var logical = word.FirstLineText is null && word is CssRectWord rectWord && rectWord.PreMirrorText.Length == visual.Length
                ? rectWord.PreMirrorText
                : rtl ? ReverseRunes(visual) : visual;

            var font = CssBox.ResolveWordFont(word, styleSource);
            var left = frame.X(rect.Left);
            var width = rect.Width / frame.Ppp;
            var spacing = styleSource.ActualLetterSpacing / frame.Ppp;
            var xs = Place(visual, logical, rtl, font, styleSource.ResolveWordShapingFeatures(word), left, width, spacing, out var boundaries);

            // DrawString anchors at the top of the font and the baseline lies the font's ascent below it, shifted by what lines a differently
            // sized fragment up with its neighbours and by a synthesized sub- or superscript.
            var baselineAdjust = styleSource.ActualFont.Ascent - font.Ascent;
            if (styleSource.SubSuperscriptSynthesis is { } subSuperscript)
                baselineAdjust += subSuperscript.BaselineShift;

            var baseline = frame.Y(rect.Top + baselineAdjust + font.TextBaselineOffset);
            var bounds = new Rect(left, frame.Y(rect.Top), width, rect.Height / frame.Ppp);

            return new WordGeometry(
                logical, xs[..^1], xs[^1], boundaries, bounds, baseline,
                font.TextBaselineOffset / frame.Ppp, Math.Max(0, font.Height - font.TextBaselineOffset) / frame.Ppp, rtl, word);
        }

        /// <summary>
        /// The x of the edge before each UTF-16 index of <paramref name="logical"/> and after its last (<c>length + 1</c> values), with the indices
        /// that are grapheme boundaries. The glyphs of <paramref name="visual"/> are shaped for their advances, which are scaled to fill
        /// <paramref name="width"/> exactly (absorbing what shaping alone does not know: justification, rounding).
        /// </summary>
        private static double[] Place(
            string visual, string logical, bool rtl, Font font, ShapeSettings features, double left, double width, double spacing, out int[] boundaries)
        {
            var length = logical.Length;
            boundaries = Segmenter.FindGraphemeBoundaries(logical);

            // The width of every UTF-16 unit of the visual text; a glyph's advance is shared out over the units of its cluster.
            var widths = new double[visual.Length];
            var typeface = font.Typeface;
            if (typeface is { } face && face.Metrics.UnitsPerEm > 0 && visual.Length == length)
            {
                var scale = font.Size / face.Metrics.UnitsPerEm;
                var glyphs = Shaper.Shape(face, visual, features).Glyphs;
                foreach (var glyph in glyphs)
                {
                    var advance = (face.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta) * scale + spacing;
                    if (glyph.ClusterLength <= 0)
                    {
                        // A glyph added after the first output of its source: it widens the character before the cluster it is anchored at.
                        widths[Math.Clamp(glyph.ClusterStart - 1, 0, widths.Length - 1)] += advance;
                        continue;
                    }

                    var first = Math.Clamp(glyph.ClusterStart, 0, widths.Length - 1);
                    var last = Math.Min(widths.Length, glyph.ClusterStart + glyph.ClusterLength);
                    var count = Math.Max(1, last - first);
                    for (var i = first; i < first + count && i < widths.Length; i++)
                        widths[i] += advance / count;
                }
            }

            var total = 0d;
            foreach (var unit in widths)
                total += unit;

            if (total <= 1e-9)
            {
                for (var i = 0; i < widths.Length; i++)
                    widths[i] = 1;
                total = widths.Length;
            }

            var factor = width > 0 ? width / total : 1;
            var cumulative = new double[widths.Length + 1];
            for (var i = 0; i < widths.Length; i++)
                cumulative[i + 1] = cumulative[i] + widths[i] * factor;

            // Only a grapheme boundary is a position; an index inside a cluster (or a surrogate pair) takes the edge before the cluster.
            var xs = new double[length + 1];
            var boundaryIndex = 0;
            var snapped = left;
            for (var j = 0; j <= length; j++)
            {
                while (boundaryIndex < boundaries.Length && boundaries[boundaryIndex] < j)
                    boundaryIndex++;

                var isBoundary = j == 0 || (boundaryIndex < boundaries.Length && boundaries[boundaryIndex] == j);
                if (isBoundary)
                    snapped = left + (rtl ? cumulative[length - j] : cumulative[j]);

                xs[j] = snapped;
            }

            return xs;
        }

        private static string ReverseRunes(string text)
        {
            var builder = new StringBuilder(text.Length);
            for (var i = text.Length; i > 0;)
            {
                var start = i - 1;
                if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1]))
                    start--;

                builder.Append(text, start, i - start);
                i = start;
            }

            return builder.ToString();
        }
    }
}

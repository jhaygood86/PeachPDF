using PeachPDF.CSS;
using PeachDrawing.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
using System.Text;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// The <c>text-overflow: ellipsis</c> half of the text painter: per-line truncation of whichever
    /// line(s) of a box actually overflow its content edge, in whatever writing mode/direction the box
    /// uses. See <see cref="FragmentPainter.PaintWords"/>'s own remarks for why this is a paint-time-only
    /// concern - layout already produces the (possibly overflowing) word rectangles this consumes.
    /// </summary>
    internal sealed partial class FragmentPainter
    {
        /// <summary>One word's kept substring and where to paint it, once a line's truncation point is found.</summary>
        private readonly record struct WordTruncation(string KeptText, Rect KeptRect, Size KeptSize, double EllipsisAnchor);

        /// <param name="g">the device to draw into</param>
        /// <param name="box">the box whose own words are being painted - the source of font/color/text style</param>
        /// <param name="containingBlock">
        /// <paramref name="box"/>'s <see cref="CssBox.ContainingBlock"/> - the source of the
        /// writing-mode/direction the line's content edge is resolved against, per
        /// <see cref="FragmentPainter.PaintWords"/>'s own remarks on why <c>text-overflow</c>/
        /// <c>overflow</c> apply to that box, not <paramref name="box"/> itself.
        /// </param>
        /// <param name="fragment"><paramref name="box"/>'s own fragment - the source of all geometry.</param>
        /// <remarks>
        /// Instance (not static): one <see cref="CssLineBox"/> is shared by every sibling box that
        /// contributes words to it (plain text next to a <c>&lt;b&gt;</c>/<c>&lt;span&gt;</c>/inline
        /// image run), and <see cref="FragmentPainter.PaintWords"/> is called once per <em>box</em>, not
        /// once per <em>line</em> - so a second sibling box whose own words all fall past an
        /// already-truncated line's cut point would otherwise independently rediscover "none of my own
        /// words fit" and draw a second, misplaced ellipsis. <see cref="_linesAlreadyTruncated"/> (scoped
        /// to this painter instance, which owns exactly one page's paint) is what makes "only the box
        /// that actually contains the cut point paints anything past it" hold across sibling boxes:
        /// earlier-in-paint-order boxes (which, barring a stacking-context reordering irrelevant to plain
        /// inline text, paint in the same left-to-right/top-to-bottom order the line itself reads in)
        /// either fit entirely (nothing recorded) or are the one that finds and paints the cut (recorded
        /// after); every later box then sees the line already recorded and paints nothing further on it.
        /// </remarks>
        private void PaintWordsWithEllipsis(Canvas g, CssBox box, CssBox containingBlock, BoxFragment fragment)
        {
            if (ResolveEllipsisGeometry(g, containingBlock, fragment) is not { } geometry)
            {
                // No clip to take the content edge from (MarginBoxContentFragmentBuilder, which builds a float:
                // footnote body's fragments, gives every one a null clip): nothing to truncate at.
                PaintWordSequence(g, box, fragment.Words);
                return;
            }

            var (isVertical, isRtl, boundary, lineStart) = geometry;
            var consumed = new HashSet<CssRect>(ReferenceEqualityComparer.Instance);

            foreach (var lineFragment in fragment.Lines)
            {
                var lineWords = VisualOrderWordsOf(lineFragment, box, fragment, isVertical, isRtl);
                if (lineWords.Count == 0) continue;

                foreach (var lw in lineWords) consumed.Add(lw.Word);

                var line = lineFragment.Line;
                if (line is not null && _linesAlreadyTruncated.Contains(line)) continue;

                var truncated = PaintLineWithEllipsis(g, box, lineWords, isVertical, isRtl, boundary, lineStart);
                if (truncated && line is not null) _linesAlreadyTruncated.Add(line);
            }

            // A word this fragment owns that no Lines entry claimed (atomic/replaced content outside the
            // normal line-box model) still paints via the ordinary path, unaffected by ellipsis.
            List<TextFragment>? leftover = null;
            foreach (var wf in fragment.Words)
            {
                if (consumed.Contains(wf.Word)) continue;
                (leftover ??= []).Add(wf);
            }
            if (leftover is { Count: > 0 }) PaintWordSequence(g, box, leftover);
        }

        /// <summary>The writing mode, direction and content-edge coordinates the ellipsis walk of one fragment's lines is resolved against.</summary>
        private readonly record struct EllipsisGeometry(bool IsVertical, bool IsRtl, double Boundary, double LineStart);

        /// <summary>
        /// One resolution shared by the word painter and the decoration's cut lookup, so the two cannot place
        /// the cut differently. Null when <paramref name="fragment"/> carries no clip, which is what a
        /// <c>float: footnote</c> body's words get (<c>MarginBoxContentFragmentBuilder</c> builds every one of its
        /// fragments with a null clip): with no content edge there is nothing to truncate at.
        /// </summary>
        private EllipsisGeometry? ResolveEllipsisGeometry(Canvas g, CssBox containingBlock, BoxFragment fragment)
        {
            if (OverflowClipOf(g, fragment).Clip is not { } paddingEdge) return null;

            var isVertical = containingBlock.WritingMode.Value is WritingMode.VerticalRl or WritingMode.VerticalLr;
            var isRtl = containingBlock.Direction.Value == DirectionMode.Rtl;
            // The end boundary is where the clip cuts the text, so it follows the clip when that is snapped;
            // the start boundary is where the (unsnapped) text begins, so it does not.
            var boundary = ResolveInlineEndBoundary(containingBlock, paddingEdge, isVertical, isRtl);
            var lineStart = ResolveInlineStartBoundary(containingBlock, fragment.OverflowClip ?? paddingEdge, isVertical, isRtl);
            return new EllipsisGeometry(isVertical, isRtl, boundary, lineStart);
        }

        /// <summary>
        /// Whether <paramref name="block"/> truncates its lines with an ellipsis - see
        /// <see cref="FragmentPainter.PaintWords"/>'s remarks on why <c>Overflow.Hidden</c> specifically.
        /// </summary>
        private static bool EllipsisActive(CssBox block) =>
            block.TextOverflow.Value == TextOverflow.Ellipsis && block.Overflow.Value == Overflow.Hidden;

        /// <summary>Where a <c>text-overflow: ellipsis</c> line ends: the end of everything it keeps, on the line's inline axis.</summary>
        /// <param name="Anchor">the coordinate the ellipsis begins at (LTR) or ends at (RTL)</param>
        /// <param name="IsVertical">whether <paramref name="Anchor"/> is a Y coordinate (a true vertical writing mode)</param>
        /// <param name="IsRtl">whether the line runs toward lower coordinates, so what is kept is the part past <paramref name="Anchor"/></param>
        private readonly record struct EllipsisCut(double Anchor, bool IsVertical, bool IsRtl);

        /// <summary>
        /// Where <paramref name="line"/> was truncated, or null when it was not (or is not under
        /// <c>text-overflow: ellipsis</c> at all). A text decoration ends here: css-text-decor-3 §2 decorates
        /// the inline content, and the truncated part - the ellipsis included - is not part of it.
        /// </summary>
        /// <remarks>
        /// Asked <i>before</i> the line's words are painted, because a decoration is painted with its box and
        /// ahead of the descendants whose words may hold the cut. So this re-derives the cut the word painter
        /// will make - from the same <see cref="PlanLineTruncation"/>, over every box that contributes words
        /// to the line in tree order, the first one that cuts winning exactly as
        /// <see cref="_linesAlreadyTruncated"/> makes it win at paint time - rather than waiting to read it
        /// back. Cached per line; the page's fragment tree is indexed once, on the first ask.
        /// </remarks>
        private EllipsisCut? EllipsisCutOf(Canvas g, CssLineBox? line)
        {
            var root = _pageRoot ?? _detachedRoot;
            if (line is null || root is null) return null;

            if (_ellipsisCuts.TryGetValue(line, out var known)) return known;

            _ellipsisLineFragments ??= IndexEllipsisLines(root);

            EllipsisCut? cut = null;

            if (_ellipsisLineFragments.TryGetValue(line, out var fragments))
            {
                foreach (var fragment in fragments)
                {
                    cut = CutOfFragmentOnLine(g, fragment, line);
                    if (cut is not null) break;
                }
            }

            _ellipsisCuts[line] = cut;
            return cut;
        }

        /// <summary>The cut <paramref name="fragment"/>'s own words make on <paramref name="line"/>, mirroring <see cref="PaintWordsWithEllipsis"/>.</summary>
        private EllipsisCut? CutOfFragmentOnLine(Canvas g, BoxFragment fragment, CssLineBox line)
        {
            var box = fragment.Box;
            if (box.Width is null or { Length: <= 0 }) return null;

            if (ResolveEllipsisGeometry(g, box.ContainingBlock, fragment) is not { } geometry) return null;

            var (isVertical, isRtl, boundary, lineStart) = geometry;

            foreach (var lineFragment in fragment.Lines)
            {
                if (!ReferenceEquals(lineFragment.Line, line)) continue;

                var lineWords = VisualOrderWordsOf(lineFragment, box, fragment, isVertical, isRtl);
                if (lineWords.Count == 0) continue;

                if (PlanLineTruncation(g, box, lineWords, isVertical, isRtl, boundary, lineStart) is { } plan)
                    return new EllipsisCut(plan.EllipsisAnchor, isVertical, isRtl);
            }

            return null;
        }

        /// <summary>
        /// Every fragment under <paramref name="root"/> that paints words through
        /// <see cref="PaintWordsWithEllipsis"/>, grouped by the line box it sits on, in tree order.
        /// </summary>
        private static Dictionary<CssLineBox, List<BoxFragment>> IndexEllipsisLines(BoxFragment root)
        {
            var index = new Dictionary<CssLineBox, List<BoxFragment>>(ReferenceEqualityComparer.Instance);
            IndexEllipsisLines(root, index);
            return index;
        }

        private static void IndexEllipsisLines(BoxFragment fragment, Dictionary<CssLineBox, List<BoxFragment>> index)
        {
            // A box PaintFragment skips (display: none, visibility other than visible) never reaches the word
            // painter, so it never claims a line's cut there either.
            if (fragment.Words.Count > 0 && fragment.OverflowClip is not null
                && fragment.Box.DerivedStyle.ActualDisplay != Keywords.None
                && fragment.Box.Visibility.Value == Visibility.Visible
                && fragment.Box.ContainingBlock is { } containingBlock && EllipsisActive(containingBlock))
            {
                foreach (var lineFragment in fragment.Lines)
                {
                    if (lineFragment.Line is not { } line) continue;

                    if (!index.TryGetValue(line, out var fragments)) index.Add(line, fragments = []);
                    // Reference identity: BoxFragment is a record, whose Contains would compare structurally.
                    if (fragments.Count == 0 || !ReferenceEquals(fragments[^1], fragment)) fragments.Add(fragment);
                }
            }

            foreach (var child in fragment.Children) IndexEllipsisLines(child, index);
        }

        /// <summary>
        /// The single content-edge coordinate a line's content must not cross - the inline-end edge of
        /// <paramref name="containingBlock"/>'s own content box, for its own writing mode/direction
        /// (<c>LogicalPropertyResolver.InlineStart</c>'s own mapping): physical right for
        /// horizontal-LTR, physical left for horizontal-RTL, physical bottom for
        /// vertical-rl/vertical-lr under LTR direction, physical top under RTL direction - they differ
        /// only in which side lines stack toward, never in their own top-to-bottom inline axis.
        /// <paramref name="paddingEdge"/> is <see cref="Fragments.BoxFragment.OverflowClip"/> - already exactly
        /// <paramref name="containingBlock"/>'s own padding-edge rectangle, fragment-local
        /// (<see cref="FragmentPainter.PaintWords"/>'s own remarks), snapped when the box's decorations are -
        /// so only the further padding inset (padding edge to content edge) is computed here.
        /// </summary>
        private static double ResolveInlineEndBoundary(CssBox containingBlock, Rect paddingEdge, bool isVertical, bool isRtl)
        {
            if (!isVertical)
                return isRtl ? paddingEdge.Left + containingBlock.ActualPaddingLeft : paddingEdge.Right - containingBlock.ActualPaddingRight;
            return isRtl ? paddingEdge.Top + containingBlock.ActualPaddingTop : paddingEdge.Bottom - containingBlock.ActualPaddingBottom;
        }

        /// <summary>The content-edge coordinate a line's content naturally starts from - the mirror of <see cref="ResolveInlineEndBoundary"/>, used only to anchor an ellipsis that replaces a line's very first word.</summary>
        private static double ResolveInlineStartBoundary(CssBox containingBlock, Rect paddingEdge, bool isVertical, bool isRtl)
        {
            if (!isVertical)
                return isRtl ? paddingEdge.Right - containingBlock.ActualPaddingRight : paddingEdge.Left + containingBlock.ActualPaddingLeft;
            return isRtl ? paddingEdge.Bottom - containingBlock.ActualPaddingBottom : paddingEdge.Top + containingBlock.ActualPaddingTop;
        }

        /// <summary>
        /// One line's own words belonging to <paramref name="box"/>, in true visual (start-to-end) order
        /// - never document/list order, which only matches visual order for a single-direction line.
        /// Bidi reordering repositions each word's physical <c>Left</c>/<c>Top</c> without ever permuting
        /// <see cref="CssLineBox.Words"/>' own list order (<c>CssLayoutEngine.ApplyBidiReordering</c>/
        /// <c>ApplyVerticalBidiReordering</c>), so a mixed-direction line (e.g. a Latin/digit run inside
        /// an RTL paragraph) needs a real physical-position sort here, not a list-order assumption.
        /// </summary>
        private static List<TextFragment> VisualOrderWordsOf(LineFragment lineFragment, CssBox box, BoxFragment fragment, bool isVertical, bool isRtl)
        {
            var result = new List<TextFragment>();
            if (lineFragment.Line is not { } line) return result;

            foreach (var word in line.WordsOf(box))
            {
                if (fragment.TryGetWordRect(word, out var rect))
                {
                    result.Add(new TextFragment(rect, word));
                }
            }

            if (!isVertical)
            {
                result.Sort(isRtl
                    ? (a, b) => b.Rect.X.CompareTo(a.Rect.X)
                    : (a, b) => a.Rect.X.CompareTo(b.Rect.X));
            }
            else
            {
                result.Sort(isRtl
                    ? (a, b) => b.Rect.Y.CompareTo(a.Rect.Y)
                    : (a, b) => a.Rect.Y.CompareTo(b.Rect.Y));
            }

            return result;
        }

        /// <summary>A word's own edge furthest along the line's start-to-end walk - its end coordinate (right/bottom) walking forward (LTR), its start coordinate (left/top) walking in reverse (RTL).</summary>
        private static double LeadingEdge(Rect rect, bool isVertical, bool isRtl)
        {
            if (!isVertical) return isRtl ? rect.Left : rect.Right;
            return isRtl ? rect.Top : rect.Bottom;
        }

        /// <summary>A word's own edge nearest the line's start - the mirror of <see cref="LeadingEdge"/>, and the anchor a truncated word's own kept run grows from.</summary>
        private static double TrailingEdge(Rect rect, bool isVertical, bool isRtl)
        {
            if (!isVertical) return isRtl ? rect.Right : rect.Left;
            return isRtl ? rect.Bottom : rect.Top;
        }

        /// <summary>Distance measured along the line's start-to-end walk direction - increasing for LTR, decreasing (negated) for RTL - so "does it still fit before the boundary" is one comparison for all four writing-mode/direction combinations.</summary>
        private static double Forward(double coordinate, bool isRtl) => isRtl ? -coordinate : coordinate;

        /// <returns>
        /// Whether this call actually painted a truncation (kept run + ellipsis) for the line - false
        /// when the line's content, restricted to <paramref name="lineWords"/>, fit without needing one.
        /// The caller uses this to record the line as truncated so a later sibling box sharing it paints
        /// nothing further (see <see cref="PaintWordsWithEllipsis"/>'s own remarks).
        /// </returns>
        // Not static only because PaintWordSequence stopped being static: it now records words that
        // were drawn and then clipped, which needs the container. The words this path hands it are
        // the line's SURVIVING whole words, painted normally, so recording them is right. The word
        // this path truncates itself is drawn elsewhere (FitTruncatedWord/DrawEllipsis) and is
        // deliberately NOT recorded - `text-overflow: ellipsis` is truncation the author asked for
        // and the reader can see, which is the opposite of the silent loss the report exists for.
        private bool PaintLineWithEllipsis(Canvas g, CssBox box, List<TextFragment> lineWords, bool isVertical, bool isRtl, double boundary, double lineStart)
        {
            if (PlanLineTruncation(g, box, lineWords, isVertical, isRtl, boundary, lineStart) is not { } plan)
            {
                PaintWordSequence(g, box, lineWords);
                return false;
            }

            var wf = lineWords[plan.WordIndex];
            if (plan.WordIndex > 0) PaintWordSequence(g, box, lineWords.GetRange(0, plan.WordIndex));

            if (plan.Truncation is { } c)
            {
                // Same visibility-clip check PaintWordSequence applies to every ordinary word
                // (issue #113: a box relocated to the next page's content top can leave a
                // near-zero-but-not-quite-empty clip intersection on the page it left) - the cut
                // word's kept run and the ellipsis glyph go through DrawWordGlyphs directly, not
                // PaintWordSequence, so they need their own copy of the same guard.
                if (c.KeptText.Length > 0 && IsVisible(g, c.KeptRect)) DrawWordGlyphs(g, box, wf.Word, c.KeptRect, c.KeptText, c.KeptSize,
                    precededBySeparator: wf.Word.PrecededByWordSeparator, separatorEdge: PrecedingWordEdge(g, wf.Word, c.KeptRect));
            }

            DrawEllipsis(g, box, wf.Word, isVertical, isRtl, plan.EllipsisAnchor, wf.Rect);
            return true;
        }

        /// <summary>
        /// Where one box's words on one line get cut: <paramref name="WordIndex"/> is the first word that
        /// does not fit whole; <paramref name="Truncation"/> is its kept run when part of it fits, and null
        /// when it (and everything after it) is dropped whole; <paramref name="EllipsisAnchor"/> is the
        /// coordinate the ellipsis starts from (LTR) or ends at (RTL) - the end of everything kept.
        /// </summary>
        private readonly record struct LinePlan(int WordIndex, WordTruncation? Truncation, double EllipsisAnchor);

        /// <returns>
        /// The cut for the line, or null when its content - restricted to <paramref name="lineWords"/> -
        /// fits without needing one. Decides only; painting the plan is <see cref="PaintLineWithEllipsis"/>'s
        /// job, and the decoration painter asks the same question (<see cref="EllipsisCutOf"/>) so that a
        /// text decoration ends exactly where the kept text does.
        /// </returns>
        private static LinePlan? PlanLineTruncation(Canvas g, CssBox box, List<TextFragment> lineWords, bool isVertical, bool isRtl, double boundary, double lineStart)
        {
            var lastLeading = LeadingEdge(lineWords[^1].Rect, isVertical, isRtl);
            if (!(Forward(lastLeading, isRtl) > Forward(boundary, isRtl)))
            {
                // The line's raw (untruncated) content already fits - nothing to do. Checked without any
                // ellipsis-room reservation: text-overflow only takes effect when content genuinely
                // overflows, not merely because there'd be less room left over than an ellipsis needs.
                return null;
            }

            var ellipsisReserve = ApproximateEllipsisExtent(g, box, isVertical);
            var boundaryF = Forward(boundary, isRtl);

            for (var i = 0; i < lineWords.Count; i++)
            {
                var wf = lineWords[i];
                var leadingF = Forward(LeadingEdge(wf.Rect, isVertical, isRtl), isRtl);

                if (leadingF + ellipsisReserve <= boundaryF)
                {
                    // This word, whole, still leaves room for a trailing ellipsis - keep it and move on.
                    // (The line was already found to overflow above, so this loop is guaranteed to find
                    // its cut point at or before the last word - it never runs off the end.)
                    continue;
                }

                var isAtomic = wf.Word.IsImage || wf.Word is CssRectLeader
                               || (isVertical && !ResolveIsUpright(box, wf.Word));

                if (!isAtomic && FitTruncatedWord(g, box, wf, isVertical, isRtl, boundaryF) is { } cut)
                    return new LinePlan(i, cut, cut.EllipsisAnchor);

                // Atomic content (image/leader/sideways-rotated vertical run), or a word with no room
                // even for a single character: drop this word (and everything after it in visual order)
                // whole, and place the ellipsis right after the last word actually kept.
                // The first word of THIS box is not necessarily the line's first: a sibling before it may have
                // been kept, in which case the line does not start at the content edge but where this word does.
                var dropAnchor = i > 0
                    ? LeadingEdge(lineWords[i - 1].Rect, isVertical, isRtl)
                    : Forward(lineStart, isRtl) >= Forward(TrailingEdge(wf.Rect, isVertical, isRtl), isRtl)
                        ? lineStart
                        : TrailingEdge(wf.Rect, isVertical, isRtl);
                return new LinePlan(i, null, dropAnchor);
            }

            return null;
        }

        /// <summary>Mirrors <c>PaintWordSequence</c>'s own issue-#113 visibility check for a draw call that bypasses it (a truncation's kept run/ellipsis glyph, drawn directly through <see cref="FragmentPainter.DrawWordGlyphs"/>).</summary>
        private static bool IsVisible(Canvas g, Rect rect)
        {
            var clip = g.GetClip();
            clip.Intersect(rect);
            return clip.Width > VisibilityClipEpsilon && clip.Height > VisibilityClipEpsilon;
        }

        /// <summary>
        /// Finds the longest run of characters this word can keep - taken from the start of its own text
        /// walking forward (LTR: a prefix), or from the end walking in reverse (RTL: a suffix, since a
        /// word's text is already stored in physical left-to-right/top-to-bottom paint order after bidi
        /// mirroring/reordering, so "closest to the line's start" is the tail of the string, not the
        /// head) - while still leaving room for a trailing ellipsis. Returns null if not even one
        /// character fits, so the caller falls back to dropping the whole word.
        /// </summary>
        private static WordTruncation? FitTruncatedWord(Canvas g, CssBox box, TextFragment wf, bool isVertical, bool isRtl, double boundaryF)
        {
            var word = wf.Word;
            if (word.IsLineBreak) return null;

            var text = word.FirstLineText ?? word.Text!;
            if (text.Length == 0) return null;

            var styleSource = word.FirstLineStyle ?? box;
            var font = CssBox.ResolveWordFont(word, styleSource);
            var isUpright = isVertical && ResolveIsUpright(box, word);
            var ellipsisFont = ResolveEllipsisFont(styleSource, word.FontSizeScale);
            var ellipsisExtent = MeasureRunExtent(g, "…", ellipsisFont, styleSource, isUpright);

            // The word's own natural anchor - where the kept run's growth starts from - never moves when
            // it's shortened; only how much of it is drawn does.
            var anchorF = Forward(TrailingEdge(wf.Rect, isVertical, isRtl), isRtl);

            // Rune (not raw char) boundaries - growing the candidate one char at a time can land exactly
            // inside a surrogate pair (an emoji, an astral-plane CJK Extension ideograph), producing a
            // malformed lone-surrogate candidate that both measures and (if it survives as the final
            // KeptText) paints as a replacement-glyph instead of cutting cleanly between codepoints.
            var runeLengths = new List<int>();
            foreach (var rune in text.EnumerateRunes()) runeLengths.Add(rune.Utf16SequenceLength);
            if (isRtl) runeLengths.Reverse(); // grow the candidate from the end of the string inward

            var cumulative = new List<int>(runeLengths.Count);
            var acc = 0;
            foreach (var runeLength in runeLengths) { acc += runeLength; cumulative.Add(acc); }

            // Binary search rather than growing one rune at a time: a real shaped measurement
            // (g.MeasureString, GSUB/GPOS) is not O(1), so remeasuring every growing prefix/suffix from
            // scratch is O(runeCount) calls each doing O(charsSoFar) shaping work - quadratic in the
            // word's own length. Longer text is monotonically no narrower than a leading/trailing
            // sub-run of it under any real font's shaping (ligatures narrow a multi-glyph sequence
            // relative to summing its glyphs separately, never relative to a strict prefix/suffix of the
            // same run), so bisecting the candidate rune count is safe and cuts this to O(log runeCount)
            // measurements.
            var lo = 0;
            var hi = cumulative.Count - 1;
            var kept = 0;
            var keptExtent = 0.0;
            while (lo <= hi)
            {
                var mid = lo + (hi - lo) / 2;
                var n = cumulative[mid];
                var candidate = isRtl ? text[^n..] : text[..n];
                var extent = MeasureRunExtent(g, candidate, font, styleSource, isUpright);
                if (anchorF + extent + ellipsisExtent <= boundaryF)
                {
                    kept = n;
                    keptExtent = extent;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            if (kept == 0) return null;

            var keptText = isRtl ? text[^kept..] : text[..kept];
            var (keptRect, ellipsisAnchor) = PlaceTruncatedRun(wf.Rect, isVertical, isRtl, keptExtent);
            var keptSize = isVertical ? new Size(wf.Rect.Width, keptExtent) : new Size(keptExtent, wf.Rect.Height);

            return new WordTruncation(keptText, keptRect, keptSize, ellipsisAnchor);
        }

        /// <summary>
        /// Where a truncated word's kept run belongs, and the physical coordinate immediately past it
        /// (where the ellipsis goes) - the kept run's own natural anchor edge (its start for a kept
        /// prefix, its end for a kept suffix) is unchanged from the original word's rect; only its
        /// far edge moves in by however much was dropped.
        /// </summary>
        private static (Rect KeptRect, double EllipsisAnchor) PlaceTruncatedRun(Rect original, bool isVertical, bool isRtl, double keptExtent)
        {
            if (!isVertical)
            {
                if (!isRtl)
                {
                    return (new Rect(original.X, original.Y, keptExtent, original.Height), original.X + keptExtent);
                }

                var startX = original.Right - keptExtent;
                return (new Rect(startX, original.Y, keptExtent, original.Height), startX);
            }

            if (!isRtl)
            {
                return (new Rect(original.X, original.Y, original.Width, keptExtent), original.Y + keptExtent);
            }

            var startY = original.Bottom - keptExtent;
            return (new Rect(original.X, startY, original.Width, keptExtent), startY);
        }

        /// <summary>
        /// Draws the ellipsis glyph, oriented/styled to match <paramref name="referenceWord"/> (the word
        /// it's cutting, or the last word kept before an atomically-dropped one) - <paramref name="anchor"/>
        /// is where the ellipsis starts (LTR) or ends (RTL, since it sits before - at lower coordinate
        /// than - whatever it follows in the walk direction).
        /// </summary>
        private static void DrawEllipsis(Canvas g, CssBox box, CssRect referenceWord, bool isVertical, bool isRtl, double anchor, Rect referenceRect)
        {
            var styleSource = referenceWord.FirstLineStyle ?? box;
            var isUpright = isVertical && ResolveIsUpright(box, referenceWord);
            var ellipsisFont = ResolveEllipsisFont(styleSource, referenceWord.FontSizeScale);
            var extent = MeasureRunExtent(g, "…", ellipsisFont, styleSource, isUpright);

            Rect rect;
            if (!isVertical)
            {
                var startX = isRtl ? anchor - extent : anchor;
                rect = new Rect(startX, referenceRect.Y, extent, referenceRect.Height);
            }
            else
            {
                var startY = isRtl ? anchor - extent : anchor;
                rect = new Rect(referenceRect.X, startY, referenceRect.Width, extent);
            }

            if (IsVisible(g, rect)) DrawWordGlyphs(g, box, referenceWord, rect, "…", new Size(extent, referenceRect.Height), ellipsisFont);
        }

        /// <summary>
        /// The font that actually covers U+2026 (HORIZONTAL ELLIPSIS) per <paramref name="styleSource"/>'s
        /// own authored <c>font-family</c> fallback stack - never assumed to be whatever font a
        /// neighboring word's own (possibly narrower, script-specific) codepoints happened to resolve to.
        /// </summary>
        private static Font ResolveEllipsisFont(CssBox styleSource, double fontSizeScale)
        {
            Rune.DecodeFromUtf16("…", out var rune, out _);
            return styleSource.ActualFontForCodepoint(rune, fontSizeScale);
        }

        /// <summary>Whether <paramref name="word"/> paints upright (vs. sideways-rotated) under this box's <c>text-orientation</c> - only meaningful when the box is vertical-writing-mode; mirrors the same decision <see cref="FragmentPainter.DrawWordGlyphs"/> makes for ordinary painting.</summary>
        private static bool ResolveIsUpright(CssBox box, CssRect word) => box.TextOrientation.Value switch
        {
            TextOrientation.Upright => true,
            TextOrientation.Sideways => false,
            _ => word.IsUprightOrientation
        };

        /// <summary>
        /// A run's extent along the inline (walk) axis: the ordinary horizontal glyph width for
        /// horizontal text or a sideways-rotated vertical run (its natural pre-rotation width becomes its
        /// along-column extent, exactly as <see cref="FragmentPainter.DrawWordGlyphs"/>'s own rotated
        /// branch already relies on), or the summed vertical advance for an upright vertical run -
        /// mirroring <see cref="FragmentPainter.PaintUprightVerticalRun"/>'s own per-character metric
        /// choice.
        /// </summary>
        private static double MeasureRunExtent(Canvas g, string text, Font font, CssBox styleSource, bool isVerticalUpright)
        {
            if (!isVerticalUpright)
            {
                var width = g.MeasureString(text, font, styleSource.ActualTextShapingFeatures).Width;
                // MeasureString never includes letter-spacing (see CssBox.MeasureWordsSize's identical
                // fix/comment: N gaps for an N-glyph word, via the shaped glyph count, not the raw char
                // count) - omitting this systematically undercounts a growing candidate's true width
                // under letter-spacing, keeping more characters than actually fit and letting the kept
                // run spill past the clip edge text-overflow is supposed to stay inside.
                if (styleSource.ActualLetterSpacing != 0)
                    width += g.CountShapedGlyphs(text, font, styleSource.ActualTextShapingFeatures) * styleSource.ActualLetterSpacing;
                return width;
            }

            double total = 0;
            foreach (var (_, rune, _) in CssLayoutEngine.MeasureUprightRunCharacters(g, text, font, styleSource.ActualTextShapingFeatures))
            {
                total += font.HasVerticalMetrics ? font.GetVerticalAdvance(rune) : font.Height;
                total += styleSource.ActualLetterSpacing;
            }
            return total;
        }

        /// <summary>
        /// A coarse, box-wide (not per-word-font-exact) ellipsis extent used only to decide, while
        /// walking a line's words, whether each whole word still leaves room for a trailing ellipsis.
        /// The exact cut point's own final positioning re-measures precisely against the actual cut
        /// word's own resolved font/orientation (see <see cref="FitTruncatedWord"/>/<see cref="DrawEllipsis"/>)
        /// - this approximation only risks keeping one whole word more or fewer than ideal, by a margin
        /// no larger than the difference between the box's own font and an unusually differently-sized
        /// nested run's, which is immaterial next to the walk it's gating.
        /// </summary>
        private static double ApproximateEllipsisExtent(Canvas g, CssBox box, bool isVertical)
        {
            var font = ResolveEllipsisFont(box, 1.0);
            if (!isVertical) return g.MeasureString("…", font, box.ActualTextShapingFeatures).Width;
            return font.HasVerticalMetrics ? font.GetVerticalAdvance(new Rune('…')) : font.Height;
        }
    }
}

using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// One laid-out word a decoration line passes over, as
    /// <see cref="DecorationContent.InkWordsOn"/> hands it to <c>text-decoration-skip-ink</c>.
    /// </summary>
    /// <param name="Rect">the word's rectangle, in fragmentainer-local coordinates</param>
    /// <param name="Word">the word itself — the source of its text and per-word shaping facts</param>
    /// <param name="Owner">the box whose style the word is drawn with</param>
    internal readonly record struct DecorationWord(RRect Rect, CssRect Word, CssBox Owner);

    /// <summary>
    /// What a text decoration line needs to know about the content it covers: the area to draw over, the
    /// atomic inlines to break around, and the words whose ink it may have to skip — all gathered in one
    /// walk of the fragment subtree, per line box.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The content comes from the <b>fragment tree</b>, not the box tree, so a box broken across pages
    /// decorates exactly the lines that landed on the page being painted, with no coordinate mapping —
    /// fragment rectangles are already fragmentainer-local.
    /// </para>
    /// <para>
    /// Line membership is read back off <see cref="CssLineBox.Words"/> rather than recorded during the
    /// walk, because a <see cref="CssRect"/> does not know which line box holds it. Only words the walk
    /// actually reached are returned, so a line shared with content outside the decorated subtree
    /// contributes just the part that belongs to it.
    /// </para>
    /// </remarks>
    internal sealed class DecorationContent
    {
        private readonly Dictionary<CssLineBox, RRect> _spans = [];
        private readonly Dictionary<CssLineBox, List<DecorationInterval>> _exclusions = [];
        private readonly Dictionary<CssRect, DecorationWord>? _words;
        private readonly List<CssLineBox> _order = [];

        private DecorationContent(bool collectWords)
        {
            _words = collectWords ? [] : null;
        }

        /// <summary>
        /// The line boxes that carry a span, in the order they were first reached, so painting is
        /// deterministic. Empty when <see cref="Of"/> was asked for exclusions and ink only.
        /// </summary>
        internal IReadOnlyList<CssLineBox> Order => _order;

        /// <summary>
        /// Walks <paramref name="fragment"/>'s children and collects what a decoration on
        /// <paramref name="fragment"/>'s own box needs.
        /// </summary>
        /// <param name="fragment">the decorating box's fragment</param>
        /// <param name="collectSpans">
        /// whether the walk also unions each line's content into a span. True for a block container,
        /// whose own single rectangle is its border box and so cannot be the decoration area
        /// (css-text-decor-3 §2.4). False for an inline box, whose own per-line rectangles already
        /// <i>are</i> the spans — there, only the exclusions and the ink have to be found.
        /// </param>
        /// <param name="collectWords">
        /// whether the walk also gathers the words <c>text-decoration-skip-ink</c> measures ink across.
        /// False for a box that has opted out (or cannot skip), so the opt-out costs nothing beyond the
        /// walk the exclusions need anyway.
        /// </param>
        internal static DecorationContent Of(BoxFragment fragment, bool collectSpans, bool collectWords)
        {
            var content = new DecorationContent(collectWords);

            foreach (var child in fragment.Children)
            {
                content.Collect(child, collectSpans);
            }

            // An inline box's own words sit on its own fragment rather than a child's, and they are as
            // much "the content the line covers" as a descendant's are.
            content.CollectWords(fragment);

            return content;
        }

        /// <summary>The union rectangle collected for <paramref name="line"/>.</summary>
        internal RRect SpanOf(CssLineBox line) => _spans[line];

        /// <summary>
        /// The x-ranges on <paramref name="line"/> that no decoration line may cross — the margin boxes
        /// of the atomic inlines on it. Null when there are none, which is the common case.
        /// </summary>
        internal IReadOnlyList<DecorationInterval>? ExclusionsFor(CssLineBox line) =>
            _exclusions.GetValueOrDefault(line);

        /// <summary>
        /// The words of <paramref name="line"/> that this walk reached, in line order — what
        /// <c>text-decoration-skip-ink</c> measures ink across. Null when the line holds no word this
        /// walk collected, so the caller can skip the whole measurement.
        /// </summary>
        internal List<DecorationWord>? InkWordsOn(CssLineBox? line)
        {
            if (line is null || _words is not { Count: > 0 }) return null;

            List<DecorationWord>? words = null;

            foreach (var word in line.Words)
            {
                if (_words.TryGetValue(word, out var placed))
                {
                    (words ??= []).Add(placed);
                }
            }

            return words;
        }

        /// <summary>
        /// Accumulates one fragment subtree.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>An atomic inline ends the walk down that branch and contributes no span</b>, only an
        /// exclusion: css-text-decor-3 §2.4 does not decorate it, and its contents live in an independent
        /// formatting context, so a nested image inside an <c>inline-block</c> is already covered by
        /// excluding the <c>inline-block</c> itself. Its own text is not ink the line has to skip either,
        /// for the same reason — the line never reaches it.
        /// </para>
        /// <para>
        /// <b>A line-hosted box contributes its rectangles but the walk continues past it</b>, with
        /// <paramref name="contributesSpan"/> cleared. Its rectangle already covers its content, including
        /// its own padding and border, which §2.4 requires be decorated (unlike the decorating box's own),
        /// so no descendant may widen the span a second time — but an atomic inline nested inside it, as in
        /// <c>&lt;span&gt;xx &lt;img&gt; yy&lt;/span&gt;</c>, is still one the line has to break around,
        /// and ending the walk here is what used to hide it. Anything not line-hosted is a block-level box
        /// whose own rectangle is its border box again, and is descended into still contributing.
        /// </para>
        /// <para>
        /// <b>Out-of-flow descendants are skipped outright</b>, per the same section.
        /// </para>
        /// </remarks>
        private void Collect(BoxFragment fragment, bool contributesSpan)
        {
            if (fragment.Box.IsOutOfFlow) return;

            if (DomUtils.IsAtomicInline(fragment.Box))
            {
                RecordExclusions(fragment);
                return;
            }

            var hosted = false;

            foreach (var lineFragment in fragment.Lines)
            {
                if (lineFragment.Line is not { } lineBox) continue;

                hosted = true;

                if (!contributesSpan) continue;

                if (_spans.TryGetValue(lineBox, out var existing))
                {
                    _spans[lineBox] = RRect.Union(existing, lineFragment.Rect);
                }
                else
                {
                    _spans.Add(lineBox, lineFragment.Rect);
                    _order.Add(lineBox);
                }
            }

            CollectWords(fragment);

            foreach (var child in fragment.Children)
            {
                Collect(child, contributesSpan && !hosted);
            }
        }

        private void CollectWords(BoxFragment fragment)
        {
            if (_words is null) return;

            foreach (var wordFragment in fragment.Words)
            {
                var word = wordFragment.Word;

                // Only real glyph runs carry ink to skip. A line break has no geometry, and an image or
                // a leader is not text the ink scanner can shape - a leader in particular is a tiled
                // pattern whose painted extent is not its word's own string.
                if (word.IsLineBreak || word.IsImage || word is CssRectLeader) continue;

                _words[word] = new DecorationWord(wordFragment.Rect, word, fragment.Box);
            }
        }

        /// <summary>
        /// Records <paramref name="fragment"/>'s margin box, per line box it sits on, as a range no
        /// decoration line may cross. Every rectangle layout records is a border box, so the margins are
        /// added back here — §2.4 says only that an atomic inline is not decorated, not which of its boxes
        /// bounds the gap, so taking the margin box is this engine's own choice.
        /// </summary>
        private void RecordExclusions(BoxFragment fragment)
        {
            var box = fragment.Box;

            foreach (var lineFragment in fragment.Lines)
            {
                if (lineFragment.Line is not { } lineBox) continue;

                var rect = lineFragment.Rect;
                var interval = new DecorationInterval(
                    rect.Left - box.ActualMarginLeft, rect.Right + box.ActualMarginRight);

                if (_exclusions.TryGetValue(lineBox, out var list))
                {
                    list.Add(interval);
                }
                else
                {
                    _exclusions.Add(lineBox, [interval]);
                }
            }
        }
    }
}

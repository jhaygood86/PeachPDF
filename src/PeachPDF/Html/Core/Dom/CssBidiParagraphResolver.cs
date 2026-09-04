using PeachPDF.CSS;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Text.Bidi;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Resolves UAX#9 embedding levels for every paragraph (inline formatting context) in the box tree,
    /// before <see cref="CssBox.ParseToWords"/> runs on any of it, so word-splitting can consult the
    /// result (see <see cref="CssBox.BidiLevels"/>). Must run after <c>DomParser.CascadeApplyStyles</c>
    /// (it reads each box's resolved <c>Direction</c>/<c>UnicodeBidi</c>) and before
    /// <c>DomParser.CorrectTextBoxes</c> (the first place that calls <see cref="CssBox.ParseToWords"/>).
    /// <para>
    /// A "paragraph" is rooted at any box that is not a plain (non-replaced) <c>display: inline</c> box -
    /// a block, an inline-block/inline-table/inline-flex/inline-grid, a float, an absolutely/fixed
    /// positioned box, a replaced element (<see cref="CssBoxImage"/>/<see cref="CssBoxSvg"/>), a
    /// <see cref="CssBoxMarker"/>, or the tree root. Each one's own inline-descendant text is flattened
    /// into one logical-order string (recursing only through plain inline boxes - anything else is
    /// atomic from the surrounding paragraph's point of view and represented by one Object Replacement
    /// Character placeholder, matching UAX#9's own guidance for embedded objects), resolved once via
    /// <see cref="BidiResolver.Resolve"/>, and the resulting per-character levels are sliced back onto
    /// each contributing text box.
    /// </para>
    /// <para>
    /// A plain inline box (e.g. a &lt;span&gt;/&lt;bdo&gt;/&lt;bdi&gt;) whose own <c>UnicodeBidi</c> is not
    /// <c>normal</c> contributes a synthetic explicit push over its own text range, exactly as if a real
    /// LRE/RLE/LRO/RLO/LRI/RLI/FSI control character opened there and a matching PDF/PDI closed it (see
    /// <see cref="BidiIsolateOverride"/>) - the CSS integration UAX#9 itself expects (CSS Writing Modes
    /// Level 3 §5.2).
    /// </para>
    /// </summary>
    internal static class CssBidiParagraphResolver
    {
        private const char ObjectReplacementCharacter = '￼';

        public static void AssignBidiLevels(CssBox box)
        {
            if (EstablishesOwnParagraph(box))
            {
                ResolveParagraph(box);
            }

            foreach (var child in box.Boxes)
            {
                AssignBidiLevels(child);
            }
        }

        /// <summary>
        /// Resolves <paramref name="box"/>'s own directly-set <see cref="CssBox.Text"/> (a
        /// <c>::before</c>/<c>::after</c>/<c>::marker</c>/footnote-call/footnote-marker
        /// generated-content box - see <c>CssContentEngine.ApplyContent</c>) as its own standalone
        /// paragraph. Called from every site that runs <c>ApplyContent</c> against a pseudo-element
        /// box outside <see cref="AssignBidiLevels"/>'s own whole-tree walk - both the first time
        /// (<c>DomParser.CorrectTextBoxes</c>: that walk runs *before* <c>CorrectTextBoxes</c> per its
        /// own ordering requirement, so it only ever sees the box empty and never assigns it a
        /// <see cref="CssBox.BidiLevels"/> array, leaving <see cref="CssBox.ParseToWords"/> to fall
        /// back to a uniform, wrongly-reversing level - issue #551) and on every later re-application
        /// (<c>HtmlContainerInt.ReapplyPseudoElementContent</c>/<c>ResolveTargetPageContent</c>, e.g. a
        /// <c>target-counter(_, page)</c> or <c>string()</c> value changing across a convergence
        /// round - re-resolving is required there since the box's text, and so its correct
        /// <see cref="CssBox.BidiLevels"/> array length, can genuinely change between rounds).
        /// Treating the generated text as its own independent paragraph (using this box's own resolved
        /// <c>Direction</c>/<c>UnicodeBidi</c>) is not a perfect substitute for weaving it into a
        /// shared paragraph with its element siblings - it won't cross-reorder against adjacent inline
        /// sibling text the way ordinary DOM text does - but it correctly keeps Latin/digit content
        /// left-to-right inside RTL generated content, the actual defect reported.
        /// <para>
        /// Only acts on an actual pseudo-element box (<see cref="CssBox.IsPseudoElement"/>) -
        /// <b>never</b> call this for an ordinary text box (one whose text was already present at
        /// DOM-parse time, long before <see cref="AssignBidiLevels"/>'s own tree walk ran):
        /// <see cref="AssignBidiLevels"/> already resolved every such box correctly (as part of a
        /// real, possibly cross-element, shared paragraph, honoring isolate-override pushes from
        /// ancestors like <c>&lt;bdo&gt;</c>), and re-resolving it here in isolation would discard
        /// that context - a regression, not a fix.
        /// </para>
        /// </summary>
        public static void ResolveOwnTextAsParagraph(CssBox box)
        {
            if (box.IsPseudoElement && box.Text is { Length: > 0 })
                ResolveParagraph(box);
        }

        private static bool EstablishesOwnParagraph(CssBox box) =>
            box.IsRoot || box.DerivedStyle.ActualDisplay != Keywords.Inline || box is CssBoxMarker;

        private static bool ParticipatesInParentParagraph(CssBox box) =>
            box.DerivedStyle.ActualDisplay == Keywords.Inline && box is not (CssBoxImage or CssBoxSvg or CssBoxMarker);

        private static void ResolveParagraph(CssBox paragraphRoot)
        {
            var text = new StringBuilder();
            var ranges = new List<(CssBox Box, int Start, int Length)>();
            var overrides = new List<BidiIsolateOverride>();

            // A paragraph root can carry its own Text directly (a ::before/::after pseudo-element's
            // generated-content box, set by CssContentEngine.ApplyContent) rather than only through a
            // child text box - Flatten below only ever walks box.Boxes, so a childless paragraph root
            // with its own Text would otherwise never get appended anywhere and never receive a
            // BidiLevels array, falling back to ParseToWords' uniform-Direction-derived level and
            // getting fully reversed/mirrored even when it's plain Latin text (issue #551).
            if (paragraphRoot.Text is { Length: > 0 } rootText)
            {
                ranges.Add((paragraphRoot, 0, rootText.Length));
                text.Append(rootText);
            }

            Flatten(paragraphRoot, text, ranges, overrides);

            if (text.Length == 0) return;

            var paragraphText = text.ToString();
            var direction = paragraphRoot.UnicodeBidi.Value == UnicodeMode.Plaintext
                ? BidiParagraphDirection.Auto
                : paragraphRoot.Direction.Value == DirectionMode.Rtl
                    ? BidiParagraphDirection.Rtl
                    : BidiParagraphDirection.Ltr;

            var result = BidiResolver.Resolve(paragraphText, direction, overrides);

            foreach (var (box, start, length) in ranges)
            {
                var levels = new byte[length];
                result.Levels.AsSpan(start, length).CopyTo(levels);
                box.BidiLevels = levels;
            }
        }

        private static void Flatten(
            CssBox box, StringBuilder text, List<(CssBox, int, int)> ranges, List<BidiIsolateOverride> overrides)
        {
            foreach (var child in box.Boxes)
            {
                if (child.Text is { Length: > 0 } childText)
                {
                    var start = text.Length;
                    text.Append(childText);
                    ranges.Add((child, start, childText.Length));
                }
                else if (child.Text is not null)
                {
                    // An empty text box (e.g. between two tags with nothing between them) contributes
                    // nothing to the paragraph and needs no level array of its own.
                }
                else if (ParticipatesInParentParagraph(child))
                {
                    var pushes = CssUnicodeBidiMapping.MapToPushes(child.UnicodeBidi.Value, child.Direction.Value);

                    if (pushes.Count == 0)
                    {
                        Flatten(child, text, ranges, overrides);
                    }
                    else
                    {
                        var start = text.Length;

                        // Recorded before recursing so this box's own override(s) can be inserted here -
                        // ahead of any overrides a nested box contributes while sharing the same Start (a
                        // nested box with no preceding sibling text of its own) - rather than appended
                        // after recursion returns, which would put a child's override before its
                        // parent's for any index the two happen to share. BidiResolver.Resolve pushes
                        // overrides sharing a start in list order and (deliberately) pops overrides
                        // sharing an end in the reverse order, so outer-to-inner list order is what makes
                        // both same-index nesting and a multi-push box's own two pushes resolve correctly.
                        var insertAt = overrides.Count;
                        Flatten(child, text, ranges, overrides);
                        var length = text.Length - start;

                        if (length > 0)
                        {
                            for (var i = 0; i < pushes.Count; i++)
                                overrides.Insert(insertAt + i, new BidiIsolateOverride(start, length, pushes[i]));
                        }
                    }
                }
                else
                {
                    // Atomic inline content from this paragraph's point of view (a replaced element, an
                    // inline-block/float/positioned box, a marker) - opaque to this resolution (it gets
                    // its own, separate paragraph when the outer AssignBidiLevels walk reaches it) but
                    // still occupies a place in this paragraph's text, per UAX#9's own recommendation to
                    // treat an embedded object as one U+FFFC OBJECT REPLACEMENT CHARACTER (Bidi_Class ON).
                    text.Append(ObjectReplacementCharacter);
                }
            }
        }

    }
}

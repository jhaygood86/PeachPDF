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

        private static bool EstablishesOwnParagraph(CssBox box) =>
            box.IsRoot || box.DerivedStyle.ActualDisplay != CssConstants.Inline || box is CssBoxMarker;

        private static bool ParticipatesInParentParagraph(CssBox box) =>
            box.DerivedStyle.ActualDisplay == CssConstants.Inline && box is not (CssBoxImage or CssBoxSvg or CssBoxMarker);

        private static void ResolveParagraph(CssBox paragraphRoot)
        {
            var text = new StringBuilder();
            var ranges = new List<(CssBox Box, int Start, int Length)>();
            var overrides = new List<BidiIsolateOverride>();

            Flatten(paragraphRoot, text, ranges, overrides);

            if (text.Length == 0) return;

            var paragraphText = text.ToString();
            var direction = paragraphRoot.Direction.Value == DirectionMode.Rtl
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
                        Flatten(child, text, ranges, overrides);
                        var length = text.Length - start;

                        // Added in outer-to-inner order (e.g. isolate-override's LRI before its nested
                        // LRO) - BidiResolver.Resolve pushes overrides sharing a start in that same
                        // order and (deliberately) pops overrides sharing an end in the reverse order,
                        // so this list order is what makes both ends of a multi-push box nest correctly.
                        if (length > 0)
                        {
                            foreach (var push in pushes)
                                overrides.Add(new BidiIsolateOverride(start, length, push));
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

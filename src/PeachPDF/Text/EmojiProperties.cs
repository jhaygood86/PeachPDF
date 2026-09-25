using PeachPDF.CSS;
using System;
using System.Text;

namespace PeachPDF.Text
{
    /// <summary>
    /// The Unicode emoji properties CSS Fonts 4's <c>font-variant-emoji</c> is defined over, and the rule that
    /// combines them with the property value and an explicit variation selector into one
    /// <see cref="EmojiPresentation"/>. The range tables themselves are generated from the Unicode
    /// Character Database - see <c>assets/unicode/generate_emoji_table.py</c>.
    /// </summary>
    internal static partial class EmojiProperties
    {
        /// <summary>U+FE0E VARIATION SELECTOR-15: text presentation.</summary>
        internal const int TextSelector = 0xFE0E;

        /// <summary>U+FE0F VARIATION SELECTOR-16: emoji presentation.</summary>
        internal const int EmojiSelector = 0xFE0F;

        /// <summary>
        /// <see cref="Resolve"/> for the character that starts at <paramref name="index"/> of
        /// <paramref name="text"/>, reading the variation selector that directly follows it (if any) from
        /// the text itself. The one place a run's presentation is derived, so measurement, painting and
        /// font selection - which all start from a word's own text - cannot disagree about it.
        /// </summary>
        internal static EmojiPresentation ResolveAt(FontVariantEmojiMode mode, string text, int index)
        {
            if (index < 0 || index >= text.Length || !Rune.TryGetRuneAt(text, index, out var baseRune))
                return EmojiPresentation.NoPreference;

            var next = index + baseRune.Utf16SequenceLength;
            Rune following = default;
            var hasFollowing = next < text.Length && Rune.TryGetRuneAt(text, next, out following);
            var selector = hasFollowing && IsPresentationSelector(following.Value) ? following.Value : 0;

            if (selector == 0)
            {
                // Overwhelmingly the common case - ordinary text under the initial value - so answer it
                // before the participant lookup.
                if (mode == FontVariantEmojiMode.Normal)
                    return EmojiPresentation.NoPreference;

                // The property is a default for a *presentation sequence* (base + selector). A base that
                // instead continues into an emoji modifier, a keycap or a ZWJ sequence is part of a longer
                // emoji sequence the font composes as one glyph; forcing the base alone into another font
                // would split that sequence, so it is left to the font-family order.
                if (hasFollowing && ContinuesAnEmojiSequence(following.Value))
                    return EmojiPresentation.NoPreference;
            }

            return Resolve(mode, baseRune.Value, selector);
        }

        private static bool ContinuesAnEmojiSequence(int codepoint) =>
            codepoint is 0x200D or 0x20E3 or (>= 0x1F3FB and <= 0x1F3FF);

        /// <summary>Whether <paramref name="codepoint"/> is U+FE0E or U+FE0F - the only variation selectors that steer font selection.</summary>
        internal static bool IsPresentationSelector(int codepoint) => codepoint is TextSelector or EmojiSelector;

        /// <summary>
        /// Whether <paramref name="codepoint"/> is an <i>Emoji Presentation Participating Code Point</i> (CSS
        /// Fonts 4): the base of some entry in Unicode's <c>emoji-variation-sequences.txt</c>. Only these
        /// characters are affected by <c>font-variant-emoji</c>; a U+FE0E/U+FE0F after any other character
        /// has no effect on font selection.
        /// </summary>
        internal static bool IsPresentationParticipant(int codepoint) => Contains(VariationBases, codepoint);

        /// <summary>Whether <paramref name="codepoint"/> has the Unicode <c>Emoji_Presentation</c> property (emoji-default, as opposed to text-default).</summary>
        internal static bool HasEmojiPresentationProperty(int codepoint) => Contains(EmojiPresentationRanges, codepoint);

        /// <summary>
        /// The presentation <paramref name="baseCodepoint"/> is asked to be drawn in.
        /// </summary>
        /// <param name="mode">the box's <c>font-variant-emoji</c> value</param>
        /// <param name="baseCodepoint">the character being resolved</param>
        /// <param name="followingSelector">
        /// the variation selector that immediately follows it in the content, or 0 when none does. Only
        /// U+FE0E/U+FE0F matter; any other value is treated as absent.
        /// </param>
        /// <remarks>
        /// CSS Fonts 4 §9.3: an explicit U+FE0E/U+FE0F always wins over the property ("the text being
        /// rendered can opt out"); otherwise <c>text</c>/<c>emoji</c> behave as if that selector had been
        /// appended to every participating code point, and <c>unicode</c> follows UTS #51 (an emoji-default
        /// character is drawn as emoji, a text-default one as text). <c>normal</c> leaves the choice to
        /// the user agent, and PeachPDF makes none: font-family order decides, exactly as before the
        /// property existed.
        /// </remarks>
        internal static EmojiPresentation Resolve(FontVariantEmojiMode mode, int baseCodepoint, int followingSelector)
        {
            if (!IsPresentationParticipant(baseCodepoint))
                return EmojiPresentation.NoPreference;

            if (followingSelector == EmojiSelector)
                return EmojiPresentation.Emoji;
            if (followingSelector == TextSelector)
                return EmojiPresentation.Text;

            return mode switch
            {
                FontVariantEmojiMode.Text => EmojiPresentation.Text,
                FontVariantEmojiMode.Emoji => EmojiPresentation.Emoji,
                FontVariantEmojiMode.Unicode => HasEmojiPresentationProperty(baseCodepoint) ? EmojiPresentation.Emoji : EmojiPresentation.Text,
                _ => EmojiPresentation.NoPreference
            };
        }

        /// <summary>
        /// Whether a font face should be preferred for <paramref name="baseCodepoint"/> drawn in
        /// <paramref name="presentation"/>: the face's own cmap format 14 says it supports the matching
        /// variation sequence, or - absent that declaration - its colour-ness agrees (a colour font for
        /// emoji presentation, an outline font for text presentation). The colour-ness fallback is what
        /// the CSS Fonts 4 note on §9.3 anticipates: "a UA might wish to disregard fonts which do not
        /// include color tables" when asked for emoji presentation. <see cref="EmojiPresentation.NoPreference"/>
        /// accepts every face.
        /// </summary>
        internal static bool FaceMatches(Fonts.OpenType.OpenTypeFontface face, int baseCodepoint, EmojiPresentation presentation)
        {
            if (presentation == EmojiPresentation.NoPreference)
                return true;

            if (face.cmap?.cmap14 is { } sequences
                && sequences.Lookup(baseCodepoint, SelectorFor(presentation), out _) != Fonts.OpenType.VariationSequenceSupport.None)
            {
                return true;
            }

            return face.IsColorFont == (presentation == EmojiPresentation.Emoji);
        }

        /// <summary>The variation selector that expresses <paramref name="presentation"/> (0 for <see cref="EmojiPresentation.NoPreference"/>).</summary>
        internal static int SelectorFor(EmojiPresentation presentation) => presentation switch
        {
            EmojiPresentation.Text => TextSelector,
            EmojiPresentation.Emoji => EmojiSelector,
            _ => 0
        };

        private static bool Contains((int Start, int End)[] ranges, int codepoint)
        {
            int lo = 0, hi = ranges.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (codepoint < ranges[mid].Start)
                    hi = mid - 1;
                else if (codepoint > ranges[mid].End)
                    lo = mid + 1;
                else
                    return true;
            }

            return false;
        }
    }
}

using System;
using PeachDrawing.Text.Internal.Text;
using System.Text;

namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// Which of its two appearances, text or emoji, a character that has both is to be drawn in (UTS #51 and
    /// CSS Fonts 4 <c>font-variant-emoji</c>).
    /// </summary>
    public static class Emoji
    {
        /// <summary>U+FE0E VARIATION SELECTOR-15, which asks for the text presentation.</summary>
        public const int TextSelector = EmojiProperties.TextSelector;

        /// <summary>U+FE0F VARIATION SELECTOR-16, which asks for the emoji presentation.</summary>
        public const int EmojiSelector = EmojiProperties.EmojiSelector;

        /// <summary>
        /// Decides the presentation of one character from the mode a caller asks for and what follows the character.
        /// </summary>
        /// <remarks>
        /// A U+FE0E or U+FE0F right after the character always wins, since the text may opt out of what the caller
        /// asks for. Otherwise <see cref="EmojiMode.Text"/> and <see cref="EmojiMode.Emoji"/> behave as though that
        /// selector followed every character that can take one, and <see cref="EmojiMode.Unicode"/> draws a character
        /// as emoji when its default is emoji and as text when its default is text. <see cref="EmojiMode.Normal"/>
        /// makes no choice, and the order of the font families decides.
        /// </remarks>
        /// <param name="mode">The mode the caller asks for.</param>
        /// <param name="baseCodepoint">The character to decide for.</param>
        /// <param name="followingSelector">The variation selector that directly follows it, or 0 when none does. Anything but U+FE0E and U+FE0F counts as none.</param>
        public static EmojiPresentation Resolve(EmojiMode mode, int baseCodepoint, int followingSelector)
            => EmojiProperties.Resolve(mode, baseCodepoint, followingSelector);

        /// <summary>
        /// Decides the presentation of the character that starts at an index of a text, reading the variation
        /// selector that follows it, if any, from the text itself.
        /// </summary>
        /// <param name="mode">The mode the caller asks for.</param>
        /// <param name="text">The text.</param>
        /// <param name="index">Index of the character's first UTF-16 code unit.</param>
        public static EmojiPresentation ResolveAt(EmojiMode mode, string text, int index)
        {
            ArgumentNullException.ThrowIfNull(text);
            return EmojiProperties.ResolveAt(mode, text, index);
        }

        /// <summary>
        /// Whether a character takes part in emoji presentation: it is the base of an entry of Unicode's
        /// <c>emoji-variation-sequences.txt</c>. A variation selector after any other character has no effect.
        /// </summary>
        /// <param name="rune">The character.</param>
        public static bool IsPresentationParticipant(Rune rune) => EmojiProperties.IsPresentationParticipant(rune.Value);

        /// <summary>Whether a character is U+FE0E or U+FE0F, the only variation selectors that steer the choice of font.</summary>
        /// <param name="rune">The character.</param>
        public static bool IsPresentationSelector(Rune rune) => EmojiProperties.IsPresentationSelector(rune.Value);

        /// <summary>The variation selector that spells out a presentation, or 0 for <see cref="EmojiPresentation.NoPreference"/>.</summary>
        /// <param name="presentation">The presentation.</param>
        public static int SelectorFor(EmojiPresentation presentation) => EmojiProperties.SelectorFor(presentation);
    }
}

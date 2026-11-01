using System;
using System.Collections.Generic;
using PeachDrawing.Text.Internal.Text;
using System.Text;

namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// How a character is oriented when text is set vertically (UAX #50, the <c>Vertical_Orientation</c> property).
    /// </summary>
    public static class VerticalOrientation
    {
        /// <summary>The <c>Vertical_Orientation</c> of a character.</summary>
        /// <param name="rune">The character.</param>
        public static VerticalOrientationClass Of(Rune rune) => VerticalOrientationTable.Of(rune);

        /// <summary>
        /// Whether a character stays upright in vertical text set with CSS <c>text-orientation: mixed</c>.
        /// </summary>
        /// <remarks>
        /// <see cref="VerticalOrientationClass.Tu"/> counts as upright and <see cref="VerticalOrientationClass.Tr"/>
        /// as rotated, which is what they fall back to when a font offers no dedicated vertical form.
        /// </remarks>
        /// <param name="rune">The character.</param>
        public static bool IsEffectivelyUpright(Rune rune) => VerticalOrientationTable.IsEffectivelyUpright(rune);
    }

    /// <summary>
    /// Characters that carry meaning but have no appearance of their own: the Unicode
    /// <c>Default_Ignorable_Code_Point</c> property.
    /// </summary>
    /// <remarks>
    /// Variation selectors, joiners, bidi controls, the word joiner, the Hangul fillers and the language tag
    /// characters are among them. A font is not expected to have a glyph for such a character, so drawing a
    /// "missing glyph" box for one is wrong.
    /// </remarks>
    public static class DefaultIgnorables
    {
        /// <summary>Whether a code point has the <c>Default_Ignorable_Code_Point</c> property.</summary>
        /// <param name="codepoint">A Unicode code point.</param>
        public static bool Contains(int codepoint) => UnicodeDefaultIgnorables.IsDefaultIgnorable(codepoint);

        /// <summary>Whether a code point is a variation selector.</summary>
        /// <param name="codepoint">A Unicode code point.</param>
        public static bool IsVariationSelector(int codepoint) => UnicodeDefaultIgnorables.IsVariationSelector(codepoint);
    }

    /// <summary>
    /// Finds where a word may be hyphenated, with Liang's pattern algorithm and the TeX hyphenation
    /// patterns published for about seventy languages.
    /// </summary>
    public static class Hyphenator
    {
        /// <summary>
        /// The places a word may be broken with a hyphen.
        /// </summary>
        /// <param name="word">The word, without surrounding punctuation.</param>
        /// <param name="language">A BCP 47 language tag, such as <c>de-AT</c>; the closest supported language is used.</param>
        /// <returns>
        /// Each value <c>i</c> means a hyphen may go between <c>word[i - 1]</c> and <c>word[i]</c>. The list is empty
        /// when the language is unsupported, the word is shorter than that language's minimums, or the word holds
        /// anything other than letters.
        /// </returns>
        public static IReadOnlyList<int> FindBreakPoints(string word, string? language)
        {
            ArgumentNullException.ThrowIfNull(word);
            return HyphenationEngine.FindHyphenationPoints(word, language);
        }
    }
}

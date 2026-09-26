using PeachDrawing.Text.Internal.Text;
using PeachDrawing.Text.Internal.Text.Shaping.Arabic;
using PeachDrawing.Text.Internal.Text.Shaping.Use;
using System;
using System.Collections.Generic;
using System.Text;

namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// The cursive joining of Arabic, Syriac and the other scripts that share the <c>Joining_Type</c> property (UAX #44 and
    /// the Unicode Core Specification chapter on Arabic).
    /// </summary>
    public static class ArabicJoining
    {
        /// <summary>The <c>Joining_Type</c> of a character.</summary>
        /// <param name="rune">The character.</param>
        public static ArabicJoiningType TypeOf(Rune rune) => ArabicShapingTable.Of(rune);

        /// <summary>The <c>Joining_Type</c> of a code point.</summary>
        /// <param name="codepoint">A Unicode code point.</param>
        public static ArabicJoiningType TypeOf(int codepoint) => ArabicShapingTable.Of(codepoint);

        /// <summary>
        /// Works out the positional form of every character of a text: the form that decides which glyph the font substitutes.
        /// </summary>
        /// <remarks>
        /// A character that does not join, or that is transparent to joining, gets <see cref="ArabicJoiningForm.None"/>, so the
        /// result is a correct no-op for text that is not in a joining script.
        /// </remarks>
        /// <param name="codepoints">The text as code points.</param>
        /// <returns>One form for each entry of <paramref name="codepoints"/>.</returns>
        public static ArabicJoiningForm[] Resolve(IReadOnlyList<int> codepoints)
        {
            ArgumentNullException.ThrowIfNull(codepoints);
            return ArabicJoiningShaper.Resolve(codepoints);
        }
    }

    /// <summary>
    /// The Universal Shaping Engine's classification of characters, for the Indic scripts it currently covers: Devanagari,
    /// Bengali, Gujarati and Tamil.
    /// </summary>
    public static class UniversalShaping
    {
        /// <summary>
        /// The USE category of a code point, derived from its Indic syllabic and positional categories and its general category.
        /// </summary>
        /// <param name="codepoint">A Unicode code point.</param>
        /// <returns>The category, which is <see cref="UseCategory.O"/> for a character of another script.</returns>
        public static UseCategory Classify(int codepoint) => UseCategoryClassifier.Classify(codepoint);
    }
}

using PeachDrawing.Text.Internal.Text.Segmentation;
using System;

namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// The Unicode Line Breaking Algorithm (UAX #14): where a line of text may, and where it must, end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The algorithm answers for every position between two characters. <see cref="FindOpportunities"/> returns one answer for
    /// each UTF-16 index of the text and one for its end, so <c>opportunities[i]</c> says what happens to a line that would end
    /// just before <c>text[i]</c>. Inside a surrogate pair and between a character and the combining marks that follow it the
    /// answer is <see cref="LineBreakOpportunity.Prohibited"/>, except after a space, a hard break or a zero width space,
    /// where a combining mark stands alone.
    /// </para>
    /// <para>
    /// The result is the algorithm's own view of the text. A host that lays text out still decides what to do with it:
    /// whether the space at a break stays on the line, how a word too long for a line is split, and where hyphenation adds
    /// breaks. Complex-context scripts (Thai, Lao, Khmer, Burmese) are broken as their letters, without a dictionary, so a
    /// line of them has no opportunities where the script writes no spaces.
    /// </para>
    /// <para>
    /// The rules are checked against Unicode's own <c>LineBreakTest.txt</c> conformance file, with <see cref="LineBreakStrictness.Strict"/>,
    /// which is the algorithm's own default.
    /// </para>
    /// </remarks>
    public static class LineBreaker
    {
        /// <summary>
        /// Finds where a line of text may or must break.
        /// </summary>
        /// <param name="text">The text of a paragraph.</param>
        /// <param name="options">How CSS <c>word-break</c> and <c>line-break</c> tailor the algorithm, or <see langword="default"/> for the normal behaviour.</param>
        /// <returns>
        /// One entry for each UTF-16 index of <paramref name="text"/>, plus a last one for its end, which is always
        /// <see cref="LineBreakOpportunity.Mandatory"/> (as is the only entry of empty text). The first entry is always
        /// <see cref="LineBreakOpportunity.Prohibited"/>.
        /// </returns>
        public static LineBreakOpportunity[] FindOpportunities(ReadOnlySpan<char> text, LineBreakOptions options = default)
        {
            return LineBreakAlgorithm.FindOpportunities(text, options);
        }
    }
}

using PeachDrawing.Text.Internal.Text.Segmentation;
using System;

namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// The default boundaries of Unicode Text Segmentation (UAX #29): where one grapheme cluster, word or sentence ends and the
    /// next begins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every method returns the boundaries as UTF-16 indices into the text, in increasing order, including the start of the text
    /// (0) and its end (the length). Text with nothing in it has no boundaries. A boundary never falls between the two halves of
    /// a surrogate pair. Pieces are the text between neighbouring boundaries.
    /// </para>
    /// <para>
    /// These are the default rules of the Unicode Character Database that ships with the library; they are not tailored to a
    /// language. Each algorithm is checked against Unicode's own conformance file for it (<c>GraphemeBreakTest.txt</c>,
    /// <c>WordBreakTest.txt</c>, <c>SentenceBreakTest.txt</c>).
    /// </para>
    /// </remarks>
    public static class Segmenter
    {
        /// <summary>
        /// Finds the boundaries between extended grapheme clusters: the units a reader sees as one character, such as a letter with
        /// its accents, a Hangul syllable, an emoji sequence or a flag.
        /// </summary>
        /// <param name="text">The text to segment.</param>
        /// <returns>The boundaries, as UTF-16 indices.</returns>
        public static int[] FindGraphemeBoundaries(ReadOnlySpan<char> text) => ToOffsets(text, GraphemeBreaker.FindBoundaries);

        /// <summary>
        /// Finds the boundaries between words, as a search or a double click would treat them: runs of letters and numbers,
        /// with the punctuation inside them, are one word, a run of spaces is one piece, and every other character is a piece of its own.
        /// </summary>
        /// <param name="text">The text to segment.</param>
        /// <returns>The boundaries, as UTF-16 indices.</returns>
        public static int[] FindWordBoundaries(ReadOnlySpan<char> text) => ToOffsets(text, WordBreaker.FindBoundaries);

        /// <summary>
        /// Finds the boundaries between sentences. A sentence keeps its closing punctuation, the spaces after it and its
        /// paragraph separator.
        /// </summary>
        /// <param name="text">The text to segment.</param>
        /// <returns>The boundaries, as UTF-16 indices.</returns>
        public static int[] FindSentenceBoundaries(ReadOnlySpan<char> text) => ToOffsets(text, SentenceBreaker.FindBoundaries);

        private delegate bool[] BoundaryFinder(in ScalarText text);

        private static int[] ToOffsets(ReadOnlySpan<char> source, BoundaryFinder finder)
        {
            var text = new ScalarText(source);
            bool[] boundaries = finder(text);

            int found = 0;
            for (int k = 0; k <= text.Count; k++)
            {
                if (boundaries[k])
                {
                    found++;
                }
            }

            var offsets = new int[found];
            int next = 0;
            for (int k = 0; k <= text.Count; k++)
            {
                if (boundaries[k])
                {
                    offsets[next++] = text.Offset[k];
                }
            }

            return offsets;
        }
    }
}

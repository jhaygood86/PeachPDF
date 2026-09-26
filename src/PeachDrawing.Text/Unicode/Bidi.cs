using System;
using System.Collections.Generic;
using PeachDrawing.Text.Internal.Text.Bidi;
using System.Text;

namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// The Unicode Bidirectional Algorithm (UAX #9): which direction each character of a paragraph reads in,
    /// and in what order the pieces of a line are drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Work happens in two steps because the algorithm does. <see cref="Analyze"/> looks at a whole paragraph
    /// and resolves an embedding level for every character (rules P2 to I2, and L1 up to the line's trailing
    /// whitespace). Only once a host has decided where the paragraph's lines break can it call
    /// <see cref="ReorderLine"/> for each one (rule L2), so that step takes levels and a range rather than text.
    /// </para>
    /// <para>
    /// The implementation is checked against Unicode's own <c>BidiCharacterTest.txt</c> conformance file.
    /// Rule L1's last clause, which resets the trailing whitespace of each line, is left to the caller because
    /// it depends on the granularity (characters, words, glyphs) the caller lays out in.
    /// </para>
    /// </remarks>
    public static class Bidi
    {
        /// <summary>
        /// Resolves the embedding levels of one paragraph.
        /// </summary>
        /// <param name="text">The paragraph's text.</param>
        /// <param name="direction">The direction the paragraph starts in, or <see cref="BaseDirection.Auto"/> to detect it.</param>
        /// <param name="spans">
        /// Embeddings that apply to stretches of <paramref name="text"/> without any control character being in it,
        /// or <see langword="null"/> for none.
        /// </param>
        /// <returns>One level for every UTF-16 code unit of <paramref name="text"/>, and the paragraph level.</returns>
        public static BidiAnalysis Analyze(string text, BaseDirection direction, IReadOnlyList<EmbeddingSpan>? spans = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            return BidiResolver.Resolve(text, direction, spans);
        }

        /// <summary>
        /// Puts the pieces of one line into the order they are drawn in, left to right (rule L2).
        /// </summary>
        /// <param name="levels">Resolved levels, as <see cref="BidiAnalysis.Levels"/> or any array of the caller's own granularity.</param>
        /// <param name="lineStart">Index in <paramref name="levels"/> of the line's first element.</param>
        /// <param name="lineLength">Number of elements on the line.</param>
        /// <returns>
        /// The line's runs of equal level in visual order. Elements inside one run stay in logical order: it is
        /// whole runs that are moved, and a run at an odd level still has to be reversed by whoever draws it
        /// (see <see cref="Mirror"/>).
        /// </returns>
        public static IReadOnlyList<BidiRun> ReorderLine(byte[] levels, int lineStart, int lineLength)
        {
            ArgumentNullException.ThrowIfNull(levels);
            ArgumentOutOfRangeException.ThrowIfNegative(lineStart);
            ArgumentOutOfRangeException.ThrowIfNegative(lineLength);
            if (lineLength > levels.Length - lineStart)
            {
                throw new ArgumentOutOfRangeException(nameof(lineLength), lineLength, "The line extends past the end of the levels.");
            }

            return BidiResolver.ReorderLine(levels, lineStart, lineLength);
        }

        /// <summary>The <c>Bidi_Class</c> of a character.</summary>
        /// <param name="rune">The character.</param>
        public static BidiClass ClassOf(Rune rune) => BidiClassTable.Of(rune);

        /// <summary>
        /// Finds the character that draws as the mirror image of <paramref name="rune"/> (a parenthesis and its
        /// counterpart, for instance), for right-to-left runs.
        /// </summary>
        /// <param name="rune">The character.</param>
        /// <param name="mirrored">The mirror image, when there is one.</param>
        /// <returns><see langword="false"/> when the character has no <c>Bidi_Mirroring_Glyph</c>.</returns>
        public static bool TryGetMirror(Rune rune, out Rune mirrored)
        {
            if (BidiMirroring.TryGetMirror(rune.Value, out var mirror))
            {
                mirrored = new Rune(mirror);
                return true;
            }

            mirrored = default;
            return false;
        }

        /// <summary>
        /// Turns the text of one run into the text to draw left to right: for an odd level, the characters are
        /// reversed (rule L2) and every mirrorable one is replaced by its mirror image (rule L4).
        /// </summary>
        /// <param name="runText">The run's text in logical order.</param>
        /// <param name="level">The run's embedding level.</param>
        /// <returns><paramref name="runText"/> itself for an even level.</returns>
        public static string Mirror(string runText, byte level)
        {
            ArgumentNullException.ThrowIfNull(runText);
            return BidiMirrorResolver.ApplyMirroring(runText, level);
        }

        /// <summary>
        /// Reverses a string by character, keeping surrogate pairs whole, and substitutes nothing.
        /// </summary>
        /// <remarks>
        /// The position-only half of <see cref="Mirror"/>, for producing a logical-order companion string whose
        /// characters line up one for one with a mirrored visual string.
        /// </remarks>
        /// <param name="text">The text to reverse.</param>
        public static string Reverse(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return BidiMirrorResolver.ReverseRunes(text);
        }
    }
}

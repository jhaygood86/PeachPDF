using System;
using System.Text;

namespace PeachPDF.Text.Bidi
{
    /// <summary>
    /// Prepares one <see cref="BidiRun"/>'s own text for left-to-right painting. <see cref="BidiResolver.ReorderLine"/>
    /// only reorders which run comes before which on the line - within a single run, UAX #9 rule L2 ("reverse
    /// any contiguous sequence of characters that are at that level or higher") still applies to the run's
    /// own characters whenever its level is odd (RTL): e.g. resolving Hebrew "alef bet" (stored/logical
    /// order) to display as "bet alef" (visual left-to-right order) is exactly one run being reversed this
    /// way, not two runs being reordered relative to each other. Rule L4 (mirrored characters - parentheses,
    /// brackets, angle brackets, etc., see <see cref="BidiMirroring"/>) is folded into the same pass since
    /// both only ever apply to an odd-level run and a caller always wants both done together before painting.
    /// This is a text-substitution "used value" concern applied once, at layout time, baked into the run's
    /// own text - paint never needs to know a run came from an RTL context at all.
    /// </summary>
    internal static class BidiMirrorResolver
    {
        /// <summary>
        /// Returns <paramref name="runText"/> unchanged if <paramref name="level"/> is even (LTR); otherwise
        /// returns the codepoint-reversed text (respecting surrogate pairs) with every mirrorable character
        /// substituted for its mirror-image codepoint - ready to paint left-to-right as-is.
        /// </summary>
        public static string ApplyMirroring(string runText, byte level)
        {
            if ((level & 1) == 0 || string.IsNullOrEmpty(runText))
                return runText;

            var runes = new System.Collections.Generic.List<Rune>(runText.Length);
            var i = 0;
            while (i < runText.Length)
            {
                Rune.DecodeFromUtf16(runText.AsSpan(i), out var rune, out var consumed);
                runes.Add(BidiMirroring.TryGetMirror(rune.Value, out var mirror) ? (Rune)mirror : rune);
                i += consumed;
            }

            var sb = new StringBuilder(runText.Length);
            for (var k = runes.Count - 1; k >= 0; k--)
                sb.Append(runes[k]);

            return sb.ToString();
        }
    }
}

using System;
using System.Buffers;

namespace PeachDrawing.Text.Internal.Text.Segmentation
{
    /// <summary>
    /// Finds where the words start in a run of Thai, Lao, Khmer or Burmese, which write no spaces between them (UAX #14, Complex_Context).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run is cut into <em>clusters</em>, the pieces a word may begin or end at: the grapheme clusters of UAX #29, and further a dependent vowel, tone mark or other sign belongs
    /// to the character before it, a leading vowel to the consonant after it, a Khmer subscript or Burmese stacked consonant to the one
    /// it hangs from, and a Burmese consonant that carries an asat closes the syllable before it. A word never starts or ends inside a
    /// cluster, and no break is ever placed there.
    /// </para>
    /// <para>
    /// At each position the segmenter takes the words of the list that start there and ends on a cluster boundary, and chooses the
    /// one that lets the text be covered best over the next few words (<see cref="Lookahead"/>): every character of a matched word
    /// scores one, and a cluster no word starts at costs <see cref="UnknownPenalty"/>. Among equal choices the longest word wins. A
    /// stretch no word matches stays whole, one break before it and one after it. The scores of a position are computed once, so the
    /// time is linear in the length of the run; a run of more than <see cref="MaxChunk"/> characters is cut into chunks first.
    /// </para>
    /// </remarks>
    internal static class DictionarySegmenter
    {
        /// <summary>How many words the choice of the next one looks ahead, itself included.</summary>
        private const int Lookahead = 3;

        /// <summary>What a cluster that no word starts at costs, in characters of matched word.</summary>
        private const int UnknownPenalty = 3;

        /// <summary>The length a stretch analysed at once is cut at, at the next cluster boundary from there, so the working arrays are about this long.</summary>
        private const int MaxChunk = 16384;

        private const int NotComputed = int.MinValue;

        /// <summary>The script of a code point by its block, for the scripts that have a word list.</summary>
        internal static ComplexScript ScriptOf(int codePoint) => codePoint switch
        {
            >= 0x0E01 and <= 0x0E5B => ComplexScript.Thai,
            >= 0x0E81 and <= 0x0EDF => ComplexScript.Lao,
            >= 0x1780 and <= 0x17FF => ComplexScript.Khmer,
            >= 0x1000 and <= 0x109F => ComplexScript.Burmese,
            _ => ComplexScript.None,
        };

        /// <summary>
        /// Marks in <paramref name="starts"/>, for each scalar of <paramref name="run"/> that begins a word other than the first
        /// scalar, the index of that scalar plus <paramref name="offset"/>. The whole run is in one script, which has a word list, and
        /// <paramref name="graphemes"/> tells for each scalar of the run whether a grapheme cluster boundary (UAX #29) lies before it.
        /// </summary>
        /// <remarks>
        /// A zero width joiner or non-joiner does not take part in matching (the lists do not spell them), and no word starts right
        /// after one: what a joiner separates is not broken.
        /// </remarks>
        internal static void FindWordStarts(ReadOnlySpan<int> run, ReadOnlySpan<bool> graphemes, ComplexScript script, WordDictionary dictionary, bool[] starts, int offset)
        {
            int joiners = 0;
            foreach (int code in run)
            {
                if (code is 0x200C or 0x200D)
                {
                    joiners++;
                }
            }

            if (joiners == 0)
            {
                FindWordStartsWithoutJoiners(run, graphemes, script, dictionary, starts, offset);
                return;
            }

            var compact = new int[run.Length - joiners];
            var original = new int[compact.Length];
            var found = new bool[compact.Length];
            var compactGraphemes = new bool[compact.Length];
            int length = 0;
            for (int i = 0; i < run.Length; i++)
            {
                if (run[i] is not (0x200C or 0x200D))
                {
                    compact[length] = run[i];
                    compactGraphemes[length] = graphemes[i];
                    original[length++] = i;
                }
            }

            FindWordStartsWithoutJoiners(compact, compactGraphemes, script, dictionary, found, 0);
            for (int j = 1; j < length; j++)
            {
                if (found[j] && run[original[j] - 1] is not (0x200C or 0x200D))
                {
                    starts[offset + original[j]] = true;
                }
            }
        }

        private static void FindWordStartsWithoutJoiners(ReadOnlySpan<int> run, ReadOnlySpan<bool> graphemes, ComplexScript script, WordDictionary dictionary, bool[] starts, int offset)
        {
            int length = run.Length;
            var clusterBoundary = ArrayPool<bool>.Shared.Rent(length + 1);
            try
            {
                for (int i = 0; i <= length; i++)
                {
                    clusterBoundary[i] = i == 0 || i == length || (graphemes[i] && IsClusterBoundary(script, run, i));
                }

                for (int start = 0; start < length;)
                {
                    int end = start + MaxChunk;
                    while (end < length && !clusterBoundary[end])
                    {
                        end++;
                    }

                    end = Math.Min(end, length);
                    if (start > 0)
                    {
                        starts[offset + start] = true;
                    }

                    FindChunk(run[start..end], clusterBoundary.AsSpan(start, end - start + 1), dictionary, starts, offset + start);
                    start = end;
                }
            }
            finally
            {
                ArrayPool<bool>.Shared.Return(clusterBoundary);
            }
        }

        private static void FindChunk(ReadOnlySpan<int> text, ReadOnlySpan<bool> boundary, WordDictionary dictionary, bool[] starts, int offset)
        {
            int length = text.Length;
            var covered1 = ArrayPool<int>.Shared.Rent(length + 1);
            var covered2 = ArrayPool<int>.Shared.Rent(length + 1);
            covered1.AsSpan(0, length + 1).Fill(NotComputed);
            covered2.AsSpan(0, length + 1).Fill(NotComputed);
            Span<int> candidates = stackalloc int[Math.Max(dictionary.Longest, 1)];

            try
            {
                bool inUnknown = false;
                int position = 0;
                while (position < length)
                {
                    int count = Candidates(text, boundary, dictionary, position, candidates);
                    if (count == 0)
                    {
                        // No word starts here: the cluster stays with the unknown stretch it is part of, which is cut off from the words
                        // around it and never inside.
                        if (!inUnknown && position > 0)
                        {
                            starts[offset + position] = true;
                        }

                        inUnknown = true;
                        position = NextBoundary(boundary, position);
                        continue;
                    }

                    int best = -1;
                    int bestScore = int.MinValue;
                    for (int c = count - 1; c >= 0; c--)
                    {
                        int score = candidates[c] + Covered(text, boundary, dictionary, position + candidates[c], Lookahead - 1, covered1, covered2);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = candidates[c];
                        }
                    }

                    if (position > 0)
                    {
                        starts[offset + position] = true;
                    }

                    inUnknown = false;
                    position += best;
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(covered1);
                ArrayPool<int>.Shared.Return(covered2);
            }
        }

        /// <summary>
        /// The best score of covering the text from <paramref name="position"/> with at most <paramref name="depth"/> more words, where
        /// each character of a word scores one and each cluster no word starts at costs <see cref="UnknownPenalty"/>. Depths 1 and 2 are
        /// kept, so every position is worked out once per depth.
        /// </summary>
        private static int Covered(ReadOnlySpan<int> text, ReadOnlySpan<bool> boundary, WordDictionary dictionary, int position, int depth,
            int[] covered1, int[] covered2)
        {
            if (depth == 0 || position >= text.Length)
            {
                return 0;
            }

            var memo = depth == 1 ? covered1 : covered2;
            if (memo[position] != NotComputed)
            {
                return memo[position];
            }

            Span<int> candidates = stackalloc int[dictionary.Longest];
            int count = Candidates(text, boundary, dictionary, position, candidates);
            int best;
            if (count == 0)
            {
                best = Covered(text, boundary, dictionary, NextBoundary(boundary, position), depth - 1, covered1, covered2) - UnknownPenalty;
            }
            else
            {
                best = int.MinValue;
                for (int c = 0; c < count; c++)
                {
                    best = Math.Max(best, candidates[c] + Covered(text, boundary, dictionary, position + candidates[c], depth - 1, covered1, covered2));
                }
            }

            memo[position] = best;
            return best;
        }

        /// <summary>The lengths of the words that start at <paramref name="position"/> and end on a cluster boundary, shortest first.</summary>
        private static int Candidates(ReadOnlySpan<int> text, ReadOnlySpan<bool> boundary, WordDictionary dictionary, int position, Span<int> lengths)
        {
            int found = dictionary.FindPrefixes(text[position..], lengths);
            int kept = 0;
            for (int i = 0; i < found; i++)
            {
                if (boundary[position + lengths[i]])
                {
                    lengths[kept++] = lengths[i];
                }
            }

            return kept;
        }

        private static int NextBoundary(ReadOnlySpan<bool> boundary, int position)
        {
            int next = position + 1;
            while (!boundary[next])
            {
                next++;
            }

            return next;
        }

        /// <summary>Whether a word may end before <c>run[index]</c> and the next start there: <paramref name="index"/> is in 1 to the length less one.</summary>
        private static bool IsClusterBoundary(ComplexScript script, ReadOnlySpan<int> run, int index)
        {
            int code = run[index];
            int previous = run[index - 1];
            if (CannotStartWord(script, code) || CannotEndWord(script, previous))
            {
                return false;
            }

            // A Burmese consonant with an asat is the end of the syllable before it, unless the asat belongs to a kinzi (the asat and
            // then a virama, which stack the consonant over the next one, and begin a syllable).
            if (script == ComplexScript.Burmese && index + 1 < run.Length && run[index + 1] == 0x103A
                && !(index + 2 < run.Length && run[index + 2] == 0x1039))
            {
                return false;
            }

            return true;
        }

        /// <summary>A character that is written with the one before it: a dependent vowel, a tone mark, a sign, a repetition mark.</summary>
        private static bool CannotStartWord(ComplexScript script, int code)
        {
            if (SegmentationData.IsComplexContextMark(code))
            {
                return true;
            }

            return script switch
            {
                // paiyannoi, sara a, sara aa, sara am, lakkhangyao, maiyamok
                ComplexScript.Thai => code is 0x0E2F or 0x0E30 or 0x0E32 or 0x0E33 or 0x0E45 or 0x0E46,
                // ellipsis, sara a, sara aa, sara am, semivowel yo, ko la
                ComplexScript.Lao => code is 0x0EAF or 0x0EB0 or 0x0EB2 or 0x0EB3 or 0x0EBD or 0x0EC6,
                // lek too, avakrahasanya
                ComplexScript.Khmer => code is 0x17D7 or 0x17DC,
                _ => false,
            };
        }

        /// <summary>A character that is written before the one it goes with: a leading vowel, a subscript or stacking sign.</summary>
        private static bool CannotEndWord(ComplexScript script, int code) => script switch
        {
            ComplexScript.Thai => code is >= 0x0E40 and <= 0x0E44,
            ComplexScript.Lao => code is >= 0x0EC0 and <= 0x0EC4,
            ComplexScript.Khmer => code == 0x17D2,
            ComplexScript.Burmese => code == 0x1039,
            _ => false,
        };
    }
}

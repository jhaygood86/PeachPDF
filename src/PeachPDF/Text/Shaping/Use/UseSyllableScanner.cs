// Grammar ported from HarfBuzz's src/hb-ot-shaper-use-machine.rl, retrieved 2026-09-05 from
// https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper-use-machine.rl
//
// Copyright © 2015  Mozilla Foundation.
// Copyright © 2015  Google, Inc.
//
//  This is part of HarfBuzz, a text shaping library.
//
// Permission is hereby granted, without written agreement and without
// license or royalty fees, to use, copy, modify, and distribute this
// software and its documentation for any purpose, provided that the
// above copyright notice and the following two paragraphs appear in
// all copies of this software.
//
// IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR
// DIRECT, INDIRECT, SPECIAL, INCIDENTAL, OR CONSEQUENTIAL DAMAGES
// ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION, EVEN
// IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH
// DAMAGE.
//
// THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING,
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE.  THE SOFTWARE PROVIDED HEREUNDER IS
// ON AN "AS IS" BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO
// PROVIDE MAINTENANCE, SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.
//
// Mozilla Author(s): Jonathan Kew
// Google Author(s): Behdad Esfahbod
//
// See THIRD-PARTY-LICENSES.md for how this fits into PeachPDF's own licensing.

using System.Collections.Generic;

namespace PeachPDF.Text.Shaping.Use
{
    /// <summary>
    /// Splits a run of <see cref="UseCategory"/> values into <see cref="UseSyllable"/>s - a
    /// hand-written scanner standing in for HarfBuzz's own Ragel-generated syllable machine
    /// (<c>hb-ot-shaper-use-machine.rl</c>, retrieved 2026-09-05 from
    /// https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper-use-machine.rl - Copyright ©
    /// 2015 Mozilla Foundation, Copyright © 2015 Google, Inc., "Old MIT" license, see
    /// THIRD-PARTY-LICENSES.md) since this repo has no Ragel toolchain (matching this feature's own
    /// plan/precedent for why a hand-written scanner replaces the state machine specifically, while
    /// the category/reorder logic elsewhere is still a direct algorithmic port).
    ///
    /// <b>Grammar</b> (HarfBuzz's real grammar, reduced to exactly the alternatives a
    /// Devanagari/Bengali/Gujarati/Tamil-only <see cref="UseCategory"/> stream can ever exercise - see
    /// <see cref="UseCategoryClassifier"/>'s own remarks on scope):
    /// <code>
    /// consonant_modifiers = CMAbv* CMBlw* ( (H B) CMAbv* CMBlw* )*
    /// dependent_vowels    = VPre* VAbv* VBlw* VPst* | H
    /// vowel_modifiers     = VMPre* VMAbv* VMBlw* VMPst*
    /// final_modifiers     = FMAbv*
    /// tail                = consonant_modifiers dependent_vowels vowel_modifiers final_modifiers
    /// standard_cluster    = (B | GB) tail
    /// broken_cluster      = tail                       (tail's own first token, no leading B/GB)
    /// symbol_cluster      = O tail?
    /// </code>
    /// <c>final_modifiers</c> and <see cref="UseCategory.GB"/> as an alternate cluster-start token are
    /// this scanner's own Bengali-driven extension of the grammar (HarfBuzz's real
    /// <c>complex_syllable_start = (R | CS)? (B | GB)</c> and <c>final_modifiers = FMAbv* FMBlw* |
    /// FMPst?</c>, reduced to the FM-family member this classifier's scope actually produces - see
    /// <see cref="UseCategory.FMAbv"/>'s own remarks). A GB-led syllable is scanned identically to a
    /// B-led one (<see cref="UseSyllableType.StandardCluster"/>, fully reorderable) - real HarfBuzz's
    /// own grammar also lists <c>GB</c> as an alternative <c>symbol_cluster</c> leading token, but since
    /// its Ragel machine always prefers the longest match and <c>standard_cluster</c>'s own grammar is
    /// a strict superset of <c>symbol_cluster</c>'s whenever a GB glyph is followed by tail content,
    /// treating GB exactly like B here reproduces the identical practical outcome without needing a
    /// backtracking scanner.
    ///
    /// Every real Devanagari/Bengali/Gujarati/Tamil syllable type HarfBuzz's own grammar defines beyond
    /// these three (<c>virama_terminated_cluster</c>, <c>sakot_terminated_cluster</c>,
    /// <c>number_joiner_terminated_cluster</c>, <c>numeral_cluster</c>, <c>hieroglyph_cluster</c>)
    /// structurally requires a <see cref="UseCategory"/> member (<c>IS</c>/<c>RK</c>/<c>Sk</c>/
    /// <c>N</c>/<c>HN</c>/hieroglyph categories) this classifier never produces for a codepoint in any
    /// of these four scripts, so they can never match - omitted rather than dead code.
    ///
    /// Unlike HarfBuzz's own Ragel machine (which tries every alternative at each position and keeps
    /// the longest match, since some scripts' grammars are genuinely ambiguous at the first token),
    /// this reduced grammar's three cluster-starting alternatives (<see cref="UseCategory.B"/>,
    /// <see cref="UseCategory.O"/>, or any tail-starting category) are mutually exclusive by their
    /// own first token for Devanagari, so a single deterministic dispatch on the current category is
    /// sufficient - no backtracking or lookahead needed.
    ///
    /// <see cref="UseCategory.CGJ"/> is filtered out of the grammar entirely (mirroring HarfBuzz's own
    /// <c>find_syllables_use</c> pre-filter): a CGJ glyph is absorbed into the immediately preceding
    /// syllable when one directly precedes it (extending that syllable's own span by one, without
    /// re-entering its grammar), or becomes its own trivial <see cref="UseSyllableType.NonCluster"/>
    /// otherwise. A CGJ glyph appearing in the *middle* of what would otherwise be one syllable's tail
    /// (e.g. a ZWJ between a base and its nukta) ends that syllable early rather than being skipped
    /// transparently and resuming the interrupted grammar - a documented, narrow simplification (see
    /// <c>.claude/accepted-gaps/no-text-shaping.md</c>) accepted because embedded, non-initial ZWJ is
    /// rare in real Devanagari text; it degrades to two adjacent syllables instead of one; and a
    /// pre-base vowel from the wrong sub-syllable never becomes visible, since <see cref="UseReorderer"/>
    /// stays entirely inside each syllable's own bounds.
    ///
    /// Unlike HarfBuzz's own scanner, this one does not special-case <see cref="UseCategory.ZWNJ"/>'s
    /// visibility based on what follows it (HarfBuzz keeps a ZWNJ visible to the grammar only when a
    /// combining mark follows, otherwise drops it) - here, a trailing ZWNJ is always consumed as the
    /// current syllable's own optional final token (matching every grammar alternative's own trailing
    /// <c>... ZWNJ?</c>), which achieves the same practical outcome for the common case (ZWNJ right
    /// after a virama, forcing/blocking conjunct formation) without the extra lookahead rule.
    /// </summary>
    internal static class UseSyllableScanner
    {
        public static List<UseSyllable> Scan(IReadOnlyList<UseCategory> categories)
        {
            var syllables = new List<UseSyllable>();
            int n = categories.Count;
            int i = 0;

            while (i < n)
            {
                int start = i;
                UseCategory cat = categories[i];

                if (cat == UseCategory.CGJ)
                {
                    if (syllables.Count > 0 && syllables[^1].Start + syllables[^1].Length == i)
                    {
                        UseSyllable last = syllables[^1];
                        syllables[^1] = last with { Length = last.Length + 1 };
                    }
                    else
                    {
                        syllables.Add(new UseSyllable(i, 1, UseSyllableType.NonCluster));
                    }
                    i++;
                    continue;
                }

                if (cat == UseCategory.B || cat == UseCategory.GB)
                {
                    i = ConsumeTail(categories, i + 1);
                    i = ConsumeTrailingZwnj(categories, i);
                    syllables.Add(new UseSyllable(start, i - start, UseSyllableType.StandardCluster));
                    continue;
                }

                if (cat == UseCategory.O)
                {
                    i = ConsumeTail(categories, i + 1);
                    i = ConsumeTrailingZwnj(categories, i);
                    syllables.Add(new UseSyllable(start, i - start, UseSyllableType.SymbolCluster));
                    continue;
                }

                if (IsTailStart(cat))
                {
                    i = ConsumeTail(categories, i);
                    i = ConsumeTrailingZwnj(categories, i);
                    syllables.Add(new UseSyllable(start, i - start, UseSyllableType.BrokenCluster));
                    continue;
                }

                // R (only reachable if something upstream pre-tagged it, which never happens before
                // this scan runs - see UseCategory.R's own remarks) and anything else (a lone ZWNJ,
                // or a category this classifier never assigns) - a single-glyph fallback, matching
                // the grammar's own catch-all `other => use_non_cluster` rule.
                i++;
                syllables.Add(new UseSyllable(start, i - start, UseSyllableType.NonCluster));
            }

            return syllables;
        }

        private static bool IsTailStart(UseCategory cat) => cat is
            UseCategory.CMAbv or UseCategory.CMBlw or
            UseCategory.VPre or UseCategory.VAbv or UseCategory.VBlw or UseCategory.VPst or
            UseCategory.VMPre or UseCategory.VMAbv or UseCategory.VMBlw or UseCategory.VMPst or
            UseCategory.FMAbv or
            UseCategory.H;

        /// <summary><c>tail = consonant_modifiers dependent_vowels vowel_modifiers final_modifiers</c> -
        /// may consume zero tokens (every component can be empty), which is exactly what lets
        /// <see cref="UseSyllableType.SymbolCluster"/>'s own trailing <c>tail?</c> be optional.</summary>
        private static int ConsumeTail(IReadOnlyList<UseCategory> categories, int i)
        {
            i = ConsumeConsonantModifiers(categories, i);
            i = ConsumeDependentVowels(categories, i);
            i = ConsumeVowelModifiers(categories, i);
            i = ConsumeFinalModifiers(categories, i);
            return i;
        }

        /// <summary><c>consonant_modifiers = CMAbv* CMBlw* ( (H B) CMAbv* CMBlw* )*</c> - the
        /// <c>(H B)</c> repeat is what extends a syllable across a full multi-consonant conjunct
        /// (e.g. Devanagari's क्ष, KA + VIRAMA + SSA): each halant-joined additional base consonant
        /// stays part of the *same* syllable, not a new one, exactly like real conjunct
        /// orthography.</summary>
        private static int ConsumeConsonantModifiers(IReadOnlyList<UseCategory> categories, int i)
        {
            int n = categories.Count;
            i = ConsumeWhile(categories, i, UseCategory.CMAbv);
            i = ConsumeWhile(categories, i, UseCategory.CMBlw);
            while (i + 1 < n && categories[i] == UseCategory.H && categories[i + 1] == UseCategory.B)
            {
                i += 2;
                i = ConsumeWhile(categories, i, UseCategory.CMAbv);
                i = ConsumeWhile(categories, i, UseCategory.CMBlw);
            }
            return i;
        }

        /// <summary><c>dependent_vowels = VPre* VAbv* VBlw* VPst* | H</c> - the <c>| H</c> alternative
        /// handles a syllable-final halant with no further conjunct member after it (a word simply
        /// ending on a virama), consuming it here rather than leaving it to start a stray
        /// syllable/broken cluster of its own.</summary>
        private static int ConsumeDependentVowels(IReadOnlyList<UseCategory> categories, int i)
        {
            if (i < categories.Count && categories[i] == UseCategory.H)
                return i + 1;

            i = ConsumeWhile(categories, i, UseCategory.VPre);
            i = ConsumeWhile(categories, i, UseCategory.VAbv);
            i = ConsumeWhile(categories, i, UseCategory.VBlw);
            i = ConsumeWhile(categories, i, UseCategory.VPst);
            return i;
        }

        /// <summary><c>vowel_modifiers = VMPre* VMAbv* VMBlw* VMPst*</c>.</summary>
        private static int ConsumeVowelModifiers(IReadOnlyList<UseCategory> categories, int i)
        {
            i = ConsumeWhile(categories, i, UseCategory.VMPre);
            i = ConsumeWhile(categories, i, UseCategory.VMAbv);
            i = ConsumeWhile(categories, i, UseCategory.VMBlw);
            i = ConsumeWhile(categories, i, UseCategory.VMPst);
            return i;
        }

        /// <summary>HarfBuzz's own <c>final_modifiers = FMAbv* FMBlw* | FMPst?</c>, reduced to
        /// <c>FMAbv*</c> - the only FM-family member reachable for Devanagari/Bengali/Gujarati/Tamil
        /// (Bengali's own Sandhi Mark, U+09FE - see <see cref="UseCategory.FMAbv"/>'s own
        /// remarks).</summary>
        private static int ConsumeFinalModifiers(IReadOnlyList<UseCategory> categories, int i) =>
            ConsumeWhile(categories, i, UseCategory.FMAbv);

        private static int ConsumeTrailingZwnj(IReadOnlyList<UseCategory> categories, int i) =>
            i < categories.Count && categories[i] == UseCategory.ZWNJ ? i + 1 : i;

        private static int ConsumeWhile(IReadOnlyList<UseCategory> categories, int i, UseCategory target)
        {
            while (i < categories.Count && categories[i] == target)
                i++;
            return i;
        }
    }
}

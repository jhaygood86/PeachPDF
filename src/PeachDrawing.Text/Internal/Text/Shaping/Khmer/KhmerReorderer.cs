// ReorderSyllable ported from HarfBuzz's src/hb-ot-shaper-khmer.cc (reorder_consonant_syllable/
// reorder_syllable_khmer), retrieved 2026-09-27 from
// https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper-khmer.cc (commit
// 409c467b8259ad5fcc3fdcc477a1796fec256853)
//
// Copyright © 2011,2012  Google, Inc.
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
// Google Author(s): Behdad Esfahbod
//
// See THIRD-PARTY-LICENSES.md for how this fits into PeachPDF's own licensing.

using PeachDrawing.Text.Unicode;
using PeachDrawing.Text.Shaping;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Text.Shaping.Khmer
{
    /// <summary>
    /// The Khmer shaper's own glyph-array reorder pass - a direct port of HarfBuzz's
    /// <c>reorder_consonant_syllable</c>/<c>reorder_syllable_khmer</c> (retrieved 2026-09-27 from
    /// https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper-khmer.cc, commit
    /// 409c467b8259ad5fcc3fdcc477a1796fec256853 - Copyright © 2011,2012 Google, Inc., "Old MIT"
    /// license, see THIRD-PARTY-LICENSES.md), adapted to PeachPDF's own <see cref="PlacedGlyph"/> list
    /// (a plain shift-and-drop loop in place of HarfBuzz's <c>memmove</c>, exactly like
    /// <see cref="Use.UseReorderer"/> already does for the Universal Shaping Engine) and to a plain
    /// mutable <see cref="KhmerCategory"/> array kept in lockstep with the glyph list (see
    /// <see cref="Use.UseReorderer"/>'s own remarks on why - the same reasoning applies verbatim here).
    ///
    /// Unlike <see cref="Use.UseReorderer"/>'s two independent passes, this is <b>one single
    /// left-to-right loop</b> over each reorderable syllable's own <c>[start, end)</c> span - matching
    /// HarfBuzz's real Khmer-specific code exactly, which is not equivalent to two separate passes: a
    /// syllable-initial <see cref="KhmerCategory.VPre"/> vowel that appears *after* a coeng+
    /// <see cref="KhmerCategory.Ra"/> pair in logical order still ends up positioned *before* it in the
    /// reordered result (both move to the syllable's own start, and whichever the loop reaches second
    /// lands closer to the front) - verified byte-for-byte against real HarfBuzz's own output via
    /// <c>uharfbuzz</c> for exactly this sequence (KA+COENG+RO+VOWEL-SIGN-E) in
    /// <c>KhmerUseShapingCharacterizationTests</c>. Real HarfBuzz's own mask assignment in this same
    /// loop (tagging the moved coeng+Ra pair for its font's own <c>pref</c> feature, and every
    /// subsequent glyph for <c>cfar</c>) is not ported - see <c>GsubShaper</c>'s own Khmer-stage remarks
    /// on why applying <c>pref</c>/<c>blwf</c>/<c>abvf</c>/<c>pstf</c> globally after this reorder runs
    /// reproduces the identical result for well-formed text, and <c>.claude/accepted-gaps/no-text-shaping.md</c>
    /// for why <c>cfar</c> itself is out of scope.
    /// </summary>
    internal static class KhmerReorderer
    {
        /// <summary>The syllable types HarfBuzz's own <c>reorder_syllable_khmer</c> actually reorders -
        /// both by calling <c>reorder_consonant_syllable</c> (a <see cref="KhmerSyllableType.BrokenCluster"/>
        /// is simply one HarfBuzz has already inserted a dotted circle into, per its own comment "we
        /// already inserted dotted-circles, so just call the consonant_syllable"; this port does not
        /// insert a dotted circle - see <c>.claude/accepted-gaps/no-text-shaping.md</c> - so a broken
        /// cluster here is reordered exactly as found, without one).
        /// <see cref="KhmerSyllableType.NonKhmerCluster"/> is never reordered.</summary>
        private static bool IsReorderable(KhmerSyllableType type) => type is
            KhmerSyllableType.ConsonantSyllable or KhmerSyllableType.BrokenCluster;

        /// <summary>Reorders every eligible syllable in <paramref name="syllables"/> in place, over
        /// <paramref name="glyphs"/>/<paramref name="categories"/> (kept in lockstep - see this class's
        /// own remarks).</summary>
        public static void ReorderAll(List<PlacedGlyph> glyphs, KhmerCategory[] categories, IReadOnlyList<KhmerSyllable> syllables)
        {
            foreach (KhmerSyllable syllable in syllables)
            {
                if (IsReorderable(syllable.Type))
                    ReorderSyllable(glyphs, categories, syllable.Start, syllable.Start + syllable.Length);
            }
        }

        internal static void ReorderSyllable(List<PlacedGlyph> glyphs, KhmerCategory[] categories, int start, int end)
        {
            // "When a COENG + (Cons | IndV) combination are found (and subscript count is less than
            // two) the character combination is handled according to the subscript type of the
            // character following the COENG. ... Subscript Type 2 - The COENG + RO characters are
            // reordered to immediately before the base glyph." (the spec comment HarfBuzz's own source
            // quotes verbatim).
            int numCoengs = 0;
            for (int i = start + 1; i < end; i++)
            {
                if (categories[i] == KhmerCategory.H && numCoengs <= 2 && i + 1 < end)
                {
                    numCoengs++;

                    if (categories[i + 1] == KhmerCategory.Ra)
                    {
                        // Move the Coeng,Ro sequence to the start.
                        MoveRangeToStart(glyphs, categories, start, i, count: 2);
                        numCoengs = 2; // Done.
                    }
                }
                // Reorder left matra piece.
                else if (categories[i] == KhmerCategory.VPre)
                {
                    MoveRangeToStart(glyphs, categories, start, i, count: 1);
                }
            }
        }

        /// <summary>Moves the <paramref name="count"/> glyphs starting at <paramref name="from"/>
        /// (<c>from &gt;= start</c>) to <paramref name="start"/>, shifting every glyph in between
        /// forward by <paramref name="count"/> - HarfBuzz's own <c>memmove</c> expressed as a loop,
        /// since a syllable never spans more than a handful of glyphs. <paramref name="count"/> is
        /// always 1 (a single <see cref="KhmerCategory.VPre"/> glyph) or 2 (a coeng+
        /// <see cref="KhmerCategory.Ra"/> pair) - see <see cref="ReorderSyllable"/>'s own two call
        /// sites.</summary>
        private static void MoveRangeToStart(List<PlacedGlyph> glyphs, KhmerCategory[] categories, int start, int from, int count)
        {
            PlacedGlyph movedGlyph0 = glyphs[from];
            KhmerCategory movedCategory0 = categories[from];
            PlacedGlyph movedGlyph1 = default;
            KhmerCategory movedCategory1 = default;
            if (count == 2)
            {
                movedGlyph1 = glyphs[from + 1];
                movedCategory1 = categories[from + 1];
            }

            for (int k = from - 1; k >= start; k--)
            {
                glyphs[k + count] = glyphs[k];
                categories[k + count] = categories[k];
            }

            glyphs[start] = movedGlyph0;
            categories[start] = movedCategory0;
            if (count == 2)
            {
                glyphs[start + 1] = movedGlyph1;
                categories[start + 1] = movedCategory1;
            }
        }
    }
}

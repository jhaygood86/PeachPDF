// ReorderSyllable ported from HarfBuzz's src/hb-ot-shaper-use.cc (reorder_syllable_use), retrieved
// 2026-09-05 from https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper-use.cc
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
using PeachPDF.Text;

namespace PeachPDF.Text.Shaping.Use
{
    /// <summary>
    /// The Universal Shaping Engine's own glyph-array reorder pass - a direct port of HarfBuzz's
    /// <c>reorder_syllable_use</c> (retrieved 2026-09-05 from
    /// https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper-use.cc - Copyright © 2015
    /// Mozilla Foundation, Copyright © 2015 Google, Inc., "Old MIT" license, see
    /// THIRD-PARTY-LICENSES.md), adapted to PeachPDF's own <see cref="ShapedGlyph"/> list (a plain
    /// shift-and-drop loop in place of HarfBuzz's <c>memmove</c> - equivalent for the handful of
    /// glyphs any one syllable ever spans) and to a plain mutable <see cref="UseCategory"/> array kept
    /// in lockstep with the glyph list (HarfBuzz stores the category directly on each
    /// <c>hb_glyph_info_t</c>, so its own array-of-structs already moves the category with the glyph;
    /// PeachPDF's <see cref="ShapedGlyph"/> doesn't carry a USE category field of its own, so this
    /// class's caller (<c>GsubShaper</c>'s USE-gated stage) must shift the category array by exactly
    /// the same operations it applies to the glyph list, which is exactly what each method below does
    /// whenever it moves a glyph).
    ///
    /// Two independent passes, run in this order, matching HarfBuzz's own:
    /// <list type="number">
    /// <item><b>Move things forward</b> - repositions a syllable-initial <see cref="UseCategory.R"/>
    /// (repha) glyph forward, to immediately before the syllable's first post-base-category glyph (or
    /// to the syllable's last position, if none).</item>
    /// <item><b>Move things back</b> - repositions every <see cref="UseCategory.VPre"/>/
    /// <see cref="UseCategory.VMPre"/> glyph backward, to immediately after the nearest preceding
    /// halant (or to the syllable start, if none).</item>
    /// </list>
    /// Neither pass ever crosses a syllable boundary - <see cref="ReorderAll"/> calls
    /// <see cref="ReorderSyllable"/> once per syllable, each confined to its own <c>[start, end)</c>
    /// span.
    /// </summary>
    internal static class UseReorderer
    {
        /// <summary>The syllable types HarfBuzz's own <c>reorder_syllable_use</c> actually reorders
        /// (its own early-return checks a flag set covering <c>virama_terminated_cluster</c>/
        /// <c>sakot_terminated_cluster</c>/<c>standard_cluster</c>/<c>symbol_cluster</c>/
        /// <c>broken_cluster</c> - the first two are never produced by <see cref="UseSyllableScanner"/>
        /// for Devanagari, per its own remarks, so only the latter three ever reach here in
        /// practice).</summary>
        private static bool IsReorderable(UseSyllableType type) => type is
            UseSyllableType.StandardCluster or UseSyllableType.BrokenCluster or UseSyllableType.SymbolCluster;

        /// <summary>Reorders every eligible syllable in <paramref name="syllables"/> in place, over
        /// <paramref name="glyphs"/>/<paramref name="categories"/> (kept in lockstep - see this
        /// class's own remarks).</summary>
        public static void ReorderAll(List<ShapedGlyph> glyphs, UseCategory[] categories, IReadOnlyList<UseSyllable> syllables)
        {
            foreach (UseSyllable syllable in syllables)
            {
                if (IsReorderable(syllable.Type))
                    ReorderSyllable(glyphs, categories, syllable.Start, syllable.Start + syllable.Length);
            }
        }

        internal static void ReorderSyllable(List<ShapedGlyph> glyphs, UseCategory[] categories, int start, int end)
        {
            if (end - start < 1)
                return;

            // Pass 1: move a syllable-initial repha forward, to immediately before the first
            // post-base-category glyph (or the syllable's last position, if none).
            if (categories[start] == UseCategory.R && end - start > 1)
            {
                for (int i = start + 1; i < end; i++)
                {
                    bool isPostBaseGlyph = IsPostBaseCategory(categories[i]) || IsHalant(categories[i]);
                    if (isPostBaseGlyph || i == end - 1)
                    {
                        int target = isPostBaseGlyph ? i - 1 : i;
                        MoveForward(glyphs, categories, start, target);
                        break;
                    }
                }
            }

            // Pass 2: move every pre-base vowel/vowel-modifier back to immediately after the nearest
            // preceding halant (or the syllable start, if none).
            int j = start;
            for (int i = start; i < end; i++)
            {
                if (IsHalant(categories[i]))
                {
                    j = i + 1;
                }
                else if ((categories[i] == UseCategory.VPre || categories[i] == UseCategory.VMPre) && j < i)
                {
                    MoveBackward(glyphs, categories, i, j);
                }
            }
        }

        /// <summary>Moves the glyph at <paramref name="from"/> to <paramref name="to"/> (<c>to &lt;
        /// from</c>), shifting every glyph in between forward by one - HarfBuzz's own <c>memmove</c>
        /// expressed as a loop, since a syllable never spans more than a handful of glyphs.</summary>
        private static void MoveForward(List<ShapedGlyph> glyphs, UseCategory[] categories, int from, int to)
        {
            ShapedGlyph movedGlyph = glyphs[from];
            UseCategory movedCategory = categories[from];
            for (int k = from; k < to; k++)
            {
                glyphs[k] = glyphs[k + 1];
                categories[k] = categories[k + 1];
            }
            glyphs[to] = movedGlyph;
            categories[to] = movedCategory;
        }

        /// <summary>Moves the glyph at <paramref name="from"/> to <paramref name="to"/> (<c>to &lt;
        /// from</c>), shifting every glyph in between backward by one.</summary>
        private static void MoveBackward(List<ShapedGlyph> glyphs, UseCategory[] categories, int from, int to)
        {
            ShapedGlyph movedGlyph = glyphs[from];
            UseCategory movedCategory = categories[from];
            for (int k = from; k > to; k--)
            {
                glyphs[k] = glyphs[k - 1];
                categories[k] = categories[k - 1];
            }
            glyphs[to] = movedGlyph;
            categories[to] = movedCategory;
        }

        /// <summary>HarfBuzz's own <c>POST_BASE_FLAGS64</c>, reduced to the members
        /// <see cref="UseCategory"/> can actually hold (the full macro also lists <c>FAbv</c>/
        /// <c>FBlw</c>/<c>FPst</c>/<c>FMAbv</c>/<c>FMBlw</c>/<c>FMPst</c>/<c>MAbv</c>/<c>MBlw</c>/
        /// <c>MPst</c>/<c>MPre</c> - final-consonant and medial-consonant categories no Devanagari
        /// codepoint ever produces, per <see cref="UseCategoryClassifier"/>'s own remarks). Notably
        /// does NOT include <see cref="UseCategory.CMAbv"/>/<see cref="UseCategory.CMBlw"/> (a nukta)
        /// - matching HarfBuzz's own macro exactly - so a nukta between a repha and its target
        /// position does not stop pass 1's forward search.</summary>
        private static bool IsPostBaseCategory(UseCategory category) => category is
            UseCategory.VPre or UseCategory.VAbv or UseCategory.VBlw or UseCategory.VPst or
            UseCategory.VMPre or UseCategory.VMAbv or UseCategory.VMBlw or UseCategory.VMPst;

        /// <summary>HarfBuzz's own <c>is_halant_use</c>, minus its "not ligated" guard - a virama that
        /// a font's <c>half</c>/<c>cjct</c> feature merged away as part of a conjunct ligature simply
        /// has no surviving glyph of its own by the time this runs (see <see cref="GsubShaper"/>'s own
        /// USE-stage remarks on why re-deriving categories from each glyph's current
        /// <see cref="ShapedGlyph.ClusterStart"/> after every substitution stage makes that guard
        /// unnecessary here).</summary>
        private static bool IsHalant(UseCategory category) => category == UseCategory.H;
    }
}

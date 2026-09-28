// Grammar ported from HarfBuzz's src/hb-ot-shaper-khmer-machine.rl, retrieved 2026-09-27 from
// https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper-khmer-machine.rl (commit
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
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Text.Shaping.Khmer
{
    /// <summary>
    /// Splits a run of <see cref="KhmerCategory"/> values into <see cref="KhmerSyllable"/>s - a
    /// hand-written scanner standing in for HarfBuzz's own Ragel-generated syllable machine
    /// (<c>hb-ot-shaper-khmer-machine.rl</c>, retrieved 2026-09-27 from
    /// https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper-khmer-machine.rl, commit
    /// 409c467b8259ad5fcc3fdcc477a1796fec256853 - "Old MIT" license, see THIRD-PARTY-LICENSES.md) since
    /// this repo has no Ragel toolchain (matching <see cref="Use.UseSyllableScanner"/>'s own precedent
    /// for why a hand-written scanner replaces the state machine specifically, while the
    /// category/reorder logic elsewhere is still a direct algorithmic port).
    ///
    /// <b>Grammar</b> (HarfBuzz's own, unreduced - every alternative applies to real Khmer text):
    /// <code>
    /// c               = (C | Ra | V)
    /// cn              = c . ((ZWJ|ZWNJ)? . Robatic)?
    /// joiner          = (ZWJ | ZWNJ)
    /// xgroup          = (joiner* . Xgroup)*
    /// ygroup          = Ygroup*
    /// matra_group     = VPre? xgroup VBlw? xgroup (joiner? . VAbv)? xgroup VPst?
    /// syllable_tail   = xgroup matra_group xgroup (H . c)? ygroup
    /// broken_cluster  = Robatic? (H . cn)* (H | syllable_tail)
    /// consonant_syllable = (cn | Placeholder | DottedCircle) broken_cluster
    /// other           = any
    /// </code>
    /// HarfBuzz's own Ragel machine tries every alternative at each position and keeps the longest
    /// match (ties broken by rule order: <c>consonant_syllable</c>, then <c>broken_cluster</c>, then
    /// <c>other</c>). <c>consonant_syllable</c>'s own grammar is <c>(cn|Placeholder|DottedCircle)</c>
    /// followed by the *same* <c>broken_cluster</c> production, so whenever that leading token matches,
    /// <c>consonant_syllable</c> is always at least as long as <c>broken_cluster</c> alone would be
    /// starting there - a single deterministic dispatch on the current category (this scanner's own
    /// <see cref="IsConsonantSyllableStart"/>/<see cref="IsBrokenClusterStart"/>) is therefore sufficient
    /// with no backtracking, the same reasoning <see cref="Use.UseSyllableScanner"/>'s own remarks give
    /// for its reduced grammar. Byte-for-byte verified against real HarfBuzz's own output (via
    /// <c>uharfbuzz</c>) for a real Khmer font - see <c>KhmerUseShapingCharacterizationTests</c>.
    /// </summary>
    internal static class KhmerSyllableScanner
    {
        public static List<KhmerSyllable> Scan(IReadOnlyList<KhmerCategory> categories)
        {
            var syllables = new List<KhmerSyllable>();
            int n = categories.Count;
            int i = 0;

            while (i < n)
            {
                int start = i;
                KhmerCategory cat = categories[i];

                if (IsConsonantSyllableStart(cat))
                {
                    i = ConsumeConsonantSyllablePrefix(categories, i);
                    i = ConsumeBrokenCluster(categories, i);
                    syllables.Add(new KhmerSyllable(start, i - start, KhmerSyllableType.ConsonantSyllable));
                    continue;
                }

                if (IsBrokenClusterStart(cat))
                {
                    i = ConsumeBrokenCluster(categories, i);
                    syllables.Add(new KhmerSyllable(start, i - start, KhmerSyllableType.BrokenCluster));
                    continue;
                }

                // other => khmer_non_khmer_cluster - a single glyph that doesn't extend a cluster.
                i++;
                syllables.Add(new KhmerSyllable(start, i - start, KhmerSyllableType.NonKhmerCluster));
            }

            return syllables;
        }

        private static bool IsC(KhmerCategory cat) => cat is KhmerCategory.C or KhmerCategory.Ra or KhmerCategory.V;

        private static bool IsJoiner(KhmerCategory cat) => cat is KhmerCategory.ZWJ or KhmerCategory.ZWNJ;

        private static bool IsConsonantSyllableStart(KhmerCategory cat) =>
            IsC(cat) || cat is KhmerCategory.Placeholder or KhmerCategory.DottedCircle;

        /// <summary>Whether <see cref="ConsumeBrokenCluster"/> starting at a category of exactly
        /// <paramref name="cat"/> is guaranteed to consume at least one token - every first token
        /// <c>broken_cluster</c>'s own grammar can start on (<c>Robatic</c>; <c>H</c>, via either the
        /// <c>(H.cn)*</c> loop or the bare <c>H</c> alternative; or anything <c>syllable_tail</c> itself
        /// can open on - <c>Xgroup</c>/<c>Ygroup</c>/the vowel-sign categories, or a joiner leading into
        /// <c>xgroup</c>'s/<c>matra_group</c>'s own optional leading joiner).</summary>
        private static bool IsBrokenClusterStart(KhmerCategory cat) => cat is
            KhmerCategory.Robatic or KhmerCategory.H or
            KhmerCategory.Xgroup or KhmerCategory.Ygroup or
            KhmerCategory.VPre or KhmerCategory.VAbv or KhmerCategory.VBlw or KhmerCategory.VPst or
            KhmerCategory.ZWJ or KhmerCategory.ZWNJ;

        /// <summary><c>(cn | Placeholder | DottedCircle)</c> - the caller has already verified
        /// <see cref="IsConsonantSyllableStart"/>, so exactly one of the three ever applies.</summary>
        private static int ConsumeConsonantSyllablePrefix(IReadOnlyList<KhmerCategory> categories, int i) =>
            categories[i] is KhmerCategory.Placeholder or KhmerCategory.DottedCircle ? i + 1 : ConsumeCn(categories, i);

        /// <summary><c>cn = c . ((ZWJ|ZWNJ)? . Robatic)?</c> - the caller has already verified the
        /// token at <paramref name="i"/> is <see cref="IsC"/>.</summary>
        private static int ConsumeCn(IReadOnlyList<KhmerCategory> categories, int i)
        {
            int n = categories.Count;
            i++; // consume c
            int j = i;
            if (j < n && IsJoiner(categories[j]))
                j++;
            if (j < n && categories[j] == KhmerCategory.Robatic)
                return j + 1;
            return i; // no Robatic found - the optional joiner (if any) is not part of cn either.
        }

        /// <summary><c>xgroup = (joiner* . Xgroup)*</c> - greedily repeats "zero or more joiners then an
        /// Xgroup" for as long as an Xgroup actually follows; never consumes trailing joiners that turn
        /// out not to lead into one (they belong to whatever comes next instead).</summary>
        private static int ConsumeXgroup(IReadOnlyList<KhmerCategory> categories, int i)
        {
            int n = categories.Count;
            while (true)
            {
                int j = i;
                while (j < n && IsJoiner(categories[j]))
                    j++;
                if (j < n && categories[j] == KhmerCategory.Xgroup)
                    i = j + 1;
                else
                    return i;
            }
        }

        /// <summary><c>ygroup = Ygroup*</c>.</summary>
        private static int ConsumeYgroup(IReadOnlyList<KhmerCategory> categories, int i)
        {
            int n = categories.Count;
            while (i < n && categories[i] == KhmerCategory.Ygroup)
                i++;
            return i;
        }

        /// <summary><c>matra_group = VPre? xgroup VBlw? xgroup (joiner? . VAbv)? xgroup VPst?</c>.</summary>
        private static int ConsumeMatraGroup(IReadOnlyList<KhmerCategory> categories, int i)
        {
            int n = categories.Count;

            if (i < n && categories[i] == KhmerCategory.VPre)
                i++;
            i = ConsumeXgroup(categories, i);

            if (i < n && categories[i] == KhmerCategory.VBlw)
                i++;
            i = ConsumeXgroup(categories, i);

            int j = i;
            if (j < n && IsJoiner(categories[j]))
                j++;
            if (j < n && categories[j] == KhmerCategory.VAbv)
                i = j + 1;
            i = ConsumeXgroup(categories, i);

            if (i < n && categories[i] == KhmerCategory.VPst)
                i++;
            return i;
        }

        /// <summary><c>syllable_tail = xgroup matra_group xgroup (H . c)? ygroup</c>.</summary>
        private static int ConsumeSyllableTail(IReadOnlyList<KhmerCategory> categories, int i)
        {
            int n = categories.Count;

            i = ConsumeXgroup(categories, i);
            i = ConsumeMatraGroup(categories, i);
            i = ConsumeXgroup(categories, i);

            if (i + 1 < n && categories[i] == KhmerCategory.H && IsC(categories[i + 1]))
                i += 2;

            i = ConsumeYgroup(categories, i);
            return i;
        }

        /// <summary><c>broken_cluster = Robatic? (H . cn)* (H | syllable_tail)</c>. The final
        /// alternative always prefers <c>syllable_tail</c> when it consumes anything at all - the only
        /// way a bare <c>H</c> can tie with it in length is when <c>syllable_tail</c> matches nothing,
        /// which can never happen while the current token is itself <c>H</c> (none of
        /// <c>syllable_tail</c>'s own components can start on <c>H</c> except via its own trailing
        /// <c>(H.c)?</c>, which needs 2 tokens - see this method's own use of
        /// <see cref="ConsumeSyllableTail"/>).</summary>
        private static int ConsumeBrokenCluster(IReadOnlyList<KhmerCategory> categories, int i)
        {
            int n = categories.Count;

            if (i < n && categories[i] == KhmerCategory.Robatic)
                i++;

            while (i + 1 < n && categories[i] == KhmerCategory.H && IsC(categories[i + 1]))
            {
                i++; // consume H
                i = ConsumeCn(categories, i);
            }

            int afterTail = ConsumeSyllableTail(categories, i);
            if (afterTail > i)
                return afterTail;
            if (i < n && categories[i] == KhmerCategory.H)
                return i + 1;
            return i;
        }
    }
}

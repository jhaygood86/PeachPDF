// Ported from HarfBuzz's src/hb-ot-shaper-arabic.cc (arabic_joining), retrieved 2026-09-04 from
// https://github.com/harfbuzz/harfbuzz/blob/main/src/hb-ot-shaper-arabic.cc
//
// Copyright © 2010,2012  Google, Inc.
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

using System.Collections.Generic;

namespace PeachPDF.Text.Shaping.Arabic
{
    /// <summary>
    /// Resolves the Arabic-family cursive-joining form (<see cref="ArabicJoiningForm"/>) of every
    /// character in a logical-order codepoint sequence, driving <see cref="ArabicJoiningStateTable"/>'s
    /// ported state machine exactly the way HarfBuzz's own <c>arabic_joining</c> does. A pure function -
    /// no font/glyph dependency, so it's unit-testable without a real font, and no notion of "buffer
    /// context" (HarfBuzz's own pre/post-context handling exists because it shapes fixed-size buffer
    /// chunks for incremental/streaming use; PeachPDF shapes a whole logical-order span - a paragraph, or
    /// at least one contiguous same-script run within it - in a single call, which already gives the
    /// state machine full context with nothing left outside it to special-case).
    /// </summary>
    internal static class ArabicJoiningShaper
    {
        /// <summary>The five Syriac codepoints whose <c>Joining_Group</c> (not just <c>Joining_Type</c>)
        /// determines which state-table column they use - see <see cref="ArabicJoiningStateTable"/>'s
        /// own remarks. <c>Alaph</c> is a distinct group of exactly one codepoint; <c>DalathRish</c>
        /// covers the other four.</summary>
        private const int Alaph = 0x0710;

        /// <summary>Resolves the joining form of every character in <paramref name="codepoints"/>,
        /// processed in logical (not visual/bidi-reordered) order - joining is defined purely in terms
        /// of logical adjacency, independent of any bidi mirroring/reordering applied later for display.
        /// A codepoint whose <see cref="ArabicJoiningType"/> is <see cref="ArabicJoiningType.T"/>
        /// (Transparent - a combining mark) resolves to <see cref="ArabicJoiningForm.None"/> and is
        /// skipped over when determining adjacency between the characters around it (a diacritic between
        /// two joined letters must not break the join) - it neither reads nor advances the state.</summary>
        public static ArabicJoiningForm[] Resolve(IReadOnlyList<int> codepoints)
        {
            var count = codepoints.Count;
            var forms = new ArabicJoiningForm[count];

            var state = 0;
            var prev = -1; // index of the last non-Transparent character processed, or -1 if none yet.

            for (var i = 0; i < count; i++)
            {
                var codepoint = codepoints[i];
                var joiningType = ArabicShapingTable.Of(codepoint);

                if (joiningType == ArabicJoiningType.T)
                {
                    forms[i] = ArabicJoiningForm.None;
                    continue;
                }

                var column = GetColumn(codepoint, joiningType);
                var entry = ArabicJoiningStateTable.States[state][(int)column];

                // The table can retroactively upgrade the PREVIOUS participating character's form now
                // that this one's own joining type is known (e.g. an ISOL becomes a FINA once a
                // following letter turns out willing to join with it) - joining is inherently a
                // lookahead decision, which is exactly why this must run over the whole logical-order
                // span in one pass rather than character-by-character in isolation.
                if (entry.PrevAction != ArabicJoiningForm.None && prev >= 0)
                    forms[prev] = entry.PrevAction;

                forms[i] = entry.CurrAction;
                prev = i;
                state = entry.NextState;
            }

            return forms;
        }

        private static ArabicJoiningStateTable.Column GetColumn(int codepoint, ArabicJoiningType joiningType)
        {
            if (codepoint == Alaph)
                return ArabicJoiningStateTable.Column.Alaph;

            if (codepoint is 0x0715 or 0x0716 or 0x072A or 0x072F) // DALATH, DOTLESS DALATH RISH, RISH, PERSIAN DHALATH
                return ArabicJoiningStateTable.Column.DalathRish;

            return joiningType switch
            {
                ArabicJoiningType.U => ArabicJoiningStateTable.Column.U,
                ArabicJoiningType.L => ArabicJoiningStateTable.Column.L,
                ArabicJoiningType.R => ArabicJoiningStateTable.Column.R,
                ArabicJoiningType.D or ArabicJoiningType.C => ArabicJoiningStateTable.Column.D,
                _ => ArabicJoiningStateTable.Column.U, // ArabicJoiningType.T is filtered out by the caller before this is reached.
            };
        }
    }
}

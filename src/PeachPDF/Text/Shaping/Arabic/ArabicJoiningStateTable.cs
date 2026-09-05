// Ported from HarfBuzz's src/hb-ot-shaper-arabic.cc (arabic_state_table), retrieved 2026-09-04 from
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

namespace PeachPDF.Text.Shaping.Arabic
{
    /// <summary>
    /// The Arabic-family cursive-joining state machine's transition table - a direct port of HarfBuzz's
    /// <c>arabic_state_table</c>. 7 states × 6 columns; each entry says what positional form to
    /// (possibly retroactively) assign the *previous* participating character, what form to assign the
    /// *current* one, and which state to move to next - see <see cref="ArabicJoiningShaper"/> for how
    /// the table is driven.
    /// </summary>
    /// <remarks>
    /// The 6 columns are <c>Joining_Type</c> <c>U</c>/<c>L</c>/<c>R</c>/<c>D</c> (<c>C</c> maps onto the
    /// same column as <c>D</c> - both are "causes the neighbor to take a joining form", <c>C</c> just
    /// has no visible form of its own, e.g. Tatweel/ZWJ) plus two <em>Joining_Group</em> overrides that
    /// take priority over the plain type for a handful of Syriac codepoints whose joining behavior
    /// genuinely differs from an ordinary <c>R</c>-type letter: <c>ALAPH</c> (only U+0710) and
    /// <c>DALATH_RISH</c> (U+0715, U+0716, U+072A, U+072F - see <c>ArabicShaping.txt</c>'s own
    /// <c>Joining_Group</c> field). <see cref="ArabicJoiningShaper"/>'s column selection special-cases
    /// exactly these 5 codepoints rather than this repo carrying a full <c>Joining_Group</c> data table
    /// for a property only 5 codepoints ever need at this state-machine layer.
    /// </remarks>
    internal static class ArabicJoiningStateTable
    {
        internal enum Column
        {
            U = 0,
            L = 1,
            R = 2,
            D = 3,
            Alaph = 4,
            DalathRish = 5,
        }

        internal readonly record struct Entry(ArabicJoiningForm PrevAction, ArabicJoiningForm CurrAction, int NextState);

        private const ArabicJoiningForm None = ArabicJoiningForm.None;
        private const ArabicJoiningForm Isol = ArabicJoiningForm.Isol;
        private const ArabicJoiningForm Fina = ArabicJoiningForm.Fina;
        private const ArabicJoiningForm Fin2 = ArabicJoiningForm.Fin2;
        private const ArabicJoiningForm Fin3 = ArabicJoiningForm.Fin3;
        private const ArabicJoiningForm Medi = ArabicJoiningForm.Medi;
        private const ArabicJoiningForm Med2 = ArabicJoiningForm.Med2;
        private const ArabicJoiningForm Init = ArabicJoiningForm.Init;

        //                                            jt_U             jt_L             jt_R             jt_D             jg_ALAPH         jg_DALATH_RISH
        internal static readonly Entry[][] States =
        [
            // State 0: prev was U, not willing to join.
            [new(None, None, 0), new(None, Isol, 2), new(None, Isol, 1), new(None, Isol, 2), new(None, Isol, 1), new(None, Isol, 6)],

            // State 1: prev was R or ISOL/ALAPH, not willing to join.
            [new(None, None, 0), new(None, Isol, 2), new(None, Isol, 1), new(None, Isol, 2), new(None, Fin2, 5), new(None, Isol, 6)],

            // State 2: prev was D/L in ISOL form, willing to join.
            [new(None, None, 0), new(None, Isol, 2), new(Init, Fina, 1), new(Init, Fina, 3), new(Init, Fina, 4), new(Init, Fina, 6)],

            // State 3: prev was D in FINA form, willing to join.
            [new(None, None, 0), new(None, Isol, 2), new(Medi, Fina, 1), new(Medi, Fina, 3), new(Medi, Fina, 4), new(Medi, Fina, 6)],

            // State 4: prev was FINA ALAPH, not willing to join.
            [new(None, None, 0), new(None, Isol, 2), new(Med2, Isol, 1), new(Med2, Isol, 2), new(Med2, Fin2, 5), new(Med2, Isol, 6)],

            // State 5: prev was FIN2/FIN3 ALAPH, not willing to join.
            [new(None, None, 0), new(None, Isol, 2), new(Isol, Isol, 1), new(Isol, Isol, 2), new(Isol, Fin2, 5), new(Isol, Isol, 6)],

            // State 6: prev was DALATH/RISH, not willing to join.
            [new(None, None, 0), new(None, Isol, 2), new(None, Isol, 1), new(None, Isol, 2), new(None, Fin3, 5), new(None, Isol, 6)],
        ];
    }
}

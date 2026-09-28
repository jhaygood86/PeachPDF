// Ported from HarfBuzz's src/gen-indic-table.py (the `category_map`/`category_overrides`/
// `position_map` tables and the Khmer-block matra-to-position-category rewrite), retrieved
// 2026-09-27 from https://github.com/harfbuzz/harfbuzz/blob/main/src/gen-indic-table.py (commit
// 409c467b8259ad5fcc3fdcc477a1796fec256853) - "Old MIT" license. This script carries no individual
// per-file header (true of most build-time/tooling scripts under src/), so it falls under
// HarfBuzz's own project-wide notice from COPYING (https://github.com/harfbuzz/harfbuzz/blob/main/COPYING)
// rather than one file's own narrower header - see THIRD-PARTY-LICENSES.md for the full notice text
// and how this fits into PeachPDF's own licensing.

using PeachDrawing.Text.Unicode;
using System.Globalization;

namespace PeachDrawing.Text.Internal.Text.Shaping.Khmer
{
    /// <summary>
    /// Derives a codepoint's <see cref="KhmerCategory"/> - a pure function, ported from HarfBuzz's own
    /// category-derivation script (<c>gen-indic-table.py</c>'s <c>category_map</c>/
    /// <c>category_overrides</c>/<c>position_map</c> tables, retrieved 2026-09-27 from
    /// https://github.com/harfbuzz/harfbuzz/blob/main/src/gen-indic-table.py, commit
    /// 409c467b8259ad5fcc3fdcc477a1796fec256853 - "Old MIT" license; see THIRD-PARTY-LICENSES.md for
    /// the full text), against <see cref="IndicSyllabicCategoryTable"/>/<see cref="IndicPositionalCategoryTable"/>
    /// (the same two raw UCD tables <see cref="Shaping.Use.UseCategoryClassifier"/> already uses - Khmer
    /// and the Universal Shaping Engine both start from the same <c>Indic_Syllabic_Category</c>/
    /// <c>Indic_Positional_Category</c> data, they just derive a different, script-specific alphabet from
    /// it, matching how HarfBuzz's own C++ shares <c>hb-ot-shaper-indic-table.cc</c> between its Khmer,
    /// Myanmar and (legacy) Indic shapers).
    ///
    /// <b>Scope: only the categories a Khmer-block (U+1780-U+17FF) codepoint, U+25CC (dotted circle),
    /// or U+200C/U+200D (ZWNJ/ZWJ) can ever produce are ported.</b> HarfBuzz's real
    /// <c>gen-indic-table.py</c> derives categories for every Brahmic block it lists in
    /// <c>ALLOWED_BLOCKS</c> (Devanagari, Bengali, Myanmar, etc.) from one shared table; this port keeps
    /// only the rows a Khmer codepoint can actually reach (verified by enumerating every assigned
    /// codepoint in <c>assets/unicode/IndicSyllabicCategory.txt</c>/<c>IndicPositionalCategory.txt</c>
    /// against the Khmer block, not assumed).
    ///
    /// <b>Category derivation order</b> (mirrors the generator's own multi-pass pipeline exactly):
    /// <list type="number">
    /// <item>Start from <c>category_map[Indic_Syllabic_Category]</c> - HarfBuzz's own raw ISC-to-shaper-category
    /// table (e.g. <c>Consonant</c>/<c>Consonant_Dead</c>/<c>Consonant_Head_Letter</c> all become <c>C</c>,
    /// <c>Invisible_Stacker</c> becomes <c>H</c> - the coeng).</item>
    /// <item><b>Khmer-block matra rewrite</b>: if that base category is <c>M</c> (<c>Vowel_Dependent</c>) or
    /// <c>MPst</c>, HarfBuzz's generator does NOT use the general Indic per-block <c>indic_matra_position</c>
    /// table for Khmer (that table is Devanagari-family-specific) - instead it rewrites the category
    /// directly from the codepoint's raw <c>Indic_Positional_Category</c> (<c>Left</c>&#8594;<c>VPre</c>,
    /// <c>Top</c>&#8594;<c>VAbv</c>, <c>Bottom</c>&#8594;<c>VBlw</c>, <c>Right</c>&#8594;<c>VPst</c>, with the
    /// compound positions folding into whichever of those four they resolve to - see
    /// <see cref="ResolveVowelPosition"/>).</item>
    /// <item><b>Codepoint overrides</b> (<c>category_overrides</c>) always win, applied last: Khmer's own
    /// <c>Robatic</c>/<c>Xgroup</c>/<c>Ygroup</c> members are not derivable from ISC/IPC at all (they are
    /// HarfBuzz's own hand-curated groupings of specific signs - see <see cref="Classify"/>'s own switch),
    /// and a handful of codepoints whose ISC-derived category would otherwise be wrong for Khmer are
    /// corrected here the same way (U+179A KHMER LETTER RO to <see cref="KhmerCategory.Ra"/>; U+17D9
    /// KHMER SIGN PHNAEK MUAN, whose ISC is <c>Other</c>, to <see cref="KhmerCategory.Placeholder"/>; the
    /// shared U+25CC dotted circle to <see cref="KhmerCategory.DottedCircle"/>).</item>
    /// </list>
    /// A codepoint this classifier has no rule for (e.g. U+17DC KHMER SIGN AVAKRAHASANYA, ISC
    /// <c>Avagraha</c> &#8594; HarfBuzz's own <c>Symbol</c> category, which the Khmer syllable grammar has no
    /// role for) falls through to <see cref="KhmerCategory.Other"/> - the syllable scanner's own catch-all,
    /// exactly like <see cref="UseCategory.O"/> is for the Universal Shaping Engine.
    /// </summary>
    internal static class KhmerCategoryClassifier
    {
        private const int Ro = 0x179A;
        private const int PhnaekMuan = 0x17D9;
        private const int DottedCircleCodepoint = 0x25CC;
        private const int ZeroWidthNonJoiner = 0x200C;
        private const int ZeroWidthJoiner = 0x200D;

        public static KhmerCategory Classify(int codepoint)
        {
            // Codepoint overrides that do not depend on ISC/IPC at all - checked first since they win
            // over everything else, matching category_overrides' own application order (last write
            // wins, in gen-indic-table.py's own pipeline - see this class's own remarks).
            switch (codepoint)
            {
                case Ro: return KhmerCategory.Ra;
                case PhnaekMuan: return KhmerCategory.Placeholder;
                case DottedCircleCodepoint: return KhmerCategory.DottedCircle;
                case ZeroWidthNonJoiner: return KhmerCategory.ZWNJ;
                case ZeroWidthJoiner: return KhmerCategory.ZWJ;

                // Robatic: KHMER SIGN ROBAT (Consonant_Succeeding_Repha), MUUSIKATOAN/TRIISAP
                // (Register_Shifter).
                case 0x17CC: case 0x17C9: case 0x17CA:
                    return KhmerCategory.Robatic;

                // Xgroup: NIKAHIT (Bindu), BANTOC (Syllable_Modifier), TOANDAKHIAT (Consonant_Killer),
                // KAKABAT..SAMYOK SANNYA (Syllable_Modifier), VIRIAM (Pure_Killer).
                case 0x17C6: case 0x17CB: case 0x17CD: case 0x17CE: case 0x17CF: case 0x17D0: case 0x17D1:
                    return KhmerCategory.Xgroup;

                // Ygroup: REAHMUK (Visarga), YUUKALEAPINTU (Vowel_Dependent - overridden away from the
                // matra rewrite below), ATTHACAN/BATHAMASAT (Syllable_Modifier).
                case 0x17C7: case 0x17C8: case 0x17DD: case 0x17D3:
                    return KhmerCategory.Ygroup;
            }

            IndicSyllabicCategory uisc = IndicSyllabicCategoryTable.Of(codepoint);
            IndicPositionalCategory uipc = IndicPositionalCategoryTable.Of(codepoint);

            // category_map: Consonant/Consonant_Dead/Consonant_Head_Letter -> C (a Khmer codepoint
            // never has Consonant_Initial_Postfixed).
            if (uisc is IndicSyllabicCategory.Consonant or IndicSyllabicCategory.ConsonantDead
                    or IndicSyllabicCategory.ConsonantHeadLetter)
                return KhmerCategory.C;

            // category_map: Vowel_Independent -> V.
            if (uisc == IndicSyllabicCategory.VowelIndependent)
                return KhmerCategory.V;

            // category_map: Invisible_Stacker -> H (the coeng, U+17D2 - "we use category H for spec
            // category Coeng", HarfBuzz's own comment).
            if (uisc == IndicSyllabicCategory.InvisibleStacker)
                return KhmerCategory.H;

            // category_map: Number -> PLACEHOLDER (the Khmer digits, U+17E0-U+17E9).
            if (uisc == IndicSyllabicCategory.Number)
                return KhmerCategory.Placeholder;

            // Khmer-block matra rewrite (see this class's own remarks): Vowel_Dependent's base category
            // (M) is replaced by its own positional category, not by the general Indic per-block matra
            // table.
            if (uisc == IndicSyllabicCategory.VowelDependent)
                return ResolveVowelPosition(uipc);

            // Every other ISC value (Avagraha, Bindu, Visarga, Syllable_Modifier, Register_Shifter,
            // Consonant_Killer, Pure_Killer, Consonant_Succeeding_Repha, Joiner, Non_Joiner, Other, ...)
            // either never reaches here for a real Khmer-block codepoint (it was already claimed by the
            // codepoint-override switch above) or has no role in the Khmer syllable grammar at all
            // (Avagraha's own Symbol category - see this class's own remarks) - both fall through to the
            // syllable scanner's own catch-all.
            return KhmerCategory.Other;
        }

        /// <summary>HarfBuzz's own <c>position_to_category</c>, applied to a Vowel_Dependent codepoint's
        /// raw <c>Indic_Positional_Category</c> (via <c>position_map</c> first) - the compound positions
        /// (<c>Top_And_Left</c>, <c>Left_And_Right</c>, etc.) fold into whichever single position their
        /// own comment says a split sequence's last part would resolve to.</summary>
        private static KhmerCategory ResolveVowelPosition(IndicPositionalCategory uipc) => uipc switch
        {
            IndicPositionalCategory.Left => KhmerCategory.VPre,
            IndicPositionalCategory.Top or IndicPositionalCategory.TopAndLeft => KhmerCategory.VAbv,
            IndicPositionalCategory.Bottom => KhmerCategory.VBlw,
            IndicPositionalCategory.Right or IndicPositionalCategory.LeftAndRight
                or IndicPositionalCategory.TopAndLeftAndRight or IndicPositionalCategory.TopAndBottomAndRight => KhmerCategory.VPst,
            _ => KhmerCategory.VAbv,
        };
    }
}
